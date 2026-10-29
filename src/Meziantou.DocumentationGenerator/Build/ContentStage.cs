using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class ContentStage : IBuildStage
{
    public string Name => "Render Markdown content";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var links = context.Links ?? throw new InvalidOperationException("The link index must be created before content is processed");
        var assets = new SiteAssetRegistry(links);
        context.Assets = assets;
        RegisterConfiguredAssets(context, assets);
        var processor = new ContentProcessor(context.Configuration, links, assets, context.Diagnostics);
        context.Content = processor.Process(cancellationToken);
        return Task.CompletedTask;
    }

    private static void RegisterConfiguredAssets(BuildContext context, SiteAssetRegistry assets)
    {
        if (context.Configuration.Content is not { } content)
            return;

        foreach (var asset in content.Assets)
        {
            var path = content.Root / asset;
            var files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Select(Meziantou.Framework.FullPath.FromPath)
                : [path];
            foreach (var file in files.Order())
            {
                var outputPath = file.MakePathRelativeTo(content.Root).Replace('\\', '/');
                assets.Register(new SiteAsset(outputPath, file), new DiagnosticLocation { FilePath = context.Configuration.GetDisplayPath(file) });
            }
        }
    }
}
