namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>An implementation assembly that may provide debug information for an API asset.</summary>
/// <param name="AssemblyEntry">Package entry of the implementation assembly.</param>
/// <param name="PdbEntry">Package entry of the adjacent portable PDB, when present. Embedded PDBs are detected when reading the assembly.</param>
/// <param name="RuntimeIdentifier">Runtime identifier for <c>runtimes/{rid}/lib</c> assets.</param>
internal sealed record SymbolCandidate(string AssemblyEntry, string? PdbEntry, string? RuntimeIdentifier);
