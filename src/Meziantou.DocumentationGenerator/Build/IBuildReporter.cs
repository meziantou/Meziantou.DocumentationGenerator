namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Receives progress messages. Diagnostics are reported separately through the diagnostic bag.</summary>
internal interface IBuildReporter
{
    void StageStarted(string name);

    void StageCompleted(string name, TimeSpan duration);

    /// <summary>Reports a progress or timing message shown with the normal verbosity.</summary>
    void Info(string message);

    void Verbose(string message);
}
