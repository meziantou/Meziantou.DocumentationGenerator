using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Loads and validates <c>docgen.json</c>. Loading never fetches packages nor writes to the output directory.</summary>
internal static partial class ConfigurationLoader
{
    public const string DefaultFileName = "docgen.json";
    public const string CacheEnvironmentVariable = "DOCGEN_CACHE";

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static SiteConfiguration? Load(FullPath configurationFile, DiagnosticBag diagnostics)
    {
        if (!File.Exists(configurationFile))
        {
            diagnostics.Report(DiagnosticDescriptors.InputNotFound, $"Configuration file '{configurationFile.Name}' not found", new DiagnosticLocation { FilePath = configurationFile.Name });
            return null;
        }

        var bytes = File.ReadAllBytes(configurationFile);
        return Load(bytes, configurationFile, diagnostics);
    }

    public static SiteConfiguration? Load(ReadOnlySpan<byte> utf8Json, FullPath configurationFile, DiagnosticBag diagnostics)
    {
        if (utf8Json.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8Json = utf8Json[3..];
        }

        var fileName = configurationFile.Name;
        var locations = JsonLocationMap.Create(utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        ConfigurationFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigurationFile>(utf8Json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } l ? (int)l + 1 : (int?)null;
            var column = ex.BytePositionInLine is { } c ? (int)c + 1 : (int?)null;
            diagnostics.Report(DiagnosticDescriptors.InvalidConfigurationSyntax, CleanJsonExceptionMessage(ex.Message), new DiagnosticLocation { FilePath = fileName, Line = line, Column = column, JsonPath = ex.Path });
            return null;
        }

        if (file is null)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidConfigurationSyntax, "The configuration must be a JSON object", new DiagnosticLocation { FilePath = fileName, Line = 1, Column = 1 });
            return null;
        }

        var context = new ValidationContext(configurationFile, locations, diagnostics);
        return context.Validate(file);
    }

    private static string CleanJsonExceptionMessage(string message)
    {
        // Remove the location suffix added by System.Text.Json because the location is reported separately
        var index = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return index >= 0 ? message[..index] : message;
    }

    [GeneratedRegex("^[a-z0-9]+(?:[-.][a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex SlugRegex();

    [GeneratedRegex(@"^DG\d{4}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DiagnosticCodeRegex();

    private sealed class ValidationContext(FullPath configurationFile, JsonLocationMap locations, DiagnosticBag diagnostics)
    {
        private readonly FullPath _baseDirectory = configurationFile.Parent;
        private readonly List<string> _defaultedValues = [];
        private int _errorCount;

        public SiteConfiguration? Validate(ConfigurationFile file)
        {
            ValidateVersion(file);
            var site = ValidateSite(file.Site);
            var output = ValidateOutput(file.Output);
            var cache = ValidateCacheDirectory(file.CacheDirectory);
            var packages = ValidatePackages(file.Packages);
            var products = ValidateProducts(file.Products, packages);
            var content = ValidateContent(file.Content);
            var apiFilter = ValidateApi(file.Api);
            var xrefs = ValidateXrefs(file.Xrefs);
            var diagnosticOptions = ValidateDiagnostics(file.Diagnostics);
            var sourceLinks = new SourceLinkOptions
            {
                Enabled = file.SourceLinks?.Enabled ?? Default("$.sourceLinks.enabled", value: true),
                SymbolPackages = file.SourceLinks?.SymbolPackages ?? Default("$.sourceLinks.symbolPackages", value: false),
            };

            if ((file.Packages is null || file.Packages.Count == 0) && file.Content is null)
            {
                Error(DiagnosticDescriptors.NothingToGenerate, "Configure at least one package in 'packages' or a content directory in 'content.root'", "$");
            }

            if (output is { } outputPath)
            {
                ValidateOutputOverlap(outputPath, cache, content, packages, products);
            }

            if (_errorCount > 0 || site is null || output is null || cache is null || apiFilter is null)
                return null;

            return new SiteConfiguration
            {
                ConfigurationFile = configurationFile,
                Site = site,
                OutputDirectory = output.Value,
                CacheDirectory = cache.Value,
                Packages = packages,
                Products = products,
                Content = content,
                ApiFilter = apiFilter,
                ExternalReferences = xrefs,
                Diagnostics = diagnosticOptions,
                SourceLinks = sourceLinks,
                DefaultedValues = [.. _defaultedValues],
            };
        }

        private void ValidateVersion(ConfigurationFile file)
        {
            if (file.Version is null)
            {
                Error(DiagnosticDescriptors.UnsupportedConfigurationVersion, $"The 'version' property is required; use {ConfigurationFile.CurrentVersion}", "$");
            }
            else if (file.Version != ConfigurationFile.CurrentVersion)
            {
                Error(DiagnosticDescriptors.UnsupportedConfigurationVersion, $"Configuration version {file.Version.Value.ToString(CultureInfo.InvariantCulture)} is not supported; supported version: {ConfigurationFile.CurrentVersion}", "$.version");
            }
        }

        private SiteSettings? ValidateSite(ConfigurationFile.SiteSection? section)
        {
            section ??= new ConfigurationFile.SiteSection();
            if (!UrlBasePath.TryParse(section.BasePath, out var basePath, out var basePathError))
            {
                Error(DiagnosticDescriptors.InvalidSiteUrl, $"Invalid base path '{section.BasePath}': {basePathError}", "$.site.basePath");
            }
            else if (section.BasePath is null)
            {
                _defaultedValues.Add("$.site.basePath");
            }

            Uri? siteUrl = null;
            if (section.Url is not null)
            {
                if (!Uri.TryCreate(section.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                {
                    Error(DiagnosticDescriptors.InvalidSiteUrl, $"Invalid site URL '{section.Url}': an absolute http or https URL is required", "$.site.url");
                }
                else if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    Error(DiagnosticDescriptors.CredentialsInConfiguration, "The site URL must not contain credentials", "$.site.url");
                }
                else if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                {
                    Error(DiagnosticDescriptors.InvalidSiteUrl, $"The site URL '{section.Url}' must be an origin without path, query or fragment; set the path in site.basePath (for example \"basePath\": \"{uri.AbsolutePath}\")", "$.site.url");
                }
                else
                {
                    siteUrl = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
                }
            }

            var robots = RobotsPolicy.None;
            switch (section.Robots?.ToLowerInvariant())
            {
                case null:
                    _defaultedValues.Add("$.site.robots");
                    break;
                case "none":
                    break;
                case "allow":
                    robots = RobotsPolicy.Allow;
                    break;
                case "disallow":
                    robots = RobotsPolicy.Disallow;
                    break;
                default:
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, $"Invalid robots policy '{section.Robots}'; supported values: none, allow, disallow", "$.site.robots");
                    break;
            }

            if (robots is not RobotsPolicy.None && !basePath.IsRoot)
            {
                Report(DiagnosticDescriptors.RobotsUnderSubpath, $"robots.txt is only honored at the origin root; it is not generated under the base path '{basePath}'. Configure robots.txt at the origin root instead", "$.site.robots");
            }

            var language = section.Language ?? Default("$.site.language", "en");
            try
            {
                _ = CultureInfo.GetCultureInfo(language, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                Error(DiagnosticDescriptors.InvalidConfigurationValue, $"Unknown language '{language}'", "$.site.language");
            }

            var socialImage = ResolveExistingFile(section.SocialImage, "$.site.socialImage");
            if (socialImage is not null && siteUrl is null)
            {
                Error(DiagnosticDescriptors.InvalidSiteUrl, "site.socialImage requires site.url because social previews need absolute image URLs", "$.site.socialImage");
            }

            return new SiteSettings
            {
                Title = string.IsNullOrWhiteSpace(section.Title) ? Default("$.site.title", "Documentation") : section.Title.Trim(),
                Description = section.Description,
                Language = language,
                BasePath = basePath,
                SiteUrl = siteUrl,
                Logo = ResolveExistingFile(section.Logo, "$.site.logo"),
                Favicon = ResolveExistingFile(section.Favicon, "$.site.favicon"),
                SocialImage = socialImage,
                Robots = robots,
                Footer = section.Footer,
            };
        }

        private FullPath? ValidateOutput(string? output)
        {
            if (output is not null && string.IsNullOrWhiteSpace(output))
            {
                Error(DiagnosticDescriptors.InvalidConfigurationValue, "The output directory cannot be empty", "$.output");
                return null;
            }

            return Resolve(output ?? Default("$.output", "_site"));
        }

        private FullPath? ValidateCacheDirectory(string? cacheDirectory)
        {
            if (cacheDirectory is not null)
                return Resolve(cacheDirectory);

            _defaultedValues.Add("$.cacheDirectory");
            var fromEnvironment = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment))
                return FullPath.FromPath(fromEnvironment);

            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(localData))
            {
                localData = Path.GetTempPath();
            }

            return FullPath.FromPath(localData) / "Meziantou.DocumentationGenerator" / "cache";
        }

        private ImmutableArray<PackageConfiguration> ValidatePackages(List<ConfigurationFile.PackageSection>? sections)
        {
            if (sections is null)
            {
                _defaultedValues.Add("$.packages");
                return [];
            }

            var result = ImmutableArray.CreateBuilder<PackageConfiguration>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sections.Count; i++)
            {
                var path = $"$.packages[{i.ToString(CultureInfo.InvariantCulture)}]";
                var section = sections[i];
                if (section is null)
                {
                    Error(DiagnosticDescriptors.InvalidPackageReference, "A package entry cannot be null", path);
                    continue;
                }

                var isValid = true;
                if (string.IsNullOrWhiteSpace(section.Id) || !PackageIdValidator.IsValidPackageId(section.Id))
                {
                    Error(DiagnosticDescriptors.InvalidPackageReference, $"Invalid package identifier '{section.Id}'", path + ".id");
                    isValid = false;
                }
                else if (!ids.Add(section.Id))
                {
                    Error(DiagnosticDescriptors.DuplicateIdentity, $"Package '{section.Id}' is configured more than once", path + ".id");
                    isValid = false;
                }

                NuGetVersion? version = null;
                if (string.IsNullOrWhiteSpace(section.Version))
                {
                    Error(DiagnosticDescriptors.InvalidPackageReference, $"The version of package '{section.Id}' is required; versions must be pinned for reproducible builds", path);
                    isValid = false;
                }
                else if (!NuGetVersion.TryParse(section.Version, out version))
                {
                    var message = section.Version.IndexOfAny(['[', '(', '*', ',']) >= 0
                        ? $"Version ranges are not supported ('{section.Version}'); use an exact version"
                        : $"Invalid version '{section.Version}'";
                    Error(DiagnosticDescriptors.InvalidPackageReference, message, path + ".version");
                    isValid = false;
                }

                if (section.Source is not null)
                {
                    if (Uri.TryCreate(section.Source, UriKind.Absolute, out var sourceUri) && !string.IsNullOrEmpty(sourceUri.UserInfo))
                    {
                        Error(DiagnosticDescriptors.CredentialsInConfiguration, "Package sources must not contain credentials; configure them in NuGet.Config or a credential provider", path + ".source");
                        isValid = false;
                    }
                    else if (section.Path is not null)
                    {
                        Error(DiagnosticDescriptors.InvalidPackageReference, "'source' and 'path' cannot be used together", path + ".path");
                        isValid = false;
                    }
                }

                FullPath? localPath = null;
                if (section.Path is not null)
                {
                    localPath = ResolveExistingFile(section.Path, path + ".path");
                    if (localPath is { } p && !p.Extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
                    {
                        Error(DiagnosticDescriptors.InvalidPackageReference, $"'{section.Path}' is not a .nupkg file", path + ".path");
                        isValid = false;
                    }
                }

                var frameworks = ImmutableArray.CreateBuilder<NuGetFramework>();
                if (section.TargetFrameworks is not null)
                {
                    for (var j = 0; j < section.TargetFrameworks.Count; j++)
                    {
                        var value = section.TargetFrameworks[j];
                        var framework = string.IsNullOrWhiteSpace(value) ? NuGetFramework.UnsupportedFramework : NuGetFramework.Parse(value);
                        if (framework.IsUnsupported || framework.IsAny || framework.IsAgnostic)
                        {
                            Error(DiagnosticDescriptors.InvalidTargetFramework, $"Invalid target framework '{value}'", $"{path}.targetFrameworks[{j.ToString(CultureInfo.InvariantCulture)}]");
                            isValid = false;
                        }
                        else if (frameworks.Contains(framework))
                        {
                            Error(DiagnosticDescriptors.DuplicateIdentity, $"Target framework '{value}' is listed more than once", $"{path}.targetFrameworks[{j.ToString(CultureInfo.InvariantCulture)}]");
                            isValid = false;
                        }
                        else
                        {
                            frameworks.Add(framework);
                        }
                    }
                }

                var assemblies = ImmutableArray.CreateBuilder<string>();
                if (section.Assemblies is not null)
                {
                    for (var j = 0; j < section.Assemblies.Count; j++)
                    {
                        var value = section.Assemblies[j];
                        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['/', '\\']) >= 0 || !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            Error(DiagnosticDescriptors.InvalidConfigurationValue, $"Invalid assembly filter '{value}': use an assembly file name such as Contoso.dll", $"{path}.assemblies[{j.ToString(CultureInfo.InvariantCulture)}]");
                            isValid = false;
                        }
                        else
                        {
                            assemblies.Add(value);
                        }
                    }
                }

                var readme = ResolveExistingFile(section.Readme, path + ".readme");
                if (isValid && version is not null)
                {
                    result.Add(new PackageConfiguration
                    {
                        Id = section.Id!,
                        Version = version,
                        Source = section.Source,
                        LocalPath = localPath,
                        TargetFrameworks = frameworks.ToImmutable(),
                        Assemblies = assemblies.ToImmutable(),
                        ReadmeOverride = readme,
                        Index = i,
                    });
                }
            }

            return result.ToImmutable();
        }

        private ImmutableArray<ProductConfiguration> ValidateProducts(List<ConfigurationFile.ProductSection>? sections, ImmutableArray<PackageConfiguration> packages)
        {
            if (sections is null)
            {
                _defaultedValues.Add("$.products");
                var implicitProducts = ImmutableArray.CreateBuilder<ProductConfiguration>();
                var slugs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var package in packages)
                {
                    var slug = package.Id.ToLowerInvariant();
                    if (!SlugRegex().IsMatch(slug))
                    {
                        Error(DiagnosticDescriptors.InvalidProductMapping, $"Package '{package.Id}' cannot be used as a product identifier; declare a product with an explicit id", $"$.packages[{package.Index.ToString(CultureInfo.InvariantCulture)}].id");
                        continue;
                    }

                    if (!slugs.Add(slug))
                        continue;

                    implicitProducts.Add(new ProductConfiguration
                    {
                        Id = slug,
                        Name = package.Id,
                        Packages = [package.Id],
                        IsImplicit = true,
                    });
                }

                return implicitProducts.ToImmutable();
            }

            var result = ImmutableArray.CreateBuilder<ProductConfiguration>();
            var productIds = new HashSet<string>(StringComparer.Ordinal);
            var packageOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sections.Count; i++)
            {
                var path = $"$.products[{i.ToString(CultureInfo.InvariantCulture)}]";
                var section = sections[i];
                if (section is null)
                {
                    Error(DiagnosticDescriptors.InvalidProductMapping, "A product entry cannot be null", path);
                    continue;
                }

                var isValid = true;
                if (string.IsNullOrEmpty(section.Id) || !SlugRegex().IsMatch(section.Id))
                {
                    Error(DiagnosticDescriptors.InvalidProductMapping, $"Invalid product identifier '{section.Id}': use lowercase letters, digits, '-' and '.'", path + ".id");
                    isValid = false;
                }
                else if (!productIds.Add(section.Id))
                {
                    Error(DiagnosticDescriptors.DuplicateIdentity, $"Product '{section.Id}' is defined more than once", path + ".id");
                    isValid = false;
                }

                if (section.Packages is null || section.Packages.Count == 0)
                {
                    Error(DiagnosticDescriptors.InvalidProductMapping, $"Product '{section.Id}' must reference at least one package", path);
                    continue;
                }

                var productPackages = ImmutableArray.CreateBuilder<string>();
                for (var j = 0; j < section.Packages.Count; j++)
                {
                    var packageId = section.Packages[j];
                    var packagePath = $"{path}.packages[{j.ToString(CultureInfo.InvariantCulture)}]";
                    var package = packages.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
                    if (package is null)
                    {
                        Error(DiagnosticDescriptors.InvalidProductMapping, $"Product '{section.Id}' references the package '{packageId}' which is not declared in 'packages'", packagePath);
                        isValid = false;
                    }
                    else if (packageOwners.TryGetValue(package.Id, out var owner))
                    {
                        Error(DiagnosticDescriptors.InvalidProductMapping, $"Package '{package.Id}' belongs to products '{owner}' and '{section.Id}'; a package can belong to one product only", packagePath);
                        isValid = false;
                    }
                    else
                    {
                        packageOwners[package.Id] = section.Id ?? "";
                        productPackages.Add(package.Id);
                    }
                }

                string? readmePackage = null;
                if (section.ReadmePackage is not null)
                {
                    readmePackage = productPackages.FirstOrDefault(p => string.Equals(p, section.ReadmePackage, StringComparison.OrdinalIgnoreCase));
                    if (readmePackage is null)
                    {
                        Error(DiagnosticDescriptors.InvalidProductMapping, $"readmePackage '{section.ReadmePackage}' is not one of the packages of product '{section.Id}'", path + ".readmePackage");
                        isValid = false;
                    }
                }

                var landingPage = ResolveExistingFile(section.LandingPage, path + ".landingPage");
                if (isValid)
                {
                    result.Add(new ProductConfiguration
                    {
                        Id = section.Id!,
                        Name = string.IsNullOrWhiteSpace(section.Name) ? productPackages[0] : section.Name.Trim(),
                        Description = section.Description,
                        Packages = productPackages.ToImmutable(),
                        LandingPage = landingPage,
                        ReadmePackage = readmePackage,
                        IsImplicit = false,
                    });
                }
            }

            foreach (var package in packages)
            {
                if (!packageOwners.ContainsKey(package.Id))
                {
                    Error(DiagnosticDescriptors.InvalidProductMapping, $"Package '{package.Id}' does not belong to any product", $"$.packages[{package.Index.ToString(CultureInfo.InvariantCulture)}]");
                }
            }

            return result.ToImmutable();
        }

        private ContentConfiguration? ValidateContent(ConfigurationFile.ContentSection? section)
        {
            if (section is null)
            {
                _defaultedValues.Add("$.content");
                return null;
            }

            if (string.IsNullOrWhiteSpace(section.Root))
            {
                Error(DiagnosticDescriptors.InvalidConfigurationValue, "content.root is required", "$.content");
                return null;
            }

            var root = Resolve(section.Root);
            if (!Directory.Exists(root))
            {
                Error(DiagnosticDescriptors.InputNotFound, $"Content directory '{section.Root}' not found", "$.content.root");
                return null;
            }

            FullPath? toc = null;
            if (section.Toc is not null)
            {
                var path = root / section.Toc;
                if (!path.IsChildOf(root))
                {
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, $"The table of contents '{section.Toc}' must be inside the content root", "$.content.toc");
                }
                else if (!File.Exists(path))
                {
                    Error(DiagnosticDescriptors.InputNotFound, $"Table of contents '{section.Toc}' not found", "$.content.toc");
                }
                else
                {
                    toc = path;
                }
            }
            else
            {
                _defaultedValues.Add("$.content.toc");
                if (File.Exists(root / "toc.yml"))
                {
                    toc = root / "toc.yml";
                }
            }

            var assets = ImmutableArray.CreateBuilder<string>();
            if (section.Assets is not null)
            {
                for (var i = 0; i < section.Assets.Count; i++)
                {
                    var asset = section.Assets[i];
                    var jsonPath = $"$.content.assets[{i.ToString(CultureInfo.InvariantCulture)}]";
                    if (string.IsNullOrWhiteSpace(asset))
                    {
                        Error(DiagnosticDescriptors.InvalidConfigurationValue, "An asset path cannot be empty", jsonPath);
                        continue;
                    }

                    var assetPath = root / asset;
                    if (!assetPath.IsChildOf(root))
                    {
                        Error(DiagnosticDescriptors.InvalidConfigurationValue, $"The asset '{asset}' must be inside the content root", jsonPath);
                    }
                    else if (!File.Exists(assetPath) && !Directory.Exists(assetPath))
                    {
                        Error(DiagnosticDescriptors.InputNotFound, $"Asset '{asset}' not found", jsonPath);
                    }
                    else
                    {
                        assets.Add(assetPath.MakePathRelativeTo(root).Replace('\\', '/'));
                    }
                }
            }

            return new ContentConfiguration
            {
                Root = root,
                Toc = toc,
                Assets = assets.ToImmutable(),
                Exclude = section.Exclude is null ? [] : [.. section.Exclude],
            };
        }

        private ApiFilter? ValidateApi(ConfigurationFile.ApiSection? section)
        {
            if (section is null)
                return ApiFilter.All;

            if (!ApiFilter.TryCreate(section.Include ?? [], section.Exclude ?? [], out var filter, out var error))
            {
                Error(DiagnosticDescriptors.InvalidFilterPattern, error!, "$.api");
                return null;
            }

            return filter;
        }

        private ImmutableArray<ExternalReferenceMapping> ValidateXrefs(List<ConfigurationFile.XrefSection>? sections)
        {
            if (sections is null)
                return [];

            var result = ImmutableArray.CreateBuilder<ExternalReferenceMapping>();
            for (var i = 0; i < sections.Count; i++)
            {
                var path = $"$.xrefs[{i.ToString(CultureInfo.InvariantCulture)}]";
                var section = sections[i];
                if (section is null || string.IsNullOrWhiteSpace(section.Namespace))
                {
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, "An external reference requires a namespace", path);
                    continue;
                }

                if (section.Url is null || !section.Url.Contains("{uid}", StringComparison.Ordinal) ||
                    !Uri.TryCreate(section.Url.Replace("{uid}", "x", StringComparison.Ordinal), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                {
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, "The external reference URL must be an absolute http or https URL containing {uid}", path + ".url");
                    continue;
                }

                result.Add(new ExternalReferenceMapping(section.Namespace.Trim(), section.Url));
            }

            // Longest prefix first, so that a specific mapping wins over a general one
            return [.. result.OrderByDescending(x => x.Namespace.Length).ThenBy(x => x.Namespace, StringComparer.Ordinal)];
        }

        private DiagnosticOptions ValidateDiagnostics(ConfigurationFile.DiagnosticsSection? section)
        {
            if (section is null)
                return DiagnosticOptions.Default;

            var overrides = ImmutableDictionary.CreateBuilder<string, DiagnosticSeverity>(StringComparer.OrdinalIgnoreCase);
            if (section.Severity is not null)
            {
                foreach (var (code, value) in section.Severity)
                {
                    var path = $"$.diagnostics.severity.{code}";
                    if (!DiagnosticCodeRegex().IsMatch(code) || DiagnosticDescriptors.Find(code) is not { } descriptor)
                    {
                        Error(DiagnosticDescriptors.InvalidDiagnosticOverride, $"Unknown diagnostic code '{code}'", path);
                        continue;
                    }

                    DiagnosticSeverity? severity = value?.ToLowerInvariant() switch
                    {
                        "error" => DiagnosticSeverity.Error,
                        "warning" => DiagnosticSeverity.Warning,
                        "info" => DiagnosticSeverity.Info,
                        "none" => DiagnosticSeverity.None,
                        _ => null,
                    };

                    if (severity is null)
                    {
                        Error(DiagnosticDescriptors.InvalidDiagnosticOverride, $"Invalid severity '{value}' for {code}; supported values: error, warning, info, none", path);
                    }
                    else if (descriptor.IsSafetyCritical && severity < descriptor.DefaultSeverity)
                    {
                        Error(DiagnosticDescriptors.InvalidDiagnosticOverride, $"{descriptor.Code} reports unsafe input and cannot be lowered below {descriptor.DefaultSeverity.ToString().ToLowerInvariant()}", path);
                    }
                    else
                    {
                        overrides[descriptor.Code] = severity.Value;
                    }
                }
            }

            return new DiagnosticOptions
            {
                WarningsAsErrors = section.WarningsAsErrors ?? false,
                SeverityOverrides = overrides.ToImmutable(),
            };
        }

        private void ValidateOutputOverlap(FullPath output, FullPath? cache, ContentConfiguration? content, ImmutableArray<PackageConfiguration> packages, ImmutableArray<ProductConfiguration> products)
        {
            // The publisher deletes stale generated files from the output directory, so inputs must never be inside it
            void Check(FullPath input, string description, string jsonPath)
            {
                if (input == output || input.IsChildOf(output))
                {
                    Error(DiagnosticDescriptors.DangerousOutputPath, $"The output directory contains the {description}; choose a dedicated output directory", jsonPath);
                }
            }

            if (output == _baseDirectory || _baseDirectory.IsChildOf(output) || output.Parent == output)
            {
                Error(DiagnosticDescriptors.DangerousOutputPath, "The output directory must be a dedicated directory, not the configuration directory, one of its parents or a filesystem root", "$.output");
                return;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrEmpty(home) && output == FullPath.FromPath(home))
            {
                Error(DiagnosticDescriptors.DangerousOutputPath, "The output directory cannot be the user profile directory", "$.output");
                return;
            }

            if (content is not null)
            {
                Check(content.Root, "content directory", "$.content.root");
                if (output.IsChildOf(content.Root))
                {
                    Error(DiagnosticDescriptors.DangerousOutputPath, "The output directory cannot be inside the content directory", "$.output");
                }
            }

            if (cache is { } cachePath)
            {
                Check(cachePath, "package cache", "$.cacheDirectory");
            }

            foreach (var package in packages)
            {
                if (package.LocalPath is { } localPath)
                {
                    Check(localPath, $"package '{package.Id}'", $"$.packages[{package.Index.ToString(CultureInfo.InvariantCulture)}].path");
                }

                if (package.ReadmeOverride is { } readme)
                {
                    Check(readme, $"README of package '{package.Id}'", $"$.packages[{package.Index.ToString(CultureInfo.InvariantCulture)}].readme");
                }
            }

            foreach (var product in products)
            {
                if (product.LandingPage is { } landingPage)
                {
                    Check(landingPage, $"landing page of product '{product.Id}'", "$.products");
                }
            }
        }

        private FullPath Resolve(string path) => FullPath.Combine(_baseDirectory, path);

        private FullPath? ResolveExistingFile(string? path, string jsonPath)
        {
            if (path is null)
                return null;

            if (string.IsNullOrWhiteSpace(path))
            {
                Error(DiagnosticDescriptors.InvalidConfigurationValue, "The path cannot be empty", jsonPath);
                return null;
            }

            var fullPath = Resolve(path);
            if (!File.Exists(fullPath))
            {
                Error(DiagnosticDescriptors.InputNotFound, $"File '{path}' not found", jsonPath);
                return null;
            }

            return fullPath;
        }

        private T Default<T>(string jsonPath, T value)
        {
            _defaultedValues.Add(jsonPath);
            return value;
        }

        private void Error(DiagnosticDescriptor descriptor, string message, string jsonPath)
        {
            _errorCount++;
            Report(descriptor, message, jsonPath);
        }

        private void Report(DiagnosticDescriptor descriptor, string message, string jsonPath)
        {
            var location = locations.Find(jsonPath);
            diagnostics.Report(descriptor, message, new DiagnosticLocation
            {
                FilePath = configurationFile.Name,
                Line = location?.Line,
                Column = location?.Column,
                JsonPath = jsonPath,
            });
        }
    }
}
