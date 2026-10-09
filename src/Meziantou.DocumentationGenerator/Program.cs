using System.CommandLine;

namespace Meziantou.DocumentationGenerator;

internal static class Program
{
    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate a fully static documentation site from NuGet packages and Markdown pages.");
        return rootCommand.Parse(args).Invoke();
    }
}
