using System.Collections.Immutable;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>Splits a <c>&lt;member&gt;</c> element into sections and validates it against the documented symbol.</summary>
internal static class DocumentationCommentParser
{
    /// <summary>Top-level tags that are recognized but not displayed.</summary>
    private static readonly HashSet<string> IgnoredTags = new(StringComparer.Ordinal) { "filterpriority", "completionlist", "threadsafety", "permission", "overloads", "note" };

    public static DocumentationComment Parse(XElement member, PublicApiSymbol? symbol, string? scope, string sourcePath, DiagnosticBag diagnostics)
    {
        var location = new DiagnosticLocation { FilePath = sourcePath, Symbol = member.Attribute("name")?.Value };
        XElement? summary = null, remarks = null, returns = null, value = null, inheritDoc = null;
        var examples = ImmutableArray.CreateBuilder<XElement>();
        var parameters = ImmutableArray.CreateBuilder<NamedSection>();
        var typeParameters = ImmutableArray.CreateBuilder<NamedSection>();
        var exceptions = ImmutableArray.CreateBuilder<NamedSection>();
        var seeAlso = ImmutableArray.CreateBuilder<XElement>();
        var includes = ImmutableArray.CreateBuilder<XElement>();

        var parameterNames = GetParameterNames(symbol);
        var typeParameterNames = GetTypeParameterNames(symbol);

        foreach (var element in member.Elements())
        {
            var elementLocation = location with { Line = XmlDocumentationFile.GetLine(element) };
            switch (element.Name.LocalName)
            {
                case "summary":
                    SetOnce(ref summary, element, elementLocation, diagnostics);
                    break;
                case "remarks":
                    SetOnce(ref remarks, element, elementLocation, diagnostics);
                    break;
                case "returns":
                    SetOnce(ref returns, element, elementLocation, diagnostics);
                    break;
                case "value":
                    SetOnce(ref value, element, elementLocation, diagnostics);
                    break;
                case "example":
                    examples.Add(element);
                    break;
                case "param":
                    AddNamed(parameters, element, parameterNames, "parameter", elementLocation, diagnostics);
                    break;
                case "typeparam":
                    AddNamed(typeParameters, element, typeParameterNames, "type parameter", elementLocation, diagnostics);
                    break;
                case "exception":
                    exceptions.Add(new NamedSection(element.Attribute("cref")?.Value ?? "", element));
                    break;
                case "seealso":
                    seeAlso.Add(element);
                    break;
                case "inheritdoc":
                    inheritDoc = element;
                    break;
                case "include":
                    includes.Add(element);
                    break;
                default:
                    if (!IgnoredTags.Contains(element.Name.LocalName))
                    {
                        diagnostics.Report(DiagnosticDescriptors.UnknownXmlTag, $"The documentation tag <{element.Name.LocalName}> is not supported and is ignored", elementLocation);
                    }

                    break;
            }
        }

        foreach (var reference in member.Descendants("paramref"))
        {
            ValidateReference(reference, parameterNames, "parameter", location with { Line = XmlDocumentationFile.GetLine(reference) }, diagnostics);
        }

        foreach (var reference in member.Descendants("typeparamref"))
        {
            ValidateReference(reference, typeParameterNames, "type parameter", location with { Line = XmlDocumentationFile.GetLine(reference) }, diagnostics);
        }

        return new DocumentationComment
        {
            Summary = summary,
            Remarks = remarks,
            Returns = returns,
            Value = value,
            Examples = examples.ToImmutable(),
            Parameters = parameters.ToImmutable(),
            TypeParameters = typeParameters.ToImmutable(),
            Exceptions = exceptions.ToImmutable(),
            SeeAlso = seeAlso.ToImmutable(),
            InheritDoc = inheritDoc,
            Includes = includes.ToImmutable(),
            Scope = scope,
            SourcePath = sourcePath,
        };
    }

    public static IReadOnlySet<string>? GetParameterNames(PublicApiSymbol? symbol)
    {
        // Members of C# 14 extension blocks can reference the receiver of the block
        if (symbol is PublicApiMember { ExtensionBlock: { } block } && GetParameterNamesCore(symbol) is { } names && !string.IsNullOrEmpty(block.Receiver.Name))
            return names.Append(block.Receiver.Name).ToHashSet(StringComparer.Ordinal);

        return GetParameterNamesCore(symbol);
    }

    private static IReadOnlySet<string>? GetParameterNamesCore(PublicApiSymbol? symbol)
    {
        return symbol switch
        {
            PublicApiMethod method => method.Parameters.Select(p => p.Name ?? "").ToHashSet(StringComparer.Ordinal),
            PublicApiProperty property => GetPropertyParameterNames(property),
            PublicApiType { DelegateInvokeMethod: { } invoke } => invoke.Parameters.Select(p => p.Name ?? "").ToHashSet(StringComparer.Ordinal),

            // Records and types with primary constructors document the constructor parameters on the type
            PublicApiType type => type.Members.OfType<PublicApiMethod>().Where(m => m.MethodKind is PublicApiMethodKind.Constructor).SelectMany(m => m.Parameters).Select(p => p.Name ?? "").ToHashSet(StringComparer.Ordinal),
            null => null,
            _ => new HashSet<string>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Returns the parameters a property documentation can reference. The compiler copies the <c>&lt;param&gt;</c> of a
    /// positional record parameter to the generated property, so a property named like a constructor parameter can
    /// also reference the other parameters of that constructor.
    /// </summary>
    private static HashSet<string> GetPropertyParameterNames(PublicApiProperty property)
    {
        var names = property.Parameters.Select(p => p.Name ?? "").ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 && property.DeclaringType is { } type)
        {
            foreach (var constructor in type.Members.OfType<PublicApiMethod>().Where(m => m.MethodKind is PublicApiMethodKind.Constructor))
            {
                if (constructor.Parameters.Any(p => p.Name == property.Name))
                {
                    names.UnionWith(constructor.Parameters.Select(p => p.Name ?? ""));
                }
            }
        }

        return names;
    }

    public static IReadOnlySet<string>? GetTypeParameterNames(PublicApiSymbol? symbol)
    {
        if (symbol is null)
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (symbol is PublicApiMethod method)
        {
            names.UnionWith(method.GenericParameters.Select(p => p.Name));
        }

        if (symbol is PublicApiMember { ExtensionBlock: { } block })
        {
            names.UnionWith(block.GenericParameters.Select(p => p.Name));
        }

        for (var type = symbol as PublicApiType ?? symbol.DeclaringType; type is not null; type = type.DeclaringType)
        {
            names.UnionWith(type.GenericParameters.Select(p => p.Name));
        }

        return names;
    }

    private static void SetOnce(ref XElement? target, XElement element, DiagnosticLocation location, DiagnosticBag diagnostics)
    {
        if (target is not null)
        {
            diagnostics.Report(DiagnosticDescriptors.DuplicateXmlDocumentation, $"The <{element.Name.LocalName}> section is specified more than once; the first one is used", location);
            return;
        }

        target = element;
    }

    private static void AddNamed(ImmutableArray<NamedSection>.Builder sections, XElement element, IReadOnlySet<string>? validNames, string kind, DiagnosticLocation location, DiagnosticBag diagnostics)
    {
        var name = element.Attribute("name")?.Value ?? "";
        if (validNames is not null && !validNames.Contains(name))
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidParameterReference, $"The {kind} '{name}' does not exist", location);
            return;
        }

        if (sections.Any(s => s.Name == name))
        {
            diagnostics.Report(DiagnosticDescriptors.DuplicateXmlDocumentation, $"The {kind} '{name}' is documented more than once; the first one is used", location);
            return;
        }

        sections.Add(new NamedSection(name, element));
    }

    private static void ValidateReference(XElement reference, IReadOnlySet<string>? validNames, string kind, DiagnosticLocation location, DiagnosticBag diagnostics)
    {
        var name = reference.Attribute("name")?.Value ?? "";
        if (validNames is not null && !validNames.Contains(name))
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidParameterReference, $"The referenced {kind} '{name}' does not exist", location);
        }
    }
}
