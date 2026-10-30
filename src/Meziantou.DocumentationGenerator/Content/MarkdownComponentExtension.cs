using Meziantou.Framework.Markdown;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Adds component elements (see <see cref="MarkdownComponents"/>) to a Markdown pipeline.</summary>
internal sealed class MarkdownComponentExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Components look like raw HTML, so their parsers must run first
        if (!pipeline.BlockParsers.Contains<MarkdownComponentBlockParser>() && !pipeline.BlockParsers.InsertBefore<HtmlBlockParser>(new MarkdownComponentBlockParser()))
        {
            pipeline.BlockParsers.Insert(0, new MarkdownComponentBlockParser());
        }

        if (!pipeline.InlineParsers.Contains<MarkdownComponentInlineParser>() && !pipeline.InlineParsers.InsertBefore<AutolinkInlineParser>(new MarkdownComponentInlineParser()))
        {
            pipeline.InlineParsers.Insert(0, new MarkdownComponentInlineParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            htmlRenderer.ObjectRenderers.AddIfNotAlready<MarkdownComponentRenderers.Block>();
            htmlRenderer.ObjectRenderers.AddIfNotAlready<MarkdownComponentRenderers.Inline>();
        }
    }
}
