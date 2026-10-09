using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Builds a complete site with the command-line application from local packages and content files.</summary>
internal sealed class SiteBuilder : IAsyncDisposable
{
    private SiteBuilder(TemporaryDirectory directory, ProcessResult result)
    {
        Directory = directory;
        Result = result;
    }

    public TemporaryDirectory Directory { get; }
    public ProcessResult Result { get; }
    public FullPath Output => Directory.FullPath / "_site";

    public static async Task<SiteBuilder> BuildAsync(IReadOnlyList<TestPackageBuilder> packages, Dictionary<string, string>? files = null, string extraConfiguration = "", string[]? arguments = null)
    {
        var directory = TemporaryDirectory.Create();
        foreach (var package in packages)
        {
            package.Save(directory.FullPath / "packages");
        }

        foreach (var (path, content) in files ?? [])
        {
            directory.CreateTextFile(path, content);
        }

        var contentConfiguration = files?.Keys.Any(k => k.StartsWith("docs/", StringComparison.Ordinal)) == true ? """ "content": { "root": "docs" }, """ : "";
        directory.CreateTextFile("docgen.json", $$"""
            {
              "version": 1,
              "cacheDirectory": "cache",
              {{contentConfiguration}}
              {{(extraConfiguration.Contains("\"site\"", StringComparison.Ordinal) ? "" : "\"site\": { \"title\": \"Test site\" },")}}
              {{extraConfiguration}}
              "packages": [ {{string.Join(", ", packages.Select(p => TestBuild.PackageReference(p)))}} ]
            }
            """);
        var result = await CliRunner.RunAsync(["build", "--config", directory.FullPath / "docgen.json", "--verbosity", "detailed", .. arguments ?? []]);
        return new SiteBuilder(directory, result);
    }

    public string ReadFile(string relativePath) => File.ReadAllText(Output / relativePath);

    public ValueTask DisposeAsync() => Directory.DisposeAsync();
}
