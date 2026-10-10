using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>
/// Namespace, type and member selection rules.
/// A pattern is an optional documentation ID prefix (<c>N:</c>, <c>T:</c>, <c>M:</c>, <c>P:</c>, <c>F:</c>, <c>E:</c>) followed by a qualified name where <c>*</c> matches any sequence of characters.
/// A pattern matches a symbol when it matches the symbol or one of its containers (namespace or declaring types), so excluding a namespace excludes its types and members.
/// Include patterns select namespaces and types; exclude patterns apply to namespaces, types and members and take precedence.
/// </summary>
internal sealed class ApiFilter
{
    public static ApiFilter All { get; } = new([], []);

    private readonly ImmutableArray<Pattern> _include;
    private readonly ImmutableArray<Pattern> _exclude;

    private ApiFilter(ImmutableArray<Pattern> include, ImmutableArray<Pattern> exclude)
    {
        _include = include;
        _exclude = exclude;
    }

    public static bool TryCreate(IEnumerable<string> include, IEnumerable<string> exclude, out ApiFilter filter, out string? error)
    {
        var includePatterns = ImmutableArray.CreateBuilder<Pattern>();
        var excludePatterns = ImmutableArray.CreateBuilder<Pattern>();
        filter = All;
        foreach (var value in include)
        {
            if (!Pattern.TryParse(value, out var pattern, out error))
                return false;

            if (pattern.Kind is not (null or 'N' or 'T'))
            {
                error = $"include pattern '{value}' must target a namespace or a type";
                return false;
            }

            includePatterns.Add(pattern);
        }

        foreach (var value in exclude)
        {
            if (!Pattern.TryParse(value, out var pattern, out error))
                return false;

            excludePatterns.Add(pattern);
        }

        filter = new ApiFilter(includePatterns.ToImmutable(), excludePatterns.ToImmutable());
        error = null;
        return true;
    }

    /// <param name="namespaceName">Namespace of the type, empty for the global namespace.</param>
    /// <param name="typeNames">Qualified names of the type and its declaring types, outermost first, such as <c>Contoso.Outer</c>, <c>Contoso.Outer.Inner</c>.</param>
    public bool IsTypeIncluded(string namespaceName, IReadOnlyList<string> typeNames)
    {
        var candidates = GetCandidates(namespaceName, typeNames);
        if (_exclude.Any(p => candidates.Any(c => p.IsMatch(c.Kind, c.Name))))
            return false;

        if (_include.IsEmpty)
            return true;

        return _include.Any(p => candidates.Any(c => p.IsMatch(c.Kind, c.Name)));
    }

    /// <param name="kind">Documentation ID prefix of the member: M, P, F or E.</param>
    /// <param name="memberName">Qualified name of the member without parameters, such as <c>Contoso.Type.Method</c>.</param>
    public bool IsMemberIncluded(char kind, string memberName)
    {
        return !_exclude.Any(p => p.IsMatch(kind, memberName));
    }

    private static List<(char Kind, string Name)> GetCandidates(string namespaceName, IReadOnlyList<string> typeNames)
    {
        var result = new List<(char, string)>();
        if (!string.IsNullOrEmpty(namespaceName))
        {
            var parts = namespaceName.Split('.');
            for (var i = 1; i <= parts.Length; i++)
            {
                result.Add(('N', string.Join('.', parts, 0, i)));
            }
        }

        foreach (var typeName in typeNames)
        {
            result.Add(('T', typeName));
        }

        return result;
    }

    private sealed class Pattern
    {
        private readonly Regex _regex;

        private Pattern(char? kind, Regex regex)
        {
            Kind = kind;
            _regex = regex;
        }

        public char? Kind { get; }

        public static bool TryParse(string value, out Pattern pattern, out string? error)
        {
            pattern = null!;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "a pattern cannot be empty";
                return false;
            }

            value = value.Trim();
            char? kind = null;
            if (value.Length > 2 && value[1] == ':')
            {
                kind = value[0];
                if (kind is not ('N' or 'T' or 'M' or 'P' or 'F' or 'E'))
                {
                    error = $"pattern '{value}' has an unknown prefix '{kind}:'; supported prefixes are N:, T:, M:, P:, F: and E:";
                    return false;
                }

                value = value[2..];
            }

            foreach (var c in value)
            {
                if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or '*' or '`'))
                {
                    error = $"pattern '{value}' contains the invalid character '{c}'";
                    return false;
                }
            }

            var regex = new StringBuilder("^");
            foreach (var c in value)
            {
                regex.Append(c == '*' ? ".*" : Regex.Escape(c.ToString()));
            }

            regex.Append('$');
            pattern = new Pattern(kind, new Regex(regex.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            return true;
        }

        public bool IsMatch(char kind, string name)
        {
            if (Kind is not null && Kind != kind)
                return false;

            return _regex.IsMatch(name);
        }
    }
}
