using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Meziantou.DocumentationGenerator.Configuration;

namespace Meziantou.DocumentationGenerator.Cli;

/// <summary>Exposes configuration properties using the same serialization model as docgen.json.</summary>
internal sealed class ConfigurationCommandLineOptions
{
    private readonly List<(Option<JsonNode?> Option, JsonPropertyInfo[] Path)> _options = [];

    public ConfigurationCommandLineOptions(Command command)
    {
        AddProperties(command, typeof(ConfigurationFile), []);
    }

    public ConfigurationOverrides GetOverrides(ParseResult parseResult)
    {
        var file = new ConfigurationFile();
        foreach (var (option, path) in _options)
        {
            if (parseResult.GetValue(option) is not { } value)
                continue;

            object section = file;
            foreach (var property in path[..^1])
            {
                var next = property.Get!(section);
                if (next is null)
                {
                    next = ConfigurationLoader.SerializerOptions.GetTypeInfo(property.PropertyType).CreateObject!();
                    property.Set!(section, next);
                }

                section = next;
            }

            var leaf = path[^1];
            leaf.Set!(section, value.Deserialize(leaf.PropertyType, ConfigurationLoader.SerializerOptions));
        }

        return new ConfigurationOverrides { Values = file };
    }

    private void AddProperties(Command command, Type type, JsonPropertyInfo[] path)
    {
        foreach (var property in ConfigurationLoader.SerializerOptions.GetTypeInfo(type).Properties)
        {
            JsonPropertyInfo[] propertyPath = [.. path, property];
            var typeInfo = ConfigurationLoader.SerializerOptions.GetTypeInfo(property.PropertyType);
            if (typeInfo.Kind is JsonTypeInfoKind.Object)
            {
                AddProperties(command, property.PropertyType, propertyPath);
                continue;
            }

            var name = "--" + string.Join('-', propertyPath.Select(p => ToKebabCase(p.Name.TrimStart('$'))));
            var isBoolean = property.PropertyType == typeof(bool?);
            var isString = property.PropertyType == typeof(string);
            var description = property.AttributeProvider?.GetCustomAttributes(inherit: true).OfType<DescriptionAttribute>().FirstOrDefault()?.Description;
            var option = new Option<JsonNode?>(name)
            {
                Description = $"Overrides '{string.Join('.', propertyPath.Select(p => p.Name))}'. {description}" +
                    (typeInfo.Kind switch
                    {
                        JsonTypeInfoKind.Enumerable => " Pass a JSON array; replaces the configured collection.",
                        JsonTypeInfoKind.Dictionary => " Pass a JSON object; replaces the configured collection.",
                        _ => "",
                    }) +
                    (isBoolean ? " Omit the value for true, or pass true or false explicitly." : "") +
                    (name == "--output" ? " On the command line, relative paths are resolved against the current directory." : ""),
                Arity = isBoolean ? ArgumentArity.ZeroOrOne : ArgumentArity.ExactlyOne,
                CustomParser = result =>
                {
                    var text = result.Tokens.Count == 0 ? "true" : result.Tokens[0].Value;
                    try
                    {
                        var node = isString ? JsonValue.Create(text) : JsonNode.Parse(text);
                        if (node is null)
                        {
                            result.AddError($"The value of {name} must not be null.");
                            return null;
                        }

                        _ = node.Deserialize(property.PropertyType, ConfigurationLoader.SerializerOptions);
                        return node;
                    }
                    catch (JsonException ex)
                    {
                        result.AddError($"Invalid value for {name}: {ex.Message}");
                        return null;
                    }
                },
            };

            switch (name)
            {
                case "--output":
                    option.Aliases.Add("-o");
                    break;
                case "--site-base-path":
                    option.Aliases.Add("--base-path");
                    break;
                case "--diagnostics-warnings-as-errors":
                    option.Aliases.Add("--warnaserror");
                    break;
                case "--diagnostics-allow-invalid-cref":
                    option.Aliases.Add("--allow-invalid-cref");
                    break;
            }

            command.Options.Add(option);
            _options.Add((option, propertyPath));
        }
    }

    private static string ToKebabCase(string value)
    {
        return string.Concat(value.Select((c, index) => char.IsUpper(c) ? (index == 0 ? "" : "-") + char.ToLowerInvariant(c) : c.ToString()));
    }
}
