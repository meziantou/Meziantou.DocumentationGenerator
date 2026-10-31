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

    [Description("Authored Markdown content.")]
    public ContentSection? Content { get; set; }

    [Description("API selection rules.")]
    public ApiSection? Api { get; set; }

    [Description("Links to documentation of symbols that are not part of the configured packages. Default: System and Microsoft namespaces link to Microsoft Learn. An empty list disables external links.")]
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

        [Description("Links displayed on the right side of the header.")]
        public List<HeaderLinkSection>? HeaderLinks { get; set; }
    }

    internal sealed class HeaderLinkSection
    {
        [Description("Text displayed for the link.")]
        public string? Text { get; set; }

        [Description("Absolute http or https URL of the link.")]
        public string? Url { get; set; }

        [Description("Optional emoji or text glyph displayed before the link text.")]
        public string? Icon { get; set; }
    }

    internal sealed class PackageSection
    {
        [Description("NuGet package identifier. Omit when path is a folder to discover all packages in that folder.")]
        public string? Id { get; set; }

        [Description("Package version: an exact version (1.2.3), a NuGet version range ([1.0,2.0)) or a floating version (1.*, *, *-*). The highest matching version is used. Default: the latest stable version, or the latest version including prereleases when id is omitted for folder discovery.")]
        public string? Version { get; set; }

        [Description("Package source URL or name from NuGet.Config. Default: the sources from NuGet.Config.")]
        public string? Source { get; set; }

        [Description("Local .nupkg file, or folder containing .nupkg files, relative to the configuration file. In a folder, the highest version matching 'version' is used for each package identifier. Omit 'id' to discover all packages at the root of the folder. When set, no feed is queried.")]
        public string? Path { get; set; }

        [Description("Target framework folders to document, such as net8.0. Default: all.")]
        public List<string>? TargetFrameworks { get; set; }

        [Description("Assembly file names to document, such as Contoso.dll. Default: all managed assemblies.")]
        public List<string>? Assemblies { get; set; }

        [Description("Preprocessor symbols defined when compiling a source package, in addition to the symbols of the target framework, such as the symbols its MSBuild targets define in the consuming project. Default: none.")]
        public List<string>? DefineConstants { get; set; }
    }

    internal sealed class ContentSection
    {
        [Description("Directory containing the Markdown pages, relative to the configuration file.")]
        public string? Root { get; set; }

        [Description("Additional files or directories copied as-is, relative to the content root.")]
        public List<string>? Assets { get; set; }

        [Description("Glob patterns of Markdown files to ignore, relative to the content root.")]
        public List<string>? Exclude { get; set; }

        [Description("URL of the editor of a page, containing {path}: the path of the Markdown file relative to the content root (https://github.com/contoso/docs/edit/main/docs/{path}). Default: no edit link.")]
        public string? EditUrl { get; set; }
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
        [Description("Report unresolved or ambiguous cref links as warnings instead of errors. The link text is still rendered without a link. Default: false.")]
        public bool? AllowInvalidCref { get; set; }

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
