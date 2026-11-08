namespace Meziantou.DocumentationGenerator.Content;

/// <summary>How <c>&lt;SectionList /&gt;</c> and <c>&lt;PackageList /&gt;</c> display their entries.</summary>
internal enum EntryListLayout
{
    /// <summary>A grid of cards, like the generated home page.</summary>
    Cards,

    /// <summary>A compact list: one line per entry.</summary>
    List,

    /// <summary>A table, like the API reference page.</summary>
    Table,
}
