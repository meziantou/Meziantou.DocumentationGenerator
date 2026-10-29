using System.Collections.Immutable;
using Meziantou.Framework;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class PackageConfiguration
{
    public required string Id { get; init; }
    public required NuGetVersion Version { get; init; }

    /// <summary>Source URL or name of a source defined in NuGet.Config.</summary>
    public string? Source { get; init; }
    public FullPath? LocalPath { get; init; }
    public required ImmutableArray<NuGetFramework> TargetFrameworks { get; init; }
    public required ImmutableArray<string> Assemblies { get; init; }

    /// <summary>Preprocessor symbols added to those of the target framework when compiling a source package.</summary>
    public ImmutableArray<string> DefineConstants { get; init; } = [];

    /// <summary>Index of the package in the configuration, used to locate diagnostics.</summary>
    public required int Index { get; init; }

    /// <summary><see langword="true"/> for packages declared in <c>references</c>: their APIs are not published.</summary>
    public bool IsReference { get; init; }

    public string JsonPath => (IsReference ? "$.references[" : "$.packages[") + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";

    public string Identity => Id + "@" + Version.ToNormalizedString();

    public override string ToString() => Identity;
}
