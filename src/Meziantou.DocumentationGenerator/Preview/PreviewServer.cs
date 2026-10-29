using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Preview;

/// <summary>
/// Local web server used to preview the generated site. It behaves like an ordinary static host: files are served under
/// the base path, directory URLs serve <c>index.html</c> and missing files return the site's <c>404.html</c>.
/// When live reload is enabled, a script is injected in HTML responses (the files on disk are not modified) and open
/// pages reload when <see cref="NotifyReload"/> is called.
/// </summary>
/// <remarks>
/// <see cref="HttpListener"/> is used so that the tool does not depend on the ASP.NET Core shared framework.
/// </remarks>
internal sealed class PreviewServer : IAsyncDisposable
{
    public const string LiveReloadEventsPath = "/__docgen/livereload";
    public const string LiveReloadScriptPath = "/__docgen/livereload.js";

    private const string LiveReloadScript = """
        (() => {
            "use strict";
            let opened = false;
            const source = new EventSource("/__docgen/livereload");
            source.addEventListener("reload", () => location.reload());
            source.addEventListener("open", () => {
                // The server was restarted while the page was open: the site may have changed
                if (opened) {
                    location.reload();
                }

                opened = true;
            });
        })();
        """;

    private static readonly byte[] ReloadEvent = Encoding.UTF8.GetBytes("event: reload\ndata: reload\n\n");
    private static readonly byte[] ConnectedEvent = Encoding.UTF8.GetBytes(": connected\n\n");
    private static readonly byte[] HeartbeatEvent = Encoding.UTF8.GetBytes(": heartbeat\n\n");
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".xml"] = "application/xml; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".md"] = "text/markdown; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".pdf"] = "application/pdf",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".wasm"] = "application/wasm",
    };

    private readonly HttpListener _listener;
    private readonly bool _liveReload;
    private readonly ConcurrentDictionary<Channel<bool>, byte> _subscribers = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _acceptLoop;
    private volatile Site? _site;

    private PreviewServer(HttpListener listener, int port, bool liveReload)
    {
        _listener = listener;
        _liveReload = liveReload;
        Origin = "http://localhost:" + port.ToString(CultureInfo.InvariantCulture);
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Origin of the server, such as <c>http://localhost:8080</c>.</summary>
    public string Origin { get; }

    /// <summary>Starts the server on <c>localhost</c>. Use port 0 to pick a free port.</summary>
    /// <exception cref="HttpListenerException">The port is not available.</exception>
    public static PreviewServer Start(int port, bool liveReload)
    {
        var listener = new HttpListener();
        var actualPort = port is 0 ? GetFreePort() : port;
        listener.Prefixes.Add("http://localhost:" + actualPort.ToString(CultureInfo.InvariantCulture) + "/");
        try
        {
            listener.Start();
        }
        catch
        {
            listener.Close();
            throw;
        }

        return new PreviewServer(listener, actualPort, liveReload);
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    /// <summary>Sets the directory to serve and the URL path where it is served. The values come from the latest configuration.</summary>
    public void SetSite(FullPath root, UrlBasePath basePath) => _site = new Site(root, basePath);

    public string GetUrl(UrlBasePath basePath) => Origin + basePath.Value;

    /// <summary>Asks every open page to reload.</summary>
    public void NotifyReload()
    {
        foreach (var subscriber in _subscribers.Keys)
        {
            subscriber.Writer.TryWrite(true);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // The listener was stopped
                return;
            }

            _ = Task.Run(() => HandleRequestAsync(context));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            await HandleAsync(context.Request, response);
            response.Close();
        }
#pragma warning disable CA1031 // A failed request must not stop the server
        catch (Exception)
#pragma warning restore CA1031
        {
            // The client disconnected or the server is stopping
            response.Abort();
        }
    }

    private async Task HandleAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        response.Headers[HttpResponseHeader.CacheControl] = "no-store";
        var isHead = string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);
        if (!isHead && !string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Headers[HttpResponseHeader.Allow] = "GET, HEAD";
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";
        if (_liveReload && path == LiveReloadEventsPath)
        {
            await StreamReloadEventsAsync(response);
            return;
        }

        if (_liveReload && path == LiveReloadScriptPath)
        {
            await WriteAsync(response, "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(LiveReloadScript), isHead);
            return;
        }

        var site = _site;
        if (site is null)
        {
            response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteAsync(response, "text/plain; charset=utf-8", "The site has not been generated yet."u8.ToArray(), isHead);
            return;
        }

        var basePath = site.BasePath.Value;
        if (!path.StartsWith(basePath, StringComparison.Ordinal))
        {
            // Help users who open the origin, or the base path without its trailing slash
            if (path == "/" || path == basePath[..^1])
            {
                response.Redirect(basePath);
                return;
            }

            await WriteNotFoundAsync(response, site, isHead);
            return;
        }

        var relativePath = Uri.UnescapeDataString(path[basePath.Length..]);
        if (relativePath.Length == 0 || relativePath.EndsWith('/', StringComparison.Ordinal))
        {
            relativePath += "index.html";
        }

        var file = relativePath.Contains('\\', StringComparison.Ordinal) || relativePath.Contains('\0', StringComparison.Ordinal) ? (FullPath?)null : site.Root / relativePath;
        if (file is not { } filePath || !filePath.IsChildOf(site.Root))
        {
            await WriteNotFoundAsync(response, site, isHead);
            return;
        }

        if (Directory.Exists(filePath))
        {
            response.Redirect(path + "/" + request.Url?.Query);
            return;
        }

        if (!await TryWriteFileAsync(response, filePath, isHead))
        {
            await WriteNotFoundAsync(response, site, isHead);
        }
    }

    private async Task WriteNotFoundAsync(HttpListenerResponse response, Site site, bool isHead)
    {
        response.StatusCode = (int)HttpStatusCode.NotFound;
        if (!await TryWriteFileAsync(response, site.Root / "404.html", isHead))
        {
            await WriteAsync(response, "text/plain; charset=utf-8", "Not found"u8.ToArray(), isHead);
        }
    }

    private async Task<bool> TryWriteFileAsync(HttpListenerResponse response, FullPath file, bool isHead)
    {
        byte[] content;
        try
        {
            // The file may be replaced or deleted by a build at any time
            content = await File.ReadAllBytesAsync(file, _stopping.Token);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }

        var contentType = ContentTypes.TryGetValue(file.Extension ?? "", out var value) ? value : "application/octet-stream";
        if (_liveReload && contentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            content = InjectLiveReloadScript(content);
        }

        await WriteAsync(response, contentType, content, isHead);
        return true;
    }

    private async Task WriteAsync(HttpListenerResponse response, string contentType, byte[] content, bool isHead)
    {
        response.ContentType = contentType;
        response.ContentLength64 = content.Length;
        if (!isHead)
        {
            await response.OutputStream.WriteAsync(content, _stopping.Token);
        }
    }

    internal static byte[] InjectLiveReloadScript(byte[] html)
    {
        const string Script = "<script src=\"" + LiveReloadScriptPath + "\"></script>";
        var text = Encoding.UTF8.GetString(html);
        var index = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        text = index >= 0 ? text.Insert(index, Script) : text + Script;
        return Encoding.UTF8.GetBytes(text);
    }

    private async Task StreamReloadEventsAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers[HttpResponseHeader.CacheControl] = "no-cache";
        response.SendChunked = true;

        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        _subscribers.TryAdd(channel, 0);
        try
        {
            var stream = response.OutputStream;
            await stream.WriteAsync(ConnectedEvent, _stopping.Token);
            await stream.FlushAsync(_stopping.Token);
            while (true)
            {
                // A comment is sent periodically so that the request ends (writing fails) soon after the page is closed
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                heartbeat.CancelAfter(HeartbeatInterval);
                byte[] message;
                try
                {
                    await channel.Reader.ReadAsync(heartbeat.Token);
                    message = ReloadEvent;
                }
                catch (OperationCanceledException) when (!_stopping.IsCancellationRequested)
                {
                    message = HeartbeatEvent;
                }

                await stream.WriteAsync(message, _stopping.Token);
                await stream.FlushAsync(_stopping.Token);
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Close();
        await _acceptLoop;
        _stopping.Dispose();
    }

    private sealed record Site(FullPath Root, UrlBasePath BasePath);
}
