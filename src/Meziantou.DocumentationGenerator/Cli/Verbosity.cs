namespace Meziantou.DocumentationGenerator.Cli;

internal enum Verbosity
{
    /// <summary>Errors and the final summary.</summary>
    Quiet,

    /// <summary>Errors, warnings and the final summary.</summary>
    Minimal,

    /// <summary>Stage progress, errors, warnings and the final summary.</summary>
    Normal,

    /// <summary>Everything, including informational diagnostics and exception details.</summary>
    Detailed,
}
