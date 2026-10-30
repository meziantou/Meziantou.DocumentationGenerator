using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework.PublicApiGenerator;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// Resolves <c>&lt;include&gt;</c> and <c>&lt;inheritdoc&gt;</c> before rendering. Inheritance follows the structured model:
/// overridden members, base types, explicit interface implementations and implemented interface members, with generic
/// arguments substituted. Local sections take precedence over inherited ones; inherited parameter documentation is mapped
/// by position to the local parameter names. Includes are only read from the package that contains the XML file.
/// </summary>
internal sealed class InheritanceResolver(ApiCatalog catalog, DocumentationProvider provider, DiagnosticBag diagnostics)
{
    public const string ScopeAttributeName = "docgen-scope";
    private const int MaxDepth = 32;
    private const int MaxIncludeDepth = 4;
    private const long MaxIncludeFileSize = 4 * 1024 * 1024;

    private readonly HashSet<(ApiAssemblyModel, string)> _resolved = [];

    public void ResolveAll(CancellationToken cancellationToken)
    {
        foreach (var group in catalog.Assemblies)
        {
            foreach (var model in group.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var symbol in model.Assembly.GetAllSymbols())
                {
                    Resolve(model, symbol, []);
                }
            }
        }
    }

    private DocumentationComment Resolve(ApiAssemblyModel model, PublicApiSymbol symbol, List<string> stack)
    {
        var comment = provider.GetComment(model, symbol);
        var key = model.Scope + "!" + symbol.DocumentationId + "@" + model.TargetFramework;
        if (stack.Contains(key, StringComparer.Ordinal) || stack.Count > MaxDepth)
        {
            diagnostics.Report(DiagnosticDescriptors.InheritDocCycle, $"Inherited documentation forms a cycle: {string.Join(" -> ", stack.Append(key))}", Location(model, symbol));
            return comment with { InheritDoc = null, InheritanceNote = "Inherited documentation forms a cycle." };
        }

        if (!_resolved.Add((model, symbol.DocumentationId)) || (comment.InheritDoc is null && comment.Includes.IsEmpty && !HasNestedDirectives(comment)))
            return comment;

        stack.Add(key);
        try
        {
            if (!comment.Includes.IsEmpty || HasNestedDirectives(comment))
            {
                comment = ResolveIncludes(model, symbol, comment);
            }

            if (comment.InheritDoc is { } inheritDoc)
            {
                comment = ResolveInheritDoc(model, symbol, comment, inheritDoc, stack);
            }

            provider.SetComment(model, symbol.DocumentationId, comment);
            return comment;
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    private static bool HasNestedDirectives(DocumentationComment comment)
    {
        return comment.Summary?.Descendants("include").Any() == true || comment.Remarks?.Descendants("include").Any() == true;
    }

    private DocumentationComment ResolveInheritDoc(ApiAssemblyModel model, PublicApiSymbol symbol, DocumentationComment comment, XElement inheritDoc, List<string> stack)
    {
        var cref = inheritDoc.Attribute("cref")?.Value;
        (ApiAssemblyModel Model, PublicApiSymbol Symbol)? source;
        string description;
        if (cref is not null)
        {
            source = FindExplicitSource(model, cref, out description);
        }
        else
        {
            source = FindImplicitSource(model, symbol, out description);
        }

        if (source is not { } target)
        {
            diagnostics.Report(DiagnosticDescriptors.InheritDocUnresolved, $"Cannot inherit the documentation of '{symbol.DocumentationId}': {description}", Location(model, symbol));
            return comment with { InheritDoc = null, InheritanceNote = $"Inherited documentation is not available ({description})." };
        }

        var inherited = Resolve(target.Model, target.Symbol, stack);
        if (inherited.IsEmpty)
        {
            // Continue up the hierarchy when the direct source has no documentation of its own
            if (cref is null && inherited.InheritDoc is null)
            {
                diagnostics.Report(DiagnosticDescriptors.InheritDocUnresolved, $"'{target.Symbol.DocumentationId}' has no documentation to inherit for '{symbol.DocumentationId}'", Location(model, symbol));
            }

            return comment with { InheritDoc = null, InheritanceNote = inherited.InheritanceNote ?? $"'{target.Symbol.DocumentationId}' has no documentation." };
        }

        var path = inheritDoc.Attribute("path")?.Value;
        if (path is not null)
        {
            // Paths are evaluated from the member element: "/summary" and "summary" are equivalent
            var selector = path.StartsWith("//", StringComparison.Ordinal) ? null : XmlSelector.TryParse(path.TrimStart('/'));
            if (selector is null)
            {
                diagnostics.Report(DiagnosticDescriptors.UnsupportedSelector, $"The inheritdoc path '{path}' is not supported; supported paths are element steps with optional [@attribute='value'] predicates", Location(model, symbol));
                return comment with { InheritDoc = null, InheritanceNote = "Inherited documentation uses an unsupported path." };
            }

            inherited = FilterByPath(inherited, selector);
        }

        return Merge(comment, inherited, symbol, target.Symbol, target.Model.Scope);
    }

    private (ApiAssemblyModel, PublicApiSymbol)? FindExplicitSource(ApiAssemblyModel model, string cref, out string description)
    {
        var candidates = catalog.FindByDocumentationId(cref);
        var preferred = candidates.Where(c => c.Assembly.Scope == model.Scope).ToList();
        if (preferred.Count == 0)
        {
            preferred = candidates.Where(c => string.Equals(c.Assembly.Package.Id, model.Package.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (preferred.Count == 0)
        {
            preferred = [.. candidates];
        }

        var targets = preferred.Select(c => c.Assembly).Distinct().ToList();
        if (targets.Count > 1)
        {
            description = $"'{cref}' is ambiguous ({string.Join(", ", targets.Select(t => t.Scope))})";
            return null;
        }

        if (targets.Count == 0)
        {
            description = $"'{cref}' is not part of the configured packages or references";
            return null;
        }

        description = "";
        return GetSymbolForFramework(targets[0], cref, model.Asset.Framework);
    }

    private (ApiAssemblyModel, PublicApiSymbol)? FindImplicitSource(ApiAssemblyModel model, PublicApiSymbol symbol, out string description)
    {
        description = "no base member or interface member provides documentation";
        switch (symbol)
        {
            case PublicApiType type:
                {
                    var baseTypes = new List<PublicApiNamedTypeReference>();
                    if (type.BaseType is PublicApiNamedTypeReference baseType && baseType.DocumentationId is not ("T:System.Object" or "T:System.ValueType" or "T:System.Enum" or "T:System.MulticastDelegate"))
                    {
                        baseTypes.Add(baseType);
                    }

                    baseTypes.AddRange(type.Interfaces.OfType<PublicApiNamedTypeReference>());
                    foreach (var reference in baseTypes)
                    {
                        if (FindType(reference, model) is { } found)
                            return found;
                    }

                    description = baseTypes.Count == 0 ? "the type has no base type or interface" : $"{string.Join(", ", baseTypes.Select(b => b.DocumentationId))} {(baseTypes.Count == 1 ? "is" : "are")} not part of the configured packages or references";
                    return null;
                }

            case PublicApiMember member when member.DeclaringType is { } declaringType:
                {
                    // Explicit interface implementations name their interface member
                    foreach (var implemented in member.ExplicitInterfaceImplementations)
                    {
                        if (implemented.ContainingType is PublicApiNamedTypeReference interfaceReference && FindType(interfaceReference, model) is { } interfaceType)
                        {
                            var interfaceMember = interfaceType.Symbol is PublicApiType t ? t.Members.FirstOrDefault(m => m.DocumentationId == implemented.DocumentationId) : null;
                            if (interfaceMember is not null)
                                return (interfaceType.Model, interfaceMember);
                        }
                    }

                    if (member.IsOverride || member is PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor })
                    {
                        for (var baseReference = declaringType.BaseType as PublicApiNamedTypeReference; baseReference is not null;)
                        {
                            if (FindType(baseReference, model) is not { Symbol: PublicApiType baseType } found)
                            {
                                description = $"the base type '{baseReference.DocumentationId}' is not part of the configured packages or references";
                                break;
                            }

                            var match = FindMatchingMember(member, baseType, baseReference);
                            if (match is not null)
                                return (found.Model, match);

                            baseReference = baseType.BaseType as PublicApiNamedTypeReference;
                        }
                    }

                    var interfaceMatches = new List<(ApiAssemblyModel, PublicApiSymbol)>();
                    foreach (var interfaceReference in declaringType.Interfaces.OfType<PublicApiNamedTypeReference>())
                    {
                        if (FindType(interfaceReference, model) is { Symbol: PublicApiType interfaceType } found && FindMatchingMember(member, interfaceType, interfaceReference) is { } match)
                        {
                            interfaceMatches.Add((found.Model, match));
                        }
                    }

                    if (interfaceMatches.Count > 1)
                    {
                        diagnostics.Report(DiagnosticDescriptors.SeveralInheritanceCandidates, $"Several interfaces provide documentation for '{member.DocumentationId}'; '{interfaceMatches[0].Item2.DocumentationId}' is used", Location(model, member));
                    }

                    return interfaceMatches.Count > 0 ? interfaceMatches[0] : null;
                }
        }

        return null;
    }

    /// <summary>Finds a member with the same name, kind and parameter types, substituting the generic arguments of the base type reference.</summary>
    private static PublicApiMember? FindMatchingMember(PublicApiMember member, PublicApiType baseType, PublicApiNamedTypeReference baseReference)
    {
        var substitutions = baseReference.TypeArguments;
        var expected = GetSignatureKey(member, []);
        var name = member.IsExplicitInterfaceImplementation ? member.Name[(member.Name.LastIndexOf('.', StringComparison.Ordinal) + 1)..] : member.Name;
        return baseType.Members.FirstOrDefault(candidate =>
            candidate.Kind == member.Kind &&
            candidate.Name == name &&
            candidate.IsStatic == member.IsStatic &&
            GetSignatureKey(candidate, substitutions) == expected);
    }

    private static string GetSignatureKey(PublicApiMember member, ImmutableArray<PublicApiTypeReference> substitutions)
    {
        var parameters = member switch
        {
            PublicApiMethod method => method.Parameters,
            PublicApiProperty property => property.Parameters,
            _ => [],
        };

        var genericArity = member is PublicApiMethod m ? m.GenericParameters.Length : 0;
        return genericArity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "(" + string.Join(",", parameters.Select(p => GetTypeKey(p.Type, substitutions) + (p.RefKind is PublicApiRefKind.None ? "" : "@"))) + ")";
    }

    private static string GetTypeKey(PublicApiTypeReference? type, ImmutableArray<PublicApiTypeReference> substitutions)
    {
        return type switch
        {
            null => "",
            PublicApiTypeParameterReference { IsMethodTypeParameter: true } methodParameter => "``" + methodParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PublicApiTypeParameterReference typeParameter when !substitutions.IsDefaultOrEmpty && typeParameter.Ordinal < substitutions.Length => GetTypeKey(substitutions[typeParameter.Ordinal], []),
            PublicApiTypeParameterReference typeParameter => "`" + typeParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PublicApiArrayTypeReference array => GetTypeKey(array.ElementType, substitutions) + "[" + new string(',', Math.Max(0, array.Rank - 1)) + "]",
            PublicApiPointerTypeReference pointer => GetTypeKey(pointer.ElementType, substitutions) + "*",
            PublicApiNamedTypeReference named => named.DocumentationId + (named.TypeArguments.IsDefaultOrEmpty ? "" : "{" + string.Join(",", named.TypeArguments.Select(a => GetTypeKey(a, substitutions))) + "}"),
            _ => type.Kind.ToString(),
        };
    }

    private (ApiAssemblyModel Model, PublicApiSymbol Symbol)? FindType(PublicApiNamedTypeReference reference, ApiAssemblyModel context)
    {
        var candidates = catalog.FindByDocumentationId(reference.DocumentationId).OfType<ApiTypeModel>().ToList();
        if (reference.AssemblyName is { } assemblyName)
        {
            var sameAssembly = candidates.Where(c => string.Equals(c.Assembly.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameAssembly.Count > 0)
            {
                candidates = sameAssembly;
            }
        }

        var preferred = candidates.Where(c => c.Assembly.Scope == context.Scope).ToList();
        if (preferred.Count == 0)
        {
            preferred = candidates;
        }

        return preferred.Count == 0 ? null : GetSymbolForFramework(preferred[0].Assembly, reference.DocumentationId, context.Asset.Framework);
    }

    /// <summary>Uses the build of the same target framework, or the nearest compatible one, so relationships can differ per framework.</summary>
    private static (ApiAssemblyModel, PublicApiSymbol)? GetSymbolForFramework(ApiAssemblyGroup group, string documentationId, NuGetFramework framework)
    {
        var model = group.Models.FirstOrDefault(m => m.Asset.Framework == framework);
        if (model is null)
        {
            var nearest = new FrameworkReducer().GetNearest(framework, group.Models.Select(m => m.Asset.Framework));
            model = nearest is null ? group.Models[0] : group.Models.First(m => m.Asset.Framework == nearest);
        }

        var symbol = model.Assembly.FindSymbolByDocumentationId(documentationId);
        if (symbol is null)
        {
            // The symbol may only exist in other frameworks of the group
            foreach (var other in group.Models)
            {
                symbol = other.Assembly.FindSymbolByDocumentationId(documentationId);
                if (symbol is not null)
                    return (other, symbol);
            }

            return null;
        }

        return (model, symbol);
    }

    private static DocumentationComment FilterByPath(DocumentationComment inherited, XmlSelector selector)
    {
        var member = new XElement("member");
        foreach (var element in GetSections(inherited))
        {
            member.Add(element);
        }

        var selected = selector.Select(new XDocument(new XElement("root", member)).Root!.Element("member")!).ToList();
        bool Keep(XElement? element) => element is not null && selected.Any(s => XNode.DeepEquals(s, element));
        return inherited with
        {
            Summary = Keep(inherited.Summary) ? inherited.Summary : null,
            Remarks = Keep(inherited.Remarks) ? inherited.Remarks : null,
            Returns = Keep(inherited.Returns) ? inherited.Returns : null,
            Value = Keep(inherited.Value) ? inherited.Value : null,
            Examples = [.. inherited.Examples.Where(Keep)],
            Parameters = [.. inherited.Parameters.Where(p => Keep(p.Content))],
            TypeParameters = [.. inherited.TypeParameters.Where(p => Keep(p.Content))],
            Exceptions = [.. inherited.Exceptions.Where(p => Keep(p.Content))],
            SeeAlso = [.. inherited.SeeAlso.Where(Keep)],
        };
    }

    private static IEnumerable<XElement> GetSections(DocumentationComment comment)
    {
        IEnumerable<XElement?> sections = [comment.Summary, comment.Remarks, comment.Returns, comment.Value, .. comment.Examples, .. comment.Parameters.Select(p => p.Content), .. comment.TypeParameters.Select(p => p.Content), .. comment.Exceptions.Select(p => p.Content), .. comment.SeeAlso];
        return sections.Where(s => s is not null).Select(s => new XElement(s!));
    }

    /// <summary>Local sections win; missing sections come from the inherited documentation, with parameters renamed by position.</summary>
    private static DocumentationComment Merge(DocumentationComment local, DocumentationComment inherited, PublicApiSymbol symbol, PublicApiSymbol source, string sourceScope)
    {
        var parameterNames = MapNames(GetParameters(source), GetParameters(symbol));
        var typeParameterNames = MapNames(GetTypeParameters(source), GetTypeParameters(symbol));
        XElement? Adapt(XElement? element)
        {
            if (element is null)
                return null;

            var copy = new XElement(element);
            foreach (var reference in copy.DescendantsAndSelf().Where(e => e.Name.LocalName is "paramref" or "param"))
            {
                if (reference.Attribute("name")?.Value is { } name && parameterNames.TryGetValue(name, out var renamed))
                {
                    reference.SetAttributeValue("name", renamed);
                }
            }

            foreach (var reference in copy.DescendantsAndSelf().Where(e => e.Name.LocalName is "typeparamref" or "typeparam"))
            {
                if (reference.Attribute("name")?.Value is { } name && typeParameterNames.TryGetValue(name, out var renamed))
                {
                    reference.SetAttributeValue("name", renamed);
                }
            }

            copy.SetAttributeValue(ScopeAttributeName, sourceScope);
            return copy;
        }

        ImmutableArray<NamedSection> MergeNamed(ImmutableArray<NamedSection> localSections, ImmutableArray<NamedSection> inheritedSections, Dictionary<string, string> names)
        {
            var result = localSections.ToBuilder();
            foreach (var section in inheritedSections)
            {
                var name = names.TryGetValue(section.Name, out var renamed) ? renamed : section.Name;
                if (!result.Any(r => r.Name == name))
                {
                    result.Add(new NamedSection(name, Adapt(section.Content)!));
                }
            }

            return result.ToImmutable();
        }

        return local with
        {
            Summary = local.Summary ?? Adapt(inherited.Summary),
            Remarks = local.Remarks ?? Adapt(inherited.Remarks),
            Returns = local.Returns ?? Adapt(inherited.Returns),
            Value = local.Value ?? Adapt(inherited.Value),
            Examples = local.Examples.IsEmpty ? [.. inherited.Examples.Select(e => Adapt(e)!)] : local.Examples,
            Parameters = MergeNamed(local.Parameters, inherited.Parameters, parameterNames),
            TypeParameters = MergeNamed(local.TypeParameters, inherited.TypeParameters, typeParameterNames),
            Exceptions = local.Exceptions.IsEmpty ? [.. inherited.Exceptions.Select(e => e with { Content = Adapt(e.Content)! })] : local.Exceptions,
            SeeAlso = local.SeeAlso.IsEmpty ? [.. inherited.SeeAlso.Select(e => Adapt(e)!)] : local.SeeAlso,
            InheritDoc = null,
            InheritanceNote = inherited.InheritanceNote,
        };
    }

    private static Dictionary<string, string> MapNames(IReadOnlyList<string> sourceNames, IReadOnlyList<string> targetNames)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(sourceNames.Count, targetNames.Count); i++)
        {
            result[sourceNames[i]] = targetNames[i];
        }

        return result;
    }

    private static List<string> GetParameters(PublicApiSymbol symbol) => symbol switch
    {
        PublicApiMethod method => [.. method.Parameters.Select(p => p.Name ?? "")],
        PublicApiProperty property => [.. property.Parameters.Select(p => p.Name ?? "")],
        PublicApiType { DelegateInvokeMethod: { } invoke } => [.. invoke.Parameters.Select(p => p.Name ?? "")],
        _ => [],
    };

    private static List<string> GetTypeParameters(PublicApiSymbol symbol) => symbol switch
    {
        PublicApiMethod method => [.. method.GenericParameters.Select(p => p.Name)],
        PublicApiType type => [.. type.GenericParameters.Select(p => p.Name)],
        _ => [],
    };

    private DocumentationComment ResolveIncludes(ApiAssemblyModel model, PublicApiSymbol symbol, DocumentationComment comment)
    {
        var file = provider.GetFile(model);
        var member = file?.Find(symbol.DocumentationId);
        if (member is null || model.Asset.XmlDocumentationEntry is not { } xmlEntry)
            return comment with { Includes = [] };

        var copy = new XElement(member);
        using var archive = PackageArchive.Open(model.Package.Package.Path);
        ExpandIncludes(copy, archive, xmlEntry, model, symbol, depth: 0);
        var reparsed = DocumentationCommentParser.Parse(copy, symbol, model.Scope, file!.DisplayPath, new DiagnosticBag());
        return reparsed with { Includes = [] };
    }

    private void ExpandIncludes(XElement element, PackageArchive archive, string xmlEntry, ApiAssemblyModel model, PublicApiSymbol symbol, int depth)
    {
        foreach (var include in element.Descendants("include").ToList())
        {
            var location = Location(model, symbol);
            if (depth >= MaxIncludeDepth)
            {
                diagnostics.Report(DiagnosticDescriptors.IncludeUnresolved, "Includes are nested too deeply", location);
                include.Remove();
                continue;
            }

            var fileName = include.Attribute("file")?.Value;
            var selector = XmlSelector.TryParse(include.Attribute("path")?.Value);
            if (fileName is null || selector is null)
            {
                diagnostics.Report(DiagnosticDescriptors.UnsupportedSelector, $"The include path '{include.Attribute("path")?.Value}' is not supported", location);
                include.Remove();
                continue;
            }

            var directory = xmlEntry.Contains('/', StringComparison.Ordinal) ? xmlEntry[..xmlEntry.LastIndexOf('/', StringComparison.Ordinal)] : "";
            var entry = ResolveEntry(directory, fileName);
            var actual = entry is null ? null : archive.FindEntry(entry);
            if (entry is null)
            {
                diagnostics.Report(DiagnosticDescriptors.PathEscapesRoot, $"The included file '{fileName}' is outside the package", location);
                include.Remove();
                continue;
            }

            if (actual is null || archive.GetLength(actual) > MaxIncludeFileSize)
            {
                diagnostics.Report(DiagnosticDescriptors.IncludeUnresolved, $"The included file '{fileName}' is not in the package or is too large", location);
                include.Remove();
                continue;
            }

            XDocument document;
            try
            {
                using var stream = archive.OpenRead(actual, MaxIncludeFileSize);
                using var reader = XmlReader.Create(stream, XmlDocumentationFile.ReaderSettings);
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                diagnostics.Report(DiagnosticDescriptors.IncludeUnresolved, $"The included file '{fileName}' is not valid XML: {ex.Message}", location);
                include.Remove();
                continue;
            }

            var selected = selector.SelectFromDocument(document);
            if (selected.Count == 0)
            {
                diagnostics.Report(DiagnosticDescriptors.IncludeUnresolved, $"The include path '{include.Attribute("path")?.Value}' selects nothing in '{fileName}'", location);
                include.Remove();
                continue;
            }

            var replacement = selected.Select(e => new XElement(e)).ToList();
            foreach (var item in replacement)
            {
                ExpandIncludes(item, archive, actual, model, symbol, depth + 1);
            }

            include.ReplaceWith(replacement);
        }
    }

    private static string? ResolveEntry(string directory, string fileName)
    {
        var segments = new List<string>(directory.Length == 0 ? [] : directory.Split('/'));
        foreach (var segment in fileName.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (segment.Contains(':', StringComparison.Ordinal))
                return null;

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static DiagnosticLocation Location(ApiAssemblyModel model, PublicApiSymbol symbol)
    {
        return new DiagnosticLocation { Package = model.Package.Package.Identity, TargetFramework = model.TargetFramework, Symbol = symbol.DocumentationId };
    }
}
