using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Renders Razor components to static HTML without a web host.</summary>
internal sealed class RazorRenderer : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public RazorRenderer(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        // Only characters that are significant in HTML are encoded, so that Unicode text stays readable
        services.AddSingleton(System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));
        configureServices?.Invoke(services);
        _services = services.BuildServiceProvider();
    }

    public Task<string> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        return RenderAsync(typeof(TComponent), parameters);
    }

    public async Task<string> RenderAsync(Type componentType, IReadOnlyDictionary<string, object?> parameters)
    {
        // A renderer retains its root components until disposed. Release each page's tree before rendering the next page.
        await using var renderer = new HtmlRenderer(_services, NullLoggerFactory.Instance);
        // HtmlRenderer has thread affinity: every call must go through its dispatcher, and asynchronous component work
        // must complete before the HTML is read
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync(componentType, ParameterView.FromDictionary(parameters.ToDictionary(StringComparer.Ordinal)));
            await root.QuiescenceTask;
            return root.ToHtmlString();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
    }
}
