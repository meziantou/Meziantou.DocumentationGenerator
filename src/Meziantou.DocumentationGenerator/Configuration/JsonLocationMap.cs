using System.Text;
using System.Text.Json;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Maps JSON paths such as <c>$.packages[0].version</c> to their line and column in the source document.</summary>
internal sealed class JsonLocationMap
{
    private readonly Dictionary<string, (int Line, int Column)> _locations;

    private JsonLocationMap(Dictionary<string, (int Line, int Column)> locations)
    {
        _locations = locations;
    }

    public (int Line, int Column)? Find(string jsonPath)
    {
        // Fall back to the closest parent so that a missing property is reported on its parent object
        var path = jsonPath;
        while (true)
        {
            if (_locations.TryGetValue(path, out var location))
                return location;

            var index = path.AsSpan().LastIndexOfAny('.', '[');
            if (index <= 0)
                return null;

            path = path[..index];
        }
    }

    public static JsonLocationMap Create(ReadOnlySpan<byte> utf8Json, JsonReaderOptions options)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < utf8Json.Length; i++)
        {
            if (utf8Json[i] == (byte)'\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        var locations = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(utf8Json, options);
        var stack = new Stack<Frame>();
        string? pendingProperty = null;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pendingProperty = AppendProperty(CurrentPath(stack), reader.GetString()!);
                        locations[pendingProperty] = GetLocation(utf8Json, lineStarts, (int)reader.TokenStartIndex);
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        {
                            var path = GetValuePath(stack, ref pendingProperty);
                            locations.TryAdd(path, GetLocation(utf8Json, lineStarts, (int)reader.TokenStartIndex));
                            stack.Push(new Frame(path, reader.TokenType == JsonTokenType.StartArray));
                            break;
                        }

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        stack.Pop();
                        break;

                    case JsonTokenType.Comment:
                        break;

                    default:
                        {
                            var path = GetValuePath(stack, ref pendingProperty);
                            locations.TryAdd(path, GetLocation(utf8Json, lineStarts, (int)reader.TokenStartIndex));
                            break;
                        }
                }
            }
        }
        catch (JsonException)
        {
            // Syntax errors are reported by the deserializer
        }

        return new JsonLocationMap(locations);
    }

    private static string GetValuePath(Stack<Frame> stack, ref string? pendingProperty)
    {
        if (pendingProperty is not null)
        {
            var result = pendingProperty;
            pendingProperty = null;
            return result;
        }

        if (stack.Count == 0)
            return "$";

        var frame = stack.Peek();
        if (frame.IsArray)
        {
            var path = frame.Path + "[" + frame.NextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            frame.NextIndex++;
            return path;
        }

        return frame.Path;
    }

    private static string CurrentPath(Stack<Frame> stack) => stack.Count == 0 ? "$" : stack.Peek().Path;

    private static string AppendProperty(string parent, string name)
    {
        var simple = name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$');
        return simple ? parent + "." + name : parent + "['" + name.Replace("'", "\\'", StringComparison.Ordinal) + "']";
    }

    private static (int Line, int Column) GetLocation(ReadOnlySpan<byte> utf8Json, List<int> lineStarts, int offset)
    {
        var index = lineStarts.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        var column = Encoding.UTF8.GetCharCount(utf8Json[lineStarts[index]..offset]) + 1;
        return (index + 1, column);
    }

    private sealed class Frame(string path, bool isArray)
    {
        public string Path { get; } = path;
        public bool IsArray { get; } = isArray;
        public int NextIndex { get; set; }
    }
}
