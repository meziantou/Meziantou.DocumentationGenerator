using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Parses a component element inside a paragraph. It runs before the raw HTML inline parser.</summary>
internal sealed class MarkdownComponentInlineParser : InlineParser
{
    public MarkdownComponentInlineParser()
    {
        OpeningCharacters = ['<'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice;
        if (!HtmlHelper.TryParseHtmlTag(ref slice, out var tag) || !MarkdownComponents.TryCreate(tag, out var component, out var error))
        {
            slice = start;
            return false;
        }

        processor.Inline = new MarkdownComponentInline(component, error)
        {
            Span = new SourceSpan(processor.GetSourcePosition(start.Start, out var line, out var column), processor.GetSourcePosition(slice.Start - 1)),
            Line = line,
            Column = column,
        };
        return true;
    }
}
