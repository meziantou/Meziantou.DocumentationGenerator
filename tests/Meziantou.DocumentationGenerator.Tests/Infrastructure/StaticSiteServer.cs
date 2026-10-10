using System.Collections.Concurrent;
using Meziantou.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>
/// An ordinary static file server: files are served from a directory under a base path, directory URLs serve index.html,
/// and missing files return 404 with the site's 404.html, as configured on static hosts. There is no SPA fallback.
/// </summary>
internal sealed class StaticSiteServer : IAsyncDisposable
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private readonly WebApplication _app;

    private StaticSiteServer(WebApplication app, string basePath)
    {
        _app = app;
        BasePath = basePath;
    }

    public string BasePath { get; }
    public string Origin => _app.Urls.First();
    public string BaseUrl => Origin + BasePath;
    public ConcurrentQueue<(string Path, int StatusCode)> Requests { get; } = new();

    public static async Task<StaticSiteServer> StartAsync(FullPath root, string basePath = "/")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var server = new StaticSiteServer(app, basePath);
        app.Run(async context =>
        {
            var path = context.Request.Path.Value ?? "/";
            var file = Resolve(root, basePath, path);
            if (file is not null)
            {
                context.Response.ContentType = ContentTypes.TryGetContentType(file, out var contentType) ? contentType : "application/octet-stream";
                await context.Response.SendFileAsync(file);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                var notFound = root / "404.html";
                if (File.Exists(notFound))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.SendFileAsync(notFound);
                }
            }

            server.Requests.Enqueue((path, context.Response.StatusCode));
        });
        await app.StartAsync();
        return server;
    }

    private static string? Resolve(FullPath root, string basePath, string path)
    {
        if (!path.StartsWith(basePath, StringComparison.Ordinal))
            return null;

        var relative = Uri.UnescapeDataString(path[basePath.Length..]);
        if (relative.Length == 0 || relative.EndsWith('/', StringComparison.Ordinal))
        {
            relative += "index.html";
        }

        var file = root / relative;
        return file.IsChildOf(root) && File.Exists(file) ? file : null;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
