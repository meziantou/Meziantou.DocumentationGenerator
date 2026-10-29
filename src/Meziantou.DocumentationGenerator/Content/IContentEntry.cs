namespace Meziantou.DocumentationGenerator.Content;

/// <summary>An entry of a content folder: a page or a sub-folder.</summary>
internal interface IContentEntry
{
    /// <summary>File or folder name, which sets the order of the entries of a folder.</summary>
    string Name { get; }
}
