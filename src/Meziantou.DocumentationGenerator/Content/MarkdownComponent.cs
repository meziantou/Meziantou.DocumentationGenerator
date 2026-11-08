using Meziantou.DocumentationGenerator.DependencyGraphs;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>What a Markdown component is replaced with: a shell command, a link, a dependency graph or a generated list.</summary>
internal sealed record MarkdownComponent(MarkdownComponentKind Kind, string Text, string? Url = null)
{
    /// <summary>The graph to draw when <see cref="Kind"/> is <see cref="MarkdownComponentKind.DependencyGraph"/>.</summary>
    public DependencyGraphRequest? Graph { get; init; }

    /// <summary>The entries to list when <see cref="Kind"/> is <see cref="MarkdownComponentKind.SectionList"/> or <see cref="MarkdownComponentKind.PackageList"/>.</summary>
    public EntryListRequest? List { get; init; }
}
