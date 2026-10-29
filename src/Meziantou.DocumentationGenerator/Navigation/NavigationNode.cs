using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>An entry of a navigation tree. A node without <see cref="Url"/> is a group label.</summary>
internal sealed class NavigationNode
{
    public const string ApiReferenceKind = "api";
    public const string ProductKind = "product";
    public const string NamespaceKind = "namespace";
    public const string TypeKind = "type";

    public required string Title { get; init; }
    public string? Url { get; init; }

    /// <summary>Output file of the target page, used to select the active page. <see langword="null"/> for external links and groups.</summary>
    public string? PagePath { get; init; }
    public bool IsExternal { get; init; }
    public bool IsExpanded { get; init; }

    /// <summary>Kind of generated node (<c>api</c>, <c>product</c>, <c>namespace</c>, <c>type</c>), or <see langword="null"/> for authored pages and folders.</summary>
    public string? Kind { get; init; }
    public ImmutableArray<NavigationNode> Children { get; init; } = [];

    public IEnumerable<NavigationNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.DescendantsAndSelf())
            {
                yield return node;
            }
        }
    }

    public override string ToString() => Title;
}
