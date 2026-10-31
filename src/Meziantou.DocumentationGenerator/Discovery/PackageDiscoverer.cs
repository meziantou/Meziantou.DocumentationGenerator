using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>
/// Turns an acquired package into documentation inputs. For each target framework, the compile-time assets are
/// <c>ref/{tfm}/*.dll</c> when the package has a <c>ref/{tfm}</c> folder, otherwise <c>lib/{tfm}/*.dll</c>, which is the
/// choice the compiler makes. <c>lib/</c> assemblies shadowed by <c>ref/</c> and <c>runtimes/{rid}/lib/</c> assemblies are
/// only symbol candidates and never add APIs. Discovery reads entries without loading or executing assemblies.
/// A package without compile-time assembly whose <c>contentFiles</c> contain C# files is a source package: its files are
/// recorded per target framework and compiled later by <see cref="SourcePackageCompiler"/>.
/// </summary>
internal sealed class PackageDiscoverer(DiagnosticBag diagnostics)
{
    private const string PlaceholderFileName = "_._";

    public DiscoveredPackage? Discover(AcquiredPackage package)
    {
        using var archive = PackageArchive.Open(package.Path);
        var location = new DiagnosticLocation { Package = package.Identity, JsonPath = package.Configuration.JsonPath };

        var refFolders = GetFrameworkFolders(archive, "ref", location);
        var libFolders = GetFrameworkFolders(archive, "lib", location);
        var runtimeAssemblies = GetRuntimeAssemblies(archive);

        // Source packages often add lib/{tfm}/_._ placeholders, so the sources are used when no folder contains an assembly
        var hasAssemblies = refFolders.Values.Concat(libFolders.Values).Any(files => files.Any(IsCandidateAssembly));
        var sourceFolders = hasAssemblies ? [] : GetSourceFolders(archive, location);

        var frameworks = (sourceFolders.Count > 0 ? sourceFolders.Keys : refFolders.Keys.Union(libFolders.Keys)).Distinct().OrderBy(f => f.GetShortFolderName(), StringComparer.Ordinal).ToList();
        var configuredFrameworks = package.Configuration.TargetFrameworks;
        foreach (var configured in configuredFrameworks)
        {
            if (!frameworks.Contains(configured))
            {
                diagnostics.Report(DiagnosticDescriptors.FilterMatchesNothing, $"Target framework '{configured.GetShortFolderName()}' is not an asset folder of the package; available: {FormatList(frameworks.Select(f => f.GetShortFolderName()))}", location);
            }
        }

        var assets = ImmutableArray.CreateBuilder<ApiAsset>();
        var placeholders = ImmutableArray.CreateBuilder<NuGetFramework>();
        var sources = ImmutableArray.CreateBuilder<SourceFolder>();
        foreach (var framework in frameworks)
        {
            if (!configuredFrameworks.IsEmpty && !configuredFrameworks.Contains(framework))
                continue;

            if (sourceFolders.TryGetValue(framework, out var sourceFolder))
            {
                // The sources are compiled into an assembly named after the package
                if (package.Configuration.Assemblies.IsEmpty || package.Configuration.Assemblies.Contains(GetSourceAssemblyName(package) + ".dll", StringComparer.OrdinalIgnoreCase))
                {
                    sources.Add(sourceFolder);
                }

                continue;
            }

            var isReference = refFolders.TryGetValue(framework, out var compileFiles);
            if (!isReference)
            {
                compileFiles = libFolders[framework];
            }

            var assemblies = compileFiles!.Where(IsCandidateAssembly).ToList();
            if (assemblies.Count == 0)
            {
                if (compileFiles!.Any(f => GetFileName(f) == PlaceholderFileName))
                {
                    placeholders.Add(framework);
                }

                continue;
            }

            foreach (var assemblyEntry in assemblies)
            {
                var assemblyFileName = GetFileName(assemblyEntry);
                if (!package.Configuration.Assemblies.IsEmpty && !package.Configuration.Assemblies.Contains(assemblyFileName, StringComparer.OrdinalIgnoreCase))
                    continue;

                var assetLocation = location with { TargetFramework = framework.GetShortFolderName(), Assembly = assemblyEntry };
                if (!IsManagedAssembly(archive, assemblyEntry))
                {
                    diagnostics.Report(DiagnosticDescriptors.InvalidManagedAssembly, $"'{assemblyEntry}' is not a valid managed assembly and is ignored", assetLocation);
                    continue;
                }

                var assemblyName = assemblyFileName[..^4];
                var xml = FindXmlDocumentation(archive, assemblyEntry, assemblyName, framework, isReference);
                if (xml is null && !package.Configuration.IsReference)
                {
                    diagnostics.Report(DiagnosticDescriptors.MissingXmlDocumentation, $"No XML documentation file for '{assemblyEntry}'; API pages are generated without documentation", assetLocation);
                }

                var symbolCandidates = GetSymbolCandidates(archive, assemblyEntry, assemblyFileName, framework, isReference, libFolders, runtimeAssemblies);
                assets.Add(new ApiAsset
                {
                    Framework = framework,
                    AssemblyEntry = assemblyEntry,
                    AssemblyName = assemblyName,
                    XmlDocumentationEntry = xml,
                    IsReferenceAssembly = isReference,
                    SymbolCandidates = symbolCandidates,
                });

                diagnostics.Report(DiagnosticDescriptors.AssetSelected, isReference
                    ? $"Documenting '{assemblyEntry}' for {framework.GetShortFolderName()} (reference assembly); symbol candidates: {FormatList(symbolCandidates.Select(c => c.AssemblyEntry))}"
                    : $"Documenting '{assemblyEntry}' for {framework.GetShortFolderName()}", assetLocation);
            }
        }

        foreach (var assemblyFilter in package.Configuration.Assemblies)
        {
            if (!assets.Any(a => string.Equals(a.AssemblyName + ".dll", assemblyFilter, StringComparison.OrdinalIgnoreCase)) && !(sources.Count > 0 && string.Equals(GetSourceAssemblyName(package) + ".dll", assemblyFilter, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Report(DiagnosticDescriptors.FilterMatchesNothing, $"Assembly filter '{assemblyFilter}' matches no compile-time assembly of the package", location);
            }
        }

        if (assets.Count == 0 && sourceFolders.Count == 0 && configuredFrameworks.IsEmpty && package.Configuration.Assemblies.IsEmpty && !package.Configuration.IsReference)
        {
            diagnostics.Report(DiagnosticDescriptors.NoDocumentableAssembly, "The package contains no compile-time managed assembly (dependency-only, native or tool package); it has no API reference", location);
        }

        return new DiscoveredPackage
        {
            Package = package,
            Assets = [.. assets.OrderBy(a => a.AssemblyName, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.TargetFramework, StringComparer.Ordinal)],
            PlaceholderFrameworks = placeholders.ToImmutable(),
            Dependencies = package.Metadata.DependencyGroups,
            SourceFolders = sources.ToImmutable(),
        };
    }

    /// <summary>Name of the assembly compiled from the sources of a source package.</summary>
    public static string GetSourceAssemblyName(AcquiredPackage package) => package.Id;

    /// <summary>
    /// Returns the C# files of <c>contentFiles/{cs|any}/{tfm}/</c>, including sub-folders, grouped by framework.
    /// Preprocessed files (<c>.cs.pp</c>) are not supported because their tokens depend on the consuming project.
    /// </summary>
    private Dictionary<NuGetFramework, SourceFolder> GetSourceFolders(PackageArchive archive, DiagnosticLocation location)
    {
        var files = new Dictionary<NuGetFramework, List<(string Language, string Folder, string Entry)>>();
        var reportedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var segments = entry.Split('/');
            if (segments.Length < 4 || !string.Equals(segments[0], "contentFiles", StringComparison.OrdinalIgnoreCase) || segments[1].ToLowerInvariant() is not ("cs" or "any"))
                continue;

            if (!entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var framework = NuGetFramework.ParseFolder(segments[2]);
            if (framework.IsUnsupported)
            {
                if (reportedFolders.Add(segments[2]))
                {
                    diagnostics.Report(DiagnosticDescriptors.InvalidManagedAssembly, $"'contentFiles/{segments[1]}/{segments[2]}/' is not a recognized target framework folder and is ignored", location);
                }

                continue;
            }

            if (!files.TryGetValue(framework, out var list))
            {
                list = [];
                files.Add(framework, list);
            }

            list.Add((segments[1], segments[2], entry));
        }

        var result = new Dictionary<NuGetFramework, SourceFolder>();
        foreach (var (framework, list) in files)
        {
            var languages = list.Select(f => f.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var displayPath = "contentFiles/" + (languages.Count == 1 ? languages[0] : "{" + string.Join(',', languages) + "}") + "/" + list[0].Folder + "/";
            result.Add(framework, new SourceFolder(framework, displayPath, [.. list.Select(f => f.Entry).Order(StringComparer.Ordinal)]));
        }

        return result;
    }

    /// <summary>Returns the files directly inside <c>{root}/{tfm}/</c>, grouped by framework. Files in sub-folders (satellite assemblies) are ignored.</summary>
    private Dictionary<NuGetFramework, List<string>> GetFrameworkFolders(PackageArchive archive, string root, DiagnosticLocation location)
    {
        var result = new Dictionary<NuGetFramework, List<string>>();
        var reportedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var segments = entry.Split('/');
            if (segments.Length != 3 || !string.Equals(segments[0], root, StringComparison.OrdinalIgnoreCase))
                continue;

            var framework = NuGetFramework.ParseFolder(segments[1]);
            if (framework.IsUnsupported)
            {
                if (reportedFolders.Add(segments[1]))
                {
                    diagnostics.Report(DiagnosticDescriptors.InvalidManagedAssembly, $"'{root}/{segments[1]}/' is not a recognized target framework folder and is ignored", location);
                }

                continue;
            }

            if (!result.TryGetValue(framework, out var files))
            {
                files = [];
                result.Add(framework, files);
            }

            files.Add(entry);
        }

        return result;
    }

    private static List<(string Rid, NuGetFramework Framework, string Entry)> GetRuntimeAssemblies(PackageArchive archive)
    {
        var result = new List<(string, NuGetFramework, string)>();
        foreach (var entry in archive.Entries)
        {
            var segments = entry.Split('/');
            if (segments is ["runtimes", var rid, "lib", var tfm, var file] && IsAssemblyFileName(file))
            {
                var framework = NuGetFramework.ParseFolder(tfm);
                if (!framework.IsUnsupported)
                {
                    result.Add((rid, framework, entry));
                }
            }
        }

        return result;
    }

    private static ImmutableArray<SymbolCandidate> GetSymbolCandidates(PackageArchive archive, string assemblyEntry, string assemblyFileName, NuGetFramework framework, bool isReference, Dictionary<NuGetFramework, List<string>> libFolders, List<(string Rid, NuGetFramework Framework, string Entry)> runtimeAssemblies)
    {
        var result = ImmutableArray.CreateBuilder<SymbolCandidate>();
        if (!isReference)
        {
            result.Add(new SymbolCandidate(assemblyEntry, FindAdjacentPdb(archive, assemblyEntry), RuntimeIdentifier: null));
        }
        else
        {
            // The implementation for a reference assembly is the lib assembly of the nearest compatible framework
            var nearest = new FrameworkReducer().GetNearest(framework, libFolders.Keys);
            if (nearest is not null)
            {
                var implementation = libFolders[nearest].FirstOrDefault(e => string.Equals(GetFileName(e), assemblyFileName, StringComparison.OrdinalIgnoreCase));
                if (implementation is not null)
                {
                    result.Add(new SymbolCandidate(implementation, FindAdjacentPdb(archive, implementation), RuntimeIdentifier: null));
                }
            }
        }

        var runtimeCandidates = runtimeAssemblies.Where(r => string.Equals(GetFileName(r.Entry), assemblyFileName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var group in runtimeCandidates.GroupBy(r => r.Rid, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var nearest = new FrameworkReducer().GetNearest(framework, group.Select(g => g.Framework));
            if (nearest is not null)
            {
                var entry = group.First(g => g.Framework == nearest).Entry;
                result.Add(new SymbolCandidate(entry, FindAdjacentPdb(archive, entry), group.Key));
            }
        }

        return result.ToImmutable();
    }

    private static string? FindXmlDocumentation(PackageArchive archive, string assemblyEntry, string assemblyName, NuGetFramework framework, bool isReference)
    {
        var directory = assemblyEntry[..assemblyEntry.LastIndexOf('/', StringComparison.Ordinal)];
        var xml = archive.FindEntry(directory + "/" + assemblyName + ".xml");
        if (xml is not null)
            return xml;

        // Documentation is sometimes shipped next to the implementation of the same target framework only.
        // Documentation of another target framework is never used.
        if (isReference)
        {
            return archive.FindEntry("lib/" + framework.GetShortFolderName() + "/" + assemblyName + ".xml")
                ?? archive.FindEntry("lib/" + directory.Split('/')[1] + "/" + assemblyName + ".xml");
        }

        return null;
    }

    private static string? FindAdjacentPdb(PackageArchive archive, string assemblyEntry)
    {
        return archive.FindEntry(assemblyEntry[..^4] + ".pdb");
    }

    private static bool IsCandidateAssembly(string entry)
    {
        var fileName = GetFileName(entry);
        return IsAssemblyFileName(fileName) && !fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssemblyFileName(string fileName) => fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsManagedAssembly(PackageArchive archive, string entry)
    {
        try
        {
            // Only the PE and metadata headers are read; types are read later by PublicApiGenerator
            using var stream = archive.OpenRead(entry);
            using var reader = new PEReader(stream);
            return reader.HasMetadata && reader.GetMetadataReader().IsAssembly;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or PackageArchiveException)
        {
            return false;
        }
    }

    private static string GetFileName(string entry) => entry[(entry.LastIndexOf('/', StringComparison.Ordinal) + 1)..];

    private static string FormatList(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }
}
