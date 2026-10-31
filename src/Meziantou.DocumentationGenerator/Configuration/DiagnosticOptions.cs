using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class DiagnosticOptions
{
    public static DiagnosticOptions Default { get; } = new() { WarningsAsErrors = false, SeverityOverrides = ImmutableDictionary<string, DiagnosticSeverity>.Empty };

    public required bool WarningsAsErrors { get; init; }
    public bool AllowInvalidCref { get; init; }
    public required ImmutableDictionary<string, DiagnosticSeverity> SeverityOverrides { get; init; }

    public DiagnosticSeverity GetSeverity(DiagnosticDescriptor descriptor)
    {
        var defaultSeverity = AllowInvalidCref && (descriptor == DiagnosticDescriptors.UnresolvedReference || descriptor == DiagnosticDescriptors.AmbiguousReference)
            ? DiagnosticSeverity.Warning
            : descriptor.DefaultSeverity;
        var severity = SeverityOverrides.TryGetValue(descriptor.Code, out var value) ? value : defaultSeverity;
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
