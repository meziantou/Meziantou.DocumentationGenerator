using Meziantou.DocumentationGenerator.Components;
using Meziantou.DocumentationGenerator.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class RazorRendererTests
{
    [Fact]
    public async Task ReleasesPageComponentsAfterEachRender()
    {
        await using var renderer = new RazorRenderer();
        for (var i = 0; i < 3; i++)
        {
            var disposed = false;
            var html = await renderer.RenderAsync<DisposablePage>(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(DisposablePage.OnDispose)] = (Action)(() => disposed = true),
            });

            Assert.Equal("<p>Page</p>", html);
            Assert.True(disposed);
        }
    }

    [Fact]
    public async Task AwaitsAsynchronousPageDisposal()
    {
        await using var renderer = new RazorRenderer();
        var disposed = false;
        await renderer.RenderAsync<AsyncDisposablePage>(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(AsyncDisposablePage.OnDispose)] = (Action)(() => disposed = true),
        });

        Assert.True(disposed);
    }

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

    private sealed class DisposablePage : ComponentBase, IDisposable
    {
        [Parameter]
        public Action? OnDispose { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, "Page");
            builder.CloseElement();
        }

        public void Dispose() => OnDispose?.Invoke();
    }

    private sealed class AsyncDisposablePage : ComponentBase, IAsyncDisposable
    {
        [Parameter]
        public Action? OnDispose { get; set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            OnDispose?.Invoke();
        }
    }
}
