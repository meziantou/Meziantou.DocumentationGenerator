using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.XmlDocs;
using Meziantou.Framework.PublicApiGenerator;
using Microsoft.AspNetCore.Components;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Produces the HTML fragments of API pages: declarations with linked types, and documentation sections.</summary>
internal sealed class ApiRenderHelper(SiteRenderContext site)
{
    private static readonly Dictionary<string, string> OperatorTokens = new(StringComparer.Ordinal)
    {
        ["op_Addition"] = "+", ["op_Subtraction"] = "-", ["op_Multiply"] = "*", ["op_Division"] = "/", ["op_Modulus"] = "%",
        ["op_Equality"] = "==", ["op_Inequality"] = "!=", ["op_LessThan"] = "<", ["op_GreaterThan"] = ">", ["op_LessThanOrEqual"] = "<=",
        ["op_GreaterThanOrEqual"] = ">=", ["op_BitwiseAnd"] = "&", ["op_BitwiseOr"] = "|", ["op_ExclusiveOr"] = "^", ["op_LeftShift"] = "<<",
        ["op_RightShift"] = ">>", ["op_UnsignedRightShift"] = ">>>", ["op_UnaryNegation"] = "-", ["op_UnaryPlus"] = "+", ["op_LogicalNot"] = "!",
        ["op_OnesComplement"] = "~", ["op_Increment"] = "++", ["op_Decrement"] = "--", ["op_True"] = "true", ["op_False"] = "false",
        ["op_Implicit"] = "implicit", ["op_Explicit"] = "explicit",
    };

    private static readonly PublicApiFormattingOptions ShortTypeOptions = new() { QualifyTypeNames = false, IncludeAttributes = false };

    private readonly XmlDocRenderer _xml = new(site.Links, site.Highlighting, site.Diagnostics);

    public static string GetTypeKind(ApiTypeModel type)
    {
        var primary = type.PrimaryType;
        return primary.TypeKind switch
        {
            PublicApiTypeKind.Interface => "Interface",
            PublicApiTypeKind.Struct => "Struct",
            PublicApiTypeKind.Enum => "Enum",
            PublicApiTypeKind.Delegate => "Delegate",
            _ => "Class",
        };
    }

    public static string GetTypeKindPlural(string kind) => kind switch
    {
        "Class" => "Classes",
        "Struct" => "Structs",
        "Interface" => "Interfaces",
        "Enum" => "Enums",
        _ => "Delegates",
    };

    public static string GetCategoryTitle(ApiMemberCategory category) => category switch
    {
        ApiMemberCategory.Constructor => "Constructors",
        ApiMemberCategory.Property => "Properties",
        ApiMemberCategory.Indexer => "Indexers",
        ApiMemberCategory.Method => "Methods",
        ApiMemberCategory.Operator => "Operators",
        ApiMemberCategory.Event => "Events",
        ApiMemberCategory.Field => "Fields",
        ApiMemberCategory.Constant => "Constants",
        _ => "Fields",
    };

    public static string GetCategoryId(ApiMemberCategory category) => "section-" + category.ToString().ToLowerInvariant();

    /// <summary>
    /// Short display name of a member, with parameter types for methods and indexers: <c>Send(Message, int)</c>. Members of
    /// C# 14 extension blocks are prefixed by their receiver type: <c>Encoding.UTF8WithoutPreamble</c>.
    /// </summary>
    public static string GetMemberDisplayName(ApiMemberModel member)
    {
        var name = GetMemberDisplayNameCore(member);
        return member.ExtensionReceiverDisplayName is { } receiver && member.Category is not ApiMemberCategory.Operator ? receiver + "." + name : name;
    }

    private static string GetMemberDisplayNameCore(ApiMemberModel member)
    {
        var symbol = member.PrimaryMember;
        var name = member.Category switch
        {
            ApiMemberCategory.Constructor => member.DeclaringType.PrimaryType.Name,
            ApiMemberCategory.Indexer => "this",
            ApiMemberCategory.Operator => GetOperatorName(symbol),
            _ => member.Name,
        };

        var parameters = symbol switch
        {
            PublicApiMethod method => method.Parameters,
            PublicApiProperty { IsIndexer: true } property => property.Parameters,
            _ => default,
        };

        if (parameters.IsDefault)
            return name;

        var typeParameters = symbol is PublicApiMethod { IsGenericMethod: true } genericMethod ? "<" + string.Join(", ", genericMethod.GenericParameters.Select(p => p.Name)) + ">" : "";
        var parameterList = string.Join(", ", parameters.Select(p => PublicApiFormatter.Format(p.Type, ShortTypeOptions).Text));
        return member.Category is ApiMemberCategory.Indexer ? $"this[{parameterList}]" : $"{name}{typeParameters}({parameterList})";
    }

    private static string GetOperatorName(PublicApiMember member)
    {
        if (!OperatorTokens.TryGetValue(member.Name, out var token))
            return member.Name;

        if (member is PublicApiMethod { MethodKind: PublicApiMethodKind.Conversion } conversion)
            return token + " operator " + PublicApiFormatter.Format(conversion.ReturnType, ShortTypeOptions).Text;

        return "operator " + token;
    }

    /// <summary>Renders a declaration with highlighting classes and links on the types that have a page.</summary>
    public MarkupString Declaration(PublicApiSymbol symbol, LinkContext context)
    {
        var declaration = DeclarationFormatter.Format(symbol);
        var builder = new StringBuilder("<pre class=\"declaration\"><code class=\"hljs language-csharp\">");
        foreach (var segment in declaration.Segments)
        {
            var text = HtmlEncoding.Encode(segment.Text);
            switch (segment.Kind)
            {
                case PublicApiDeclarationSegmentKind.Keyword:
                    builder.Append("<span class=\"hljs-keyword\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.StringLiteral:
                    builder.Append("<span class=\"hljs-string\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.NumericLiteral:
                    builder.Append("<span class=\"hljs-number\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.MemberName:
                    builder.Append("<span class=\"hljs-title function_\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.ParameterName:
                    builder.Append("<span class=\"hljs-params\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.TypeParameterName:
                    builder.Append("<span class=\"hljs-type\">").Append(text).Append("</span>");
                    break;
                case PublicApiDeclarationSegmentKind.TypeName:
                    var url = segment.TypeReference is PublicApiNamedTypeReference reference ? site.Links.ResolveType(reference, context).Url : null;
                    builder.Append(url is null ? "<span class=\"hljs-type\">" : "<a class=\"hljs-type\" href=\"" + HtmlEncoding.Encode(url) + "\">").Append(text).Append(url is null ? "</span>" : "</a>");
                    break;
                case PublicApiDeclarationSegmentKind.LineBreak:
                    builder.Append('\n');
                    break;
                default:
                    builder.Append(text);
                    break;
            }
        }

        builder.Append("</code></pre>");
        return new MarkupString(builder.ToString());
    }

    /// <summary>Renders a type reference inline, with a link when the type has a page.</summary>
    public MarkupString TypeReference(PublicApiTypeReference? type, LinkContext context)
    {
        if (type is null)
            return default;

        var text = HtmlEncoding.Encode(PublicApiFormatter.Format(type, ShortTypeOptions).Text);
        var url = type is PublicApiNamedTypeReference named ? site.Links.ResolveType(named, context).Url : null;
        return new MarkupString(url is null ? "<code>" + text + "</code>" : "<a href=\"" + HtmlEncoding.Encode(url) + "\"><code>" + text + "</code></a>");
    }

    public MarkupString Block(XElement? section, DocumentationComment comment, ApiSymbolModel symbol)
    {
        return section is null ? default : new MarkupString(_xml.RenderBlock(section, CreateContext(comment, symbol)));
    }

    public MarkupString Inline(XElement? section, DocumentationComment comment, ApiSymbolModel symbol)
    {
        return section is null ? default : new MarkupString(_xml.RenderInline(section, CreateContext(comment, symbol)));
    }

    public MarkupString Reference(XElement reference, DocumentationComment comment, ApiSymbolModel symbol)
    {
        var html = _xml.RenderReference(reference, CreateContext(comment, symbol));
        return html is null ? default : new MarkupString(html);
    }

    /// <summary>Plain summary of the primary documentation, used in tables and search.</summary>
    public MarkupString Summary(ApiSymbolModel symbol)
    {
        var variant = site.Documentation.GetVariants(symbol).FirstOrDefault();
        return variant?.Comment.Summary is { } summary ? Inline(summary, variant.Comment, symbol) : default;
    }

    /// <summary>Plain text of the summary, for meta descriptions.</summary>
    public string? SummaryText(ApiSymbolModel symbol)
    {
        var html = Summary(symbol).Value;
        if (string.IsNullOrEmpty(html))
            return null;

        var text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 160 ? text : text[..157].TrimEnd() + "...";
    }

    public static LinkContext GetLinkContext(ApiSymbolModel symbol) => new(symbol.Assembly.Scope, symbol.Assembly.Package.Id);

    private static XmlRenderContext CreateContext(DocumentationComment comment, ApiSymbolModel symbol)
    {
        return new XmlRenderContext(new LinkContext(comment.Scope ?? symbol.Assembly.Scope, symbol.Assembly.Package.Id), comment.SourcePath, symbol.DocumentationId);
    }

    /// <summary>Attributes worth a badge: obsolescence, experimental APIs, and platform support.</summary>
    public static ImmutableArray<(string Kind, string Text)> GetBadges(PublicApiSymbol symbol)
    {
        var result = ImmutableArray.CreateBuilder<(string, string)>();
        foreach (var attribute in symbol.Attributes)
        {
            var argument = attribute.ConstructorArguments.FirstOrDefault()?.Value as string;
            switch (attribute.AttributeType.FullName)
            {
                case "System.ObsoleteAttribute":
                    result.Add(("obsolete", argument is null ? "Obsolete" : "Obsolete: " + argument));
                    break;
                case "System.Diagnostics.CodeAnalysis.ExperimentalAttribute":
                    result.Add(("experimental", argument is null ? "Experimental" : "Experimental (" + argument + ")"));
                    break;
                case "System.Runtime.Versioning.SupportedOSPlatformAttribute" when argument is not null:
                    result.Add(("platform", "Supported on " + argument));
                    break;
                case "System.Runtime.Versioning.UnsupportedOSPlatformAttribute" when argument is not null:
                    result.Add(("platform", "Not supported on " + argument));
                    break;
            }
        }

        return result.ToImmutable();
    }

    /// <summary>Base types from the most general to the type itself, excluding <see cref="object"/>.</summary>
    public static IReadOnlyList<PublicApiNamedTypeReference> GetBaseTypes(PublicApiType type)
    {
        var result = new List<PublicApiNamedTypeReference>();
        if (type.BaseType is PublicApiNamedTypeReference baseType && baseType.DocumentationId is not ("T:System.Object" or "T:System.ValueType" or "T:System.Enum" or "T:System.MulticastDelegate"))
        {
            result.Add(baseType);
        }

        return result;
    }
}
