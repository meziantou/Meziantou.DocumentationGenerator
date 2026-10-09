namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public string AllOutput => Output + Error;
}
