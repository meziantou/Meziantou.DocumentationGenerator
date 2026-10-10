namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <param name="Code">Stable identifier such as <c>DG1001</c>.</param>
/// <param name="Title">Short description used in documentation.</param>
/// <param name="DefaultSeverity">Severity used when the configuration does not override it.</param>
/// <param name="IsSafetyCritical">Diagnostics reporting unsafe input cannot be lowered below <see cref="DiagnosticSeverity.Error"/>.</param>
internal sealed record DiagnosticDescriptor(string Code, string Title, DiagnosticSeverity DefaultSeverity, bool IsSafetyCritical = false);
