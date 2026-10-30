using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>HTML output of components: commands as code, package links as links with the NuGet icon (see site.css).</summary>
internal static class MarkdownComponentRenderers
{
    public static void WriteInline(HtmlRenderer renderer, MarkdownComponent component)
    {
        if (component.Kind is MarkdownComponentKind.Command)
        {
            renderer.Write("<code>").Write(HtmlEncoding.Encode(component.Text.Replace("\n", "; ", StringComparison.Ordinal))).Write("</code>");
            return;
        }

        renderer.Write("<a href=\"").Write(HtmlEncoding.Encode(component.Url)).Write("\" class=\"nuget-reference\">").Write(HtmlEncoding.Encode(component.Text)).Write("</a>");
    }

    public sealed class Block : HtmlObjectRenderer<MarkdownComponentBlock>
    {
        protected override void Write(HtmlRenderer renderer, MarkdownComponentBlock obj)
        {
            if (obj.Component is not { } component)
                return;

            if (component.Kind is MarkdownComponentKind.Command)
            {
                // Rendered by the code block renderer of the pipeline, so the command is highlighted like any shell code block
                renderer.Write(new FencedCodeBlock(null!) { Info = "bash", Lines = new StringLineGroup(component.Text), Line = obj.Line });
                return;
            }

            renderer.EnsureLine();
            renderer.Write("<p>");
            WriteInline(renderer, component);
            renderer.WriteLine("</p>");
        }
    }

    public sealed class Inline : HtmlObjectRenderer<MarkdownComponentInline>
    {
        protected override void Write(HtmlRenderer renderer, MarkdownComponentInline obj)
        {
            if (obj.Component is { } component)
            {
                WriteInline(renderer, component);
            }
        }
    }
}
