using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Parses a line that holds only a component element. It runs before the raw HTML block parser.</summary>
internal sealed class MarkdownComponentBlockParser : BlockParser
{
    public MarkdownComponentBlockParser()
    {
        OpeningCharacters = ['<'];
    }

    // Like raw HTML, an element on the line after paragraph text does not interrupt the paragraph: it is parsed as an inline
    public override bool CanInterrupt(BlockProcessor processor, Block block) => block is not ParagraphBlock;

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
            return BlockState.None;

        var start = processor.Start;
        var line = processor.Line;
        if (!HtmlHelper.TryParseHtmlTag(ref line, out var tag))
            return BlockState.None;

        for (var c = line.CurrentChar; c != '\0'; c = line.NextChar())
        {
            if (!char.IsWhiteSpace(c))
                return BlockState.None;
        }

        if (!MarkdownComponents.TryCreate(tag, out var component, out var error))
            return BlockState.None;

        processor.NewBlocks.Push(new MarkdownComponentBlock(this, component, error)
        {
            Column = processor.Column,
            Span = new SourceSpan(start, line.End),
        });
        return BlockState.BreakDiscard;
    }
}
