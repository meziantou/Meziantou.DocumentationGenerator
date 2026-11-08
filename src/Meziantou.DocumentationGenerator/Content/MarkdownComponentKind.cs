namespace Meziantou.DocumentationGenerator.Content;

internal enum MarkdownComponentKind
{
    /// <summary>A shell command, rendered as a code block (or inline code inside a paragraph).</summary>
    Command,

    /// <summary>A link to <see cref="MarkdownComponent.Url"/> whose text is <see cref="MarkdownComponent.Text"/>.</summary>
    PackageLink,

    /// <summary>A graph of package dependencies described by <see cref="MarkdownComponent.Graph"/>. Block-level only.</summary>
    DependencyGraph,

    /// <summary>The top-level pages and folders of the site, described by <see cref="MarkdownComponent.List"/>. Block-level only.</summary>
    SectionList,

    /// <summary>The documented packages, described by <see cref="MarkdownComponent.List"/>. Block-level only.</summary>
    PackageList,
}
