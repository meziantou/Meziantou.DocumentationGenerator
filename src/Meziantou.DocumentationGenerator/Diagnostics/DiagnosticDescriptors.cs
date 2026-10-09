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
