using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Meziantou.DocumentationGenerator.Configuration;

/// <summary>Serialization model of <c>docgen.json</c>. Values are nullable so that missing values can be distinguished from explicit ones.</summary>
[Description("Configuration of Meziantou.DocumentationGenerator.")]
internal sealed class ConfigurationFile
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("$schema")]
    [Description("JSON schema used by editors.")]
    public string? Schema { get; set; }

    [Description("Version of the configuration format. Must be 1.")]
    public int? Version { get; set; }

    [Description("Site identity and deployment settings.")]
    public SiteSection? Site { get; set; }

    [Description("Output directory, relative to the configuration file. Default: _site.")]
    public string? Output { get; set; }

    [Description("Package cache directory, relative to the configuration file. Default: a per-user cache directory, or the DOCGEN_CACHE environment variable.")]
    public string? CacheDirectory { get; set; }

    [Description("NuGet packages whose APIs are documented.")]
    public List<PackageSection>? Packages { get; set; }

    [Description("Packages whose metadata and XML documentation are used for inherited documentation and cross-references, without publishing their APIs.")]
    public List<PackageSection>? References { get; set; }

    [Description("Products group packages under a landing page and an API reference. Default: one product per package.")]
    public List<ProductSection>? Products { get; set; }

    [Description("Authored Markdown content.")]
    public ContentSection? Content { get; set; }

    [Description("API selection rules.")]
    public ApiSection? Api { get; set; }

    [Description("Links to documentation of symbols that are not part of the configured packages.")]
    public List<XrefSection>? Xrefs { get; set; }

    [Description("Diagnostic settings.")]
    public DiagnosticsSection? Diagnostics { get; set; }

    [Description("Source link settings.")]
    public SourceLinksSection? SourceLinks { get; set; }

    internal sealed class SiteSection
    {
        [Description("Site title. Default: Documentation.")]
        public string? Title { get; set; }

        [Description("Site description used in metadata.")]
        public string? Description { get; set; }

        [Description("Language of the content (BCP 47). Default: en.")]
        public string? Language { get; set; }

        [Description("URL path where the site is deployed, such as / or /docs/. Default: /.")]
        public string? BasePath { get; set; }

        [Description("Absolute origin of the site, such as https://example.com, used for canonical URLs and the sitemap. Must not contain a path; use basePath instead.")]
        public string? Url { get; set; }

        [Description("Logo image file, relative to the configuration file.")]
        public string? Logo { get; set; }

        [Description("Favicon file, relative to the configuration file.")]
        public string? Favicon { get; set; }

        [Description("Image used for social previews, relative to the configuration file. Requires url.")]
        public string? SocialImage { get; set; }

        [Description("robots policy: none (no robots.txt), allow, or disallow. Default: none.")]
        public string? Robots { get; set; }

        [Description("Footer text displayed on every page.")]
        public string? Footer { get; set; }
    }

    internal sealed class PackageSection
    {
        [Description("NuGet package identifier.")]
        public string? Id { get; set; }

        [Description("Exact package version. Version ranges are not supported.")]
        public string? Version { get; set; }

        [Description("Package source URL or name from NuGet.Config. Default: the sources from NuGet.Config.")]
        public string? Source { get; set; }

        [Description("Local .nupkg file, relative to the configuration file. When set, no feed is queried.")]
        public string? Path { get; set; }

        [Description("Target framework folders to document, such as net8.0. Default: all.")]
        public List<string>? TargetFrameworks { get; set; }

        [Description("Assembly file names to document, such as Contoso.dll. Default: all managed assemblies.")]
        public List<string>? Assemblies { get; set; }
    }

    internal sealed class ProductSection
    {
        [Description("Product identifier, used in URLs. Lowercase letters, digits, '-' and '.'.")]
        public string? Id { get; set; }

        [Description("Display name. Default: the first package identifier.")]
        public string? Name { get; set; }

        [Description("Short description displayed on the API reference page of the product. Default: the package description.")]
        public string? Description { get; set; }

        [Description("Identifiers of the packages of the product.")]
        public List<string>? Packages { get; set; }
    }

    internal sealed class ContentSection
    {
        [Description("Directory containing the Markdown pages, relative to the configuration file.")]
        public string? Root { get; set; }

        [Description("Additional files or directories copied as-is, relative to the content root.")]
        public List<string>? Assets { get; set; }

        [Description("Glob patterns of Markdown files to ignore, relative to the content root.")]
        public List<string>? Exclude { get; set; }
    }

    internal sealed class ApiSection
    {
        [Description("Patterns of namespaces or types to document. Default: all visible APIs.")]
        public List<string>? Include { get; set; }

        [Description("Patterns of namespaces, types or members to exclude.")]
        public List<string>? Exclude { get; set; }
    }

    internal sealed class XrefSection
    {
        [Description("Namespace prefix handled by this entry, such as System.")]
        public string? Namespace { get; set; }

        [Description("URL template. {uid} is replaced by the lowercase documentation ID without its prefix.")]
        public string? Url { get; set; }
    }

    internal sealed class DiagnosticsSection
    {
        [Description("Treat warnings as errors.")]
        public bool? WarningsAsErrors { get; set; }

        [Description("Severity overrides: code to error, warning, info or none.")]
        public Dictionary<string, string>? Severity { get; set; }
    }

    internal sealed class SourceLinksSection
    {
        [Description("Generate links to source files using PDBs and Source Link. Default: true.")]
        public bool? Enabled { get; set; }

        [Description("Symbol servers (SSQP, such as https://symbols.nuget.org/download/symbols) queried for PDBs that are not in the package. Default: none.")]
        public List<string>? SymbolServers { get; set; }
    }
}
