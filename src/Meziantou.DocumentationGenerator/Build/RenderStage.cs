using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.DocumentationGenerator.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Renders every page and copies every asset into a staging directory. The output directory is not modified.</summary>
internal sealed class RenderStage : IBuildStage
{
    public string Name => "Render pages";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var links = context.Links ?? throw new InvalidOperationException("The link index must be created before rendering");
        var staging = OutputStaging.Create(context.Configuration.OutputDirectory);
        context.Staging = staging;
        context.RegisterCleanup(staging.Dispose);

        var validator = new SiteLinkValidator(links.Urls, context.Diagnostics);
        context.LinkValidator = validator;

        foreach (var (fileName, content) in StaticAssets.GetAll())
        {
            var path = LinkService.AssetsDirectory + "/" + fileName;
            if (links.TryRegister(new SiteRoute(path, PageKind.Asset, "static asset")))
            {
                staging.WriteBytes(path, content);
                validator.AddFile(path, [], []);
            }
        }

        CopyAssets(context, staging, validator, cancellationToken);

        var site = new SiteRenderContext
        {
            Configuration = context.Configuration,
            Links = links,
            Navigation = context.Navigation,
            Content = context.Content,
            Catalog = context.ApiCatalog,
            Documentation = context.Documentation,
            SourceLinks = context.SourceLinks,
            Highlighting = new HighlightingService(context.Diagnostics),
            Diagnostics = context.Diagnostics,
        };

        // A single renderer is used sequentially: HtmlRenderer has dispatcher affinity, and sequential rendering keeps the output deterministic
        await using var renderer = new RazorRenderer(services => services.AddSingleton(site));
        foreach (var page in SitePageCatalog.GetPages(site))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await renderer.RenderAsync(page.Component, page.Parameters);
            html = html.ReplaceLineEndings("\n");
            staging.WriteText(page.FilePath, html);
            validator.AddFile(page.FilePath, HtmlLinkExtractor.GetIds(html), HtmlLinkExtractor.GetLinks(html));
        }

        context.GeneratedFileCount = staging.Files.Count;
    }

    private static void CopyAssets(BuildContext context, OutputStaging staging, SiteLinkValidator validator, CancellationToken cancellationToken)
    {
        if (context.Assets is null)
            return;

        foreach (var asset in context.Assets.Assets.Where(a => a.LocalPath is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            staging.CopyFile(asset.OutputPath, asset.LocalPath!.Value);
            validator.AddFile(asset.OutputPath, [], []);
        }

        foreach (var group in context.Assets.Assets.Where(a => a.PackagePath is not null).GroupBy(a => a.PackagePath!.Value))
        {
            try
            {
                using var archive = PackageArchive.Open(group.Key);
                foreach (var asset in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    staging.WriteBytes(asset.OutputPath, archive.ReadAllBytes(asset.PackageEntry!));
                    validator.AddFile(asset.OutputPath, [], []);
                }
            }
            catch (PackageArchiveException ex)
            {
                context.Diagnostics.Report(DiagnosticDescriptors.AssetNotFound, ex.Message);
            }
        }
    }
}
