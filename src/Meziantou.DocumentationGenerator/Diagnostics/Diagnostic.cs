namespace Meziantou.DocumentationGenerator.Diagnostics;

internal sealed record Diagnostic(DiagnosticDescriptor Descriptor, DiagnosticSeverity Severity, string Message, DiagnosticLocation? Location)
{
    public string Code => Descriptor.Code;

    public override string ToString()
    {
        var severity = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        var location = Location?.ToString();
        return string.IsNullOrEmpty(location) ? $"{severity} {Code}: {Message}" : $"{location}: {severity} {Code}: {Message}";
    }
}
