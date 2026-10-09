using Meziantou.DocumentationGenerator.Configuration;
using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Packages;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>State of one build. A new instance is created for each build; there is no global mutable state.</summary>
internal sealed class BuildContext
{
    public BuildContext(SiteConfiguration configuration, BuildOptions options, DiagnosticBag diagnostics, IBuildReporter reporter)
    {
        Configuration = configuration;
        Options = options;
        Diagnostics = diagnostics;
        Reporter = reporter;
    }

    public SiteConfiguration Configuration { get; }
    public BuildOptions Options { get; }
    public DiagnosticBag Diagnostics { get; }
    public IBuildReporter Reporter { get; }

    /// <summary>Packages acquired by <see cref="AcquisitionStage"/>, in configuration order.</summary>
    public ImmutableArray<AcquiredPackage> Packages { get; set; } = [];

    /// <summary>Reference packages acquired by <see cref="AcquisitionStage"/>; their APIs are not published.</summary>
    public ImmutableArray<AcquiredPackage> References { get; set; } = [];

    public ImmutableArray<DiscoveredPackage> DiscoveredPackages { get; set; } = [];

    public ImmutableArray<DiscoveredPackage> DiscoveredReferences { get; set; } = [];

    /// <summary>One model per API asset of the packages and references, in package then asset order.</summary>
    public ImmutableArray<ApiAssemblyModel> ApiAssemblies { get; set; } = [];

    /// <summary>API models aggregated across target frameworks.</summary>
    public ApiCatalog ApiCatalog { get; set; } = ApiCatalog.Empty;

    /// <summary>Set by the publication stage once the output directory contains the new site.</summary>
    public bool IsPublished { get; set; }

    public int GeneratedFileCount { get; set; }
}
