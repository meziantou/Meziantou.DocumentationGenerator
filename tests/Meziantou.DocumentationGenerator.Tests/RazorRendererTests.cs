using Meziantou.DocumentationGenerator.Components;
using Meziantou.DocumentationGenerator.Rendering;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class RazorRendererTests
{
    [Fact]
    public async Task RenderComponentWithoutWebHost()
    {
        await using var renderer = new RazorRenderer();
        var html = await renderer.RenderAsync<HtmlDocument>(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(HtmlDocument.Title)] = "<script>alert(1)</script>",
        });

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt;</title>", html);
        Assert.DoesNotContain("blazor", html, ignoreCase: true);
        Assert.DoesNotContain("_framework", html);
    }
}
