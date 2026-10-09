using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class DiagnosticOptions
{
    public static DiagnosticOptions Default { get; } = new() { WarningsAsErrors = false, SeverityOverrides = ImmutableDictionary<string, DiagnosticSeverity>.Empty };

    public required bool WarningsAsErrors { get; init; }
    public required ImmutableDictionary<string, DiagnosticSeverity> SeverityOverrides { get; init; }

    public DiagnosticSeverity GetSeverity(DiagnosticDescriptor descriptor)
    {
        var severity = SeverityOverrides.TryGetValue(descriptor.Code, out var value) ? value : descriptor.DefaultSeverity;
        if (descriptor.IsSafetyCritical && severity < descriptor.DefaultSeverity)
        {
            severity = descriptor.DefaultSeverity;
        }

        if (WarningsAsErrors && severity is DiagnosticSeverity.Warning)
        {
            severity = DiagnosticSeverity.Error;
        }

        return severity;
    }
}
