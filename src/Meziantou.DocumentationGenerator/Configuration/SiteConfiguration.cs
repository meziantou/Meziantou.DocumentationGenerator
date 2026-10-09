using System.Collections.Immutable;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Validated configuration with every path resolved against the configuration directory.</summary>
internal sealed class SiteConfiguration
{
    public required FullPath ConfigurationFile { get; init; }
    public FullPath BaseDirectory => ConfigurationFile.Parent;
    public required SiteSettings Site { get; init; }
    public required FullPath OutputDirectory { get; init; }
    public required FullPath CacheDirectory { get; init; }
    public required ImmutableArray<PackageConfiguration> Packages { get; init; }
    public required ImmutableArray<ProductConfiguration> Products { get; init; }
    public ContentConfiguration? Content { get; init; }
    public required ApiFilter ApiFilter { get; init; }
    public required ImmutableArray<ExternalReferenceMapping> ExternalReferences { get; init; }
    public required DiagnosticOptions Diagnostics { get; init; }
    public required SourceLinkOptions SourceLinks { get; init; }

    /// <summary>JSON paths of the values that were not configured and use their default value.</summary>
    public required ImmutableArray<string> DefaultedValues { get; init; }

    /// <summary>Returns a path relative to the configuration directory, so diagnostics and generated content do not contain machine-specific paths.</summary>
    public string GetDisplayPath(FullPath path)
    {
        return path.IsChildOf(BaseDirectory) || path == BaseDirectory ? path.MakePathRelativeTo(BaseDirectory).Replace('\\', '/') : path.Name;
    }

    public PackageConfiguration? FindPackage(string id) => Packages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public ProductConfiguration? FindProductOfPackage(string packageId) => Products.FirstOrDefault(p => p.Packages.Contains(packageId, StringComparer.OrdinalIgnoreCase));
}
