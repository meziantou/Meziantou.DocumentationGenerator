using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <summary>
/// Computes best-effort links to source files. Tokens are only used with the PDB of the module they come from: symbols of
/// reference assemblies are first mapped to the implementation assembly by documentation ID. Methods, properties and
/// events link to the first visible sequence point of their body; types link to the file of their first method; fields
/// have no link.
/// </summary>
internal sealed class SourceLinkProvider
{
    private readonly Dictionary<ApiSymbolModel, ImmutableArray<SourceLocationVariant>> _locations = [];

    public static SourceLinkProvider Empty { get; } = new();

    public ImmutableArray<SourceLocationVariant> GetLocations(ApiSymbolModel symbol) => _locations.TryGetValue(symbol, out var locations) ? locations : [];

    public static async Task<SourceLinkProvider> CreateAsync(ApiCatalog catalog, DebugInfoLoader loader, DiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var provider = new SourceLinkProvider();
        foreach (var group in catalog.Assemblies.Where(g => !g.IsReference))
        {
            var perFramework = new Dictionary<string, Dictionary<ApiSymbolModel, SourceLocation>>(StringComparer.Ordinal);
            using var archive = PackageArchive.Open(group.Package.Package.Path);
            foreach (var model in group.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                perFramework[model.TargetFramework] = await ComputeAsync(group, model, archive, loader, diagnostics, cancellationToken);
            }

            foreach (var symbol in group.GetAllTypes().SelectMany(t => new ApiSymbolModel[] { t }.Concat(t.Members)))
            {
                var variants = new List<(List<string> TargetFrameworks, SourceLocation Location)>();
                foreach (var targetFramework in symbol.Availability.TargetFrameworks)
                {
                    if (!perFramework[targetFramework].TryGetValue(symbol, out var location))
                        continue;

                    var existing = variants.FindIndex(v => v.Location == location);
                    if (existing >= 0)
                    {
                        variants[existing].TargetFrameworks.Add(targetFramework);
                    }
                    else
                    {
                        variants.Add(([targetFramework], location));
                    }
                }

                if (variants.Count > 0)
                {
                    provider._locations[symbol] = [.. variants.Select(v => new SourceLocationVariant([.. v.TargetFrameworks], v.Location))];
                }
            }
        }

        return provider;
    }

    private static async Task<Dictionary<ApiSymbolModel, SourceLocation>> ComputeAsync(ApiAssemblyGroup group, ApiAssemblyModel model, PackageArchive archive, DebugInfoLoader loader, DiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ApiSymbolModel, SourceLocation>();
        var location = new DiagnosticLocation { Package = group.Package.Package.Identity, TargetFramework = model.TargetFramework, Assembly = model.Asset.AssemblyEntry };

        // Prefer the implementation without runtime identifier; runtime-specific implementations are only used when they agree
        var candidates = model.Asset.SymbolCandidates.Where(c => c.RuntimeIdentifier is null).ToList();
        if (candidates.Count == 0)
        {
            candidates = [.. model.Asset.SymbolCandidates];
        }

        var debugInfos = new List<(SymbolCandidate Candidate, ImplementationDebugInfo Info)>();
        foreach (var candidate in candidates)
        {
            var info = await loader.LoadAsync(archive, candidate.AssemblyEntry, candidate.PdbEntry, location with { Assembly = candidate.AssemblyEntry }, cancellationToken);
            if (info?.Map is not null)
            {
                debugInfos.Add((candidate, info));
            }
        }

        if (debugInfos.Count == 0)
            return result;

        var unsupportedHosts = new SortedSet<string>(StringComparer.Ordinal);
        var ambiguous = 0;
        foreach (var type in group.GetAllTypes())
        {
            foreach (var symbol in new ApiSymbolModel[] { type }.Concat(type.Members))
            {
                var frameworkSymbol = symbol.Aggregate.GetSymbol(model.TargetFramework);
                if (frameworkSymbol is null)
                    continue;

                var locations = debugInfos
                    .Select(d => GetLocation(model, frameworkSymbol, d.Candidate, d.Info, unsupportedHosts))
                    .Where(l => l is not null)
                    .Distinct()
                    .ToList();
                if (locations.Count == 1)
                {
                    result[symbol] = locations[0]!;
                }
                else if (locations.Count > 1)
                {
                    ambiguous++;
                }
            }
        }

        if (unsupportedHosts.Count > 0)
        {
            diagnostics.Report(DiagnosticDescriptors.UnsupportedSourceHost, $"Source Link URLs of '{model.Asset.AssemblyEntry}' use unsupported hosts ({string.Join(", ", unsupportedHosts)}); only GitHub is supported", location);
        }

        if (ambiguous > 0)
        {
            diagnostics.Report(DiagnosticDescriptors.AmbiguousImplementation, $"{ambiguous} symbols of '{model.Asset.AssemblyEntry}' have different locations in the runtime-specific implementations; they have no source link", location);
        }

        return result;
    }

    private static SourceLocation? GetLocation(ApiAssemblyModel model, PublicApiSymbol symbol, SymbolCandidate candidate, ImplementationDebugInfo info, SortedSet<string> unsupportedHosts)
    {
        // A token of the API asset is only valid for the PDB of the same module
        var sameModule = !model.Asset.IsReferenceAssembly && string.Equals(candidate.AssemblyEntry, model.Asset.AssemblyEntry, StringComparison.Ordinal) && info.Assembly.Module.ModuleVersionId == model.Assembly.Module.ModuleVersionId;
        var implementation = sameModule ? symbol : info.Assembly.FindSymbolByDocumentationId(symbol.DocumentationId);
        if (implementation is null)
            return null;

        (string Document, int Line)? point;
        var precision = SourcePrecision.Line;
        switch (implementation)
        {
            case PublicApiMethod method:
                point = GetMethodLocation(info, method);
                break;
            case PublicApiProperty property:
                point = GetMethodLocation(info, property.GetMethod) ?? GetMethodLocation(info, property.SetMethod);
                break;
            case PublicApiEvent @event:
                point = GetMethodLocation(info, @event.AddMethod) ?? GetMethodLocation(info, @event.RemoveMethod);
                break;
            case PublicApiType type:
                point = type.Members.OfType<PublicApiMethod>().Select(m => GetMethodLocation(info, m)).FirstOrDefault(p => p is not null)
                    ?? type.Members.OfType<PublicApiProperty>().Select(p => GetMethodLocation(info, p.GetMethod)).FirstOrDefault(p => p is not null);
                precision = SourcePrecision.File;
                break;
            default:
                return null;
        }

        if (point is not { } value)
            return null;

        var sourceUrl = info.Map!.GetUrl(value.Document);
        if (sourceUrl is null)
            return null;

        if (!GitHubSourceUrl.TryConvert(sourceUrl, out var url, out var path))
        {
            if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            {
                unsupportedHosts.Add(uri.Host);
            }

            return null;
        }

        return precision is SourcePrecision.Line
            ? new SourceLocation(url + "#L" + value.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), SourcePrecision.Line, path, value.Line)
            : new SourceLocation(url, SourcePrecision.File, path, Line: null);
    }

    private static (string Document, int Line)? GetMethodLocation(ImplementationDebugInfo info, PublicApiMethod? method)
    {
        // The members of C# 14 extension blocks have no body: the code is in their implementation
        method = method?.ExtensionImplementation ?? method;
        if (method?.Origin is not { } origin || origin.ModuleVersionId != info.Assembly.Module.ModuleVersionId)
            return null;

        return info.Pdb.GetMethodLocation(origin.MetadataToken);
    }
}
