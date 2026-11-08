using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A component alone on its line, such as <c>&lt;InstallNuGetPackage Id="..." /&gt;</c>.</summary>
internal sealed class MarkdownComponentBlock(BlockParser parser, MarkdownComponent? component, string? error) : LeafBlock(parser)
{
    /// <summary>The component, or <see langword="null"/> when the element is invalid (see <see cref="Error"/>).</summary>
    public MarkdownComponent? Component { get; } = component;

    public string? Error { get; } = error;

    /// <summary>
    /// Text that the renderer writes instead of the component, and that is replaced after the page is sanitized. Used
    /// by components whose markup the sanitizer would remove, such as the SVG of a dependency graph.
    /// </summary>
    public string? Placeholder { get; set; }
}
