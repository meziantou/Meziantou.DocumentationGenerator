using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Microsoft.Playwright;

namespace Meziantou.DocumentationGenerator.Tests;

[Collection(BrowserCollection.Name)]
public sealed class BrowserTests(BrowserFixture fixture)
{
    private static readonly string[] RepresentativePages =
    [
        "",
        "guide.html",
        "contoso.library/",
        "contoso.library/api/",
        "contoso.library/api/namespaces/Contoso.Library.html",
        "contoso.library/api/Contoso.Library.Client.html",
        "contoso.library/api/Contoso.Library.MessageKind.html",
    ];

    [Fact]
    public async Task Theme_FollowsSystemAndPersistsSelection()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ColorScheme = ColorScheme.Light });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");
        Assert.Equal("rgb(255, 255, 255)", await GetBackgroundAsync(page));

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
        Assert.Equal("rgb(13, 17, 23)", await GetBackgroundAsync(page));

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Light });
        await page.SelectOptionAsync("#theme-select", "dark");
        Assert.Equal("rgb(13, 17, 23)", await GetBackgroundAsync(page));

        // The explicit choice survives navigation and is applied before the stylesheet is used
        await page.GotoAsync(fixture.RootServer.BaseUrl + "contoso.library/");
        Assert.Equal("dark", await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme')"));
        Assert.Equal("dark", await page.InputValueAsync("#theme-select"));

        await page.SelectOptionAsync("#theme-select", "system");
        await page.ReloadAsync();
        Assert.Null(await page.EvaluateAsync<string?>("document.documentElement.getAttribute('data-theme')"));
        Assert.Equal("rgb(255, 255, 255)", await GetBackgroundAsync(page));
    }

    [Fact]
    public async Task BlockedStorage_DoesNotBreakThePage()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.AddInitScriptAsync("Object.defineProperty(window, 'localStorage', { get() { throw new Error('blocked'); } });");
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        await page.SelectOptionAsync("#theme-select", "dark");
        Assert.Equal("dark", await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme')"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task JavaScriptDisabled_ContentAndNavigationWork()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 375, Height = 800 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl);

        Assert.True(await page.IsVisibleAsync("#sidebar"));
        Assert.False(await page.IsVisibleAsync("[data-search]"));
        Assert.False(await page.IsVisibleAsync(".menu-button"));
        await page.ClickAsync("#sidebar >> text=Guide");
        Assert.EndsWith("/guide.html", page.Url);
        Assert.Equal("Guide", await page.TextContentAsync("h1"));

        await page.GotoAsync(fixture.RootServer.BaseUrl + "contoso.library/api/Contoso.Library.Client.html#Send(System.String)");
        Assert.True(await page.IsVisibleAsync("[id='Send(System.String)']"));
        Assert.Equal(2, await page.Locator("[id='Name'] .declaration-variant, [id='Send(System.ReadOnlySpan{System.Byte})'] .declaration-variant").CountAsync());
    }

    [Fact]
    public async Task ReducedMotion_DisablesSmoothScrolling()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        Assert.Equal("auto", await page.EvaluateAsync<string>("getComputedStyle(document.documentElement).scrollBehavior"));
    }

    [Fact]
    public async Task Keyboard_SkipLinkAndVisibleFocus()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("Skip to main content", await page.EvaluateAsync<string>("document.activeElement.textContent"));
        Assert.True(await page.IsVisibleAsync(".skip-link"));
        Assert.NotEqual("none", await page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle"));

        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("main", await page.EvaluateAsync<string>("document.activeElement.id"));

        // The next focusable elements follow the visual order: header, sidebar, content
        await page.Keyboard.PressAsync("Tab");
        var focused = await page.EvaluateAsync<string>("document.activeElement.closest('main') ? 'main' : 'other'");
        Assert.Equal("main", focused);
    }

    [Fact]
    public async Task Search_KeyboardNavigation()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.DocsServer.BaseUrl + "guide.html");

        await page.FocusAsync("#search-input");
        await page.Keyboard.TypeAsync("Client");
        await page.WaitForSelectorAsync("#search-results [role=option]");
        Assert.Equal("Client", await page.Locator("#search-results [role=option] .search-title").First.TextContentAsync());
        Assert.Contains("results", await page.TextContentAsync("#search-status"));
        Assert.Equal("true", await page.GetAttributeAsync("#search-input", "aria-expanded"));

        await page.Keyboard.PressAsync("ArrowDown");
        Assert.Equal("search-result-0", await page.GetAttributeAsync("#search-input", "aria-activedescendant"));
        var navigation = page.WaitForURLAsync(url => url.EndsWith("/Contoso.Library.Client.html", StringComparison.Ordinal));
        await page.Keyboard.PressAsync("Enter");
        await navigation;
        Assert.Equal(fixture.DocsServer.BaseUrl + "contoso.library/api/Contoso.Library.Client.html", page.Url);
    }

    [Fact]
    public async Task Search_HostileQueryAndNoResults()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var dialogs = 0;
        page.Dialog += (_, dialog) => { dialogs++; _ = dialog.DismissAsync(); };
        await page.GotoAsync(fixture.RootServer.BaseUrl);

        await page.FillAsync("#search-input", "<img src=x onerror=alert(1)>");
        await page.WaitForFunctionAsync("document.getElementById('search-status').textContent.startsWith('No results')");
        Assert.Contains("<img src=x onerror=alert(1)>", await page.TextContentAsync("#search-status"));
        Assert.Equal(0, await page.Locator("#search-status img").CountAsync());
        Assert.Equal(0, dialogs);
    }

    [Fact]
    public async Task Search_IndexFailureIsVisible()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.RouteAsync("**/search-index.json", route => route.FulfillAsync(new RouteFulfillOptions { Status = 500 }));
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl);

        await page.FillAsync("#search-input", "Client");
        await page.WaitForFunctionAsync("document.getElementById('search-status').textContent.includes('cannot be loaded')");
        Assert.Equal(0, await page.Locator("#search-results [role=option]").CountAsync());
    }

    [Fact]
    public async Task NarrowScreens_NoHorizontalOverflowAndMenu()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 }, HasTouch = true });
        var page = await context.NewPageAsync();
        foreach (var path in RepresentativePages)
        {
            await page.GotoAsync(fixture.RootServer.BaseUrl + path);
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.True(overflow <= 0, $"'{path}' overflows horizontally by {overflow}px");
        }

        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        await page.ClickAsync(".menu-button");
        Assert.True(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("true", await page.GetAttributeAsync(".menu-button", "aria-expanded"));
        Assert.True(await page.IsVisibleAsync("#search-input"));
    }

    [Fact]
    public async Task CopyButtons_AnnounceTheResult()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new BrowserContextGrantPermissionsOptions { Origin = fixture.RootServer.Origin });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        await page.Locator("pre .copy-button").First.ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('live-status').textContent === 'Code copied to the clipboard.'");
        Assert.Equal("dotnet add package Contoso.Library", (await page.EvaluateAsync<string>("navigator.clipboard.readText()")).Trim());

        await using var noClipboard = await fixture.Browser.NewContextAsync();
        await noClipboard.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { get() { return undefined; } });");
        var page2 = await noClipboard.NewPageAsync();
        await page2.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");
        await page2.Locator("pre .copy-button").First.ClickAsync();
        await page2.WaitForFunctionAsync("document.getElementById('live-status').textContent.includes('not available')");
    }

    [Fact]
    public async Task FrameworkFilter_HidesOtherFrameworks()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "contoso.library/api/Contoso.Library.Client.html");

        var span = page.Locator("[id='Send(System.ReadOnlySpan{System.Byte})']");
        Assert.True(await span.IsVisibleAsync());
        await page.SelectOptionAsync("[data-tfm-filter] select", "netstandard2.0");
        Assert.False(await span.IsVisibleAsync());
        Assert.EndsWith("Contoso.Library.Client.html", page.Url);
        await page.SelectOptionAsync("[data-tfm-filter] select", "");
        Assert.True(await span.IsVisibleAsync());
    }

    [Fact]
    public async Task Permalinks()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "contoso.library/api/Contoso.Library.Client.html");

        var href = await page.Locator("[id='Send(System.String)'] .heading-anchor").GetAttributeAsync("href");
        await page.GotoAsync(fixture.RootServer.BaseUrl + "contoso.library/api/Contoso.Library.Client.html" + href);
        Assert.Equal("#Send(System.String)", href);
        Assert.True(await page.EvaluateAsync<bool>("(() => { const top = document.getElementById('Send(System.String)').getBoundingClientRect().top; return top >= 0 && top < window.innerHeight; })()"));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Accessibility_NoSeriousViolations(string colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ColorScheme = colorScheme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
        var page = await context.NewPageAsync();
        foreach (var path in RepresentativePages)
        {
            await page.GotoAsync(fixture.RootServer.BaseUrl + path);
            var results = await page.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } });
            var violations = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();
            Assert.Empty(violations.Select(v => $"'{path}' ({colorScheme}) {v.Id}: {v.Help} ({string.Join(", ", v.Nodes.Take(3).Select(n => n.Html))})"));
        }
    }

    [Fact]
    public async Task NoExternalRequestsAndNoDotNetRuntime()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var requests = new List<string>();
        context.Request += (_, request) => requests.Add(request.Url);
        var page = await context.NewPageAsync();
        foreach (var path in RepresentativePages)
        {
            await page.GotoAsync(fixture.DocsServer.BaseUrl + path);
        }

        await page.FillAsync("#search-input", "Client");
        await page.WaitForSelectorAsync("#search-results [role=option]");

        Assert.All(requests, url => Assert.StartsWith(fixture.DocsServer.Origin, url));
        Assert.DoesNotContain(requests, url => url.Contains("_framework", StringComparison.Ordinal) || url.EndsWith(".wasm", StringComparison.Ordinal) || url.EndsWith(".dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotFound_AtADeeplyNestedUrl()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(fixture.DocsServer.BaseUrl + "a/b/c/missing-page.html");

        Assert.Equal(404, response!.Status);
        Assert.Equal("Page not found", await page.TextContentAsync("h1"));
        await page.ClickAsync("main >> text=Go to the home page");
        Assert.Equal(fixture.DocsServer.BaseUrl, page.Url);
    }

    private static Task<string> GetBackgroundAsync(IPage page) => page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
}
