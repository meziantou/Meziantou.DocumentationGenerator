using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.SourceLinks;

#pragma warning disable CA2000 // PDBs are owned by the returned ImplementationDebugInfo, which this loader disposes

/// <summary>
/// Finds the portable PDB of an implementation assembly: embedded in the assembly, next to it in the package, or on a
/// configured symbol server. A PDB is only used when its identifier matches the CodeView entry of the assembly; a matching
/// file name is not enough.
/// </summary>
internal sealed class DebugInfoLoader(IReadOnlyList<Uri> symbolServers, FullPath cacheDirectory, DiagnosticBag diagnostics) : IDisposable
{
    private const long MaxPdbSize = 256 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly List<IDisposable> _disposables = [];

    public async Task<ImplementationDebugInfo?> LoadAsync(PackageArchive archive, string assemblyEntry, string? pdbEntry, DiagnosticLocation location, CancellationToken cancellationToken)
    {
        var assemblyBytes = archive.ReadAllBytes(assemblyEntry);
        PortablePdb? pdb = null;
        Guid? expectedGuid = null;
        uint expectedStamp = 0;
        string? pdbFileName = null;
        using (var peReader = new PEReader(new MemoryStream(assemblyBytes)))
        {
            var entries = peReader.ReadDebugDirectory();
            var codeView = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView && e.IsPortableCodeView);
            if (codeView.Type == DebugDirectoryEntryType.CodeView)
            {
                var data = peReader.ReadCodeViewDebugDirectoryData(codeView);
                expectedGuid = data.Guid;
                expectedStamp = codeView.Stamp;
                pdbFileName = Path.GetFileName(data.Path.Replace('\\', '/'));
            }

            var embedded = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
            if (embedded.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                pdb = PortablePdb.TryCreate(peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embedded));
            }
        }

        if (expectedGuid is null)
        {
            pdb?.Dispose();
            diagnostics.Report(DiagnosticDescriptors.MissingDebugInformation, $"'{assemblyEntry}' has no portable debug information; no source link is generated", location);
            return null;
        }

        if (pdb is null && pdbEntry is not null && archive.GetLength(pdbEntry) <= MaxPdbSize)
        {
            pdb = PortablePdb.TryCreate(archive.ReadAllBytes(pdbEntry, MaxPdbSize));
        }

        if (pdb is null && symbolServers.Count > 0 && pdbFileName is not null)
        {
            pdb = await DownloadAsync(pdbFileName, expectedGuid.Value, location, cancellationToken);
        }

        if (pdb is null)
        {
            diagnostics.Report(DiagnosticDescriptors.MissingDebugInformation, $"No PDB found for '{assemblyEntry}'; no source link is generated", location);
            return null;
        }

        if (!pdb.Matches(expectedGuid.Value, expectedStamp))
        {
            pdb.Dispose();
            diagnostics.Report(DiagnosticDescriptors.MismatchedDebugInformation, $"The PDB of '{assemblyEntry}' does not match the assembly; no source link is generated", location);
            return null;
        }

        var map = pdb.GetSourceLinkMap();
        if (map is null)
        {
            diagnostics.Report(DiagnosticDescriptors.MissingDebugInformation, $"The PDB of '{assemblyEntry}' has no Source Link information; no source link is generated", location);
        }

        var assembly = PublicApi.ReadAssembly(new MemoryStream(assemblyBytes), new PublicApiReadOptions());
        var result = new ImplementationDebugInfo(assemblyEntry, assembly, pdb, map);
        _disposables.Add(result);
        return result;
    }

    /// <summary>Downloads a portable PDB using the Simple Symbol Query Protocol: <c>{server}/{name}/{guid}FFFFFFFF/{name}</c>.</summary>
    private async Task<PortablePdb?> DownloadAsync(string pdbFileName, Guid guid, DiagnosticLocation location, CancellationToken cancellationToken)
    {
        var name = pdbFileName.ToLowerInvariant();
        var key = $"{name}/{guid:N}FFFFFFFF/{name}";
        var cachePath = cacheDirectory / "symbols" / name / (guid.ToString("N") + "FFFFFFFF") / name;
        if (File.Exists(cachePath))
            return PortablePdb.TryCreate(await File.ReadAllBytesAsync(cachePath, cancellationToken));

        foreach (var server in symbolServers)
        {
            var url = new Uri(server.AbsoluteUri.TrimEnd('/') + "/" + key);
            try
            {
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPdbSize)
                    continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var pdb = PortablePdb.TryCreate(bytes);
                if (pdb is null)
                    continue;

                cachePath.CreateParentDirectory();
                await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
                return pdb;
            }
            catch (HttpRequestException ex)
            {
                diagnostics.Report(DiagnosticDescriptors.MissingDebugInformation, $"Symbol server '{server.Host}' failed: {ex.Message}", location);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Report(DiagnosticDescriptors.MissingDebugInformation, $"Symbol server '{server.Host}' timed out", location);
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
