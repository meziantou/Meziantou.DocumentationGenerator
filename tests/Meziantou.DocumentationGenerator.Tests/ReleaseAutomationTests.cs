using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.DocumentationGenerator.Tests;

/// <summary>Verifies the safety properties of the release automation, which cannot run outside GitHub Actions.</summary>
public sealed class ReleaseAutomationTests
{
    private static readonly string ReleaseWorkflowPath = Path.Combine(RepositoryPaths.Root, ".github", "workflows", "release.yml");

    [Fact]
    public void ReleaseWorkflow_UsesLeastPrivilegeAndGatesPublication()
    {
        var workflow = LoadWorkflow(ReleaseWorkflowPath);
        var triggers = (YamlMapping)workflow["on"]!;
        Assert.False(triggers.ContainsKey("pull_request"));
        Assert.False(triggers.ContainsKey("pull_request_target"));
        Assert.Equal("read", Scalar(workflow, "permissions", "contents"));

        var jobs = (YamlMapping)workflow["jobs"]!;
        var publish = (YamlMapping)jobs["publish"]!;
        Assert.Equal(["validate", "test"], Sequence(publish["needs"]));
        Assert.Equal("nuget", Scalar(publish, "environment"));
        Assert.Equal("write", Scalar(publish, "permissions", "id-token"));
        Assert.Equal("read", Scalar(publish, "permissions", "contents"));
        Assert.Contains("inputs.publish", Scalar(publish, "if"));

        Assert.Contains("publish", Sequence(((YamlMapping)jobs["github-release"]!)["needs"]));
        Assert.Contains("publish", Sequence(((YamlMapping)jobs["deploy-site"]!)["needs"]));

        // The exact validated package is downloaded: nothing is built after validation, and no stored secret is used
        Assert.DoesNotContain("secrets.", File.ReadAllText(ReleaseWorkflowPath));
        var steps = ((YamlSequence)publish["steps"]!).Cast<YamlMapping>().ToList();
        var commands = steps.Where(s => s.ContainsKey("run")).Select(s => ((YamlValue)s["run"]!).Value).ToList();
        Assert.DoesNotContain(commands, c => c.Contains("dotnet build", StringComparison.Ordinal) || c.Contains("dotnet pack", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.ContainsKey("uses") && ((YamlValue)s["uses"]!).Value.StartsWith("actions/download-artifact", StringComparison.Ordinal));
    }

    [Fact]
    public void CiWorkflow_HasReadOnlyPermissionsAndNoSecrets()
    {
        var path = Path.Combine(RepositoryPaths.Root, ".github", "workflows", "ci.yml");
        var workflow = LoadWorkflow(path);
        Assert.Equal("read", Scalar(workflow, "permissions", "contents"));
        Assert.DoesNotContain("secrets.", File.ReadAllText(path));
    }

    private static YamlMapping LoadWorkflow(string path) => (YamlMapping)YamlStream.Load(new StringReader(File.ReadAllText(path)))[0].Contents!;

    private static string Scalar(YamlMapping mapping, params string[] keys)
    {
        YamlElement current = mapping;
        foreach (var key in keys)
        {
            current = ((YamlMapping)current)[key]!;
        }

        return ((YamlValue)current).Value;
    }

    private static IReadOnlyList<string> Sequence(YamlElement? element) => [.. ((YamlSequence)element!).Select(e => ((YamlValue)e).Value)];
}
