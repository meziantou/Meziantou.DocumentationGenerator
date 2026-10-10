using Meziantou.DocumentationGenerator.Navigation;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class NavigationStage : IBuildStage
{
    public string Name => "Build navigation";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var links = context.Links ?? throw new InvalidOperationException("The link index must be created before the navigation");
        context.Navigation = new NavigationBuilder(context.Configuration, context.Content, context.ApiCatalog, links, context.Diagnostics).Build();
        return Task.CompletedTask;
    }
}
