namespace Meziantou.DocumentationGenerator.Links;

/// <summary>A file of the generated site.</summary>
/// <param name="FilePath">Path relative to the output directory, using '/' separators.</param>
/// <param name="Kind">Kind of page.</param>
/// <param name="Owner">Identity of what produced the route, used in collision diagnostics.</param>
internal sealed record SiteRoute(string FilePath, PageKind Kind, string Owner)
{
    public override string ToString() => FilePath;
}
