using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Minimal NuGet V3 feed (service index and flat container) served by Kestrel on a loopback port.</summary>
internal sealed class TestNuGetFeed : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<(string Id, string Version), byte[]> _packages = new();

    private TestNuGetFeed(WebApplication app)
    {
        _app = app;
    }

    public string? RequiredUserName { get; init; }
    public string? RequiredPassword { get; init; }
    public ConcurrentQueue<string> Requests { get; } = new();
    public string ServiceIndexUrl => BaseUrl + "v3/index.json";
    private string BaseUrl => _app.Services.GetRequiredServiceFeature().Addresses.First() + "/";

    public static async Task<TestNuGetFeed> StartAsync(string? userName = null, string? password = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var feed = new TestNuGetFeed(app) { RequiredUserName = userName, RequiredPassword = password };
        app.Use(feed.HandleAsync);
        await app.StartAsync();
        return feed;
    }

    public void Add(TestPackageBuilder package) => _packages[(package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant())] = package.ToBytes();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "";
        Requests.Enqueue(path);
        if (RequiredUserName is not null)
        {
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(RequiredUserName + ":" + RequiredPassword));
            if (!string.Equals(context.Request.Headers.Authorization, expected, StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"feed\"";
                return;
            }
        }

        if (path == "/v3/index.json")
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($$"""
                { "version": "3.0.0", "resources": [ { "@id": "{{BaseUrl}}v3/flat/", "@type": "PackageBaseAddress/3.0.0" } ] }
                """);
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is ["v3", "flat", var id, "index.json"])
        {
            var versions = _packages.Keys.Where(k => k.Id == id).Select(k => $"\"{k.Version}\"").ToList();
            if (versions.Count == 0)
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($$"""{ "versions": [ {{string.Join(',', versions)}} ] }""");
            return;
        }

        if (segments is ["v3", "flat", var packageId, var version, _] && _packages.TryGetValue((packageId, version), out var content))
        {
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(content);
            return;
        }

        context.Response.StatusCode = 404;
    }
}

file static class ServiceProviderExtensions
{
    public static IServerAddressesFeature GetRequiredServiceFeature(this IServiceProvider services)
    {
        var server = (IServer)services.GetService(typeof(IServer))!;
        return server.Features.Get<IServerAddressesFeature>()!;
    }
}
