using System.Globalization;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Formats the durations of the progress messages.</summary>
internal static class BuildDuration
{
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:F0} ms");

        if (duration.TotalMinutes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:F1} s");

        return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds} s");
    }
}
