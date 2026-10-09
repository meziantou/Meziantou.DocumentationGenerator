using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

[Collection(InstalledToolCollection.Name)]
public sealed class ToolPackageTests(InstalledToolFixture fixture)
{
    internal const string ToolCommandName = "meziantou-docgen";

    [Fact]
    public async Task PackAndInstallAsLocalTool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.Tool.RunAsync(["--version"], cancellationToken);
        Assert.Equal(0, version.ExitCode, version.AllOutput);
        Assert.Matches(@"^\d+\.\d+\.\d+", version.Output.Trim());

        var help = await fixture.Tool.RunAsync(["--help"], cancellationToken);
        Assert.Equal(0, help.ExitCode, help.AllOutput);
        Assert.Contains(ToolCommandName, help.Output);
        Assert.Contains("init", help.Output);
        Assert.Contains("build", help.Output);
    }
}
