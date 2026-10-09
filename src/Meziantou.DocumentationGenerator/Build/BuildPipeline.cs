namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Creates the stages of a build, in execution order.</summary>
internal static class BuildPipeline
{
    public static IReadOnlyList<IBuildStage> CreateStages()
    {
        return
        [
            new AcquisitionStage(),
            new DiscoveryStage(),
            new ApiModelStage(),
        ];
    }
}
