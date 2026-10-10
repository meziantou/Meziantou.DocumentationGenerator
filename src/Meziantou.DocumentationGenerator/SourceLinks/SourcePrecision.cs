namespace Meziantou.DocumentationGenerator.SourceLinks;

internal enum SourcePrecision
{
    /// <summary>The line of the first visible sequence point of the member body. It may differ from the declaration line.</summary>
    Line,

    /// <summary>Only the file is known, for instance for a type (the file of its first method).</summary>
    File,
}
