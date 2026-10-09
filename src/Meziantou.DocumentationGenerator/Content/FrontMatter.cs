namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Supported front matter keys. Other keys are ignored.</summary>
internal sealed record FrontMatter(string? Title, string? Description, string? Uid, string? Slug)
{
    public static FrontMatter Empty { get; } = new(null, null, null, null);
}
