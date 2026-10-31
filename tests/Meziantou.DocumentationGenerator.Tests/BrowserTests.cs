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
        "api/packages/contoso.library.html",
        "api/namespaces/Contoso.Library.html",
        "api/Contoso.Library.Client.html",
        "api/Contoso.Library.MessageKind.html",
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
        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/packages/contoso.library.html");
        Assert.Equal("dark", await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme')"));
        Assert.Equal("dark", await page.InputValueAsync("#theme-select"));

        await page.SelectOptionAsync("#theme-select", "system");
        await page.ReloadAsync();
        Assert.Null(await page.EvaluateAsync<string?>("document.documentElement.getAttribute('data-theme')"));
        Assert.Equal("rgb(255, 255, 255)", await GetBackgroundAsync(page));
    }

    [Fact]
    public async Task Breadcrumbs_UseMutedColorsInBothThemes()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.SelectOptionAsync("#theme-select", theme);
            Assert.Equal(theme == "light" ? "rgb(87, 96, 106)" : "rgb(145, 152, 161)",
                await page.Locator(".breadcrumbs a").First.EvaluateAsync<string>("e => getComputedStyle(e).color"));
            await page.Locator(".breadcrumbs a").First.HoverAsync();
            Assert.Contains("underline", await page.Locator(".breadcrumbs a").First.EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine"));
        }
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
        await page.ClickAsync(".sidebar-collapse-button");
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        await page.ClickAsync(".sidebar-collapse-button");
        Assert.True(await page.IsVisibleAsync("#sidebar"));
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
        Assert.False(await page.IsVisibleAsync(".sidebar-collapse-button"));
        await page.ClickAsync("#sidebar >> text=Guide");
        Assert.EndsWith("/guide.html", page.Url);
        Assert.Equal("Guide", await page.TextContentAsync("h1"));

        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html#Send(System.String)");
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

    [Theory]
    [InlineData("guide.html")]
    [InlineData("api/packages/contoso.library.html")]
    [InlineData("api/namespaces/Contoso.Library.html")]
    public async Task Search_KeyboardNavigation(string path)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.DocsServer.BaseUrl + path);

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
        Assert.Equal(fixture.DocsServer.BaseUrl + "api/Contoso.Library.Client.html", page.Url);
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
    public async Task Sidebar_FoldersAndPagesUseTheSameColorAndLargeChevrons()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl);

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.SelectOptionAsync("#theme-select", theme);
            Assert.True(await page.EvaluateAsync<bool>("""
                () => getComputedStyle(document.querySelector('#sidebar .nav-api-reference > a')).color ===
                      getComputedStyle(document.querySelector('#sidebar a[href$="guide.html"]')).color
                """));
        }

        var toggle = page.Locator(".nav-api-reference > details > .nav-toggle");
        var bounds = await toggle.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(bounds.Width >= 28 && bounds.Height >= 28);
        Assert.True(await toggle.EvaluateAsync<bool>("e => parseFloat(getComputedStyle(e, '::before').width) >= 8 && getComputedStyle(e, '::before').borderTopWidth === '2px'"));
        var details = page.Locator(".nav-api-reference > details");
        var wasOpen = await details.EvaluateAsync<bool>("e => e.open");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(!wasOpen, await details.EvaluateAsync<bool>("e => e.open"));
        await page.Keyboard.PressAsync("Space");
        Assert.Equal(wasOpen, await details.EvaluateAsync<bool>("e => e.open"));
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1600)]
    public async Task Sidebar_CollapsesToARailAndRemembersAcrossPages(int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = 900 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");

        Assert.False(await page.IsVisibleAsync(".menu-button"));
        Assert.True(await page.IsVisibleAsync("#sidebar"));
        var contentWidth = (await page.Locator("main").BoundingBoxAsync())!.Width;
        var button = page.Locator(".sidebar-collapse-button");
        Assert.Equal("true", await button.GetAttributeAsync("aria-expanded"));
        await button.ClickAsync();
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("false", await button.GetAttributeAsync("aria-expanded"));
        Assert.Equal("Expand table of contents", await button.GetAttributeAsync("aria-label"));
        // The content uses the freed space, up to its maximum width
        var contentMaxWidth = await page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--content-max-width')) * parseFloat(getComputedStyle(document.documentElement).fontSize)");
        Assert.True((await page.Locator("main").BoundingBoxAsync())!.Width >= Math.Min(contentWidth + 200, contentMaxWidth));
        Assert.Equal(48, (await page.Locator(".sidebar-panel").BoundingBoxAsync())!.Width);

        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html");
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.True(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("true", await button.GetAttributeAsync("aria-expanded"));
        Assert.Equal("Collapse table of contents", await button.GetAttributeAsync("aria-label"));
        Assert.True(await button.EvaluateAsync<bool>("e => document.activeElement === e"));
        await page.ReloadAsync();
        Assert.True(await page.IsVisibleAsync("#sidebar"));
    }

    [Fact]
    public async Task Sidebar_DesktopPreferenceDoesNotPreventTheMobileMenuOpening()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1600, Height = 900 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");
        await page.ClickAsync(".sidebar-collapse-button");

        await page.SetViewportSizeAsync(360, 740);
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        Assert.False(await page.IsVisibleAsync(".sidebar-collapse-button"));
        await page.ClickAsync(".menu-button");
        Assert.True(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("true", await page.GetAttributeAsync(".menu-button", "aria-expanded"));
        await page.Keyboard.PressAsync("Escape");
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("false", await page.GetAttributeAsync(".menu-button", "aria-expanded"));
        Assert.True(await page.Locator(".menu-button").EvaluateAsync<bool>("e => document.activeElement === e"));

        await page.ClickAsync(".menu-button");
        await page.SetViewportSizeAsync(1600, 900);
        Assert.False(await page.IsVisibleAsync("#sidebar"));
        Assert.Equal("false", await page.GetAttributeAsync(".sidebar-collapse-button", "aria-expanded"));
        await page.ClickAsync(".sidebar-collapse-button");
        Assert.True(await page.IsVisibleAsync("#sidebar"));
    }

    [Fact]
    public async Task Sidebar_ScrollsToTheCurrentPage()
    {
        // A short viewport makes the sidebar scroll; the API reference, and so the current type, is at its end
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 200 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html");

        var state = await page.EvaluateAsync<double[]>("""
            () => {
                const sidebar = document.getElementById('sidebar');
                const bounds = sidebar.getBoundingClientRect();
                const rect = sidebar.querySelector('[aria-current=page]').getBoundingClientRect();
                const visible = rect.top >= bounds.top && rect.bottom <= Math.min(bounds.bottom, window.innerHeight);
                return [sidebar.scrollTop, visible ? 1 : 0, window.scrollY];
            }
            """);
        Assert.True(state[0] > 0, "The sidebar did not scroll");
        Assert.Equal(1, state[1]);
        Assert.Equal(0, state[2]);
    }

    [Fact]
    public async Task NarrowScreens_NoHorizontalOverflowAndMenu()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 }, HasTouch = true });
        var page = await context.NewPageAsync();
        foreach (var path in RepresentativePages)
        {
            await page.GotoAsync(fixture.RootServer.BaseUrl + path);

            // Fonts differ between platforms: wider glyphs must not break the layout either
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = "* { letter-spacing: 0.08em !important; }" });
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
            var offenders = await page.EvaluateAsync<string[]>("""
                Array.from(document.querySelectorAll('body *'))
                    .filter(e => e.getBoundingClientRect().right > document.documentElement.clientWidth + 1 && !e.closest('pre, table, .search-results'))
                    .slice(0, 8)
                    .map(e => e.tagName.toLowerCase() + '.' + e.className + ' "' + (e.textContent || '').trim().slice(0, 40) + '"')
                """);
            Assert.True(overflow <= 0, $"'{path}' overflows horizontally by {overflow}px: {string.Join("; ", offenders)}");
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

        await page.Locator(".code-block > .copy-button").First.ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('live-status').textContent === 'Code copied to the clipboard.'");
        Assert.Equal("dotnet add package Contoso.Library", (await page.EvaluateAsync<string>("navigator.clipboard.readText()")).Trim());

        // The button stays at the right of the block when a long line is scrolled
        var moved = await page.EvaluateAsync<double>("""
            () => {
                const block = document.querySelector('.code-block');
                const pre = block.querySelector('pre');
                pre.querySelector('code').textContent += ' ' + 'x'.repeat(500);
                const before = block.querySelector('.copy-button').getBoundingClientRect().right;
                pre.scrollLeft = pre.scrollWidth;
                return Math.abs(block.querySelector('.copy-button').getBoundingClientRect().right - before) + (pre.scrollLeft > 0 ? 0 : 1000);
            }
            """);
        Assert.Equal(0, moved);

        await using var noClipboard = await fixture.Browser.NewContextAsync();
        await noClipboard.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { get() { return undefined; } });");
        var page2 = await noClipboard.NewPageAsync();
        await page2.GotoAsync(fixture.RootServer.BaseUrl + "guide.html");
        await page2.Locator(".code-block > .copy-button").First.ClickAsync();
        await page2.WaitForFunctionAsync("document.getElementById('live-status').textContent.includes('not available')");
    }

    [Fact]
    public async Task FrameworkFilter_HidesOtherFrameworks()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html");

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
        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html");

        var href = await page.Locator("[id='Send(System.String)'] .heading-anchor").GetAttributeAsync("href");
        await page.GotoAsync(fixture.RootServer.BaseUrl + "api/Contoso.Library.Client.html" + href);
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

        Assert.All(requests, url => Assert.StartsWith(fixture.DocsServer.BaseUrl, url));
        Assert.DoesNotContain(requests, url => url.Contains("_framework", StringComparison.Ordinal) || url.EndsWith(".wasm", StringComparison.Ordinal) || url.EndsWith(".dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotFound_AtADeeplyNestedUrl()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(fixture.DocsServer.BaseUrl + "a/b/c/missing-page.html");

        Assert.Equal(404, response!.Status);
        Assert.Equal(fixture.DocsServer.BaseUrl + "404.html", page.Url);
        Assert.Equal("Page not found", await page.TextContentAsync("h1"));
        await page.ClickAsync("main >> text=Go to the home page");
        Assert.Equal(fixture.DocsServer.BaseUrl, page.Url);
    }

    private static Task<string> GetBackgroundAsync(IPage page) => page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
}
