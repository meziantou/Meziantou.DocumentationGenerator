using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>The API of one assembly asset, with the provenance supplied by discovery.</summary>
internal sealed class ApiAssemblyModel
{
    public required DiscoveredPackage Package { get; init; }
    public required ApiAsset Asset { get; init; }

    /// <summary>Structured model read by PublicApiGenerator. It does not reference the package archive.</summary>
    public required PublicApiAssembly Assembly { get; init; }

    /// <summary>TFM-independent scope of the symbols: <c>{packageId}/{assemblyName}</c>.</summary>
    public string Scope => Assembly.Scope;

    public string TargetFramework => Asset.TargetFramework;

    public bool IsReference => Package.Package.Configuration.IsReference;

    public static string CreateScope(string packageId, string assemblyName) => packageId + "/" + assemblyName;

    public override string ToString() => $"{Scope} ({TargetFramework})";
}
