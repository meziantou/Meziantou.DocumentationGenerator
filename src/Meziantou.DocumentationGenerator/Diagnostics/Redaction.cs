using System.Text.RegularExpressions;

namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <summary>Removes secrets that may appear in messages coming from external libraries, such as credentials embedded in URLs.</summary>
internal static partial class Redaction
{
    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        message = UrlCredentialsRegex().Replace(message, "${scheme}://***@");
        message = SecretQueryRegex().Replace(message, "${name}=***");
        return message;
    }

    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*)://[^/\s:@]+(?::[^/\s@]*)?@", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlCredentialsRegex();

    [GeneratedRegex(@"(?<name>(?:api[-_]?key|access[-_]?token|token|password|sig|secret))=[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretQueryRegex();
}
