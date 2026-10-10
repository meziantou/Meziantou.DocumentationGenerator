using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Cli;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal static class CliRunner
{
    public static Task<ProcessResult> RunAsync(string[] args, Func<BuildContext, IReadOnlyList<IBuildStage>>? stages = null)
    {
        return RunWithCancellationAsync(args, stages, TestContext.Current.CancellationToken);
    }

    public static async Task<ProcessResult> RunWithCancellationAsync(string[] args, Func<BuildContext, IReadOnlyList<IBuildStage>>? stages, CancellationToken cancellationToken)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandLineApplication(output, error, stages);
        var exitCode = await application.RunAsync(args, cancellationToken);
        return new ProcessResult(exitCode, output.ToString(), error.ToString());
    }
}
