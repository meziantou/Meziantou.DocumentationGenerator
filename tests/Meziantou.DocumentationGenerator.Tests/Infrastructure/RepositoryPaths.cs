namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string ToolProject => Path.Combine(Root, "src", "Meziantou.DocumentationGenerator", "Meziantou.DocumentationGenerator.csproj");

    private static string FindRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Meziantou.DocumentationGenerator.slnx")))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Cannot find the repository root");
    }
}
