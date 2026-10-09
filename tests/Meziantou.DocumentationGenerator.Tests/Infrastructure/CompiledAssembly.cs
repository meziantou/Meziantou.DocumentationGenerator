namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal sealed record CompiledAssembly(string Name, byte[] Assembly, byte[]? Pdb, string Xml);
