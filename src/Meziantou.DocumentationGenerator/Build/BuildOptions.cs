using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Command-line options. Values set here override the configuration file.</summary>
internal sealed class BuildOptions
{
    public required FullPath ConfigurationFile { get; init; }
    public bool AllowMissingConfigurationFile { get; init; }
    public ConfigurationFile? ConfigurationValues { get; init; }
    public FullPath? OutputDirectory { get; init; }
    public string? BasePath { get; init; }
    public string? SiteUrl { get; init; }
    public bool WarningsAsErrors { get; init; }

    /// <summary>Use only cached packages; never contact a package source.</summary>
    public bool Offline { get; init; }
    public bool DisableSourceLinks { get; init; }
}
