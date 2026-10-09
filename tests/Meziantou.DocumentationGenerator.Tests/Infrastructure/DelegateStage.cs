using Meziantou.DocumentationGenerator.Build;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal sealed class DelegateStage(string name, Func<BuildContext, CancellationToken, Task> action) : IBuildStage
{
    public string Name { get; } = name;

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken) => action(context, cancellationToken);
}
