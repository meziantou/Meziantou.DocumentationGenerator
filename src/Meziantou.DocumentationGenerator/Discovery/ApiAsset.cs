using System.Collections.Immutable;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>A compile-time assembly whose public API is documented for one target framework folder.</summary>
internal sealed record ApiAsset
{
    public required NuGetFramework Framework { get; init; }

    /// <summary>Short folder name of <see cref="Framework"/>, such as <c>net8.0</c>.</summary>
    public string TargetFramework => Framework.GetShortFolderName();
    public required string AssemblyEntry { get; init; }

    /// <summary>File name without extension, used to group the assembly across target frameworks.</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Entry of the XML documentation file matching the assembly for the same target framework.</summary>
    public string? XmlDocumentationEntry { get; init; }

    /// <summary><see langword="true"/> when the assembly comes from <c>ref/</c>. Its metadata tokens must not be used with implementation PDBs.</summary>
    public required bool IsReferenceAssembly { get; init; }

    /// <summary>Assemblies that may provide debug information: the asset itself for <c>lib/</c>, or the matching implementations.</summary>
    public required ImmutableArray<SymbolCandidate> SymbolCandidates { get; init; }

    /// <summary>
    /// Set for a source package: the assembly is compiled from the package sources instead of being read from
    /// <see cref="AssemblyEntry"/>, which is then the source folder.
    /// </summary>
    public CompiledSource? CompiledSource { get; init; }
}
