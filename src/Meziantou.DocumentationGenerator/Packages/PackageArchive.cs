using System.IO.Compression;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Packages;

/// <summary>
/// Read-only view of a package archive. Entries are read directly from the ZIP file and never extracted to disk.
/// Every entry name is validated when the archive is opened: rooted paths, '..' segments, symbolic links, duplicate
/// names and excessive sizes are rejected so that no entry can be used to escape a directory or exhaust resources.
/// </summary>
internal sealed class PackageArchive : IDisposable
{
    public const int MaxEntryCount = 50_000;
    public const long MaxEntrySize = 512L * 1024 * 1024;
    public const long MaxTotalSize = 4L * 1024 * 1024 * 1024;

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    private PackageArchive(ZipArchive archive, Dictionary<string, ZipArchiveEntry> entries)
    {
        _archive = archive;
        _entries = entries;
        Entries = [.. entries.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>Normalized entry paths, using '/' separators, sorted ordinally.</summary>
    public IReadOnlyList<string> Entries { get; }

    public static PackageArchive Open(FullPath path)
    {
        FileStream? stream = null;
        ZipArchive? archive = null;
        try
        {
            stream = File.OpenRead(path);
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            stream = null;
            var entries = Validate(archive);
            var result = new PackageArchive(archive, entries);
            archive = null;
            return result;
        }
        catch (InvalidDataException ex)
        {
            throw new PackageArchiveException($"'{path.Name}' is not a valid package archive: {ex.Message}", ex);
        }
        finally
        {
            archive?.Dispose();
            stream?.Dispose();
        }
    }

    public bool Contains(string path) => _entries.ContainsKey(path);

    /// <summary>Returns the entry with the given path, compared case-insensitively like NuGet does.</summary>
    public string? FindEntry(string path)
    {
        if (_entries.ContainsKey(path))
            return path;

        return Entries.FirstOrDefault(e => string.Equals(e, path, StringComparison.OrdinalIgnoreCase));
    }

    public long GetLength(string path) => _entries[path].Length;

    public byte[] ReadAllBytes(string path, long maxSize = MaxEntrySize)
    {
        var entry = _entries[path];
        if (entry.Length > maxSize)
            throw new PackageArchiveException($"Entry '{path}' is too large ({entry.Length} bytes)");

        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)entry.Length));
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            total += read;

            // The size stored in the archive cannot be trusted
            if (total > maxSize || total > entry.Length)
                throw new PackageArchiveException($"Entry '{path}' is larger than declared");

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    public MemoryStream OpenRead(string path, long maxSize = MaxEntrySize) => new(ReadAllBytes(path, maxSize), writable: false);

    public void Dispose() => _archive.Dispose();

    /// <summary>Converts a ZIP entry name to a normalized relative path, or returns an error describing why it is unsafe.</summary>
    internal static string? NormalizeEntryName(string name, out string? error)
    {
        error = null;
        var path = name.Replace('\\', '/');
        try
        {
            // NuGet escapes some characters in entry names, such as '+' (%2B)
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
        }

        if (path.Length == 0)
        {
            error = "empty entry name";
            return null;
        }

        if (path.Any(char.IsControl))
        {
            error = "the entry name contains control characters";
            return null;
        }

        if (path[0] == '/' || (path.Length >= 2 && path[1] == ':') || path.StartsWith("//", StringComparison.Ordinal))
        {
            error = "rooted paths are not allowed";
            return null;
        }

        var segments = path.Split('/');
        if (segments.Any(s => s is ".." or "."))
        {
            error = "'.' and '..' segments are not allowed";
            return null;
        }

        if (segments.Take(segments.Length - 1).Any(s => s.Length == 0))
        {
            error = "empty path segments are not allowed";
            return null;
        }

        return path;
    }

    private static Dictionary<string, ZipArchiveEntry> Validate(ZipArchive archive)
    {
        var errors = new List<string>();
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;
        if (archive.Entries.Count > MaxEntryCount)
            throw new PackageArchiveException($"The package contains too many entries ({archive.Entries.Count}); the limit is {MaxEntryCount}");

        foreach (var entry in archive.Entries)
        {
            var path = NormalizeEntryName(entry.FullName, out var error);
            if (path is null)
            {
                errors.Add($"'{entry.FullName}': {error}");
                continue;
            }

            // Unix symbolic links store S_IFLNK in the high bits of the external attributes
            const int UnixFileTypeMask = 0xF000;
            const int UnixSymbolicLink = 0xA000;
            if (((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink)
            {
                errors.Add($"'{entry.FullName}': symbolic links are not allowed");
                continue;
            }

            if (path[^1] == '/')
                continue;

            if (!names.Add(path))
            {
                errors.Add($"'{entry.FullName}': duplicate entry");
                continue;
            }

            if (entry.Length > MaxEntrySize)
            {
                errors.Add($"'{entry.FullName}': the entry is too large ({entry.Length} bytes)");
                continue;
            }

            totalSize += entry.Length;
            entries.Add(path, entry);
        }

        if (totalSize > MaxTotalSize)
        {
            errors.Add($"the uncompressed size of the package ({totalSize} bytes) exceeds the limit");
        }

        if (errors.Count > 0)
            throw new PackageArchiveException("The package contains unsafe entries: " + string.Join("; ", errors));

        return entries;
    }
}
