namespace Meziantou.DocumentationGenerator.Build;

/// <summary>A step of the build. Stages communicate only through <see cref="BuildContext"/>.</summary>
internal interface IBuildStage
{
    string Name { get; }

    Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken);
}
