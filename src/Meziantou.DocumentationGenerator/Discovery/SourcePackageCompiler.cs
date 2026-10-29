using System.Collections.Immutable;
using System.Globalization;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NuGet.Frameworks;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>
/// Compiles the sources of a source package into a metadata-only assembly and its XML documentation, per target framework,
/// so that they go through the same pipeline as compiled packages. Source packages expose <c>internal</c> symbols, which
/// the consuming project can use because the files are compiled into it: the metadata keeps them, and the assembly is read
/// with <c>PublicApiReadOptions.IncludeInternalSymbols</c>.
/// The code is only parsed and compiled, never executed: no analyzer, source generator or file resolver is used.
/// </summary>
internal sealed class SourcePackageCompiler(DiagnosticBag diagnostics)
{
    private const int MaxReportedErrors = 3;
    private static readonly Lazy<ImmutableArray<MetadataReference>> RuntimeReferences = new(GetRuntimeReferences);

    /// <summary>Framework of the running runtime, used to select the assets referenced by <c>contentFiles/{lang}/any/</c> sources.</summary>
    private static readonly NuGetFramework RuntimeFramework = NuGetFramework.Parse("net" + Environment.Version.ToString(2));

    private readonly Dictionary<(string Package, string Entry), MetadataReference> _references = [];

    /// <param name="package">The source package.</param>
    /// <param name="availablePackages">Configured packages and references whose compile-time assemblies may be referenced by the sources.</param>
    public ImmutableArray<ApiAsset> Compile(DiscoveredPackage package, IReadOnlyList<DiscoveredPackage> availablePackages, CancellationToken cancellationToken)
    {
        var result = ImmutableArray.CreateBuilder<ApiAsset>();
        using var archive = PackageArchive.Open(package.Package.Path);
        foreach (var folder in package.SourceFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = new DiagnosticLocation { Package = package.Package.Configuration.Identity, JsonPath = package.Package.Configuration.JsonPath, TargetFramework = folder.TargetFramework, Assembly = folder.DisplayPath };
            var (references, referenceNames) = GetReferences(folder.Framework, package, availablePackages);
            var compiled = Compile(package, folder, archive, references, location, cancellationToken);
            if (compiled is null)
                continue;

            result.Add(new ApiAsset
            {
                Framework = folder.Framework,
                AssemblyEntry = folder.DisplayPath,
                AssemblyName = PackageDiscoverer.GetSourceAssemblyName(package.Package),
                IsReferenceAssembly = false,
                SymbolCandidates = [],
                CompiledSource = compiled,
            });

            diagnostics.Report(DiagnosticDescriptors.AssetSelected, $"Documenting the {folder.Entries.Length.ToString(CultureInfo.InvariantCulture)} source files of '{folder.DisplayPath}' for {folder.TargetFramework} (source package compiled in memory); references: {string.Join(", ", referenceNames)}", location);
        }

        return result.ToImmutable();
    }

    private CompiledSource? Compile(DiscoveredPackage package, SourceFolder folder, PackageArchive archive, ImmutableArray<MetadataReference> references, DiagnosticLocation location, CancellationToken cancellationToken)
    {
        var preprocessorSymbols = FrameworkPreprocessorSymbols.Get(folder.Framework).Concat(package.Package.Configuration.DefineConstants);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessorSymbols);
        var syntaxTrees = new List<SyntaxTree>();
        foreach (var entry in folder.Entries)
        {
            using var stream = archive.OpenRead(entry);
            var text = SourceText.From(stream, throwIfBinaryDetected: true);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, entry, cancellationToken));
        }

        var compilation = CSharpCompilation.Create(
            PackageDiscoverer.GetSourceAssemblyName(package.Package),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, deterministic: true, nullableContextOptions: NullableContextOptions.Disable));

        using var assembly = new MemoryStream();
        using var xml = new MemoryStream();
        // Without private members, metadata-only emit drops internal members unless the assembly has InternalsVisibleTo.
        // Private members are kept in metadata and excluded by PublicApiGenerator.
        var result = compilation.Emit(assembly, xmlDocumentationStream: xml, options: new EmitOptions(metadataOnly: true, includePrivateMembers: true), cancellationToken: cancellationToken);
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == RoslynDiagnosticSeverity.Error).ToList();
            var details = string.Join("; ", errors.Take(MaxReportedErrors).Select(FormatError));
            if (errors.Count > MaxReportedErrors)
            {
                details += string.Create(CultureInfo.InvariantCulture, $"; and {errors.Count - MaxReportedErrors} more");
            }

            diagnostics.Report(DiagnosticDescriptors.SourceCompilationFailed, $"The sources of '{folder.DisplayPath}' cannot be compiled for {folder.TargetFramework}, so this framework has no API reference: {details}. Configure the packages the sources depend on in 'references'", location);
            return null;
        }

        return new CompiledSource
        {
            Assembly = assembly.ToArray(),
            XmlDocumentation = xml.ToArray(),
            SourceEntries = folder.Entries,
        };
    }

    private static string FormatError(RoslynDiagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var position = span.IsValid ? string.Create(CultureInfo.InvariantCulture, $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): ") : "";
        return position + diagnostic.Id + " " + diagnostic.GetMessage(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// References the compile-time assemblies of the configured packages for the nearest framework. When they do not provide
    /// a core library (such as Microsoft.NETCore.App.Ref or NETStandard.Library.Ref in 'references'), the assemblies of the
    /// running .NET runtime are used, which is close enough to document most source packages.
    /// </summary>
    private (ImmutableArray<MetadataReference> References, List<string> Names) GetReferences(NuGetFramework framework, DiscoveredPackage package, IReadOnlyList<DiscoveredPackage> availablePackages)
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var names = new List<string>();
        var hasCoreLibrary = false;
        var reducer = new FrameworkReducer();

        // 'any' sources are compiled in projects of every framework; the assets of the running runtime's framework are used
        var referenceFramework = framework.IsAny ? RuntimeFramework : framework;
        foreach (var other in availablePackages)
        {
            if (ReferenceEquals(other, package) || other.IsSourcePackage || other.Assets.IsEmpty)
                continue;

            var nearest = reducer.GetNearest(referenceFramework, other.Assets.Select(a => a.Framework).Distinct());
            if (nearest is null)
                continue;

            using var archive = PackageArchive.Open(other.Package.Path);
            foreach (var asset in other.Assets.Where(a => a.Framework == nearest))
            {
                var key = (other.Package.Configuration.Identity, asset.AssemblyEntry);
                if (!_references.TryGetValue(key, out var reference))
                {
                    reference = MetadataReference.CreateFromImage(archive.ReadAllBytes(asset.AssemblyEntry), filePath: other.Package.Configuration.Identity + "/" + asset.AssemblyEntry);
                    _references.Add(key, reference);
                }

                references.Add(reference);
                hasCoreLibrary |= asset.AssemblyName is "System.Runtime" or "netstandard" or "mscorlib";
            }

            names.Add(other.Package.Configuration.Identity + " (" + nearest.GetShortFolderName() + ")");
        }

        if (!hasCoreLibrary)
        {
            references.AddRange(RuntimeReferences.Value);
            names.Add("the running .NET runtime (" + Environment.Version.ToString(2) + ")");
        }

        return (references.ToImmutable(), names);
    }

    /// <summary>Assemblies of the shared framework the tool runs on (Microsoft.NETCore.App), without the tool's own dependencies.</summary>
    private static ImmutableArray<MetadataReference> GetRuntimeReferences()
    {
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        return [.. trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => directory is not null && string.Equals(Path.GetDirectoryName(path), directory, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
}
