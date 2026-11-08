namespace Meziantou.DocumentationGenerator.Content;

/// <summary>An entry of a content folder: a page or a sub-folder.</summary>
internal interface IContentEntry
{
    /// <summary>File or folder name, which sets the order of the entries of a folder with the same <see cref="SortOrder"/>.</summary>
    string Name { get; }

    /// <summary>Position among the entries of a folder; lower values come first. Default: 0.</summary>
    int SortOrder { get; }
}
