using System.Globalization;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>
/// Preprocessor symbols the .NET SDK defines for a target framework in a Release build, such as <c>NET8_0</c>,
/// <c>NET6_0_OR_GREATER</c>, <c>NETSTANDARD2_0</c> or <c>NET472</c>, so that source packages are compiled with the
/// <c>#if</c> branches a consuming project would see.
/// </summary>
internal static class FrameworkPreprocessorSymbols
{
    private static readonly Version[] NetCoreAppVersions = [new(1, 0), new(1, 1), new(2, 0), new(2, 1), new(2, 2), new(3, 0), new(3, 1)];
    private static readonly Version[] NetStandardVersions = [new(1, 0), new(1, 1), new(1, 2), new(1, 3), new(1, 4), new(1, 5), new(1, 6), new(2, 0), new(2, 1)];
    private static readonly Version[] NetFrameworkVersions = [new(2, 0), new(3, 5), new(4, 0), new(4, 5), new(4, 5, 1), new(4, 5, 2), new(4, 6), new(4, 6, 1), new(4, 6, 2), new(4, 7), new(4, 7, 1), new(4, 7, 2), new(4, 8), new(4, 8, 1)];

    public static IReadOnlyList<string> Get(NuGetFramework framework)
    {
        var symbols = new List<string> { "TRACE", "RELEASE" };
        var version = framework.Version;
        switch (framework.Framework)
        {
            case FrameworkConstants.FrameworkIdentifiers.NetCoreApp when version.Major >= 5:
                symbols.Add("NET");
                symbols.Add("NETCOREAPP");
                symbols.Add($"NET{Format(version, "_")}");
                for (var major = 5; major <= version.Major; major++)
                {
                    symbols.Add(string.Create(CultureInfo.InvariantCulture, $"NET{major}_0_OR_GREATER"));
                }

                symbols.AddRange(NetCoreAppVersions.Select(v => $"NETCOREAPP{Format(v, "_")}_OR_GREATER"));
                if (framework.HasPlatform)
                {
                    var platform = framework.Platform.ToUpperInvariant();
                    symbols.Add(platform);
                    symbols.Add(platform + "_OR_GREATER");
                }

                break;

            case FrameworkConstants.FrameworkIdentifiers.NetCoreApp:
                symbols.Add("NETCOREAPP");
                symbols.Add($"NETCOREAPP{Format(version, "_")}");
                symbols.AddRange(NetCoreAppVersions.Where(v => v <= Normalize(version)).Select(v => $"NETCOREAPP{Format(v, "_")}_OR_GREATER"));
                break;

            case FrameworkConstants.FrameworkIdentifiers.NetStandard:
                symbols.Add("NETSTANDARD");
                symbols.Add($"NETSTANDARD{Format(version, "_")}");
                symbols.AddRange(NetStandardVersions.Where(v => v <= Normalize(version)).Select(v => $"NETSTANDARD{Format(v, "_")}_OR_GREATER"));
                break;

            case FrameworkConstants.FrameworkIdentifiers.Net:
                symbols.Add("NETFRAMEWORK");
                symbols.Add(framework.GetShortFolderName().ToUpperInvariant());
                symbols.AddRange(NetFrameworkVersions.Where(v => v <= Normalize(version)).Select(v => $"NET{Format(v, "")}_OR_GREATER"));
                break;
        }

        return symbols;
    }

    /// <summary>Formats the significant parts of a version: <c>8.0</c> as <c>8_0</c>, <c>4.7.2</c> as <c>472</c>.</summary>
    private static string Format(Version version, string separator)
    {
        var parts = new List<int> { version.Major, version.Minor };
        if (version.Build > 0)
        {
            parts.Add(version.Build);
        }

        return string.Join(separator, parts.Select(p => p.ToString(CultureInfo.InvariantCulture)));
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
