using Microsoft.Playwright;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Starts Chromium once for the browser tests, and builds a representative site served at '/' and '/docs/'.</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private SiteBuilder? _rootSite;
    private SiteBuilder? _docsSite;

    public IBrowser Browser { get; private set; } = null!;
    internal StaticSiteServer RootServer { get; private set; } = null!;
    internal StaticSiteServer DocsServer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Downloads Chromium when it is not installed yet; CI installs it before running the tests
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright cannot install Chromium (exit code {exitCode})");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        _rootSite = await UiSite.BuildAsync([]);
        _docsSite = await UiSite.BuildAsync(["--base-path", "/docs/"]);
        RootServer = await StaticSiteServer.StartAsync(_rootSite.Output, "/");
        DocsServer = await StaticSiteServer.StartAsync(_docsSite.Output, "/docs/");
    }

    public async ValueTask DisposeAsync()
    {
        if (RootServer is not null)
        {
            await RootServer.DisposeAsync();
        }

        if (DocsServer is not null)
        {
            await DocsServer.DisposeAsync();
        }

        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_rootSite is not null)
        {
            await _rootSite.DisposeAsync();
        }

        if (_docsSite is not null)
        {
            await _docsSite.DisposeAsync();
        }
    }
}
