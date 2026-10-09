using System.Text;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>
/// A temporary directory next to the output directory where the site is generated before validation. Being on the same
/// volume as the output lets publication use renames. Only this directory is deleted when the build ends.
/// </summary>
internal sealed class OutputStaging : IDisposable
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    private OutputStaging(FullPath root)
    {
        Root = root;
    }

    public FullPath Root { get; }

    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_lock)
            {
                return [.. _files.Order(StringComparer.Ordinal)];
            }
        }
    }

    public static OutputStaging Create(FullPath outputDirectory)
    {
        var parent = outputDirectory.Parent;
        Directory.CreateDirectory(parent);
        var root = parent / ("." + outputDirectory.Name + ".staging-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return new OutputStaging(root);
    }

    public void WriteText(string relativePath, string content) => WriteBytes(relativePath, Encoding.UTF8.GetBytes(content));

    public void WriteBytes(string relativePath, byte[] content)
    {
        var path = Reserve(relativePath);
        File.WriteAllBytes(path, content);
    }

    public void CopyFile(string relativePath, FullPath source)
    {
        var path = Reserve(relativePath);
        File.Copy(source, path, overwrite: false);
    }

    public FullPath GetPath(string relativePath) => Root / relativePath;

    private FullPath Reserve(string relativePath)
    {
        if (!LinkService.IsSafeRelativePath(relativePath))
            throw new InvalidOperationException($"'{relativePath}' is not a safe output path");

        var path = Root / relativePath;
        if (!path.IsChildOf(Root))
            throw new InvalidOperationException($"'{relativePath}' is outside the output directory");

        lock (_lock)
        {
            if (!_files.Add(relativePath))
                throw new InvalidOperationException($"'{relativePath}' is generated more than once");
        }

        path.CreateParentDirectory();
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
