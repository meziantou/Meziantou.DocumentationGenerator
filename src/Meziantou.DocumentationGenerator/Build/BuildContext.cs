using Meziantou.DocumentationGenerator.Configuration;
using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Navigation;
using Meziantou.DocumentationGenerator.XmlDocs;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.DocumentationGenerator.Rendering;
using Meziantou.DocumentationGenerator.SourceLinks;

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

    /// <summary>Routes, URLs and cross-reference resolution, created by <see cref="LinkIndexStage"/>.</summary>
    public LinkService? Links { get; set; }

    /// <summary>Authored pages, created by <see cref="ContentStage"/>.</summary>
    public ContentModel Content { get; set; } = ContentModel.Empty;

    /// <summary>Files copied to the output, registered by the content and rendering stages.</summary>
    public SiteAssetRegistry? Assets { get; set; }

    /// <summary>XML documentation of every API asset, loaded by <see cref="XmlDocumentationStage"/>.</summary>
    public DocumentationProvider Documentation { get; set; } = DocumentationProvider.Empty;

    /// <summary>Links to source files, computed by <see cref="SourceLinkStage"/>.</summary>
    public SourceLinkProvider SourceLinks { get; set; } = SourceLinkProvider.Empty;

    /// <summary>Navigation trees, created by <see cref="NavigationStage"/>.</summary>
    public SiteNavigation Navigation { get; set; } = SiteNavigation.Empty;

    /// <summary>Staging directory created by <see cref="RenderStage"/>.</summary>
    public OutputStaging? Staging { get; set; }

    /// <summary>Collects generated files, anchors and links for validation.</summary>
    public SiteLinkValidator? LinkValidator { get; set; }

    /// <summary>Set by the publication stage once the output directory contains the new site.</summary>
    public bool IsPublished { get; set; }

    public int GeneratedFileCount { get; set; }

    private readonly List<Action> _cleanups = [];

    /// <summary>Registers an action executed when the build ends, whatever its outcome, such as deleting the staging directory.</summary>
    public void RegisterCleanup(Action cleanup) => _cleanups.Add(cleanup);

    public void RunCleanups()
    {
        for (var i = _cleanups.Count - 1; i >= 0; i--)
        {
            _cleanups[i]();
        }

        _cleanups.Clear();
    }
}
