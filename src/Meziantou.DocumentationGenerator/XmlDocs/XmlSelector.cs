using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// The supported subset of XPath for <c>include</c> and <c>inheritdoc path</c>: '/'-separated element steps (names or
/// <c>*</c>), each with an optional <c>[@attribute='value']</c> predicate, optionally starting with '/'. Other XPath
/// constructs (<c>//</c>, functions, axes, positions) are rejected rather than evaluated.
/// </summary>
internal sealed partial class XmlSelector
{
    private readonly bool _isAbsolute;
    private readonly List<(string Name, string? Attribute, string? Value)> _steps;

    private XmlSelector(bool isAbsolute, List<(string, string?, string?)> steps)
    {
        _isAbsolute = isAbsolute;
        _steps = steps;
    }

    public static XmlSelector? TryParse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.Contains("//", StringComparison.Ordinal))
            return null;

        var value = path.Trim();
        var isAbsolute = value.StartsWith('/', StringComparison.Ordinal);
        var steps = new List<(string, string?, string?)>();
        foreach (var segment in value.Trim('/').Split('/'))
        {
            var match = StepRegex().Match(segment);
            if (!match.Success)
                return null;

            steps.Add((match.Groups["name"].Value, match.Groups["attr"].Success ? match.Groups["attr"].Value : null, match.Groups["value"].Success ? match.Groups["value"].Value : null));
        }

        return new XmlSelector(isAbsolute, steps);
    }

    /// <summary>Evaluates the selector from the document node, as for <c>include</c>: the first step matches the root element.</summary>
    public IReadOnlyList<XElement> SelectFromDocument(XDocument document) => document.Root is null ? [] : Select(document.Root, fromDocument: true);

    /// <summary>Evaluates the selector from <paramref name="context"/>. An absolute selector starts from the document root element.</summary>
    public IReadOnlyList<XElement> Select(XElement context) => Select(context, fromDocument: false);

    private List<XElement> Select(XElement context, bool fromDocument)
    {
        IEnumerable<XElement> current;
        var steps = _steps.AsEnumerable();
        if (_isAbsolute || fromDocument)
        {
            var root = context.Document?.Root ?? context.AncestorsAndSelf().Last();
            var first = _steps[0];
            current = Matches(root, first) ? [root] : [];
            steps = steps.Skip(1);
        }
        else
        {
            current = [context];
        }

        foreach (var step in steps)
        {
            current = current.SelectMany(e => e.Elements()).Where(e => Matches(e, step)).ToList();
        }

        return [.. current];
    }

    private static bool Matches(XElement element, (string Name, string? Attribute, string? Value) step)
    {
        if (step.Name != "*" && element.Name.LocalName != step.Name)
            return false;

        return step.Attribute is null || string.Equals(element.Attribute(step.Attribute)?.Value, step.Value, StringComparison.Ordinal);
    }

    [GeneratedRegex("""^(?<name>\*|[A-Za-z_][A-Za-z0-9_.-]*)(?:\[@(?<attr>[A-Za-z_][A-Za-z0-9_.-]*)=(?:'(?<value>[^']*)'|"(?<value>[^"]*)")\])?$""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepRegex();
}
