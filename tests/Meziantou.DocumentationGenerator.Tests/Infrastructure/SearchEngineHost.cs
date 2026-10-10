using System.Text.Json;
using Jint;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Runs the client-side search engine (search.js) in a JavaScript interpreter.</summary>
internal sealed class SearchEngineHost : IDisposable
{
    private readonly Engine _engine;

    public SearchEngineHost(string indexJson)
    {
        _engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromMinutes(5)));
        _engine.Execute(LoadScript());
        _engine.SetValue("indexJson", indexJson);
        _engine.Execute("var engine = DocgenSearch.createEngine(JSON.parse(indexJson));");
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
        _engine.SetValue("query", query);
        var json = _engine.Evaluate($"JSON.stringify(engine.search(query, {limit}).results.map(function (r) {{ return [r.title, r.url, r.score]; }}))").AsString();
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetString()!, e[2].GetInt32()))];
    }

    public void Dispose() => _engine.Dispose();
}
