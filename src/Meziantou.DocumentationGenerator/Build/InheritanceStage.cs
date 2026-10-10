using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class InheritanceStage : IBuildStage
{
    public string Name => "Resolve inherited documentation";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        new InheritanceResolver(context.ApiCatalog, context.Documentation, context.Diagnostics).ResolveAll(cancellationToken);
        return Task.CompletedTask;
    }
}
