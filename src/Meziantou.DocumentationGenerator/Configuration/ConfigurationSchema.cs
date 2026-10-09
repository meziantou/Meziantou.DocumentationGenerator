using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Generates the JSON schema of <c>docgen.json</c> from <see cref="ConfigurationFile"/>.</summary>
internal static class ConfigurationSchema
{
    public const string SchemaUrl = "https://raw.githubusercontent.com/meziantou/Meziantou.DocumentationGenerator/main/schema/docgen.schema.json";

    public static string Generate()
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (context, schema) =>
            {
                if (schema is not JsonObject obj)
                    return schema;

                var attributeProvider = context.PropertyInfo is not null ? context.PropertyInfo.AttributeProvider : context.TypeInfo.Type;
                var description = attributeProvider?.GetCustomAttributes(inherit: true).OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
                if (description is not null)
                {
                    obj.Insert(0, "description", description);
                }

                // Configuration values are optional: allow omitting them but not setting them to null
                if (obj["type"] is JsonArray types && types.Count == 2 && types.Any(t => t?.GetValue<string>() == "null"))
                {
                    obj["type"] = types.First(t => t?.GetValue<string>() != "null")!.GetValue<string>();
                }

                if (obj["type"]?.GetValue<string>() == "object" && context.TypeInfo.Type != typeof(Dictionary<string, string>))
                {
                    obj["additionalProperties"] = false;
                }

                return obj;
            },
        };

        var schema = (JsonObject)JsonSchemaExporter.GetJsonSchemaAsNode(ConfigurationLoader.SerializerOptions, typeof(ConfigurationFile), exporterOptions);
        schema.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
        schema.Insert(1, "$id", SchemaUrl);
        schema.Insert(2, "title", "Meziantou.DocumentationGenerator configuration");
        if (schema["properties"]?["version"] is JsonObject version)
        {
            version["const"] = ConfigurationFile.CurrentVersion;
        }

        if (schema["properties"]?["site"]?["properties"]?["robots"] is JsonObject robots)
        {
            robots["enum"] = new JsonArray("none", "allow", "disallow");
        }

        schema["required"] = new JsonArray("version");
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }).ReplaceLineEndings("\n") + "\n";
    }
}
