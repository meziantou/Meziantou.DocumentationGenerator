using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class ToolPackageTests
{
    internal const string ToolCommandName = "meziantou-docgen";

    [Fact]
    public async Task PackAndInstallAsLocalTool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var temp = TemporaryDirectory.Create();
        var tool = await InstalledTool.CreateAsync(temp.FullPath, cancellationToken);

        var version = await tool.RunAsync(["--version"], cancellationToken);
        Assert.Equal(0, version.ExitCode, version.AllOutput);
        Assert.StartsWith(InstalledTool.Version, version.Output.Trim());

        var help = await tool.RunAsync(["--help"], cancellationToken);
        Assert.Equal(0, help.ExitCode, help.AllOutput);
        Assert.Contains(ToolCommandName, help.Output);
    }
}
