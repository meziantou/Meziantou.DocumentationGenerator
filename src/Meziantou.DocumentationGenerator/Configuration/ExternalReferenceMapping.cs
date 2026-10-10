namespace Meziantou.DocumentationGenerator.Configuration;

/// <param name="Namespace">Namespace prefix. <c>System</c> matches <c>System</c> and <c>System.IO</c>, not <c>SystemX</c>.</param>
/// <param name="UrlTemplate">URL containing <c>{uid}</c>.</param>
internal sealed record ExternalReferenceMapping(string Namespace, string UrlTemplate)
{
    public bool Matches(string qualifiedName)
    {
        return qualifiedName.StartsWith(Namespace, StringComparison.Ordinal) &&
            (qualifiedName.Length == Namespace.Length || qualifiedName[Namespace.Length] == '.');
    }
}
