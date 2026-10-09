using Meziantou.DocumentationGenerator.SourceLinks;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class SourceLinkStage : IBuildStage
{
    public string Name => "Resolve source links";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var options = context.Configuration.SourceLinks;
        if (!options.Enabled)
            return;

        using var loader = new DebugInfoLoader(options.SymbolServers, context.Configuration.CacheDirectory, context.Diagnostics);
        context.SourceLinks = await SourceLinkProvider.CreateAsync(context.ApiCatalog, loader, context.Diagnostics, cancellationToken);
    }
}
