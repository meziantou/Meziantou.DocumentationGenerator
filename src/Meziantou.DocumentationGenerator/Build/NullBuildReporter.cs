namespace Meziantou.DocumentationGenerator.Build;

internal sealed class NullBuildReporter : IBuildReporter
{
    public static NullBuildReporter Instance { get; } = new();

    public void StageStarted(string name)
    {
    }

    public void StageCompleted(string name, TimeSpan duration)
    {
    }

    public void Info(string message)
    {
    }

    public void Verbose(string message)
    {
    }
}
