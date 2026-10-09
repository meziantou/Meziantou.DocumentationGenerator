using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Values provided on the command line, which take precedence over the configuration file.</summary>
internal sealed class ConfigurationOverrides
{
    public static ConfigurationOverrides None { get; } = new();

    public FullPath? OutputDirectory { get; init; }
    public string? BasePath { get; init; }
    public string? SiteUrl { get; init; }
    public bool DisableSourceLinks { get; init; }
}
