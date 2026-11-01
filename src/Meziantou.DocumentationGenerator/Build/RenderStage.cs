using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
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

        var styleSheet = "";
        foreach (var (fileName, content) in StaticAssets.GetAll())
        {
            if (fileName is StaticAssets.StyleSheetFileName)
            {
                styleSheet = System.Text.Encoding.UTF8.GetString(content);
            }

            var path = LinkService.AssetsDirectory + "/" + fileName;
            if (links.TryRegister(new SiteRoute(path, PageKind.Asset, "static asset")))
            {
                staging.WriteBytes(path, content);
                validator.AddFile(path, [], []);
            }
        }

        CopyAssets(context, staging, validator, cancellationToken);
        var favicon = CopyBrandingAsset(context, links, staging, validator, context.Configuration.Site.Favicon, SiteMetadata.FaviconBaseName);
        var logo = CopyBrandingAsset(context, links, staging, validator, context.Configuration.Site.Logo, SiteMetadata.LogoBaseName);
        var socialImage = CopyBrandingAsset(context, links, staging, validator, context.Configuration.Site.SocialImage, SiteMetadata.SocialImageBaseName);

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
            Packages = context.DiscoveredPackages,
            FaviconUrl = favicon,
            LogoUrl = logo,
            SocialImageUrl = socialImage,
        };

        // Components use shared site services, so they are rendered sequentially to keep diagnostics deterministic. The
        // other steps of a page (link extraction, minification, writing) are independent and run on the other cores.
        await using var renderer = new RazorRenderer(services => services.AddSingleton(site));
        var minifier = new HtmlMinifier(styleSheet);
        var minifyHtml = context.Configuration.Site.MinifyHtml;
        var sourcePaths = context.Content.Pages.ToDictionary(p => p.Route.FilePath, p => p.DisplayPath, StringComparer.Ordinal);
        var pages = SitePageCatalog.GetPages(site).ToArray();
        var statistics = new RenderStatistics(pages) { MinifyHtml = minifyHtml };
        var workerCount = Environment.ProcessorCount;

        // Bound the number of HTML documents in memory
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(workerCount * 2) { SingleWriter = true });
        var htmlPages = new string?[pages.Length];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stageStarted = Stopwatch.GetTimestamp();
        var producer = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < pages.Length; i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var page = pages[i];
                    var started = Stopwatch.GetTimestamp();
                    var html = await renderer.RenderAsync(page.Component, page.Parameters);
                    htmlPages[i] = html.ReplaceLineEndings("\n");
                    statistics.Pages[i].Rendering = Stopwatch.GetElapsedTime(started);
                    await channel.Writer.WriteAsync(i, cancellation.Token);
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
                throw;
            }
        }, cancellation.Token);

        var consumer = Parallel.ForEachAsync(channel.Reader.ReadAllAsync(cancellation.Token), new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cancellation.Token }, (i, _) =>
        {
            var page = pages[i];
            var html = htmlPages[i]!;
            htmlPages[i] = null;
            ref var timing = ref statistics.Pages[i];

            // Links are extracted from the rendered markup: the minified markup may omit the quotes of attribute values
            var started = Stopwatch.GetTimestamp();
            validator.AddFile(page.FilePath, HtmlLinkExtractor.GetIds(html), HtmlLinkExtractor.GetLinks(html).Select(url => links.Urls.GetRelativeUrl(page.FilePath, url)), sourcePaths.GetValueOrDefault(page.FilePath));
            timing.LinkExtraction = Stopwatch.GetElapsedTime(started);

            started = Stopwatch.GetTimestamp();
            string RewriteUrl(string url) => links.Urls.GetRelativeUrl(page.FilePath, url);
            var minified = minifyHtml ? minifier.Minify(html, RewriteUrl) : HtmlMinifier.RewriteUrls(html, RewriteUrl);
            timing.Minification = Stopwatch.GetElapsedTime(started);

            started = Stopwatch.GetTimestamp();
            staging.WriteText(page.FilePath, minified);
            timing.Writing = Stopwatch.GetElapsedTime(started);
            timing.RenderedSize = html.Length;
            timing.MinifiedSize = minified.Length;
            return ValueTask.CompletedTask;
        });

        try
        {
            await consumer;
        }
        catch
        {
            // Stop rendering, and report the rendering failure rather than the closed channel
            await cancellation.CancelAsync();
            await producer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (producer.Exception?.InnerException is { } producerException && producerException is not OperationCanceledException)
                ExceptionDispatchInfo.Throw(producerException);

            throw;
        }

        await producer;
        statistics.Elapsed = Stopwatch.GetElapsedTime(stageStarted);
        statistics.WorkerCount = workerCount;
        statistics.Report(context.Reporter);

        // The search index lists URLs relative to the base path; they are validated like links
        var indexPath = LinkService.AssetsDirectory + "/" + Search.SearchIndexBuilder.FileName;
        if (links.TryRegister(new SiteRoute(indexPath, PageKind.Asset, "search index")))
        {
            var index = Search.SearchIndexBuilder.Build(site);
            staging.WriteText(indexPath, index);
            validator.AddFile(indexPath, [], Search.SearchIndexReader.GetUrls(index).Select(url => links.Urls.BasePath.Value + url));
        }

        WriteMetadataFile(links, staging, validator, SiteMetadata.SitemapFileName, SiteMetadata.BuildSitemap(context.Configuration, links.Urls, staging.Files.Where(f => f.EndsWith(".html", StringComparison.Ordinal))));
        WriteMetadataFile(links, staging, validator, SiteMetadata.RobotsFileName, SiteMetadata.BuildRobots(context.Configuration, links.Urls));
        context.GeneratedFileCount = staging.Files.Count;
    }

    private static string? CopyBrandingAsset(BuildContext context, LinkService links, OutputStaging staging, SiteLinkValidator validator, Meziantou.Framework.FullPath? source, string baseName)
    {
        if (source is not { } path)
            return null;

        var outputPath = LinkService.AssetsDirectory + "/" + baseName + path.Extension.ToLowerInvariant();
        if (!links.TryRegister(new SiteRoute(outputPath, PageKind.Asset, context.Configuration.GetDisplayPath(path))))
            return null;

        staging.CopyFile(outputPath, path);
        validator.AddFile(outputPath, [], []);
        return links.Urls.GetUrl(outputPath);
    }

    private static void WriteMetadataFile(LinkService links, OutputStaging staging, SiteLinkValidator validator, string fileName, string? content)
    {
        if (content is null || !links.TryRegister(new SiteRoute(fileName, PageKind.Asset, "site metadata")))
            return;

        staging.WriteText(fileName, content);
        validator.AddFile(fileName, [], []);
    }

    private static void CopyAssets(BuildContext context, OutputStaging staging, SiteLinkValidator validator, CancellationToken cancellationToken)
    {
        if (context.Assets is null)
            return;

        foreach (var asset in context.Assets.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            staging.CopyFile(asset.OutputPath, asset.LocalPath);
            validator.AddFile(asset.OutputPath, [], []);
        }
    }
}
