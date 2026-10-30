using System.Collections.Immutable;
using Meziantou.Framework;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed record PackageConfiguration
{
    /// <summary>The package identifier, or <see langword="null"/> to discover all packages in a local folder.</summary>
    public required string? Id { get; init; }

    /// <summary>Versions allowed by the configuration. A version without range syntax (<c>1.2.3</c>) is an exact match.</summary>
    public required VersionRange Version { get; init; }

    /// <summary>The version as written in the configuration, or <c>*</c> when it is omitted.</summary>
    public required string VersionText { get; init; }

    /// <summary>The pinned version when <see cref="Version"/> allows a single version; otherwise <see langword="null"/>.</summary>
    public NuGetVersion? ExactVersion => !Version.IsFloating && Version.HasLowerAndUpperBounds && Version.IsMinInclusive && Version.IsMaxInclusive && Version.MinVersion == Version.MaxVersion ? Version.MinVersion : null;

    /// <summary>Source URL or name of a source defined in NuGet.Config.</summary>
    public string? Source { get; init; }

    /// <summary>A local .nupkg file, or a folder of .nupkg files when <see cref="IsLocalFolder"/> is <see langword="true"/>.</summary>
    public FullPath? LocalPath { get; init; }

    /// <summary><see langword="true"/> when <see cref="LocalPath"/> is a folder: the highest matching package of the folder is used.</summary>
    public bool IsLocalFolder { get; init; }
    public required ImmutableArray<NuGetFramework> TargetFrameworks { get; init; }
    public required ImmutableArray<string> Assemblies { get; init; }

    /// <summary>Preprocessor symbols added to those of the target framework when compiling a source package.</summary>
    public ImmutableArray<string> DefineConstants { get; init; } = [];

    /// <summary>Index of the package in the configuration, used to locate diagnostics.</summary>
    public required int Index { get; init; }

    /// <summary><see langword="true"/> for packages declared in <c>references</c>: their APIs are not published.</summary>
    public bool IsReference { get; init; }

    public string JsonPath => (IsReference ? "$.references[" : "$.packages[") + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";

    /// <summary>The configured identity, such as <c>Contoso@1.2.3</c> or <c>Contoso@*</c>. Use <c>AcquiredPackage.Identity</c> for the resolved version.</summary>
    public string Identity => (Id ?? LocalPath?.Value) + "@" + (ExactVersion?.ToNormalizedString() ?? VersionText);

    public override string ToString() => Identity;
}
