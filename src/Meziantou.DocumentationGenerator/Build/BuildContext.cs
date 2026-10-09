using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;

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

    /// <summary>Set by the publication stage once the output directory contains the new site.</summary>
    public bool IsPublished { get; set; }

    public int GeneratedFileCount { get; set; }
}
