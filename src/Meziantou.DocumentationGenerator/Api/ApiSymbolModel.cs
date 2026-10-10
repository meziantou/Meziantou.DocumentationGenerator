using System.Collections.Immutable;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>A symbol aggregated across the target frameworks of an assembly.</summary>
internal abstract class ApiSymbolModel
{
    public required ApiAssemblyGroup Assembly { get; init; }
    public required PublicApiAggregatedSymbol Aggregate { get; init; }
    public required ApiAvailability Availability { get; init; }

    /// <summary>Distinct declarations. A single variant means every target framework declares the symbol the same way.</summary>
    public required ImmutableArray<ApiVariant> Variants { get; init; }

    public string DocumentationId => Aggregate.DocumentationId;
    public string Name => Aggregate.Name;
    public PublicApiSymbolIdentity Identity => new(Assembly.Scope, DocumentationId);

    /// <summary>Differences between the variants, such as signatures, nullability, constraints or attributes.</summary>
    public PublicApiSymbolDifferences Differences => Aggregate.Differences;

    /// <summary>The symbol used for display: the variant declared by the most target frameworks, then the first one.</summary>
    public PublicApiSymbol PrimarySymbol => Variants[0].Symbol;

    public override string ToString() => DocumentationId;
}
