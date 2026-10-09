namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <param name="Url">Commit-pinned URL of the file, with a line fragment for <see cref="SourcePrecision.Line"/>.</param>
/// <param name="Precision">How precise the location is.</param>
/// <param name="Path">Path of the file in the repository.</param>
/// <param name="Line">Line of the first visible sequence point.</param>
internal sealed record SourceLocation(string Url, SourcePrecision Precision, string Path, int? Line);
