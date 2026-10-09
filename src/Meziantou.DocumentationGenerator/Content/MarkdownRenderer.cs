using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework.Markdown;
using Meziantou.Framework.Markdown.Extensions.AutoIdentifiers;
using Meziantou.Framework.Markdown.Extensions.Yaml;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Renders authored Markdown and package READMEs at build time: resolves <c>cref:</c> links through the link service,
/// rewrites links to Markdown pages and local files, highlights code, and sanitizes the result.
/// Supported extensions: CommonMark, pipe tables, task lists, heading identifiers (GitHub rules), YAML front matter,
/// GitHub alerts, auto links, footnotes and emphasis extras.
/// </summary>
internal sealed partial class MarkdownRenderer(LinkService links, HighlightingService highlighting, DiagnosticBag diagnostics)
{
    private const string CrefScheme = "cref:";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAlertBlocks()
        .UseAutoLinks()
        .UseFootnotes()
        .UseEmphasisExtras()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>Parses a document and reads its front matter, without resolving links.</summary>
    public static (MarkdownDocument Document, FrontMatter FrontMatter) Parse(string content, string displayPath, DiagnosticBag diagnostics)
    {
        var document = MarkdownConverter.Parse(content, Pipeline);
        var frontMatter = FrontMatter.Empty;
        if (document.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } block)
        {
            var yaml = block.Lines.ToString();
            var element = YamlDocumentLoader.Load(yaml, displayPath, diagnostics, DiagnosticDescriptors.InvalidFrontMatter, lineOffset: block.Line + 1);
            if (element is YamlMapping mapping)
            {
                frontMatter = new FrontMatter(
                    YamlDocumentLoader.GetString(mapping, "title"),
                    YamlDocumentLoader.GetString(mapping, "description"),
                    YamlDocumentLoader.GetString(mapping, "uid"),
                    YamlDocumentLoader.GetString(mapping, "slug"));
            }
            else if (element is not null)
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidFrontMatter, "The front matter must be a YAML mapping", new DiagnosticLocation { FilePath = displayPath, Line = block.Line + 1 });
            }
        }

        return (document, frontMatter);
    }

    public RenderedMarkdown Render(MarkdownSource source)
    {
        var (document, frontMatter) = Parse(source.Content, source.DisplayPath, diagnostics);
        return Render(document, frontMatter, source);
    }

    public RenderedMarkdown Render(MarkdownDocument document, FrontMatter frontMatter, MarkdownSource source)
    {
        var title = frontMatter.Title;
        var firstHeading = document.OfType<HeadingBlock>().FirstOrDefault();
        if (firstHeading is { Level: 1 } && document.Where(b => b is not YamlFrontMatterBlock and not LinkReferenceDefinitionGroup).FirstOrDefault() == firstHeading)
        {
            // The page template displays the title, so the leading level-1 heading is not repeated
            title ??= GetPlainText(firstHeading.Inline);
            document.Remove(firstHeading);
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            RewriteLink(link, source);
        }

        foreach (var html in document.Descendants<HtmlInline>().ToList())
        {
            html.Tag = RewriteRawHtml(html.Tag, source, html.Line + 1);
        }

        foreach (var block in document.Descendants<HtmlBlock>().ToList())
        {
            var text = RewriteRawHtml(block.Lines.ToString(), source, block.Line + 1);
            block.Lines = new Meziantou.Framework.Markdown.Helpers.StringLineGroup(text);
        }

        var headings = document.Descendants<HeadingBlock>()
            .Where(h => h.Level is 2 or 3 && h.GetAttributes().Id is not null)
            .Select(h => new HeadingInfo(h.Level, h.GetAttributes().Id!, GetPlainText(h.Inline)))
            .ToImmutableArray();

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        var codeRenderer = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        if (codeRenderer is not null)
        {
            renderer.ObjectRenderers.Remove(codeRenderer);
        }

        renderer.ObjectRenderers.Insert(0, new HighlightedCodeBlockRenderer(highlighting, source.DisplayPath));
        renderer.Render(document);
        writer.Flush();

        var description = frontMatter.Description ?? GetDescription(document);
        return new RenderedMarkdown(ContentSanitizer.Sanitize(writer.ToString()), title, description, headings);
    }

    private void RewriteLink(LinkInline link, MarkdownSource source)
    {
        var url = link.Url;
        var location = new DiagnosticLocation { FilePath = source.DisplayPath, Line = link.Line + 1, Column = link.Column + 1 };
        if (string.IsNullOrEmpty(url))
            return;

        if (url.StartsWith(CrefScheme, StringComparison.OrdinalIgnoreCase))
        {
            var cref = Uri.UnescapeDataString(url[CrefScheme.Length..]);
            var resolution = links.Resolve(cref, source.LinkContext);
            if (link.FirstChild is null)
            {
                link.AppendChild(new CodeInline(resolution.DisplayName) { Delimiter = '`', DelimiterCount = 1 });
            }

            switch (resolution.Kind)
            {
                case CrefResolutionKind.Resolved or CrefResolutionKind.External:
                    link.Url = resolution.Url;
                    break;
                case CrefResolutionKind.Ambiguous:
                    diagnostics.Report(DiagnosticDescriptors.AmbiguousReference, $"'{cref}' is ambiguous: {string.Join(", ", resolution.Candidates)}; qualify it with a package (cref:Package.Id!T:Type)", location);
                    Unlink(link);
                    break;
                default:
                    diagnostics.Report(DiagnosticDescriptors.UnresolvedReference, $"Cannot resolve '{cref}'", location);
                    Unlink(link);
                    break;
            }

            return;
        }

        if (!ContentSanitizer.IsAllowedUrl(url))
        {
            diagnostics.Report(DiagnosticDescriptors.UnsafeUrl, $"The URL '{Truncate(url)}' uses a scheme that is not allowed and was removed", location);
            Unlink(link);
            return;
        }

        var resolved = ResolveUrl(url, link.IsImage, source, link.Line + 1);
        if (resolved is null)
        {
            Unlink(link);
        }
        else
        {
            link.Url = resolved;
        }
    }

    /// <summary>Returns the rewritten URL, the original URL when no rewrite is needed, or <see langword="null"/> when the target is unresolved.</summary>
    private static string? ResolveUrl(string url, bool isImage, MarkdownSource source, int? line)
    {
        if (url.StartsWith('#', StringComparison.Ordinal) || url.StartsWith('/', StringComparison.Ordinal) || url.StartsWith('?', StringComparison.Ordinal) || Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        var (path, query, fragment) = SplitUrl(url);
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            decodedPath = path;
        }

        if (decodedPath.Length == 0)
            return url;

        var target = source.Resolver.ResolveRelative(decodedPath, isImage, line);
        if (target is null)
            return null;

        if (query is not null)
        {
            target += "?" + query;
        }

        if (fragment is not null && !target.Contains('#', StringComparison.Ordinal))
        {
            target += "#" + fragment;
        }

        return target;
    }

    private string RewriteRawHtml(string html, MarkdownSource source, int line)
    {
        return RawHtmlUrlAttributeRegex().Replace(html, match =>
        {
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value);
            var attribute = match.Groups["name"].Value;
            if (!ContentSanitizer.IsAllowedUrl(value))
            {
                diagnostics.Report(DiagnosticDescriptors.UnsafeUrl, $"The URL '{Truncate(value)}' uses a scheme that is not allowed and was removed", new DiagnosticLocation { FilePath = source.DisplayPath, Line = line });
                return "";
            }

            var resolved = ResolveUrl(value, attribute.Equals("src", StringComparison.OrdinalIgnoreCase), source, line);
            return resolved is null ? "" : $"{attribute}=\"{HtmlEncoding.Encode(resolved)}\"";
        });
    }

    private static (string Path, string? Query, string? Fragment) SplitUrl(string url)
    {
        string? fragment = null;
        string? query = null;
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = url[(hash + 1)..];
            url = url[..hash];
        }

        var question = url.IndexOf('?', StringComparison.Ordinal);
        if (question >= 0)
        {
            query = url[(question + 1)..];
            url = url[..question];
        }

        return (url, query, fragment);
    }

    /// <summary>Replaces a link by its content, or an image by its alternative text.</summary>
    private static void Unlink(LinkInline link)
    {
        if (link.IsImage)
        {
            link.ReplaceBy(new LiteralInline(GetPlainText(link)), copyChildren: false);
            return;
        }

        var children = link.ToList();
        foreach (var child in children)
        {
            child.Remove();
            link.InsertBefore(child);
        }

        link.Remove();
    }

    internal static string GetPlainText(ContainerInline? inline)
    {
        if (inline is null)
            return "";

        var builder = new StringBuilder();
        foreach (var child in inline.Descendants<Inline>())
        {
            switch (child)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.ToString());
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string? GetDescription(MarkdownDocument document)
    {
        var paragraph = document.Descendants<ParagraphBlock>().FirstOrDefault(p => p.Parent is MarkdownDocument);
        if (paragraph is null)
            return null;

        var text = WhitespaceRegex().Replace(GetPlainText(paragraph.Inline), " ").Trim();
        if (text.Length == 0)
            return null;

        return text.Length <= 160 ? text : text[..157].TrimEnd() + "...";
    }

    private static string Truncate(string value) => value.Length <= 50 ? value : value[..50] + "...";

    [GeneratedRegex("""(?<name>\b(?:href|src))\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'=<>`]+))""", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RawHtmlUrlAttributeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();

    private sealed class HighlightedCodeBlockRenderer(HighlightingService highlighting, string displayPath) : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock obj)
        {
            var language = obj is FencedCodeBlock fenced ? fenced.Info : null;
            var code = obj.Lines.ToString();
            var normalized = HighlightingService.NormalizeLanguage(language);
            var html = highlighting.Highlight(code, language, new DiagnosticLocation { FilePath = displayPath, Line = obj.Line + 1 });
            renderer.EnsureLine();
            renderer.Write("<pre><code");
            renderer.Write(normalized is null ? " class=\"hljs\"" : $" class=\"hljs language-{HtmlEncoding.Encode(normalized)}\"");
            renderer.Write('>');
            renderer.Write(html);
            renderer.WriteLine("</code></pre>");
        }
    }
}
