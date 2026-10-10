namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Normalized URL path where the site is deployed. Always starts and ends with '/'.</summary>
internal sealed record UrlBasePath
{
    public static UrlBasePath Root { get; } = new("/");

    private UrlBasePath(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsRoot => Value == "/";

    public static bool TryParse(string? value, out UrlBasePath basePath, out string? error)
    {
        basePath = Root;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();
        if (value.Contains("://", StringComparison.Ordinal))
        {
            error = "the base path must be a URL path such as /docs/; use site.url for the origin";
            return false;
        }

        if (value.IndexOfAny(['?', '#', '\\', ' ', '"', '<', '>']) >= 0)
        {
            error = "the base path cannot contain a query, a fragment, backslashes or spaces";
            return false;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            error = "the base path cannot contain '.' or '..' segments";
            return false;
        }

        if (value.Contains("//", StringComparison.Ordinal))
        {
            error = "the base path cannot contain empty segments";
            return false;
        }

        basePath = new UrlBasePath(segments.Length == 0 ? "/" : "/" + string.Join('/', segments) + "/");
        return true;
    }

    public static UrlBasePath Parse(string value)
    {
        if (!TryParse(value, out var result, out var error))
            throw new ArgumentException(error, nameof(value));

        return result;
    }

    public override string ToString() => Value;
}
