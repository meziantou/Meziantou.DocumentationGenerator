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
    private readonly HtmlRenderer _renderer;

    public RazorRenderer(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        configureServices?.Invoke(services);
        _services = services.BuildServiceProvider();
        _renderer = new HtmlRenderer(_services, NullLoggerFactory.Instance);
    }

    public Task<string> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        // HtmlRenderer has thread affinity: every call must go through its dispatcher.
        return _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await _renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters.ToDictionary(StringComparer.Ordinal)));
            await root.QuiescenceTask;
            return root.ToHtmlString();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _renderer.DisposeAsync();
        await _services.DisposeAsync();
    }
}
