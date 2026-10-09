namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Validates every internal link and anchor of the staged site before publication.</summary>
internal sealed class ValidationStage : IBuildStage
{
    public string Name => "Validate links";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        context.LinkValidator?.Validate();
        return Task.CompletedTask;
    }
}
