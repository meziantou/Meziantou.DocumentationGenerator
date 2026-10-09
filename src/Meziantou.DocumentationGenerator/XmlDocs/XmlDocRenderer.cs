using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// Renders XML documentation as semantic HTML. Text and attribute values are always encoded; raw HTML from the XML is
/// never emitted. Links go through the link service, and code goes through the shared highlighting service.
/// </summary>
internal sealed partial class XmlDocRenderer(LinkService links, HighlightingService highlighting, DiagnosticBag diagnostics)
{
    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal) { "para", "p", "code", "list", "ul", "ol", "table", "example", "note", "div", "blockquote" };

    /// <summary>Renders the content of a section, such as <c>summary</c>, as block HTML.</summary>
    public string RenderBlock(XElement section, XmlRenderContext context)
    {
        var builder = new StringBuilder();
        var paragraph = new StringBuilder();
        void FlushParagraph()
        {
            var text = paragraph.ToString().Trim();
            if (text.Length > 0)
            {
                builder.Append("<p>").Append(text).Append("</p>");
            }

            paragraph.Clear();
        }

        foreach (var node in section.Nodes())
        {
            if (node is XElement element && BlockElements.Contains(element.Name.LocalName) && !IsInlineCode(element))
            {
                FlushParagraph();
                RenderElement(element, builder, context, isBlock: true);
            }
            else
            {
                RenderNode(node, paragraph, context);
            }
        }

        FlushParagraph();
        return builder.ToString();
    }

    /// <summary>Renders the content of an element as inline HTML, such as a parameter description in a table.</summary>
    public string RenderInline(XElement element, XmlRenderContext context)
    {
        var builder = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            RenderNode(node, builder, context);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Renders a <c>see</c>/<c>seealso</c> reference, or <see langword="null"/> when it has no target.</summary>
    public string? RenderReference(XElement reference, XmlRenderContext context)
    {
        var builder = new StringBuilder();
        RenderSee(reference, builder, context);
        var html = builder.ToString();
        return html.Length == 0 ? null : html;
    }

    private void RenderNode(XNode node, StringBuilder builder, XmlRenderContext context)
    {
        switch (node)
        {
            case XText text:
                builder.Append(HtmlEncoding.Encode(WhitespaceRegex().Replace(text.Value, " ")));
                break;
            case XElement element:
                RenderElement(element, builder, context, isBlock: false);
                break;
        }
    }

    private void RenderChildren(XElement element, StringBuilder builder, XmlRenderContext context)
    {
        foreach (var node in element.Nodes())
        {
            RenderNode(node, builder, context);
        }
    }

    private void RenderElement(XElement element, StringBuilder builder, XmlRenderContext context, bool isBlock)
    {
        switch (element.Name.LocalName)
        {
            case "para" or "p":
                builder.Append(isBlock ? "<p>" : " ");
                RenderChildren(element, builder, context);
                builder.Append(isBlock ? "</p>" : " ");
                break;
            case "c" or "tt":
                builder.Append("<code>").Append(HtmlEncoding.Encode(element.Value)).Append("</code>");
                break;
            case "code":
                RenderCode(element, builder, isBlock);
                break;
            case "see" or "seealso":
                RenderSee(element, builder, context);
                break;
            case "paramref":
                builder.Append("<code class=\"paramref\">").Append(HtmlEncoding.Encode(element.Attribute("name")?.Value ?? "")).Append("</code>");
                break;
            case "typeparamref":
                builder.Append("<code class=\"typeparamref\">").Append(HtmlEncoding.Encode(element.Attribute("name")?.Value ?? "")).Append("</code>");
                break;
            case "list":
                RenderList(element, builder, context);
                break;
            case "b" or "strong":
                builder.Append("<strong>");
                RenderChildren(element, builder, context);
                builder.Append("</strong>");
                break;
            case "i" or "em":
                builder.Append("<em>");
                RenderChildren(element, builder, context);
                builder.Append("</em>");
                break;
            case "br":
                builder.Append("<br>");
                break;
            case "a":
                RenderHref(element.Attribute("href")?.Value, element, builder, context);
                break;
            case "ul" or "ol":
                builder.Append('<').Append(element.Name.LocalName).Append('>');
                foreach (var item in element.Elements("li"))
                {
                    builder.Append("<li>");
                    RenderChildren(item, builder, context);
                    builder.Append("</li>");
                }

                builder.Append("</").Append(element.Name.LocalName).Append('>');
                break;
            case "example" or "note" or "div" or "blockquote":
                builder.Append(RenderBlock(element, context));
                break;
            case "inheritdoc" or "include":
                // Resolved before rendering; an unresolved directive has already been reported
                break;
            default:
                diagnostics.Report(DiagnosticDescriptors.UnknownXmlTag, $"The documentation tag <{element.Name.LocalName}> is not supported; its text is displayed", new DiagnosticLocation { FilePath = context.SourcePath, Line = XmlDocumentationFile.GetLine(element), Symbol = context.SymbolId });
                RenderChildren(element, builder, context);
                break;
        }
    }

    private void RenderCode(XElement element, StringBuilder builder, bool isBlock)
    {
        var code = TrimIndentation(element.Value);
        if (!isBlock && !code.Contains('\n', StringComparison.Ordinal))
        {
            builder.Append("<code>").Append(HtmlEncoding.Encode(code)).Append("</code>");
            return;
        }

        var language = element.Attribute("language")?.Value ?? element.Attribute("lang")?.Value ?? "csharp";
        var normalized = HighlightingService.NormalizeLanguage(language);
        builder.Append("<pre><code class=\"hljs");
        if (normalized is not null)
        {
            builder.Append(" language-").Append(HtmlEncoding.Encode(normalized));
        }

        builder.Append("\">").Append(highlighting.Highlight(code, language)).Append("</code></pre>");
    }

    private void RenderSee(XElement element, StringBuilder builder, XmlRenderContext context)
    {
        if (element.Attribute("langword")?.Value is { } langword)
        {
            builder.Append("<code>").Append(HtmlEncoding.Encode(langword)).Append("</code>");
            return;
        }

        if (element.Attribute("href")?.Value is { } href)
        {
            RenderHref(href, element, builder, context);
            return;
        }

        if (element.Attribute("cref")?.Value is not { } cref)
        {
            RenderChildren(element, builder, context);
            return;
        }

        var resolution = links.Resolve(cref, context.Links);
        var hasText = element.Nodes().Any(n => n is XElement || (n is XText t && !string.IsNullOrWhiteSpace(t.Value)));
        var content = new StringBuilder();
        if (hasText)
        {
            RenderChildren(element, content, context);
        }
        else
        {
            content.Append("<code>").Append(HtmlEncoding.Encode(resolution.DisplayName)).Append("</code>");
        }

        if (resolution.Url is not null)
        {
            builder.Append("<a href=\"").Append(HtmlEncoding.Encode(resolution.Url)).Append("\">").Append(content).Append("</a>");
            return;
        }

        var location = new DiagnosticLocation { FilePath = context.SourcePath, Line = XmlDocumentationFile.GetLine(element), Symbol = context.SymbolId };
        if (resolution.Kind is CrefResolutionKind.Ambiguous)
        {
            diagnostics.Report(DiagnosticDescriptors.AmbiguousReference, $"'{cref}' is ambiguous: {string.Join(", ", resolution.Candidates)}", location);
        }
        else if (cref.StartsWith("!:", StringComparison.Ordinal))
        {
            diagnostics.Report(DiagnosticDescriptors.UnresolvedReference, $"The compiler could not resolve '{cref[2..]}'", location);
        }
        else
        {
            // References to symbols outside the configured packages are expected unless an xrefs mapping is configured
            diagnostics.Report(DiagnosticDescriptors.ExternalReferenceWithoutMapping, $"No page or xrefs mapping for '{cref}'", location);
        }

        builder.Append(content);
    }

    private void RenderHref(string? href, XElement element, StringBuilder builder, XmlRenderContext context)
    {
        var content = new StringBuilder();
        RenderChildren(element, content, context);
        if (content.Length == 0)
        {
            content.Append(HtmlEncoding.Encode(href ?? ""));
        }

        if (href is null || !ContentSanitizer.IsAllowedUrl(href) || !Uri.TryCreate(href, UriKind.Absolute, out _))
        {
            if (href is not null)
            {
                diagnostics.Report(DiagnosticDescriptors.UnsafeUrl, $"The link '{href}' is not an absolute http, https or mailto URL and was removed", new DiagnosticLocation { FilePath = context.SourcePath, Line = XmlDocumentationFile.GetLine(element), Symbol = context.SymbolId });
            }

            builder.Append(content);
            return;
        }

        builder.Append("<a href=\"").Append(HtmlEncoding.Encode(href)).Append("\">").Append(content).Append("</a>");
    }

    private void RenderList(XElement list, StringBuilder builder, XmlRenderContext context)
    {
        var type = list.Attribute("type")?.Value ?? "bullet";
        var items = list.Elements("item").ToList();
        if (type == "table")
        {
            builder.Append("<table>");
            if (list.Element("listheader") is { } header)
            {
                builder.Append("<thead><tr>");
                foreach (var cell in GetCells(header))
                {
                    builder.Append("<th>");
                    RenderChildren(cell, builder, context);
                    builder.Append("</th>");
                }

                builder.Append("</tr></thead>");
            }

            builder.Append("<tbody>");
            foreach (var item in items)
            {
                builder.Append("<tr>");
                foreach (var cell in GetCells(item))
                {
                    builder.Append("<td>");
                    RenderChildren(cell, builder, context);
                    builder.Append("</td>");
                }

                builder.Append("</tr>");
            }

            builder.Append("</tbody></table>");
            return;
        }

        var tag = type == "number" ? "ol" : "ul";
        builder.Append('<').Append(tag).Append('>');
        foreach (var item in items)
        {
            builder.Append("<li>");
            var term = item.Element("term");
            var description = item.Element("description");
            if (term is not null)
            {
                builder.Append("<strong>");
                RenderChildren(term, builder, context);
                builder.Append("</strong>");
                if (description is not null)
                {
                    builder.Append(" – ");
                }
            }

            if (description is not null)
            {
                RenderChildren(description, builder, context);
            }
            else if (term is null)
            {
                RenderChildren(item, builder, context);
            }

            builder.Append("</li>");
        }

        builder.Append("</").Append(tag).Append('>');
    }

    private static List<XElement> GetCells(XElement row)
    {
        var cells = row.Elements().Where(e => e.Name.LocalName is "term" or "description").ToList();
        return cells.Count > 0 ? cells : [row];
    }

    /// <summary>A single-line <c>&lt;code&gt;</c> element without language, written in the middle of a sentence, is rendered inline.</summary>
    private static bool IsInlineCode(XElement element)
    {
        if (element.Name.LocalName != "code" || element.Attribute("language") is not null || element.Attribute("lang") is not null || element.Value.Trim().Contains('\n', StringComparison.Ordinal))
            return false;

        static bool HasText(XNode? node) => node is XText text && !string.IsNullOrWhiteSpace(text.Value);
        return HasText(element.PreviousNode) || HasText(element.NextNode);
    }

    /// <summary>Removes the indentation shared by every line, and leading and trailing blank lines.</summary>
    internal static string TrimIndentation(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var indentation = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indentation ? l[indentation..] : l.TrimStart()));
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
