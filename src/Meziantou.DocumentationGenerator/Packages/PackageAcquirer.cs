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

namespace Meziantou.DocumentationGenerator.Packages;

/// <summary>
/// Acquires the configured packages: local .nupkg files are used in place, remote packages are downloaded from the
/// sources defined in NuGet.Config (honoring source mapping and credentials) into a verified cache.
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
            return Verify(package, localPath, "local", reportCorruption: true);

        var entryDirectory = _cacheDirectory / package.Id.ToLowerInvariant() / package.Version.ToNormalizedString().ToLowerInvariant();
        var packagePath = entryDirectory / (package.Id.ToLowerInvariant() + "." + package.Version.ToNormalizedString().ToLowerInvariant() + ".nupkg");
        var metadataPath = entryDirectory / "docgen-cache.json";
        if (File.Exists(packagePath) && File.Exists(metadataPath))
        {
            var cacheEntry = ReadCacheEntry(metadataPath);
            if (cacheEntry is not null && string.Equals(ComputeSha512(packagePath), cacheEntry.Sha512, StringComparison.Ordinal))
            {
                var cached = Verify(package, packagePath, cacheEntry.Source, reportCorruption: false);
                if (cached is not null)
                    return cached;
            }

            // The entry is corrupt or incomplete: remove it and download the package again
            DeleteCacheEntry(entryDirectory);
            if (_offline)
            {
                Report(DiagnosticDescriptors.PackageIntegrity, $"The cached copy of {package.Identity} is corrupt and was removed; it cannot be downloaded again in offline mode", package);
                return null;
            }
        }

        if (_offline)
        {
            Report(DiagnosticDescriptors.OfflineCacheMiss, $"{package.Identity} is not in the package cache and offline mode is enabled; run once without --offline to populate the cache", package);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await DownloadAsync(package, sources.Value, entryDirectory, packagePath, metadataPath, cancellationToken);
    }

    private async Task<AcquiredPackage?> DownloadAsync(PackageConfiguration package, SourceContext context, FullPath entryDirectory, FullPath packagePath, FullPath metadataPath, CancellationToken cancellationToken)
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
                    found = await resource.CopyNupkgToStreamAsync(package.Id, package.Version, stream, context.CacheContext, _logger, cancellationToken);
                }

                if (!found)
                    continue;

                var verified = Verify(package, temporaryPath, source.Name, reportCorruption: true);
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
            Report(DiagnosticDescriptors.PackageSourceFailure, $"Cannot download {package.Identity}: {string.Join("; ", failures)}", package);
        }
        else
        {
            Report(DiagnosticDescriptors.PackageNotFound, $"{package.Identity} was not found in {string.Join(", ", candidates.Select(s => $"'{s.Name}'"))}. The exact version is required; no other version or source is used", package);
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

    private AcquiredPackage? Verify(PackageConfiguration package, FullPath path, string source, bool reportCorruption)
    {
        try
        {
            using var archive = PackageArchive.Open(path);
            var nuspecs = archive.Entries.Where(e => !e.Contains('/', StringComparison.Ordinal) && e.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToList();
            if (nuspecs.Count != 1)
                throw new PackageArchiveException($"the package must contain exactly one .nuspec file at its root (found {nuspecs.Count.ToString(CultureInfo.InvariantCulture)})");

            using var nuspecStream = archive.OpenRead(nuspecs[0], maxSize: 10 * 1024 * 1024);
            var metadata = PackageMetadata.Read(new NuspecReader(nuspecStream));
            if (!string.Equals(metadata.Id, package.Id, StringComparison.OrdinalIgnoreCase) || metadata.Version != package.Version)
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
        catch (Exception ex) when (ex is PackageArchiveException or InvalidDataException or NuGet.Packaging.Core.PackagingException or System.Xml.XmlException or IOException)
        {
            if (reportCorruption)
            {
                var descriptor = ex is PackageArchiveException && ex.Message.Contains("unsafe", StringComparison.Ordinal) ? DiagnosticDescriptors.UnsafePackageEntry : DiagnosticDescriptors.PackageIntegrity;
                Report(descriptor, $"{package.Identity} is invalid: {ex.Message}", package);
            }

            return null;
        }
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
