using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed record BuildResult(BuildStatus Status, IReadOnlyList<Diagnostic> Diagnostics, SiteConfiguration? Configuration, bool IsPublished, int GeneratedFileCount)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity is DiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(d => d.Severity is DiagnosticSeverity.Warning);
}
