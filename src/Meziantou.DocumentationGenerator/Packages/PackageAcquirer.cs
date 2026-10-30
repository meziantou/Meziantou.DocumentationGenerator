using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Packages;

/// <summary>
/// Acquires the configured packages: local .nupkg files, and the highest matching package of local folders, are used in place, remote packages are downloaded from the
/// sources defined in NuGet.Config (honoring source mapping and credentials) into a verified cache. A version range is
/// resolved to the highest matching version listed by the sources, or by the cache in offline mode.
/// Acquisition only reads package archives; it never runs build targets, tools or assembly code.
/// </summary>
internal sealed class PackageAcquirer
{
    private const int MaxConcurrency = 4;
    private static readonly Lock CredentialServiceLock = new();
    private static bool s_credentialServiceInitialized;

    private readonly FullPath _cacheDirectory;
    private readonly FullPath _settingsDirectory;
    private readonly bool _offline;
    private readonly DiagnosticBag _diagnostics;
    private readonly NuGet.Common.ILogger _logger;

    public PackageAcquirer(FullPath cacheDirectory, FullPath settingsDirectory, bool offline, DiagnosticBag diagnostics, Action<string>? log = null)
    {
        _cacheDirectory = cacheDirectory / "packages";
        _settingsDirectory = settingsDirectory;
        _offline = offline;
        _diagnostics = diagnostics;
        _logger = new NuGetLogger(log);
    }

    public async Task<ImmutableArray<AcquiredPackage>> AcquireAsync(IReadOnlyList<PackageConfiguration> packages, CancellationToken cancellationToken)
    {
        var results = new AcquiredPackage?[packages.Count];
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        Lazy<SourceContext> sources = new(CreateSourceContext, LazyThreadSafetyMode.ExecutionAndPublication);
        var tasks = packages.Select(async (package, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                results[index] = await AcquireAsync(package, sources, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return [.. results.Where(r => r is not null).Select(r => r!)];
    }

    private async Task<AcquiredPackage?> AcquireAsync(PackageConfiguration package, Lazy<SourceContext> sources, CancellationToken cancellationToken)
    {
        if (package.LocalPath is { } localPath)
        {
            return package.IsLocalFolder
                ? AcquireFromFolder(package, localPath)
                : Verify(package, expectedVersion: null, localPath, "local", reportCorruption: true);
        }

        var version = package.ExactVersion;
        if (version is null)
        {
            version = _offline ? ResolveFromCache(package) : await ResolveFromSourcesAsync(package, sources.Value, cancellationToken);
            if (version is null)
                return null;
        }

        var (entryDirectory, packagePath, metadataPath) = GetCacheEntry(package.Id, version);
        if (File.Exists(packagePath) && File.Exists(metadataPath))
        {
            var cacheEntry = ReadCacheEntry(metadataPath);
            if (cacheEntry is not null && string.Equals(ComputeSha512(packagePath), cacheEntry.Sha512, StringComparison.Ordinal))
            {
                var cached = Verify(package, version, packagePath, cacheEntry.Source, reportCorruption: false);
                if (cached is not null)
                    return cached;
            }

            // The entry is corrupt or incomplete: remove it and download the package again
            DeleteCacheEntry(entryDirectory);
            if (_offline)
            {
                Report(DiagnosticDescriptors.PackageIntegrity, $"The cached copy of {package.Id}@{version.ToNormalizedString()} is corrupt and was removed; it cannot be downloaded again in offline mode", package);
                return null;
            }
        }

        if (_offline)
        {
            Report(DiagnosticDescriptors.OfflineCacheMiss, $"{package.Identity} is not in the package cache and offline mode is enabled; run once without --offline to populate the cache", package);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await DownloadAsync(package, version, sources.Value, entryDirectory, packagePath, metadataPath, cancellationToken);
    }

    /// <summary>
    /// Selects the version to use for a range: the best match of a floating range (<c>*</c> is the latest stable
    /// version, <c>*-*</c> the latest version including prereleases), otherwise the highest version in the range.
    /// Prerelease versions only match a non-floating range when one of its bounds is a prerelease version.
    /// </summary>
    internal static NuGetVersion? SelectVersion(VersionRange range, IEnumerable<NuGetVersion> versions)
    {
        if (range.IsFloating)
            return range.FindBestMatch(versions);

        var allowPrerelease = range.MinVersion?.IsPrerelease == true || range.MaxVersion?.IsPrerelease == true;
        return versions.Where(v => range.Satisfies(v) && (allowPrerelease || !v.IsPrerelease)).Max();
    }

    private async Task<NuGetVersion?> ResolveFromSourcesAsync(PackageConfiguration package, SourceContext context, CancellationToken cancellationToken)
    {
        var candidates = context.GetSources(package, out var error);
        if (candidates.Count == 0)
        {
            Report(DiagnosticDescriptors.PackageNotFound, error ?? $"No package source is configured for {package.Identity}", package);
            return null;
        }

        // Like 'dotnet restore', a failing source is an error: a higher version may be hidden behind it
        var versions = new HashSet<NuGetVersion>();
        var failures = new List<string>();
        foreach (var source in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                    ?? throw new InvalidOperationException($"source '{source.Name}' does not support listing package versions");
                versions.UnionWith(await resource.GetAllVersionsAsync(package.Id, context.CacheContext, _logger, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is FatalProtocolException or HttpRequestException or IOException or InvalidOperationException)
            {
                failures.Add(DescribeFailure(source, ex));
            }
        }

        if (failures.Count > 0)
        {
            Report(DiagnosticDescriptors.PackageSourceFailure, $"Cannot list the versions of {package.Id}: {string.Join("; ", failures)}", package);
            return null;
        }

        var selected = SelectVersion(package.Version, versions);
        if (selected is null)
        {
            Report(DiagnosticDescriptors.PackageNotFound, $"No version of {package.Id} matching '{package.VersionText}' was found in {string.Join(", ", candidates.Select(s => $"'{s.Name}'"))}{DescribeExcludedPrereleases(package, versions)}", package);
            return null;
        }

        Report(DiagnosticDescriptors.PackageVersionResolved, $"{package.Identity} resolves to {selected.ToNormalizedString()}", package);
        return selected;
    }

    /// <summary>
    /// Uses the highest matching package of a local folder, in place. Like a NuGet local feed, only the .nupkg files
    /// at the root of the folder whose name starts with the package identifier are considered.
    /// </summary>
    private AcquiredPackage? AcquireFromFolder(PackageConfiguration package, FullPath folder)
    {
        var candidates = new List<(NuGetVersion Version, FullPath Path)>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.nupkg").Select(FullPath.FromPath).OrderBy(p => p.Value, StringComparer.Ordinal))
        {
            if (!file.Name.StartsWith(package.Id + ".", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var metadata = ReadMetadata(file);
                if (string.Equals(metadata.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add((metadata.Version, file));
                }
            }
            catch (Exception ex) when (IsInvalidPackageException(ex))
            {
                ReportInvalidPackage(package, $"'{file.Name}' is invalid: {ex.Message}", ex);
                return null;
            }
        }

        var versions = candidates.Select(c => c.Version).ToList();
        var selected = package.ExactVersion is { } exact ? versions.FirstOrDefault(v => v == exact) : SelectVersion(package.Version, versions);
        if (selected is null)
        {
            var message = package.ExactVersion is null
                ? $"No version of {package.Id} matching '{package.VersionText}' was found in the folder '{folder}'{DescribeExcludedPrereleases(package, versions)}"
                : $"{package.Identity} was not found in the folder '{folder}'";
            Report(DiagnosticDescriptors.PackageNotFound, message, package);
            return null;
        }

        var path = candidates.First(c => c.Version == selected).Path;
        if (package.ExactVersion is null)
        {
            Report(DiagnosticDescriptors.PackageVersionResolved, $"{package.Identity} resolves to {selected.ToNormalizedString()} ('{path.Name}')", package);
        }

        return Verify(package, selected, path, "local", reportCorruption: true);
    }

    private NuGetVersion? ResolveFromCache(PackageConfiguration package)
    {
        var versions = new List<NuGetVersion>();
        var packageDirectory = _cacheDirectory / package.Id.ToLowerInvariant();
        if (Directory.Exists(packageDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(packageDirectory))
            {
                if (NuGetVersion.TryParse(System.IO.Path.GetFileName(directory), out var version))
                {
                    var entry = GetCacheEntry(package.Id, version);
                    if (File.Exists(entry.PackagePath) && File.Exists(entry.MetadataPath))
                    {
                        versions.Add(version);
                    }
                }
            }
        }

        var selected = SelectVersion(package.Version, versions);
        if (selected is null)
        {
            Report(DiagnosticDescriptors.OfflineCacheMiss, $"No version of {package.Id} matching '{package.VersionText}' is in the package cache and offline mode is enabled; run once without --offline to populate the cache{DescribeExcludedPrereleases(package, versions)}", package);
            return null;
        }

        Report(DiagnosticDescriptors.PackageVersionResolved, $"{package.Identity} resolves to {selected.ToNormalizedString()} (offline: highest matching version in the package cache)", package);
        return selected;
    }

    private static string DescribeExcludedPrereleases(PackageConfiguration package, IEnumerable<NuGetVersion> versions)
    {
        return versions.Any(v => v.IsPrerelease && package.Version.Satisfies(v))
            ? "; prerelease versions are excluded, use a floating prerelease version such as *-* to include them"
            : "";
    }

    private (FullPath EntryDirectory, FullPath PackagePath, FullPath MetadataPath) GetCacheEntry(string id, NuGetVersion version)
    {
        var normalizedId = id.ToLowerInvariant();
        var normalizedVersion = version.ToNormalizedString().ToLowerInvariant();
        var entryDirectory = _cacheDirectory / normalizedId / normalizedVersion;
        return (entryDirectory, entryDirectory / (normalizedId + "." + normalizedVersion + ".nupkg"), entryDirectory / "docgen-cache.json");
    }

    private async Task<AcquiredPackage?> DownloadAsync(PackageConfiguration package, NuGetVersion version, SourceContext context, FullPath entryDirectory, FullPath packagePath, FullPath metadataPath, CancellationToken cancellationToken)
    {
        var candidates = context.GetSources(package, out var error);
        if (candidates.Count == 0)
        {
            Report(DiagnosticDescriptors.PackageNotFound, error ?? $"No package source is configured for {package.Identity}", package);
            return null;
        }

        var failures = new List<string>();
        foreach (var source in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(entryDirectory);
            var temporaryPath = entryDirectory / (Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                bool found;
                await using (var stream = File.Create(temporaryPath))
                {
                    var repository = Repository.Factory.GetCoreV3(source);
                    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                        ?? throw new InvalidOperationException($"source '{source.Name}' does not support package downloads");
                    found = await resource.CopyNupkgToStreamAsync(package.Id, version, stream, context.CacheContext, _logger, cancellationToken);
                }

                if (!found)
                    continue;

                var verified = Verify(package, version, temporaryPath, source.Name, reportCorruption: true);
                if (verified is null)
                    return null;

                // Publish the cache entry atomically: a concurrent or interrupted build never observes a partial file
                File.Move(temporaryPath, packagePath, overwrite: true);
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(new CacheEntry(verified.Sha512, source.Name)), cancellationToken);
                return new AcquiredPackage
                {
                    Configuration = package,
                    Path = packagePath,
                    Metadata = verified.Metadata,
                    Sha512 = verified.Sha512,
                    Source = source.Name,
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is FatalProtocolException or HttpRequestException or IOException or InvalidOperationException)
            {
                failures.Add(DescribeFailure(source, ex));
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        if (failures.Count > 0)
        {
            Report(DiagnosticDescriptors.PackageSourceFailure, $"Cannot download {package.Id}@{version.ToNormalizedString()}: {string.Join("; ", failures)}", package);
        }
        else
        {
            var hint = package.ExactVersion is null ? "" : ". The exact version is required; use a version range such as 1.* or * to accept other versions";
            Report(DiagnosticDescriptors.PackageNotFound, $"{package.Id}@{version.ToNormalizedString()} was not found in {string.Join(", ", candidates.Select(s => $"'{s.Name}'"))}{hint}", package);
        }

        return null;
    }

    private static string DescribeFailure(PackageSource source, Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } httpException)
            {
                return $"authentication failed for source '{source.Name}' ({(int)httpException.StatusCode.Value}); configure credentials in NuGet.Config or a credential provider";
            }
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return $"authentication failed for source '{source.Name}'; configure credentials in NuGet.Config or a credential provider";

        return $"source '{source.Name}' failed: {message}";
    }

    /// <summary>Verifies a package file. A local file has no <paramref name="expectedVersion"/>: its version must satisfy the configured range.</summary>
    private AcquiredPackage? Verify(PackageConfiguration package, NuGetVersion? expectedVersion, FullPath path, string source, bool reportCorruption)
    {
        try
        {
            var metadata = ReadMetadata(path);
            var versionMatches = expectedVersion is null ? package.Version.Satisfies(metadata.Version) : metadata.Version == expectedVersion;
            if (!string.Equals(metadata.Id, package.Id, StringComparison.OrdinalIgnoreCase) || !versionMatches)
            {
                Report(DiagnosticDescriptors.PackageIdentityMismatch, $"'{path.Name}' contains {metadata.Id}@{metadata.Version.ToNormalizedString()} but {package.Identity} is configured", package);
                return null;
            }

            return new AcquiredPackage
            {
                Configuration = package,
                Path = path,
                Metadata = metadata,
                Sha512 = ComputeSha512(path),
                Source = source,
            };
        }
        catch (Exception ex) when (IsInvalidPackageException(ex))
        {
            if (reportCorruption)
            {
                ReportInvalidPackage(package, $"{package.Id}@{expectedVersion?.ToNormalizedString() ?? package.VersionText} is invalid: {ex.Message}", ex);
            }

            return null;
        }
    }

    private static PackageMetadata ReadMetadata(FullPath path)
    {
        using var archive = PackageArchive.Open(path);
        var nuspecs = archive.Entries.Where(e => !e.Contains('/', StringComparison.Ordinal) && e.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToList();
        if (nuspecs.Count != 1)
            throw new PackageArchiveException($"the package must contain exactly one .nuspec file at its root (found {nuspecs.Count.ToString(CultureInfo.InvariantCulture)})");

        using var nuspecStream = archive.OpenRead(nuspecs[0], maxSize: 10 * 1024 * 1024);
        return PackageMetadata.Read(new NuspecReader(nuspecStream));
    }

    private static bool IsInvalidPackageException(Exception ex) => ex is PackageArchiveException or InvalidDataException or NuGet.Packaging.Core.PackagingException or System.Xml.XmlException or IOException;

    private void ReportInvalidPackage(PackageConfiguration package, string message, Exception ex)
    {
        var descriptor = ex is PackageArchiveException && ex.Message.Contains("unsafe", StringComparison.Ordinal) ? DiagnosticDescriptors.UnsafePackageEntry : DiagnosticDescriptors.PackageIntegrity;
        Report(descriptor, message, package);
    }

    internal static string ComputeSha512(FullPath path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToBase64String(SHA512.HashData(stream));
    }

    private static CacheEntry? ReadCacheEntry(FullPath path)
    {
        try
        {
            return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void DeleteCacheEntry(FullPath entryDirectory)
    {
        try
        {
            Directory.Delete(entryDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private SourceContext CreateSourceContext()
    {
        lock (CredentialServiceLock)
        {
            if (!s_credentialServiceInitialized)
            {
                // Credentials come from NuGet.Config and credential providers, as for 'dotnet restore'. Prompts are disabled.
                DefaultCredentialServiceUtility.SetupDefaultCredentialService(_logger, nonInteractive: true);
                s_credentialServiceInitialized = true;
            }
        }

        var settings = Settings.LoadDefaultSettings(_settingsDirectory);
        var sources = new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled).ToList();
        return new SourceContext(sources, PackageSourceMapping.GetPackageSourceMapping(settings));
    }

    private void Report(DiagnosticDescriptor descriptor, string message, PackageConfiguration package)
    {
        _diagnostics.Report(descriptor, message, new DiagnosticLocation
        {
            Package = package.Identity,
            JsonPath = package.JsonPath,
        });
    }

    private sealed record CacheEntry(string Sha512, string Source);

    private sealed class SourceContext(List<PackageSource> sources, PackageSourceMapping mapping)
    {
        public SourceCacheContext CacheContext { get; } = new() { NoCache = true, DirectDownload = true };

        public List<PackageSource> GetSources(PackageConfiguration package, out string? error)
        {
            error = null;
            if (package.Source is not null)
            {
                var configured = sources.FirstOrDefault(s => string.Equals(s.Name, package.Source, StringComparison.OrdinalIgnoreCase))
                    ?? sources.FirstOrDefault(s => string.Equals(s.Source.TrimEnd('/'), package.Source.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                if (configured is not null)
                    return [configured];

                if (Uri.TryCreate(package.Source, UriKind.Absolute, out _))
                    return [new PackageSource(package.Source, package.Source)];

                error = $"Package source '{package.Source}' is not defined in NuGet.Config";
                return [];
            }

            if (mapping.IsEnabled)
            {
                var names = mapping.GetConfiguredPackageSources(package.Id);
                var mapped = sources.Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (mapped.Count == 0)
                {
                    error = $"Package source mapping does not map {package.Id} to any source";
                }

                return mapped;
            }

            return sources;
        }
    }

    private sealed class NuGetLogger(Action<string>? log) : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            if (log is not null && message.Level >= LogLevel.Information)
            {
                log(Redaction.Redact(message.Message));
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
