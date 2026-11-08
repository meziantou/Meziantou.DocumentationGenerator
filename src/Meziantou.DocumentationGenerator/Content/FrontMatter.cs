namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Supported front matter keys. Other keys are ignored.</summary>
/// <param name="SortOrder">Position of the page among the entries of its folder; lower values come first. Entries with the same value are sorted by name.</param>
internal sealed record FrontMatter(string? Title, string? Description, string? Slug, int SortOrder = 0)
{
    public static FrontMatter Empty { get; } = new(null, null, null);
}
