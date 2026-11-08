using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>The attributes of a <c>&lt;DependencyGraph /&gt;</c> element.</summary>
/// <param name="Patterns">Package identifiers in which <c>*</c> matches any characters, compared case-insensitively.</param>
/// <param name="External">Whether dependencies that the site does not document are drawn.</param>
/// <param name="Direction">Direction of the arrows, from a package to its dependencies.</param>
internal sealed record DependencyGraphRequest(ImmutableArray<string> Patterns, bool External, DependencyGraphDirection Direction);
