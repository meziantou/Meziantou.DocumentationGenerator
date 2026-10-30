namespace Meziantou.DocumentationGenerator.Content;

internal enum MarkdownComponentKind
{
    /// <summary>A shell command, rendered as a code block (or inline code inside a paragraph).</summary>
    Command,

    /// <summary>A link to <see cref="MarkdownComponent.Url"/> whose text is <see cref="MarkdownComponent.Text"/>.</summary>
    PackageLink,
}
