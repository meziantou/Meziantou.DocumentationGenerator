using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>
/// Reads the API of discovered assets with PublicApiGenerator. Assemblies are read as metadata from the package archive:
/// they are never loaded or executed, and no second metadata reader is implemented here.
/// </summary>
internal sealed class ApiAssemblyReader(DiagnosticBag diagnostics)
{
    public ImmutableArray<ApiAssemblyModel> Read(DiscoveredPackage package, string? productId, CancellationToken cancellationToken)
    {
        if (package.Assets.IsEmpty)
            return [];

        var result = ImmutableArray.CreateBuilder<ApiAssemblyModel>();
        using var archive = PackageArchive.Open(package.Package.Path);
        foreach (var asset in package.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = new DiagnosticLocation { Package = package.Package.Identity, TargetFramework = asset.TargetFramework, Assembly = asset.AssemblyEntry };
            try
            {
                using var stream = asset.CompiledSource is { } compiled ? new MemoryStream(compiled.Assembly, writable: false) : archive.OpenRead(asset.AssemblyEntry);
                var assembly = PublicApi.ReadAssembly(stream, new PublicApiReadOptions
                {
                    TargetFramework = asset.TargetFramework,
                    InputIdentity = ApiAssemblyModel.CreateScope(package.Id, asset.AssemblyName),

                    // The internal symbols of a source package are part of the consuming project
                    IncludeInternalSymbols = asset.CompiledSource is not null,
                });

                ReportUndecodedAttributes(assembly, location);
                result.Add(new ApiAssemblyModel
                {
                    Package = package,
                    Asset = asset,
                    ProductId = productId,
                    Assembly = assembly,
                });
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or NotSupportedException or PackageArchiveException)
            {
                diagnostics.Report(DiagnosticDescriptors.UnsupportedMetadata, $"Cannot read the API of '{asset.AssemblyEntry}': {ex.Message}", location);
            }
        }

        return result.ToImmutable();
    }

    private void ReportUndecodedAttributes(PublicApiAssembly assembly, DiagnosticLocation location)
    {
        foreach (var symbol in assembly.GetAllSymbols())
        {
            foreach (var attribute in symbol.Attributes)
            {
                if (!attribute.AreArgumentsDecoded && DeclarationFormatter.IsDisplayedAttribute(attribute))
                {
                    diagnostics.Report(DiagnosticDescriptors.UnsupportedMetadata, $"The arguments of attribute '{attribute.AttributeType.FullName}' on '{symbol.DocumentationId}' cannot be decoded; the attribute is displayed without arguments", location with { Symbol = symbol.DocumentationId });
                }
            }
        }
    }
}
