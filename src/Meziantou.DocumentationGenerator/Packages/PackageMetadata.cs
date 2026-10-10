using System.Collections.Immutable;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Packages;

/// <summary>Metadata read from the package manifest (.nuspec).</summary>
internal sealed class PackageMetadata
{
    public required string Id { get; init; }
    public required NuGetVersion Version { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string? ProjectUrl { get; init; }
    public string? LicenseExpression { get; init; }
    public string? LicenseUrl { get; init; }
    public string? RepositoryType { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? RepositoryBranch { get; init; }
    public string? RepositoryCommit { get; init; }

    /// <summary>Path of the README declared in the manifest, relative to the package root.</summary>
    public string? ReadmePath { get; init; }
    public string? Tags { get; init; }
    public required ImmutableArray<PackageDependencyGroup> DependencyGroups { get; init; }

    public static PackageMetadata Read(NuspecReader reader)
    {
        var repository = reader.GetRepositoryMetadata();
        var license = reader.GetLicenseMetadata();
        return new PackageMetadata
        {
            Id = reader.GetId(),
            Version = reader.GetVersion(),
            Title = NullIfEmpty(reader.GetTitle()),
            Description = NullIfEmpty(reader.GetDescription()),
            Authors = NullIfEmpty(reader.GetAuthors()),
            ProjectUrl = NullIfEmpty(reader.GetProjectUrl()),
            LicenseExpression = license?.Type == LicenseType.Expression ? license.License : null,
            LicenseUrl = NullIfEmpty(license?.LicenseUrl?.ToString() ?? reader.GetLicenseUrl()),
            RepositoryType = NullIfEmpty(repository?.Type),
            RepositoryUrl = NullIfEmpty(repository?.Url),
            RepositoryBranch = NullIfEmpty(repository?.Branch),
            RepositoryCommit = NullIfEmpty(repository?.Commit),
            ReadmePath = NullIfEmpty(reader.GetReadme()),
            Tags = NullIfEmpty(reader.GetTags()),
            DependencyGroups = [.. reader.GetDependencyGroups()],
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
