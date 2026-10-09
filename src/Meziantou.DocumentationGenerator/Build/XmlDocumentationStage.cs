using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class XmlDocumentationStage : IBuildStage
{
    public string Name => "Load XML documentation";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var provider = new DocumentationProvider(context.Diagnostics);
        provider.Load(context.ApiCatalog, cancellationToken);
        context.Documentation = provider;
        return Task.CompletedTask;
    }
}
