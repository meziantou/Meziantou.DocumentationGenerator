namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <summary>Thread-safe collection of diagnostics. Diagnostics are returned in a deterministic order whatever the reporting order.</summary>
internal sealed class DiagnosticBag
{
    private readonly Lock _lock = new();
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Func<DiagnosticDescriptor, DiagnosticSeverity> _severityProvider;

    public DiagnosticBag()
        : this(descriptor => descriptor.DefaultSeverity)
    {
    }

    public DiagnosticBag(Func<DiagnosticDescriptor, DiagnosticSeverity> severityProvider)
    {
        _severityProvider = severityProvider;
    }

    public bool HasErrors
    {
        get
        {
            lock (_lock)
            {
                return _diagnostics.Exists(d => d.Severity is DiagnosticSeverity.Error);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _diagnostics.Count;
            }
        }
    }

    public void Report(DiagnosticDescriptor descriptor, string message, DiagnosticLocation? location = null)
    {
        var severity = _severityProvider(descriptor);
        if (severity is DiagnosticSeverity.None)
            return;

        var diagnostic = new Diagnostic(descriptor, severity, Redaction.Redact(message), location);
        lock (_lock)
        {
            _diagnostics.Add(diagnostic);
        }
    }

    public IReadOnlyList<Diagnostic> GetDiagnostics()
    {
        lock (_lock)
        {
            return [.. _diagnostics.Distinct().Order(DiagnosticComparer.Instance)];
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
