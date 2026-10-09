using System.Collections.Immutable;
using System.Xml.Linq;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>Documentation of a symbol for one target framework, split into sections. Section content is kept as XML until rendering.</summary>
internal sealed record DocumentationComment
{
    public static DocumentationComment Empty { get; } = new();

    public XElement? Summary { get; init; }
    public XElement? Remarks { get; init; }
    public XElement? Returns { get; init; }
    public XElement? Value { get; init; }
    public ImmutableArray<XElement> Examples { get; init; } = [];
    public ImmutableArray<NamedSection> Parameters { get; init; } = [];
    public ImmutableArray<NamedSection> TypeParameters { get; init; } = [];

    /// <summary>Exceptions, keyed by the <c>cref</c> of the exception type.</summary>
    public ImmutableArray<NamedSection> Exceptions { get; init; } = [];
    public ImmutableArray<XElement> SeeAlso { get; init; } = [];

    /// <summary><c>&lt;inheritdoc/&gt;</c> directive, resolved by the inheritance stage.</summary>
    public XElement? InheritDoc { get; init; }

    /// <summary><c>&lt;include/&gt;</c> directives, resolved by the inheritance stage.</summary>
    public ImmutableArray<XElement> Includes { get; init; } = [];

    /// <summary>Assembly scope and XML file the documentation comes from, used to resolve references in the right context.</summary>
    public string? Scope { get; init; }
    public string? SourcePath { get; init; }

    public bool IsEmpty => Summary is null && Remarks is null && Returns is null && Value is null && Examples.IsEmpty && Parameters.IsEmpty && TypeParameters.IsEmpty && Exceptions.IsEmpty && SeeAlso.IsEmpty;

    /// <summary>Canonical text used to detect documentation that differs between target frameworks.</summary>
    public string GetComparisonKey()
    {
        static string Text(XElement? element) => element is null ? "" : element.ToString(SaveOptions.DisableFormatting);
        return string.Join(
            "\u0001",
            Text(Summary),
            Text(Remarks),
            Text(Returns),
            Text(Value),
            string.Join('\u0002', Examples.Select(Text)),
            string.Join('\u0002', Parameters.Select(p => p.Name + "=" + Text(p.Content))),
            string.Join('\u0002', TypeParameters.Select(p => p.Name + "=" + Text(p.Content))),
            string.Join('\u0002', Exceptions.Select(p => p.Name + "=" + Text(p.Content))),
            string.Join('\u0002', SeeAlso.Select(Text)));
    }
}
