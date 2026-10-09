using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Packs the tool and installs it once in a temporary local tool manifest.</summary>
public sealed class InstalledToolFixture : IAsyncLifetime
{
    private TemporaryDirectory? _directory;

    internal InstalledTool Tool { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _directory = TemporaryDirectory.Create();
        Tool = await InstalledTool.CreateAsync(_directory.FullPath, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_directory is not null)
        {
            await _directory.DisposeAsync();
        }
    }
}
