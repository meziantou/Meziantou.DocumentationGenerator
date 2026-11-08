namespace Meziantou.DocumentationGenerator.Content;

/// <summary>The entries to list for <c>&lt;SectionList /&gt;</c> or <c>&lt;PackageList /&gt;</c>.</summary>
/// <param name="Layout">How the entries are displayed.</param>
/// <param name="Patterns">Package identifier patterns (<c>*</c> matches any characters); empty for sections.</param>
internal sealed record EntryListRequest(EntryListLayout Layout, IReadOnlyList<string> Patterns);
