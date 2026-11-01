using System.Globalization;
using Meziantou.DocumentationGenerator.Build;

namespace Meziantou.DocumentationGenerator.Cli;

internal sealed class ConsoleBuildReporter(TextWriter output, Verbosity verbosity) : IBuildReporter
{
    public void StageStarted(string name)
    {
        if (verbosity >= Verbosity.Normal)
        {
            output.WriteLine($"{name}...");
        }
    }

    public void StageCompleted(string name, TimeSpan duration)
    {
        if (verbosity >= Verbosity.Normal)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name} completed in {BuildDuration.Format(duration)}"));
        }
    }

    public void Info(string message)
    {
        if (verbosity >= Verbosity.Normal)
        {
            output.WriteLine(message);
        }
    }

    public void Verbose(string message)
    {
        if (verbosity >= Verbosity.Detailed)
        {
            output.WriteLine(message);
        }
    }
}
