using System.Text;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Encodes only the characters that are significant in HTML text and attribute values, so Unicode text stays readable.</summary>
internal static class HtmlEncoding
{
    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.AsSpan().IndexOfAny("&<>\"'") < 0)
            return value;

        var builder = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }
}
