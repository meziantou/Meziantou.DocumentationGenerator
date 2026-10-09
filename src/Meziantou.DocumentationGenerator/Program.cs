using Meziantou.DocumentationGenerator.Cli;

namespace Meziantou.DocumentationGenerator;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            // Let the build stop gracefully so that the output directory is left untouched
            e.Cancel = true;
            cancellation.Cancel();
        };

        var application = new CommandLineApplication(Console.Out, Console.Error);
        return await application.RunAsync(args, cancellation.Token);
    }
}
