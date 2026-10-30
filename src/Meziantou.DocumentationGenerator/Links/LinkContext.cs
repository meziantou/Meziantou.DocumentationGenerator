namespace Meziantou.DocumentationGenerator.Links;

/// <summary>Where a reference appears. References are resolved first in the same assembly, then in the same package.</summary>
/// <param name="Scope">Assembly scope (<c>{packageId}/{assemblyName}</c>) of the documentation containing the reference.</param>
/// <param name="PackageId">Package of the page containing the reference.</param>
internal sealed record LinkContext(string? Scope, string? PackageId)
{
    public static LinkContext None { get; } = new(Scope: null, PackageId: null);
}
