using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A component inside a paragraph, such as <c>See &lt;NuGetReference Id="..." /&gt;</c>.</summary>
internal sealed class MarkdownComponentInline(MarkdownComponent? component, string? error) : LeafInline
{
    /// <summary>The component, or <see langword="null"/> when the element is invalid (see <see cref="Error"/>).</summary>
    public MarkdownComponent? Component { get; } = component;

    public string? Error { get; } = error;
}
