namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Receives progress messages. Diagnostics are reported separately through the diagnostic bag.</summary>
internal interface IBuildReporter
{
    void StageStarted(string name);

    void StageCompleted(string name, TimeSpan duration);

    void Verbose(string message);
}
