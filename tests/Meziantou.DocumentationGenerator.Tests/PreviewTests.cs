using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Preview;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed partial class PreviewTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Serve_ServesSiteUnderBasePath()
    {
        await using var temp = CreateSite();
        await using var session = BackgroundCli.Start(["build", "--config", temp.FullPath / "docgen.json", "--serve", "--port", "0", "--base-path", "/docs/"]);
        var url = await session.WaitForPreviewUrlAsync();
        Assert.EndsWith("/docs/", url, StringComparison.Ordinal);
        var origin = url[..^"docs/".Length];

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using var client = new HttpClient(handler);
        using (var response = await client.GetAsync(url, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("First version", html, StringComparison.Ordinal);
            Assert.DoesNotContain(PreviewServer.LiveReloadScriptPath, html, StringComparison.Ordinal);
        }

        using (var response = await client.GetAsync(origin, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/docs/", response.Headers.Location?.OriginalString);
        }

        using (var response = await client.GetAsync(url + "a/b/missing", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/docs/404.html", response.Headers.Location?.OriginalString);
        }

        using (var response = await client.GetAsync(url + "404.html", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        using (var response = await client.GetAsync(url + "%2e%2e/docgen.json", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using (var response = await client.GetAsync(origin + PreviewServer.LiveReloadScriptPath.TrimStart('/'), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var exitCode = await session.StopAsync();
        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task Serve_FailedBuild_DoesNotStartServer()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", """{ "version": 1, "content": { "root": "docs" }, "unknown": true }""");
        temp.CreateTextFile("docs/index.md", "# Home");

        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--serve", "--port", "0"]);
        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.DoesNotContain("Preview:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serve_InvalidPort()
    {
        var result = await CliRunner.RunAsync(["build", "--serve", "--port", "70000"]);
        Assert.Equal(ExitCodes.InvalidArguments, result.ExitCode);
    }

    [Fact]
    public async Task WatchAndServe_RebuildsAndReloadsPages()
    {
        await using var temp = CreateSite();
        await using var session = BackgroundCli.Start(["build", "--config", temp.FullPath / "docgen.json", "--watch", "--serve", "--port", "0"]);
        var url = await session.WaitForPreviewUrlAsync();
        await session.WaitForOutputAsync("Watching for changes", occurrences: 1);

        using var client = new HttpClient();
        var html = await client.GetStringAsync(url, TestContext.Current.CancellationToken);
        Assert.Contains("First version", html, StringComparison.Ordinal);
        Assert.Contains($"<script src=\"{PreviewServer.LiveReloadScriptPath}\"></script></body>", html, StringComparison.Ordinal);
        // The generated files are not modified
        Assert.DoesNotContain(PreviewServer.LiveReloadScriptPath, File.ReadAllText(temp.FullPath / "_site" / "index.html"), StringComparison.Ordinal);

        var script = await client.GetStringAsync(url.TrimEnd('/') + PreviewServer.LiveReloadScriptPath, TestContext.Current.CancellationToken);
        Assert.Contains("EventSource", script, StringComparison.Ordinal);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        using var events = await client.GetStreamAsync(url.TrimEnd('/') + PreviewServer.LiveReloadEventsPath, timeout.Token);
        using var reader = new StreamReader(events, Encoding.UTF8);
        Assert.Equal(": connected", await reader.ReadLineAsync(timeout.Token));

        File.WriteAllText(temp.FullPath / "docs" / "index.md", "---\ntitle: Home\n---\n\nSecond version\n");

        string? line;
        do
        {
            line = await reader.ReadLineAsync(timeout.Token);
        }
        while (line is not null && line != "event: reload");

        Assert.Equal("event: reload", line);
        Assert.Contains("Change detected: docs/index.md", session.Output, StringComparison.Ordinal);
        html = await client.GetStringAsync(url, TestContext.Current.CancellationToken);
        Assert.Contains("Second version", html, StringComparison.Ordinal);

        Assert.Equal(ExitCodes.Success, await session.StopAsync());
    }

    [Fact]
    public async Task Watch_RebuildsAfterFailedBuild()
    {
        await using var temp = CreateSite();
        await using var session = BackgroundCli.Start(["build", "--config", temp.FullPath / "docgen.json", "--watch"]);
        await session.WaitForOutputAsync("Watching for changes", occurrences: 1);

        // An invalid configuration fails the build but watching continues
        File.WriteAllText(temp.FullPath / "docgen.json", """{ "version": 1, "content": { "root": "docs" }, "unknown": true }""");
        await session.WaitForOutputAsync("Watching for changes", occurrences: 2);
        Assert.Contains("Build failed", session.Error, StringComparison.Ordinal);

        File.WriteAllText(temp.FullPath / "docgen.json", """{ "version": 1, "content": { "root": "docs" } }""");
        await session.WaitForOutputAsync("Watching for changes", occurrences: 3);
        File.WriteAllText(temp.FullPath / "docs" / "index.md", "---\ntitle: Home\n---\n\nSecond version\n");
        await session.WaitForOutputAsync("Watching for changes", occurrences: 4);

        Assert.Contains("Second version", File.ReadAllText(temp.FullPath / "_site" / "index.html"), StringComparison.Ordinal);
        Assert.DoesNotContain("Preview:", session.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, await session.StopAsync());
    }

    [Fact]
    public void InjectLiveReloadScript_InsertsBeforeClosingBody()
    {
        var result = Encoding.UTF8.GetString(PreviewServer.InjectLiveReloadScript("<html><body><p>é</p></BODY></html>"u8.ToArray()));
        Assert.Equal($"<html><body><p>é</p><script src=\"{PreviewServer.LiveReloadScriptPath}\"></script></BODY></html>", result);

        result = Encoding.UTF8.GetString(PreviewServer.InjectLiveReloadScript("<p>fragment</p>"u8.ToArray()));
        Assert.Equal($"<p>fragment</p><script src=\"{PreviewServer.LiveReloadScriptPath}\"></script>", result);
    }

    private static TemporaryDirectory CreateSite()
    {
        var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", """{ "version": 1, "content": { "root": "docs" } }""");
        temp.CreateTextFile("docs/index.md", "---\ntitle: Home\n---\n\nFirst version\n");
        return temp;
    }

    [GeneratedRegex(@"Preview: (?<url>http://\S+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PreviewUrlRegex();

    /// <summary>Runs the command line in the background, as a user running a long-lived command stopped with Ctrl+C.</summary>
    private sealed class BackgroundCli : IAsyncDisposable
    {
        private readonly ConcurrentStringWriter _output = new();
        private readonly ConcurrentStringWriter _error = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task<int> _task;

        private BackgroundCli(string[] args)
        {
            var application = new CommandLineApplication(_output, _error);
            _task = Task.Run(() => application.RunAsync(args, _cancellation.Token));
        }

        public string Output => _output.ToString();
        public string Error => _error.ToString();

        public static BackgroundCli Start(string[] args) => new(args);

        public async Task<string> WaitForPreviewUrlAsync()
        {
            await WaitForOutputAsync("Preview: ", occurrences: 1);
            return PreviewUrlRegex().Match(Output).Groups["url"].Value;
        }

        public async Task WaitForOutputAsync(string text, int occurrences)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (CountOccurrences(Output, text) < occurrences)
            {
                if (_task.IsCompleted)
                    Assert.Fail($"The command exited with code {await _task}.\n{Output}\n{Error}");

                if (DateTime.UtcNow > deadline)
                    Assert.Fail($"'{text}' was not written {occurrences} time(s).\n{Output}\n{Error}");

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        private static int CountOccurrences(string value, string text)
        {
            var count = 0;
            for (var index = value.IndexOf(text, StringComparison.Ordinal); index >= 0; index = value.IndexOf(text, index + text.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        public async Task<int> StopAsync()
        {
            await _cancellation.CancelAsync();
            return await _task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            try
            {
                await _task.WaitAsync(Timeout);
            }
            catch (Exception) when (_task.IsFaulted || !_task.IsCompleted)
            {
                // The test already failed
            }

            _cancellation.Dispose();
            _output.Dispose();
            _error.Dispose();
        }
    }

    private sealed class ConcurrentStringWriter : TextWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly Lock _lock = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_lock)
            {
                _builder.Append(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_lock)
            {
                _builder.Append(buffer, index, count);
            }
        }

        public override void Write(string? value)
        {
            lock (_lock)
            {
                _builder.Append(value);
            }
        }

        public override string ToString()
        {
            lock (_lock)
            {
                return _builder.ToString();
            }
        }
    }
}
