using Meziantou.Framework.Markdown;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>
/// Parses the footer of the site: one paragraph of Markdown inline content (emphasis, code, links). Raw HTML is shown as
/// text. The same footer is rendered on every page, so links must be absolute.
/// </summary>
internal static class FooterMarkdown
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static MarkdownDocument Parse(string markdown) => MarkdownConverter.Parse(markdown, Pipeline);

    /// <summary>Returns the URLs of the links, images and autolinks of the footer.</summary>
    public static IEnumerable<string?> GetUrls(MarkdownDocument document)
    {
        foreach (var inline in document.Descendants<Inline>())
        {
            switch (inline)
            {
                case LinkInline link:
                    yield return link.Url;
                    break;

                case AutolinkInline autolink:
                    yield return autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
                    break;
            }
        }
    }

    /// <summary>Returns <see langword="true"/> when the footer contains only inline content: one paragraph at most, and link reference definitions.</summary>
    public static bool IsInline(MarkdownDocument document)
        => document.Count(block => block is ParagraphBlock) <= 1 && document.All(block => block is ParagraphBlock or LinkReferenceDefinitionGroup);
}
