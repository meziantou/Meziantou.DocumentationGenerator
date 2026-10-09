using System.Collections.Immutable;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class ContentConfiguration
{
    public required FullPath Root { get; init; }
    public FullPath? Toc { get; init; }
    public required ImmutableArray<string> Assets { get; init; }
    public required ImmutableArray<string> Exclude { get; init; }
}
