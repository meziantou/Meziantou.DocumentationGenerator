using System.Text;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>
/// The single source of routes, anchors and URLs. API pages, XML documentation, Markdown, navigation and search all use
/// this service instead of building URLs themselves. Routes only depend on product identifiers, namespaces, metadata
/// names and authored paths, never on titles, discovery order or the selected target framework.
/// </summary>
internal sealed partial class LinkService
{
    public const string NotFoundPath = "404.html";
    public const string HomePath = "index.html";
    public const string AssetsDirectory = "_assets";

    private static readonly char[] DocumentationIdKinds = ['T', 'M', 'P', 'F', 'E'];

    private readonly SiteConfiguration _configuration;
    private readonly ApiCatalog _catalog;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, SiteRoute> _routesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ApiTypeModel, SiteRoute> _typeRoutes = [];
    private readonly HashSet<string> _publishedNamespaces = new(StringComparer.Ordinal);

    public LinkService(SiteConfiguration configuration, ApiCatalog catalog, DiagnosticBag diagnostics)
    {
        _configuration = configuration;
        _catalog = catalog;
        _diagnostics = diagnostics;
        Urls = new UrlBuilder(configuration.Site.BasePath);

        Register(new SiteRoute(HomePath, PageKind.Home, "site home"));
        Register(new SiteRoute(NotFoundPath, PageKind.NotFound, "404 page"));
        foreach (var product in configuration.Products)
        {
            Register(new SiteRoute(RouteNames.GetProductPath(product.Id), PageKind.Product, "product " + product.Id));
        }

        foreach (var product in catalog.Products)
        {
            if (product.Namespaces.IsEmpty)
                continue;

            Register(new SiteRoute(RouteNames.GetApiReferencePath(product.Product.Id), PageKind.ApiReference, "API reference of " + product.Product.Id));
            foreach (var ns in product.Namespaces)
            {
                _publishedNamespaces.Add(product.Product.Id + "|" + ns.Name);
                Register(new SiteRoute(RouteNames.GetNamespacePath(product.Product.Id, ns.Name), PageKind.Namespace, "namespace " + ns.Name));
            }

            RegisterTypeRoutes(product);
        }
    }

    public UrlBuilder Urls { get; }

    public IEnumerable<SiteRoute> Routes => _routesByPath.Values.OrderBy(r => r.FilePath, StringComparer.Ordinal);

    public string HomeUrl => Urls.GetUrl(HomePath);

    public string NotFoundUrl => Urls.GetUrl(NotFoundPath);

    public string GetAssetUrl(string fileName) => Urls.GetUrl(AssetsDirectory + "/" + fileName);

    public string GetProductUrl(string productId) => Urls.GetUrl(RouteNames.GetProductPath(productId));

    public string GetApiReferenceUrl(string productId) => Urls.GetUrl(RouteNames.GetApiReferencePath(productId));

    public string GetNamespaceUrl(string productId, string namespaceName) => Urls.GetUrl(RouteNames.GetNamespacePath(productId, namespaceName));

    public SiteRoute? GetTypeRoute(ApiTypeModel type) => _typeRoutes.GetValueOrDefault(type);

    public string? GetUrl(ApiSymbolModel symbol)
    {
        return symbol switch
        {
            ApiTypeModel type => GetTypeRoute(type) is { } route ? Urls.GetUrl(route.FilePath) : null,
            ApiMemberModel member => GetTypeRoute(member.DeclaringType) is { } route ? Urls.GetUrl(route.FilePath, RouteNames.GetMemberAnchor(member)) : null,
            _ => null,
        };
    }

    public string? GetOverloadGroupUrl(ApiMemberModel member)
    {
        return GetTypeRoute(member.DeclaringType) is { } route ? Urls.GetUrl(route.FilePath, RouteNames.GetOverloadAnchor(member.OverloadName)) : null;
    }

    public bool TryGetRoute(string filePath, out SiteRoute route) => _routesByPath.TryGetValue(filePath, out route!);

    /// <summary>
    /// Registers a page or asset produced by another stage, such as an authored page. Returns <see langword="false"/> and
    /// reports a diagnostic when the path is unsafe or collides with another file, before anything is written.
    /// </summary>
    public bool TryRegister(SiteRoute route, DiagnosticLocation? location = null)
    {
        if (!IsSafeRelativePath(route.FilePath))
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsafeOutputPath, $"'{route.FilePath}' is not a safe output path", location);
            return false;
        }

        if (_routesByPath.TryGetValue(route.FilePath, out var existing))
        {
            // An authored index.md at the root of the content directory is the home page of the site
            if (existing.Kind is PageKind.Home && route.Kind is PageKind.Article && string.Equals(existing.FilePath, route.FilePath, StringComparison.Ordinal))
            {
                _routesByPath[route.FilePath] = route;
                return true;
            }

            _diagnostics.Report(DiagnosticDescriptors.PageCollision, $"'{route.FilePath}' ({route.Owner}) collides with '{existing.FilePath}' ({existing.Owner}); paths are compared case-insensitively", location);
            return false;
        }

        _routesByPath.Add(route.FilePath, route);
        return true;
    }

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))
            return false;

        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." && !segment.Any(char.IsControl));
    }

    /// <summary>Resolves a compiler documentation ID or a <c>cref:</c> reference.</summary>
    /// <param name="cref">
    /// <c>T:Contoso.Client</c>, <c>Contoso.Client</c> (any kind), <c>M:Contoso.Client.Send</c> (every overload),
    /// <c>Contoso.Package!T:Contoso.Client</c> (explicit package), or <c>!:Name</c> (reference the compiler could not resolve).
    /// </param>
    public CrefResolution Resolve(string cref, LinkContext context)
    {
        var value = cref.Trim();
        string? packageId = null;
        if (value.StartsWith("!:", StringComparison.Ordinal))
        {
            value = value[2..];
        }
        else if (value.IndexOf('!', StringComparison.Ordinal) is var separator and > 0)
        {
            packageId = value[..separator];
            value = value[(separator + 1)..];
        }

        value = NormalizeGenericSyntax(value);
        char? kind = null;
        if (value.Length > 2 && value[1] == ':')
        {
            kind = value[0];
            value = value[2..];
        }

        if (kind == 'N')
            return ResolveNamespace(value, context);

        var candidates = new List<ApiSymbolModel>();
        foreach (var candidateKind in kind is { } k ? [k] : DocumentationIdKinds)
        {
            var documentationId = candidateKind + ":" + value;
            var matches = _catalog.FindByDocumentationId(documentationId);
            if (matches.Count == 0 && candidateKind is 'M' or 'P' && !value.Contains('(', StringComparison.Ordinal))
            {
                matches = _catalog.FindByNameKey(documentationId);
            }

            candidates.AddRange(matches);
            if (candidates.Count > 0)
                break;
        }

        if (packageId is not null)
        {
            candidates.RemoveAll(c => !string.Equals(c.Assembly.Package.Id, packageId, StringComparison.OrdinalIgnoreCase));
        }

        if (candidates.Count == 0)
        {
            if (kind is null && ResolveNamespace(value, context) is { Kind: CrefResolutionKind.Resolved } namespaceResolution)
                return namespaceResolution;

            // Without prefix, an external symbol is a type, unless it is a value of a framework enum (System.StringComparison.Ordinal)
            kind ??= GetQualifiedName(value) is var name && name.AsSpan().LastIndexOf('.') is > 0 and var separator && FrameworkEnumIndex.IsEnum(name[..separator]) ? 'F' : 'T';
            return ResolveExternal(kind + ":" + value);
        }

        return Select(candidates, context, cref);
    }

    /// <summary>Resolves a type used in a signature, preferring the assembly the reference points to.</summary>
    public CrefResolution ResolveType(PublicApiNamedTypeReference reference, LinkContext context)
    {
        var definition = reference;
        var documentationId = definition.DocumentationId;
        var candidates = _catalog.FindByDocumentationId(documentationId).Where(c => c is ApiTypeModel).ToList();
        if (reference.AssemblyName is { } assemblyName)
        {
            var sameAssembly = candidates.Where(c => string.Equals(c.Assembly.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameAssembly.Count > 0)
            {
                candidates = sameAssembly;
            }
        }

        if (candidates.Count == 0)
            return ResolveExternal(documentationId);

        return Select(candidates, context, documentationId);
    }

    /// <summary>Returns the URL of a symbol from a configured external mapping, or <see langword="null"/>. URLs are never guessed.</summary>
    public string? GetExternalUrl(string documentationId) => GetExternalUrl(documentationId, symbol: null);

    private string? GetExternalUrl(string documentationId, ApiSymbolModel? symbol)
    {
        var qualifiedName = GetQualifiedName(documentationId);
        foreach (var mapping in _configuration.ExternalReferences)
        {
            if (!mapping.Matches(qualifiedName))
                continue;

            if (GetEnumName(documentationId, qualifiedName, symbol) is { } enumName)
            {
                // Enum values have no page: they are documented on the page of their enum, such as
                // system.stringcomparison#system-stringcomparison-ordinal. The fragment goes after any query string.
                var enumUrl = mapping.UrlTemplate.Replace("{uid}", GetExternalUid("T:" + enumName), StringComparison.Ordinal);
                return enumUrl.Contains('#', StringComparison.Ordinal) ? enumUrl : enumUrl + "#" + GetExternalUid(documentationId).Replace('.', '-');
            }

            return mapping.UrlTemplate.Replace("{uid}", GetExternalUid(documentationId), StringComparison.Ordinal);
        }

        return null;
    }

    /// <summary>
    /// <c>T:System.Collections.Generic.List`1</c> becomes <c>system.collections.generic.list-1</c>,
    /// <c>M:System.String.Join(System.String,System.String[])</c> becomes <c>system.string.join</c> (the overload page), and
    /// <c>M:System.String.System#IComparable#CompareTo(System.Object)</c> becomes <c>system.string.system-icomparable-compareto</c>.
    /// </summary>
    public static string GetExternalUid(string documentationId)
    {
        var key = ApiCatalog.GetNameKey(documentationId);
        var kind = 'T';
        if (key.Length > 2 && key[1] == ':')
        {
            kind = key[0];
            key = key[2..];
        }

        // Select{TSource,TResult} is normalized to Select`2, and generic methods share the page of their overloads
        if (kind is 'M')
        {
            key = TrailingGenericArityRegex().Replace(key, "");
        }

        // #ctor becomes -ctor, and explicit implementations such as System#IEquatable{System#String}#Equals become
        // system-iequatable-system-string--equals
        return string.Create(key.Length, key, static (span, value) =>
        {
            for (var i = 0; i < value.Length; i++)
            {
                span[i] = value[i] is '`' or '#' or '{' or '}' or ',' ? '-' : char.ToLowerInvariant(value[i]);
            }
        });
    }

    /// <summary>Returns the enum of an enum value, such as <c>System.StringComparison</c> for <c>F:System.StringComparison.Ordinal</c>.</summary>
    private static string? GetEnumName(string documentationId, string qualifiedName, ApiSymbolModel? symbol)
    {
        if (symbol is not null)
            return symbol is ApiMemberModel { Category: ApiMemberCategory.EnumValue } member ? GetQualifiedName(member.DeclaringType.DocumentationId) : null;

        if (!documentationId.StartsWith("F:", StringComparison.Ordinal) || qualifiedName.AsSpan().LastIndexOf('.') is not (> 0 and var separator))
            return null;

        var typeName = qualifiedName[..separator];
        return FrameworkEnumIndex.IsEnum(typeName) ? typeName : null;
    }

    /// <summary>Display name of a documentation ID that has no page, such as <c>Client.Send</c> for <c>M:Contoso.Client.Send(System.String)</c>.</summary>
    public static string GetShortDisplayName(string documentationId)
    {
        var kind = documentationId.Length > 2 && documentationId[1] == ':' ? documentationId[0] : 'T';
        var parts = GenericArityRegex().Replace(GetQualifiedName(documentationId), "").Split('.');
        if (kind is 'T' or 'N' || parts.Length < 2)
            return parts[^1];

        var typeName = parts[^2];
        return parts[^1] == "#ctor" ? typeName : typeName + "." + parts[^1];
    }

    private CrefResolution Select(List<ApiSymbolModel> candidates, LinkContext context, string cref)
    {
        // Priority: same assembly, same product, published packages, reference packages
        var levels = new Func<ApiSymbolModel, bool>[]
        {
            c => context.Scope is not null && string.Equals(c.Assembly.Scope, context.Scope, StringComparison.OrdinalIgnoreCase),
            c => context.ProductId is not null && string.Equals(c.Assembly.ProductId, context.ProductId, StringComparison.Ordinal),
            c => !c.Assembly.IsReference,
            c => true,
        };

        foreach (var level in levels)
        {
            var matches = candidates.Where(level).ToList();
            if (matches.Count == 0)
                continue;

            var targets = matches.GroupBy(GetTargetKey, StringComparer.Ordinal).ToList();
            if (targets.Count > 1)
            {
                var descriptions = targets.Select(t => t.First().Assembly.Package.Id + "!" + t.First().DocumentationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                return new CrefResolution(CrefResolutionKind.Ambiguous, Url: null, GetShortDisplayName(cref), Symbol: null, descriptions);
            }

            var symbol = matches[0];
            var isOverloadGroup = matches.Count > 1;
            if (symbol.Assembly.IsReference)
            {
                var external = GetExternalUrl(symbol.DocumentationId, symbol);
                return new CrefResolution(external is null ? CrefResolutionKind.Unresolved : CrefResolutionKind.External, external, GetDisplayName(symbol), symbol, []);
            }

            var url = isOverloadGroup && symbol is ApiMemberModel member ? GetOverloadGroupUrl(member) : GetUrl(symbol);
            return new CrefResolution(url is null ? CrefResolutionKind.Unresolved : CrefResolutionKind.Resolved, url, GetDisplayName(symbol), symbol, []);
        }

        return ResolveExternal(cref);
    }

    /// <summary>Overloads of a member in the same type share a target: the overload group.</summary>
    private static string GetTargetKey(ApiSymbolModel symbol)
    {
        return symbol switch
        {
            ApiMemberModel member => member.Assembly.Scope + "|" + member.DeclaringType.DocumentationId + "|" + member.OverloadName,
            _ => symbol.Assembly.Scope + "|" + symbol.DocumentationId,
        };
    }

    private static string GetDisplayName(ApiSymbolModel symbol)
    {
        return symbol switch
        {
            ApiTypeModel type => type.DisplayName,
            ApiMemberModel { Category: ApiMemberCategory.Constructor } member => member.DeclaringType.DisplayName,
            ApiMemberModel member => member.DeclaringType.DisplayName + "." + member.Name,
            _ => symbol.Name,
        };
    }

    private CrefResolution ResolveNamespace(string namespaceName, LinkContext context)
    {
        var products = _catalog.Products.Where(p => _publishedNamespaces.Contains(p.Product.Id + "|" + namespaceName)).Select(p => p.Product.Id).ToList();
        if (context.ProductId is not null && products.Contains(context.ProductId, StringComparer.Ordinal))
        {
            products = [context.ProductId];
        }

        return products.Count switch
        {
            0 => ResolveExternal("N:" + namespaceName),
            1 => new CrefResolution(CrefResolutionKind.Resolved, GetNamespaceUrl(products[0], namespaceName), namespaceName, Symbol: null, []),
            _ => new CrefResolution(CrefResolutionKind.Ambiguous, Url: null, namespaceName, Symbol: null, [.. products.Select(p => p + "!N:" + namespaceName)]),
        };
    }

    private CrefResolution ResolveExternal(string documentationId)
    {
        var url = GetExternalUrl(documentationId);
        return new CrefResolution(url is null ? CrefResolutionKind.Unresolved : CrefResolutionKind.External, url, GetShortDisplayName(documentationId), Symbol: null, []);
    }

    private void RegisterTypeRoutes(ApiProductModel product)
    {
        var preferred = product.GetAllTypes()
            .Select(type => (Type: type, FileName: RouteNames.GetTypeFileName(type)))
            .ToList();

        foreach (var group in preferred.GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                AddTypeRoute(product.Product.Id, items[0].Type, items[0].FileName);
                continue;
            }

            // Several types map to the same file: disambiguate with the assembly name, then with a stable hash
            var withAssembly = items.Select(i => (i.Type, FileName: i.FileName + "--" + RouteNames.SanitizeFileName(i.Type.Assembly.AssemblyName))).ToList();
            var stillColliding = withAssembly.GroupBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
            var resolved = stillColliding
                ? items.Select(i => (i.Type, FileName: i.FileName + "--" + RouteNames.GetStableHash(i.Type.Assembly.Scope + "|" + i.Type.DocumentationId))).ToList()
                : withAssembly;

            _diagnostics.Report(DiagnosticDescriptors.RouteCollision, $"Types {string.Join(", ", items.Select(i => $"'{i.Type.Assembly.Scope}!{i.Type.DocumentationId}'").Order(StringComparer.Ordinal))} map to the same page '{group.Key}.html' in product '{product.Product.Id}'; their URLs are disambiguated and only stable while this set of types is unchanged", new DiagnosticLocation { Symbol = items[0].Type.DocumentationId });
            foreach (var (type, fileName) in resolved)
            {
                AddTypeRoute(product.Product.Id, type, fileName);
            }
        }
    }

    private void AddTypeRoute(string productId, ApiTypeModel type, string fileName)
    {
        var route = new SiteRoute(RouteNames.GetTypePath(productId, fileName), PageKind.Type, type.Assembly.Scope + "!" + type.DocumentationId);
        if (Register(route))
        {
            _typeRoutes.Add(type, route);
        }
    }

    private bool Register(SiteRoute route) => TryRegister(route, new DiagnosticLocation { Symbol = route.Owner });

    private static string NormalizeGenericSyntax(string value)
    {
        // Contoso.Tree<T>.Node becomes Contoso.Tree`1.Node, and List{T} becomes List`1 outside of parameter lists and
        // explicit implementation names such as System#IEquatable{System#String}#Equals
        var parameterStart = value.IndexOf('(', StringComparison.Ordinal);
        var name = parameterStart >= 0 ? value[..parameterStart] : value;
        var parameters = parameterStart >= 0 ? value[parameterStart..] : "";
        name = GenericArgumentsRegex().Replace(name, match => IsInExplicitImplementationName(name, match.Index)
            ? match.Value
            : "`" + (match.Groups["args"].Value.Count(c => c == ',') + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return name + parameters;

        static bool IsInExplicitImplementationName(string name, int index)
        {
            var segmentStart = name.AsSpan(0, index).LastIndexOf('.') + 1;
            return name.AsSpan(segmentStart, index - segmentStart).Contains('#');
        }
    }

    private static string GetQualifiedName(string documentationId)
    {
        var value = documentationId.Length > 2 && documentationId[1] == ':' ? documentationId[2..] : documentationId;
        var end = value.IndexOfAny(['(', '~']);
        return end >= 0 ? value[..end] : value;
    }

    [GeneratedRegex(@"[<{](?<args>[^<>{}]*)[>}]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GenericArgumentsRegex();

    [GeneratedRegex("`+[0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GenericArityRegex();

    [GeneratedRegex("`[0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrailingGenericArityRegex();
}
