using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <summary>A verified pair of implementation assembly and portable PDB.</summary>
internal sealed class ImplementationDebugInfo(string assemblyEntry, PublicApiAssembly assembly, PortablePdb pdb, SourceLinkMap? map) : IDisposable
{
    public string AssemblyEntry { get; } = assemblyEntry;

    /// <summary>API model of the implementation, used to find the metadata token of a symbol by documentation ID.</summary>
    public PublicApiAssembly Assembly { get; } = assembly;
    public PortablePdb Pdb { get; } = pdb;
    public SourceLinkMap? Map { get; } = map;

    public void Dispose() => Pdb.Dispose();
}
