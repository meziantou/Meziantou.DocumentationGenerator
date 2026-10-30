using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A component alone on its line, such as <c>&lt;InstallNuGetPackage Id="..." /&gt;</c>.</summary>
internal sealed class MarkdownComponentBlock(BlockParser parser, MarkdownComponent? component, string? error) : LeafBlock(parser)
{
    /// <summary>The component, or <see langword="null"/> when the element is invalid (see <see cref="Error"/>).</summary>
    public MarkdownComponent? Component { get; } = component;

    public string? Error { get; } = error;
}
