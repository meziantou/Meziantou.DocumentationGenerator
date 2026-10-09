using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework.SyntaxHighlighting;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Build-time syntax highlighting shared by Markdown code fences, XML documentation examples and API signatures.
/// The output uses highlight.js-compatible class names. Code is untrusted: each regular expression match is bounded and
/// unknown languages are rendered as encoded plain text.
/// </summary>
internal sealed class HighlightingService(DiagnosticBag diagnostics)
{
    public const string ClassPrefix = "hljs-";

    private static readonly HighlightOptions Options = new()
    {
        ClassPrefix = ClassPrefix,
        MatchTimeout = TimeSpan.FromSeconds(2),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = "csharp",
        ["cs"] = "csharp",
        ["f#"] = "fsharp",
        ["fs"] = "fsharp",
        ["vb"] = "vbnet",
        ["sh"] = "bash",
        ["shell"] = "bash",
        ["ps1"] = "powershell",
        ["pwsh"] = "powershell",
        ["yml"] = "yaml",
        ["htm"] = "html",
        ["xaml"] = "xml",
        ["csproj"] = "xml",
        ["razor"] = "cshtml",
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["jsonc"] = "json",
    };

    private static readonly HashSet<string> PlainTextLanguages = new(StringComparer.OrdinalIgnoreCase) { "", "text", "plaintext", "txt", "output", "console" };

    /// <summary>Returns the normalized language name, or <see langword="null"/> for plain text.</summary>
    public static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        language = language.Trim();
        if (PlainTextLanguages.Contains(language))
            return null;

        return Aliases.TryGetValue(language, out var alias) ? alias : language.ToLowerInvariant();
    }

    /// <summary>Returns HTML for the content of a <c>code</c> element. The result is always safe to embed.</summary>
    public string Highlight(string code, string? language, DiagnosticLocation? location = null)
    {
        var normalized = NormalizeLanguage(language);
        if (normalized is null)
            return HtmlEncoding.Encode(code);

        if (!SyntaxHighlighter.IsSupported(normalized))
        {
            diagnostics.Report(DiagnosticDescriptors.UnknownCodeLanguage, $"The code language '{language}' is not supported; the code is displayed without highlighting", location);
            return HtmlEncoding.Encode(code);
        }

        return SyntaxHighlighter.TryHighlight(code, normalized, out var html, Options) ? html : HtmlEncoding.Encode(code);
    }
}
