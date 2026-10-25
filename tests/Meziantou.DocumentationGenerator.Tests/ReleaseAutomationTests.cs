using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.DocumentationGenerator.Tests;

/// <summary>Verifies the safety properties of the release automation, which cannot run outside GitHub Actions.</summary>
public sealed class ReleaseAutomationTests
{
    private static readonly string WorkflowPath = Path.Combine(RepositoryPaths.Root, ".github", "workflows", "ci.yml");

    [Fact]
    public void Workflow_UsesLeastPrivilegeAndGatesPublication()
    {
        var workflow = LoadWorkflow(WorkflowPath);
        var triggers = (YamlMapping)workflow["on"]!;
        Assert.False(triggers.ContainsKey("pull_request_target"));
        Assert.False(triggers.ContainsKey("workflow_run"));
        Assert.Equal("read", Scalar(workflow, "permissions", "contents"));
        Assert.DoesNotContain("secrets.", File.ReadAllText(WorkflowPath));

        var jobs = (YamlMapping)workflow["jobs"]!;
        Assert.False(((YamlMapping)jobs["build"]!).ContainsKey("permissions"));

        // Only builds of main, after every build succeeded, can release
        var releaseCheck = (YamlMapping)jobs["release-check"]!;
        Assert.Equal(["build"], Needs(releaseCheck));
        Assert.Contains("github.event_name != 'pull_request'", Scalar(releaseCheck, "if"), StringComparison.Ordinal);
        Assert.Contains("github.ref == 'refs/heads/main'", Scalar(releaseCheck, "if"), StringComparison.Ordinal);
        Assert.False(releaseCheck.ContainsKey("environment"));
        Assert.False(releaseCheck.ContainsKey("permissions"));

        var publish = (YamlMapping)jobs["publish"]!;
        Assert.Equal(["release-check"], Needs(publish));
        Assert.Contains("needs.release-check.outputs.release == 'true'", Scalar(publish, "if"), StringComparison.Ordinal);
        Assert.Equal("nuget", Scalar(publish, "environment"));
        Assert.Equal("write", Scalar(publish, "permissions", "id-token"));
        Assert.Equal("read", Scalar(publish, "permissions", "contents"));

        // The GitHub release and the site are only created after the package is published
        Assert.Contains("publish", Needs((YamlMapping)jobs["github-release"]!));
        Assert.Contains("publish", Needs((YamlMapping)jobs["deploy-site"]!));

        // The exact validated package is downloaded: nothing is built after validation
        var steps = ((YamlSequence)publish["steps"]!).Cast<YamlMapping>().ToList();
        var commands = steps.Where(s => s.ContainsKey("run")).Select(s => ((YamlValue)s["run"]!).Value).ToList();
        Assert.DoesNotContain(commands, c => c.Contains("dotnet build", StringComparison.Ordinal) || c.Contains("dotnet pack", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.ContainsKey("uses") && ((YamlValue)s["uses"]!).Value.StartsWith("actions/download-artifact", StringComparison.Ordinal));
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

    private static IReadOnlyList<string> Needs(YamlMapping job) => job["needs"] switch
    {
        YamlValue value => [value.Value],
        YamlSequence sequence => [.. sequence.Select(e => ((YamlValue)e).Value)],
        _ => [],
    };
}
