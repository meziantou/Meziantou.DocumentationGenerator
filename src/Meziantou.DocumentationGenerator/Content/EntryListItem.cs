namespace Meziantou.DocumentationGenerator.Content;

/// <summary>One entry of a generated list: a home page section or a package.</summary>
internal sealed record EntryListItem(string Title, string Url, string? Description, string? Version = null);
