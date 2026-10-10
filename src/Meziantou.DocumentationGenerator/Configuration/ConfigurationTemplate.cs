using System.Text.Json;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Creates the initial configuration written by the <c>init</c> command.</summary>
internal static class ConfigurationTemplate
{
    public static string Create(string? packageId, string? packageVersion, string? siteTitle)
    {
        var file = new ConfigurationFile
        {
            Schema = ConfigurationSchema.SchemaUrl,
            Version = ConfigurationFile.CurrentVersion,
            Site = new ConfigurationFile.SiteSection
            {
                Title = siteTitle ?? "Documentation",
                BasePath = "/",
            },
            Output = "_site",
            Packages = packageId is null ? [] : [new ConfigurationFile.PackageSection { Id = packageId, Version = packageVersion ?? "1.0.0" }],
            Content = new ConfigurationFile.ContentSection { Root = "docs" },
        };

        return JsonSerializer.Serialize(file, ConfigurationLoader.SerializerOptions).ReplaceLineEndings("\n") + "\n";
    }
}
