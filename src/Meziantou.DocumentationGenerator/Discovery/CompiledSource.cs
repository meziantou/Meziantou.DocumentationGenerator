using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>Metadata-only assembly and XML documentation compiled in memory from the source files of a source package.</summary>
internal sealed class CompiledSource
{
    public required byte[] Assembly { get; init; }
    public required byte[] XmlDocumentation { get; init; }
    public required ImmutableArray<string> SourceEntries { get; init; }
}
