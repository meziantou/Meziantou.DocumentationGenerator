using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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

    public static SiteConfiguration? Load(FullPath configurationFile, DiagnosticBag diagnostics, ConfigurationOverrides? overrides = null)
    {
        if (!File.Exists(configurationFile))
        {
            diagnostics.Report(DiagnosticDescriptors.InputNotFound, $"Configuration file '{configurationFile.Name}' not found", new DiagnosticLocation { FilePath = configurationFile.Name });
            return null;
        }

        var bytes = File.ReadAllBytes(configurationFile);
        return Load(bytes, configurationFile, diagnostics, overrides);
    }

    public static SiteConfiguration? Load(ReadOnlySpan<byte> utf8Json, FullPath configurationFile, DiagnosticBag diagnostics, ConfigurationOverrides? overrides = null)
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
            var message = CleanJsonExceptionMessage(ex.Message);
            if (ex.Path is not null && message.Contains("could not be mapped", StringComparison.Ordinal))
            {
                // Report unknown properties on the property name rather than after its value
                message = $"Unknown property '{ex.Path[(ex.Path.AsSpan().LastIndexOfAny('.', '[') + 1)..].Trim('[', ']', '\'')}'";
                if (locations.Find(ex.Path) is { } location)
                {
                    (line, column) = (location.Line, location.Column);
                }
            }

            diagnostics.Report(DiagnosticDescriptors.InvalidConfigurationSyntax, message, new DiagnosticLocation { FilePath = fileName, Line = line, Column = column, JsonPath = ex.Path });
            return null;
        }

        if (file is null)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidConfigurationSyntax, "The configuration must be a JSON object", new DiagnosticLocation { FilePath = fileName, Line = 1, Column = 1 });
            return null;
        }

        ApplyOverrides(file, overrides ?? ConfigurationOverrides.None);
        var context = new ValidationContext(configurationFile, locations, diagnostics);
        return context.Validate(file);
    }

    private static void ApplyOverrides(ConfigurationFile file, ConfigurationOverrides overrides)
    {
        if (overrides.OutputDirectory is { } output)
        {
            // An absolute path is kept as-is when combined with the configuration directory
            file.Output = output.Value;
        }

        if (overrides.BasePath is not null || overrides.SiteUrl is not null)
        {
            file.Site ??= new ConfigurationFile.SiteSection();
            file.Site.BasePath = overrides.BasePath ?? file.Site.BasePath;
            file.Site.Url = overrides.SiteUrl ?? file.Site.Url;
        }

        if (overrides.DisableSourceLinks)
        {
            file.SourceLinks ??= new ConfigurationFile.SourceLinksSection();
            file.SourceLinks.Enabled = false;
        }
    }

    private static bool IsPreprocessorSymbol(string? value)
    {
        return !string.IsNullOrEmpty(value) && !char.IsAsciiDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// Parses a package version. Unlike NuGet, where <c>1.2.3</c> means "1.2.3 or higher", a version without range
    /// syntax is an exact match so that existing configurations keep their pinned version.
    /// </summary>
    internal static bool TryParseVersionRange(string value, [NotNullWhen(true)] out VersionRange? range)
    {
        if (NuGetVersion.TryParse(value, out var exact))
        {
            range = new VersionRange(exact, includeMinVersion: true, exact, includeMaxVersion: true);
            return true;
        }

        return VersionRange.TryParse(value, allowFloating: true, out range);
    }

        private static string CleanJsonExceptionMessage(string message)
    {
        // Remove the location suffix added by System.Text.Json because the location is reported separately
        var index = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return index >= 0 ? message[..index] : message;
    }

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
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packages = ValidatePackages(file.Packages, "packages", isReference: false, packageIds);
            var references = ValidatePackages(file.References, "references", isReference: true, packageIds);
            var content = ValidateContent(file.Content);
            var apiFilter = ValidateApi(file.Api);
            var xrefs = ValidateXrefs(file.Xrefs);
            var diagnosticOptions = ValidateDiagnostics(file.Diagnostics);
            var sourceLinks = new SourceLinkOptions
            {
                Enabled = file.SourceLinks?.Enabled ?? Default("$.sourceLinks.enabled", value: true),
                SymbolServers = ValidateSymbolServers(file.SourceLinks?.SymbolServers),
            };

            if ((file.Packages is null || file.Packages.Count == 0) && file.Content is null)
            {
                Error(DiagnosticDescriptors.NothingToGenerate, "Configure at least one package in 'packages' or a content directory in 'content.root'", "$");
            }

            if (output is { } outputPath)
            {
                ValidateOutputOverlap(outputPath, cache, content, packages, references);
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
                References = references,
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

        private ImmutableArray<PackageConfiguration> ValidatePackages(List<ConfigurationFile.PackageSection>? sections, string propertyName, bool isReference, HashSet<string> ids)
        {
            if (sections is null)
            {
                _defaultedValues.Add("$." + propertyName);
                return [];
            }

            var result = ImmutableArray.CreateBuilder<PackageConfiguration>();
            for (var i = 0; i < sections.Count; i++)
            {
                var path = $"$.{propertyName}[{i.ToString(CultureInfo.InvariantCulture)}]";
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
                    Error(DiagnosticDescriptors.DuplicateIdentity, $"Package '{section.Id}' is configured more than once in 'packages' and 'references'", path + ".id");
                    isValid = false;
                }

                var versionText = string.IsNullOrWhiteSpace(section.Version) ? "*" : section.Version.Trim();
                if (!TryParseVersionRange(versionText, out var version))
                {
                    Error(DiagnosticDescriptors.InvalidPackageReference, $"Invalid version '{section.Version}': use an exact version (1.2.3), a version range ([1.0,2.0)) or a floating version (1.*, *-*)", path + ".version");
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
                var isLocalFolder = false;
                if (section.Path is not null)
                {
                    if (string.IsNullOrWhiteSpace(section.Path))
                    {
                        Error(DiagnosticDescriptors.InvalidConfigurationValue, "The path cannot be empty", path + ".path");
                        isValid = false;
                    }
                    else
                    {
                        var resolved = Resolve(section.Path);
                        if (Directory.Exists(resolved))
                        {
                            localPath = resolved;
                            isLocalFolder = true;
                        }
                        else if (!File.Exists(resolved))
                        {
                            Error(DiagnosticDescriptors.InputNotFound, $"File or directory '{section.Path}' not found", path + ".path");
                            isValid = false;
                        }
                        else if (!resolved.Extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
                        {
                            Error(DiagnosticDescriptors.InvalidPackageReference, $"'{section.Path}' is neither a .nupkg file nor a directory", path + ".path");
                            isValid = false;
                        }
                        else
                        {
                            localPath = resolved;
                        }
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

                var defineConstants = ImmutableArray.CreateBuilder<string>();
                if (section.DefineConstants is not null)
                {
                    for (var j = 0; j < section.DefineConstants.Count; j++)
                    {
                        var value = section.DefineConstants[j];
                        if (!IsPreprocessorSymbol(value))
                        {
                            Error(DiagnosticDescriptors.InvalidConfigurationValue, $"Invalid preprocessor symbol '{value}': use letters, digits and '_', not starting with a digit", $"{path}.defineConstants[{j.ToString(CultureInfo.InvariantCulture)}]");
                            isValid = false;
                        }
                        else if (!defineConstants.Contains(value, StringComparer.Ordinal))
                        {
                            defineConstants.Add(value);
                        }
                    }
                }

                if (isValid && version is not null)
                {
                    result.Add(new PackageConfiguration
                    {
                        Id = section.Id!,
                        Version = version,
                        VersionText = versionText,
                        Source = section.Source,
                        LocalPath = localPath,
                        IsLocalFolder = isLocalFolder,
                        TargetFrameworks = frameworks.ToImmutable(),
                        Assemblies = assemblies.ToImmutable(),
                        DefineConstants = defineConstants.ToImmutable(),
                        Index = i,
                        IsReference = isReference,
                    });
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

            string? editUrl = null;
            if (section.EditUrl is not null)
            {
                const string Placeholder = ContentConfiguration.EditUrlPathPlaceholder;
                if (!section.EditUrl.Contains(Placeholder, StringComparison.Ordinal) ||
                    !Uri.TryCreate(section.EditUrl.Replace(Placeholder, "x", StringComparison.Ordinal), UriKind.Absolute, out var editUri) || editUri.Scheme is not ("https" or "http"))
                {
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, $"The edit URL must be an absolute http or https URL containing {Placeholder}", "$.content.editUrl");
                }
                else
                {
                    editUrl = section.EditUrl;
                }
            }

            return new ContentConfiguration
            {
                Root = root,
                Assets = assets.ToImmutable(),
                Exclude = section.Exclude is null ? [] : [.. section.Exclude],
                EditUrl = editUrl,
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
            {
                _defaultedValues.Add("$.xrefs");
                return ExternalReferenceMapping.Defaults;
            }

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

        private ImmutableArray<Uri> ValidateSymbolServers(List<string>? servers)
        {
            if (servers is null)
                return [];

            var result = ImmutableArray.CreateBuilder<Uri>();
            for (var i = 0; i < servers.Count; i++)
            {
                var path = $"$.sourceLinks.symbolServers[{i.ToString(CultureInfo.InvariantCulture)}]";
                if (!Uri.TryCreate(servers[i], UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                {
                    Error(DiagnosticDescriptors.InvalidConfigurationValue, $"Invalid symbol server '{servers[i]}': an absolute http or https URL is required", path);
                }
                else if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    Error(DiagnosticDescriptors.CredentialsInConfiguration, "Symbol server URLs must not contain credentials", path);
                }
                else
                {
                    result.Add(uri);
                }
            }

            return result.ToImmutable();
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

        private void ValidateOutputOverlap(FullPath output, FullPath? cache, ContentConfiguration? content, ImmutableArray<PackageConfiguration> packages, ImmutableArray<PackageConfiguration> references)
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

            foreach (var package in packages.Concat(references))
            {
                if (package.LocalPath is { } localPath)
                {
                    Check(localPath, $"package '{package.Id}'", package.JsonPath + ".path");
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
