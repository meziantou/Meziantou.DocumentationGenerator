namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal sealed record CompileOptions
{
    /// <summary>Emit a reference assembly (metadata only, no method bodies).</summary>
    public bool ReferenceAssembly { get; init; }

    /// <summary>Embed the portable PDB in the assembly instead of returning a separate PDB.</summary>
    public bool EmbeddedPdb { get; init; }

    /// <summary>Source Link JSON stored in the PDB.</summary>
    public string? SourceLink { get; init; }

    public IReadOnlyList<CompiledAssembly> References { get; init; } = [];

    /// <summary>Preprocessor symbols, used to simulate target-framework specific APIs.</summary>
    public IReadOnlyList<string> Defines { get; init; } = [];

    public string? AssemblyVersion { get; init; }
}
