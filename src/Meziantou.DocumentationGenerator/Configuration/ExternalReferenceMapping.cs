using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <param name="Namespace">Namespace prefix. <c>System</c> matches <c>System</c> and <c>System.IO</c>, not <c>SystemX</c>.</param>
/// <param name="UrlTemplate">URL containing <c>{uid}</c>.</param>
internal sealed record ExternalReferenceMapping(string Namespace, string UrlTemplate)
{
    private const string MicrosoftLearnUrl = "https://learn.microsoft.com/dotnet/api/{uid}";

    /// <summary>Used when the configuration has no <c>xrefs</c>: .NET and Microsoft APIs link to Microsoft Learn.</summary>
    public static ImmutableArray<ExternalReferenceMapping> Defaults { get; } =
    [
        new("Microsoft", MicrosoftLearnUrl),
        new("System", MicrosoftLearnUrl),
    ];

    public bool Matches(string qualifiedName)
    {
        return qualifiedName.StartsWith(Namespace, StringComparison.Ordinal) &&
            (qualifiedName.Length == Namespace.Length || qualifiedName[Namespace.Length] == '.');
    }
}
