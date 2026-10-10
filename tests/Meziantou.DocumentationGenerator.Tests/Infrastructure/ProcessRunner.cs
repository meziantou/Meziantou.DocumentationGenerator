using System.Diagnostics;
using System.Text;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal static class ProcessRunner
{
    public static string DotnetPath => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (error) { error.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }
}
