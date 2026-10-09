using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Compiles fixture assemblies in memory, with XML documentation and portable PDBs.</summary>
internal static class TestCompiler
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkReferences = new(() =>
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. paths
            .Where(p => Path.GetFileName(p) is var name && (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard.dll" or "mscorlib.dll"))
            .Order(StringComparer.Ordinal)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    public static CompiledAssembly Compile(string assemblyName, string source, CompileOptions? options = null)
    {
        return Compile(assemblyName, [($"/_/src/{assemblyName}/Source.cs", source)], options);
    }

    public static CompiledAssembly Compile(string assemblyName, IReadOnlyList<(string Path, string Content)> sources, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose, preprocessorSymbols: options.Defines);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(SourceText.From(s.Content, Encoding.UTF8), parseOptions, s.Path)).ToList();
        if (options.AssemblyVersion is not null)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{options.AssemblyVersion}\")]", parseOptions, "/_/AssemblyInfo.cs"));
        }

        var references = FrameworkReferences.Value.AddRange(options.References.Select(r => (MetadataReference)MetadataReference.CreateFromImage(r.Assembly)));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, deterministic: true, allowUnsafe: true));

        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        using var xml = new MemoryStream();
        using var sourceLink = options.SourceLink is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(options.SourceLink));
        var emitOptions = new EmitOptions(
            metadataOnly: options.ReferenceAssembly,
            debugInformationFormat: options.EmbeddedPdb ? DebugInformationFormat.Embedded : DebugInformationFormat.PortablePdb,
            pdbFilePath: assemblyName + ".pdb",
            includePrivateMembers: !options.ReferenceAssembly);
        var result = compilation.Emit(
            assembly,
            options.ReferenceAssembly || options.EmbeddedPdb ? null : pdb,
            xml,
            options: emitOptions,
            sourceLinkStream: options.ReferenceAssembly ? null : sourceLink);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (!result.Success || errors.Count > 0)
            throw new InvalidOperationException("Compilation failed:\n" + string.Join('\n', errors));

        return new CompiledAssembly(assemblyName, assembly.ToArray(), pdb.Length > 0 ? pdb.ToArray() : null, Encoding.UTF8.GetString(xml.ToArray()));
    }
}
