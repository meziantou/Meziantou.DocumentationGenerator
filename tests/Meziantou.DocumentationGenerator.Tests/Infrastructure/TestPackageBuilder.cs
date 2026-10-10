using System.IO.Compression;
using System.Text;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Creates .nupkg files for tests.</summary>
internal sealed class TestPackageBuilder(string id, string version)
{
    private readonly List<(string Name, byte[] Content, int? UnixMode)> _entries = [];
    private string? _nuspecMetadata;
    private bool _reverseEntries;

    public string Id { get; } = id;
    public string Version { get; } = version;

    public TestPackageBuilder WithNuspecMetadata(string xml)
    {
        _nuspecMetadata = xml;
        return this;
    }

    /// <summary>Writes the archive entries in reverse order, to check that the output does not depend on it.</summary>
    public TestPackageBuilder WithReversedEntries()
    {
        _reverseEntries = true;
        return this;
    }

    public TestPackageBuilder AddFile(string name, string content) => AddFile(name, Encoding.UTF8.GetBytes(content));

    public TestPackageBuilder AddFile(string name, byte[] content)
    {
        _entries.Add((name, content, null));
        return this;
    }

    public TestPackageBuilder AddFile(string name, FullPath path) => AddFile(name, File.ReadAllBytes(path));

    public TestPackageBuilder AddSymbolicLink(string name, string target)
    {
        _entries.Add((name, Encoding.UTF8.GetBytes(target), 0xA1FF));
        return this;
    }

    public FullPath Save(FullPath directory, string? fileName = null)
    {
        Directory.CreateDirectory(directory);
        var path = directory / (fileName ?? $"{Id}.{Version}.nupkg".ToLowerInvariant());
        File.WriteAllBytes(path, ToBytes());
        return path;
    }

    public byte[] ToBytes()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nuspec = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{Id}</id>
                    <version>{Version}</version>
                    <authors>Tests</authors>
                    <description>Description of {Id}</description>
                    {_nuspecMetadata}
                  </metadata>
                </package>
                """;
            var entries = new List<(string Name, byte[] Content, int? UnixMode)> { (Id + ".nuspec", Encoding.UTF8.GetBytes(nuspec), null) };
            entries.AddRange(_entries);
            if (_reverseEntries)
            {
                entries.Reverse();
            }

            foreach (var (name, content, unixMode) in entries)
            {
                Write(archive, name, content, unixMode);
            }
        }

        return memory.ToArray();
    }

    private static void Write(ZipArchive archive, string name, byte[] content, int? unixMode)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (unixMode is not null)
        {
            entry.ExternalAttributes = unixMode.Value << 16;
        }

        using var stream = entry.Open();
        stream.Write(content);
    }
}
