using Meziantou.DocumentationGenerator.DependencyGraphs;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>What a Markdown component is replaced with: a shell command, a link or a dependency graph.</summary>
internal sealed record MarkdownComponent(MarkdownComponentKind Kind, string Text, string? Url = null)
{
    /// <summary>The graph to draw when <see cref="Kind"/> is <see cref="MarkdownComponentKind.DependencyGraph"/>.</summary>
    public DependencyGraphRequest? Graph { get; init; }
}
