namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Resolves relative links and images according to where a Markdown document comes from.</summary>
internal interface IMarkdownLinkResolver
{
    /// <summary>Returns the URL of a relative link, or <see langword="null"/> when it cannot be resolved (a diagnostic is reported).</summary>
    /// <param name="path">Unescaped relative path, without query and fragment.</param>
    /// <param name="isImage"><see langword="true"/> for images, which must be files.</param>
    /// <param name="line">Line of the link in the document, when known.</param>
    string? ResolveRelative(string path, bool isImage, int? line);
}
