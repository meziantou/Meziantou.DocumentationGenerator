using System.Collections.Immutable;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>A distinct declaration of a symbol and the target frameworks sharing it.</summary>
internal sealed class ApiVariant
{
    public required ImmutableArray<string> TargetFrameworks { get; init; }

    /// <summary>Representative symbol of the variant, used for display.</summary>
    public required PublicApiSymbol Symbol { get; init; }

    /// <summary>The symbol of each target framework of the variant, in <see cref="TargetFrameworks"/> order. Each keeps its own metadata provenance.</summary>
    public required ImmutableArray<PublicApiSymbol> Symbols { get; init; }
}
