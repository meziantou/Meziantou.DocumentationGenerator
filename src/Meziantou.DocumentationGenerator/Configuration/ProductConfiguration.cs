using System.Collections.Immutable;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class ProductConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Package identifiers, in configuration order.</summary>
    public required ImmutableArray<string> Packages { get; init; }
    public FullPath? LandingPage { get; init; }

    /// <summary>Package whose README is the landing page when <see cref="LandingPage"/> is not set.</summary>
    public string? ReadmePackage { get; init; }

    /// <summary><see langword="true"/> when the product was created implicitly for a package.</summary>
    public required bool IsImplicit { get; init; }

    public override string ToString() => Id;
}
