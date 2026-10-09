namespace Meziantou.DocumentationGenerator.Rendering;

internal sealed record ManifestInput(string Id, string Version, string Sha512, bool IsReference);
