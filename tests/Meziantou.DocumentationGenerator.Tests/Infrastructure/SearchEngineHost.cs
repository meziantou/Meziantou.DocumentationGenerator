using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Runs the client-side search engine (search.js) in a JavaScript interpreter.</summary>
internal sealed class SearchEngineHost : IDisposable
{
    private readonly JavaScriptScriptEngine _engine;
    private readonly BrowsingContext _context;

    public SearchEngineHost(string indexJson)
    {
        _engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
        {
            Timeout = TimeSpan.FromMinutes(5),
            MemoryLimit = null, // Building the index of the large corpus test allocates more than the default limit
        });

        _context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = _engine });
        _context.Navigate("https://docgen.test/", _ => "<!DOCTYPE html><title>Search</title>");
        Execute(LoadScript());
        Execute($"var engine = DocgenSearch.createEngine(JSON.parse({ToJavaScriptString(indexJson)}));");
    }

    public static string LoadScript()
    {
        var assembly = typeof(Meziantou.DocumentationGenerator.Rendering.StaticAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream("assets/search.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public IReadOnlyList<string> Search(string query, int limit = 20) => [.. SearchDetailed(query, limit).Select(r => r.Title)];

    public IReadOnlyList<(string Title, string Url, int Score)> SearchDetailed(string query, int limit = 20)
    {
        var json = Evaluate($"JSON.stringify(engine.search({ToJavaScriptString(query)}, {limit}).results.map(function (r) {{ return [r.title, r.url, r.score]; }}))");
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetString()!, e[2].GetInt32()))];
    }

    public void Dispose()
    {
        _context.Dispose();
        _engine.Dispose();
    }

    private void Execute(string script) => _engine.Evaluate(_context.Window, script);

    private string Evaluate(string script) => _engine.Evaluate(_context.Window, script).AsString();

    // A JSON string is a valid JavaScript string literal
    private static string ToJavaScriptString(string value) => JsonSerializer.Serialize(value);
}
