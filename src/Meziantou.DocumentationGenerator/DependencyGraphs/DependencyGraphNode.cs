namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <param name="Url">The package page, or the nuget.org page for an external package.</param>
internal sealed record DependencyGraphNode(string Id, string? Version, string? Description, string Url, bool IsExternal);
