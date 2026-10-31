using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Values provided on the command line, which take precedence over the configuration file.</summary>
internal sealed class ConfigurationOverrides
{
    public static ConfigurationOverrides None { get; } = new();

    public ConfigurationFile? Values { get; init; }
    public FullPath? OutputDirectory { get; init; }
    public string? BasePath { get; init; }
    public string? SiteUrl { get; init; }
    public bool DisableSourceLinks { get; init; }

    public void ApplyTo(ConfigurationFile file)
    {
        if (Values is not null)
        {
            Merge(file, Values, ConfigurationLoader.SerializerOptions.GetTypeInfo(typeof(ConfigurationFile)));
        }
    }

    private static void Merge(object target, object source, JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.Get!(source) is not { } value)
                continue;

            var propertyTypeInfo = ConfigurationLoader.SerializerOptions.GetTypeInfo(property.PropertyType);
            if (propertyTypeInfo.Kind is JsonTypeInfoKind.Object)
            {
                var section = property.Get(target) ?? propertyTypeInfo.CreateObject!();
                Merge(section, value, propertyTypeInfo);
                property.Set!(target, section);
            }
            else
            {
                property.Set!(target, value);
            }
        }
    }
}
