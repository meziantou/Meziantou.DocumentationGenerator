namespace Meziantou.DocumentationGenerator.Cli;

internal static class ExitCodes
{
    public const int Success = 0;

    /// <summary>The configuration or an input is invalid, or a stage reported an error. The output directory was not modified.</summary>
    public const int BuildFailed = 1;

    /// <summary>The command line is invalid.</summary>
    public const int InvalidArguments = 2;

    /// <summary>The build was cancelled (Ctrl+C). The output directory was not modified.</summary>
    public const int Cancelled = 130;
}
