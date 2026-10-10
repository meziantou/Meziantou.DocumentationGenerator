namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class SourceLinkOptions
{
    public required bool Enabled { get; init; }
    /// <summary>Symbol servers queried for missing PDBs. Only these servers are contacted, without credentials.</summary>
    public required System.Collections.Immutable.ImmutableArray<Uri> SymbolServers { get; init; }
}
