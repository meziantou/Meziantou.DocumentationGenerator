namespace Meziantou.DocumentationGenerator.Diagnostics;

/// <summary>Registry of every diagnostic the generator can report. Codes are stable and documented in docs/diagnostics.md.</summary>
internal static class DiagnosticDescriptors
{
    // DG1xxx: configuration
    public static readonly DiagnosticDescriptor UnsupportedConfigurationVersion = new("DG1001", "Unsupported configuration version", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidConfigurationSyntax = new("DG1002", "Invalid configuration syntax or unknown property", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor NothingToGenerate = new("DG1003", "The configuration declares no package and no content", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidPackageReference = new("DG1004", "Invalid package identifier or version", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidTargetFramework = new("DG1005", "Invalid target framework filter", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidSiteUrl = new("DG1006", "Invalid site URL or base path", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InputNotFound = new("DG1007", "Configured input file or directory not found", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DuplicateIdentity = new("DG1008", "Duplicate identity", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DangerousOutputPath = new("DG1009", "The output directory overlaps inputs", DiagnosticSeverity.Error, IsSafetyCritical: true);
    public static readonly DiagnosticDescriptor RobotsUnderSubpath = new("DG1010", "robots.txt cannot be generated under a base path", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor CredentialsInConfiguration = new("DG1011", "Credentials must not be stored in the configuration", DiagnosticSeverity.Error, IsSafetyCritical: true);
    public static readonly DiagnosticDescriptor InvalidProductMapping = new("DG1012", "Invalid product definition", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidConfigurationValue = new("DG1013", "Invalid configuration value", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidFilterPattern = new("DG1014", "Invalid API filter pattern", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidDiagnosticOverride = new("DG1015", "Invalid diagnostic severity override", DiagnosticSeverity.Error);

    // DG2xxx: package acquisition and discovery
    public static readonly DiagnosticDescriptor PackageNotFound = new("DG2001", "Package not found", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor PackageSourceFailure = new("DG2002", "Package source or authentication failure", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor PackageIntegrity = new("DG2003", "Invalid or corrupt package", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor UnsafePackageEntry = new("DG2005", "The package contains unsafe entries", DiagnosticSeverity.Error, IsSafetyCritical: true);
    public static readonly DiagnosticDescriptor OfflineCacheMiss = new("DG2006", "Package not available in offline mode", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor PackageIdentityMismatch = new("DG2009", "The package identity does not match the configuration", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor FilterMatchesNothing = new("DG2004", "A target framework or assembly filter matches no asset", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor NoDocumentableAssembly = new("DG2007", "The package contains no compile-time managed assembly", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor InvalidManagedAssembly = new("DG2008", "Invalid managed assembly or unrecognized framework folder", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DependencyNotConfigured = new("DG2010", "A package dependency is not configured as a reference", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor UnsupportedMetadata = new("DG2012", "Unsupported or unreadable metadata", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor AssetSelected = new("DG2011", "Selected API asset", DiagnosticSeverity.Info);

    // DG3xxx: missing optional content
    public static readonly DiagnosticDescriptor MissingReadme = new("DG3001", "The package has no README", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor MissingXmlDocumentation = new("DG3002", "No XML documentation for an assembly", DiagnosticSeverity.Warning);

    // DG7xxx: build, rendering and publication
    public static readonly DiagnosticDescriptor StageFailed = new("DG7001", "A build stage failed unexpectedly", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor BuildCancelled = new("DG7002", "The build was cancelled", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidCommandLine = new("DG7003", "Invalid command-line value", DiagnosticSeverity.Error);

    public static IEnumerable<DiagnosticDescriptor> GetAll()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(obj: null)!)
            .OrderBy(descriptor => descriptor.Code, StringComparer.Ordinal);
    }

    public static DiagnosticDescriptor? Find(string code)
    {
        return GetAll().FirstOrDefault(descriptor => string.Equals(descriptor.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
