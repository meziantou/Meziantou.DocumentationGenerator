using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Packages;

/// <summary>A verified package file. <see cref="Source"/> never contains credentials.</summary>
internal sealed class AcquiredPackage
{
    public required PackageConfiguration Configuration { get; init; }
    public required FullPath Path { get; init; }
    public required PackageMetadata Metadata { get; init; }

    /// <summary>Base64 SHA-512 of the package file, recorded for reproducibility.</summary>
    public required string Sha512 { get; init; }

    /// <summary>Name of the source the package came from, or <c>local</c> for a configured .nupkg file.</summary>
    public required string Source { get; init; }

    public string Id => Metadata.Id;

    /// <summary>The identity with the resolved version, such as <c>Contoso@1.2.3</c>.</summary>
    public string Identity => Configuration.Id + "@" + Metadata.Version.ToNormalizedString();

    public override string ToString() => Identity;
}
