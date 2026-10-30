using System.Threading.Channels;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Preview;

/// <summary>
/// Watches the inputs of the build (configuration file, content directory, local packages and site images) and reports
/// changes once the file system has been quiet for a short time, so that saving several files triggers a single build.
/// </summary>
internal sealed class InputWatcher : IDisposable
{
    private static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(300);

    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>();
    private readonly TimeSpan _quietPeriod;
    private readonly List<FileSystemWatcher> _watchers = [];
    private HashSet<FullPath> _files = [];
    private HashSet<FullPath> _directories = [];

    public InputWatcher(TimeSpan? quietPeriod = null)
    {
        _quietPeriod = quietPeriod ?? DefaultQuietPeriod;
    }

    /// <summary>Returns the files and directories read by a build that uses this configuration.</summary>
    public static (IReadOnlyList<FullPath> Files, IReadOnlyList<FullPath> Directories) GetInputs(FullPath configurationFile, SiteConfiguration? configuration)
    {
        var files = new List<FullPath> { configurationFile };
        var directories = new List<FullPath>();
        if (configuration is not null)
        {
            if (configuration.Content is { } content)
            {
                directories.Add(content.Root);
            }

            foreach (var path in new[] { configuration.Site.Logo, configuration.Site.Favicon, configuration.Site.SocialImage })
            {
                if (path is { } value)
                {
                    files.Add(value);
                }
            }

            foreach (var package in configuration.Packages.Concat(configuration.References))
            {
                if (package.LocalPath is { } localPath)
                {
                    if (package.IsLocalFolder)
                    {
                        directories.Add(localPath);
                    }
                    else
                    {
                        files.Add(localPath);
                    }
                }
            }
        }

        return (files, directories);
    }

    /// <summary>Replaces the watched inputs. Watchers are only recreated when the inputs change.</summary>
    public void Watch(IEnumerable<FullPath> files, IEnumerable<FullPath> directories)
    {
        var newDirectories = directories.ToHashSet();
        var newFiles = files.Where(f => !newDirectories.Any(d => f.IsChildOf(d))).ToHashSet();
        if (newFiles.SetEquals(_files) && newDirectories.SetEquals(_directories))
            return;

        DisposeWatchers();
        _files = newFiles;
        _directories = newDirectories;

        // A directory nested in another watched directory is already covered
        foreach (var directory in newDirectories.Where(d => !newDirectories.Any(other => other != d && d.IsChildOf(other))))
        {
            if (Directory.Exists(directory))
            {
                AddWatcher(directory, includeSubdirectories: true);
            }
        }

        // Files are watched through their parent directory so that files replaced by a rename are detected
        foreach (var parent in newFiles.Select(f => f.Parent).Distinct())
        {
            if (Directory.Exists(parent) && !newDirectories.Any(d => parent == d || parent.IsChildOf(d)))
            {
                AddWatcher(parent, includeSubdirectories: false);
            }
        }
    }

    /// <summary>Waits for at least one change and returns the changed paths once no change occurred during the quiet period.</summary>
    public async Task<IReadOnlyList<string>> WaitForChangesAsync(CancellationToken cancellationToken)
    {
        var changes = new List<string> { await _changes.Reader.ReadAsync(cancellationToken) };
        while (true)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            quiet.CancelAfter(_quietPeriod);
            try
            {
                changes.Add(await _changes.Reader.ReadAsync(quiet.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        while (_changes.Reader.TryRead(out var change))
        {
            changes.Add(change);
        }

        return [.. changes.Distinct(StringComparer.Ordinal)];
    }

    private void AddWatcher(FullPath directory, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        // Names are relative to the watched directory. Using them instead of the full paths keeps the paths comparable
        // when the directory is reached through a symbolic link (e.g. /var and /private/var on macOS).
        watcher.Changed += (_, e) => OnChanged(directory, e.Name);
        watcher.Created += (_, e) => OnChanged(directory, e.Name);
        watcher.Deleted += (_, e) => OnChanged(directory, e.Name);
        watcher.Renamed += (_, e) =>
        {
            OnChanged(directory, e.OldName);
            OnChanged(directory, e.Name);
        };

        // Events may have been lost: rebuild to be safe
        watcher.Error += (_, _) => _changes.Writer.TryWrite(directory.Value);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void OnChanged(FullPath directory, string? name)
    {
        if (string.IsNullOrEmpty(name) || IsTemporaryFile(Path.GetFileName(name)))
            return;

        var fullPath = directory / name;
        if (_files.Contains(fullPath) || _directories.Any(d => fullPath.IsChildOf(d) || fullPath == d))
        {
            _changes.Writer.TryWrite(fullPath.Value);
        }
    }

    // Files created by editors while saving
    private static bool IsTemporaryFile(string name)
    {
        return name.EndsWith('~', StringComparison.Ordinal)
            || name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".swx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name is ".DS_Store" or "4913";
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void Dispose()
    {
        DisposeWatchers();
        _changes.Writer.TryComplete();
    }
}
