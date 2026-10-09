namespace Meziantou.DocumentationGenerator.Links;

/// <summary>Where a reference appears. References are resolved first in the same assembly, then in the same product.</summary>
/// <param name="Scope">Assembly scope (<c>{packageId}/{assemblyName}</c>) of the documentation containing the reference.</param>
/// <param name="ProductId">Product of the page containing the reference.</param>
internal sealed record LinkContext(string? Scope, string? ProductId)
{
    public static LinkContext None { get; } = new(Scope: null, ProductId: null);
}
