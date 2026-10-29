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
    public static readonly DiagnosticDescriptor InvalidAggregation = new("DG2013", "Assemblies cannot be aggregated across target frameworks", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor AssetSelected = new("DG2011", "Selected API asset", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor SourceCompilationFailed = new("DG2014", "The sources of a source package cannot be compiled", DiagnosticSeverity.Warning);

    // DG3xxx: missing optional content
    public static readonly DiagnosticDescriptor MissingXmlDocumentation = new("DG3002", "No XML documentation for an assembly", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor MissingDebugInformation = new("DG3003", "No usable PDB or Source Link information", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor MismatchedDebugInformation = new("DG3005", "The PDB does not match the assembly", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnsupportedSourceHost = new("DG3006", "Unsupported source host", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor AmbiguousImplementation = new("DG3007", "Ambiguous implementation for a reference assembly", DiagnosticSeverity.Info);

    // DG4xxx: links and routes
    public static readonly DiagnosticDescriptor UnresolvedReference = new("DG4001", "Unresolved reference", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor AmbiguousReference = new("DG4002", "Ambiguous reference", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor BrokenLink = new("DG4003", "Broken link in the generated site", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor RouteCollision = new("DG4004", "Several types map to the same page", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor PageCollision = new("DG4005", "Several files map to the same output path", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DuplicateAnchor = new("DG4006", "Duplicate anchor in a page", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor UnsafeOutputPath = new("DG4007", "Unsafe output path", DiagnosticSeverity.Error, IsSafetyCritical: true);

    public static readonly DiagnosticDescriptor UnsafeUrl = new("DG4008", "Unsafe URL removed", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor ExternalReferenceWithoutMapping = new("DG4009", "Reference to a symbol outside the configured packages without xrefs mapping", DiagnosticSeverity.Info);

    // DG5xxx: authored content and navigation
    public static readonly DiagnosticDescriptor ContentFileNotFound = new("DG5001", "Referenced Markdown page not found", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor InvalidFrontMatter = new("DG5002", "Invalid front matter", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor AssetNotFound = new("DG5008", "Referenced file not found", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor PathEscapesRoot = new("DG5009", "Path outside the allowed root", DiagnosticSeverity.Error, IsSafetyCritical: true);
    public static readonly DiagnosticDescriptor UnknownCodeLanguage = new("DG5010", "Unknown code language", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor InvalidFolderMetadata = new("DG5011", "Invalid or missing folder metadata", DiagnosticSeverity.Warning);

    // DG6xxx: XML documentation
    public static readonly DiagnosticDescriptor MalformedXmlDocumentation = new("DG6001", "Malformed XML documentation file", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnmatchedXmlDocumentation = new("DG6002", "XML documentation entries without public symbol", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor InheritDocUnresolved = new("DG6003", "Inherited documentation cannot be resolved", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor InheritDocCycle = new("DG6004", "Cycle in inherited documentation", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor IncludeUnresolved = new("DG6005", "Included documentation cannot be resolved", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnknownXmlTag = new("DG6006", "Unsupported XML documentation tag", DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor InvalidParameterReference = new("DG6007", "Invalid parameter reference", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DuplicateXmlDocumentation = new("DG6008", "Duplicate XML documentation section or entry", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor UnsupportedSelector = new("DG6009", "Unsupported documentation selector", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor SeveralInheritanceCandidates = new("DG6010", "Several interfaces provide inherited documentation", DiagnosticSeverity.Info);

    // DG7xxx: build, rendering and publication
    public static readonly DiagnosticDescriptor StageFailed = new("DG7001", "A build stage failed unexpectedly", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor BuildCancelled = new("DG7002", "The build was cancelled", DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor PublicationFailed = new("DG7004", "The site cannot be published", DiagnosticSeverity.Error);
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
