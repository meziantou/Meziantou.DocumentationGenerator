namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <summary>
/// Thread-safe collection of diagnostics. Severities are computed when diagnostics are read, so severity overrides loaded
/// from the configuration also apply to diagnostics reported before the configuration was loaded.
/// Diagnostics are returned in a deterministic order whatever the reporting order.
/// </summary>
internal sealed class DiagnosticBag
{
    private readonly Lock _lock = new();
    private readonly List<(DiagnosticDescriptor Descriptor, string Message, DiagnosticLocation? Location)> _entries = [];
    private Func<DiagnosticDescriptor, DiagnosticSeverity> _severityProvider = descriptor => descriptor.DefaultSeverity;

    public bool HasErrors => GetDiagnostics().Any(d => d.Severity is DiagnosticSeverity.Error);

    public void SetSeverityProvider(Func<DiagnosticDescriptor, DiagnosticSeverity> severityProvider)
    {
        lock (_lock)
        {
            _severityProvider = severityProvider;
        }
    }

    public void Report(DiagnosticDescriptor descriptor, string message, DiagnosticLocation? location = null)
    {
        lock (_lock)
        {
            _entries.Add((descriptor, Redaction.Redact(message), location));
        }
    }

    public IReadOnlyList<Diagnostic> GetDiagnostics()
    {
        lock (_lock)
        {
            return [.. _entries
                .Select(entry => new Diagnostic(entry.Descriptor, _severityProvider(entry.Descriptor), entry.Message, entry.Location))
                .Where(d => d.Severity is not DiagnosticSeverity.None)
                .Distinct()
                .Order(DiagnosticComparer.Instance)];
        }
    }

    private sealed class DiagnosticComparer : IComparer<Diagnostic>
    {
        public static DiagnosticComparer Instance { get; } = new();

        public int Compare(Diagnostic? x, Diagnostic? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            var result = y.Severity.CompareTo(x.Severity);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(x.Location?.FilePath, y.Location?.FilePath);
            if (result != 0)
                return result;

            result = Nullable.Compare(x.Location?.Line, y.Location?.Line);
            if (result != 0)
                return result;

            result = Nullable.Compare(x.Location?.Column, y.Location?.Column);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(x.Code, y.Code);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(x.Location?.ToString(), y.Location?.ToString());
            if (result != 0)
                return result;

            return string.CompareOrdinal(x.Message, y.Message);
        }
    }
}
