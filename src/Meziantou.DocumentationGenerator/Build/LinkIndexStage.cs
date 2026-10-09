using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Computes every API route before rendering, so that forward references and links between packages resolve.</summary>
internal sealed class LinkIndexStage : IBuildStage
{
    public string Name => "Index routes and links";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        context.Links = new LinkService(context.Configuration, context.ApiCatalog, context.Diagnostics);
        return Task.CompletedTask;
    }
}
