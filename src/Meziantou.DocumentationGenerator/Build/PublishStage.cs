using System.Reflection;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Rendering;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>Publishes the validated staging directory. Earlier stages never modify the output directory.</summary>
internal sealed class PublishStage : IBuildStage
{
    public string Name => "Publish";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var staging = context.Staging ?? throw new InvalidOperationException("Nothing was rendered");
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = new OutputManifest
        {
            Generator = "Meziantou.DocumentationGenerator " + typeof(PublishStage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],
            Inputs = [.. context.Packages.Concat(context.References).Select(p => new ManifestInput(p.Id, p.Metadata.Version.ToNormalizedString(), p.Sha512, p.Configuration.IsReference)).OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase)],
        };

        try
        {
            OutputPublisher.Publish(staging, context.Configuration.OutputDirectory, manifest);
            context.IsPublished = true;
            context.GeneratedFileCount = manifest.Files.Count;
        }
        catch (Exception ex) when (ex is PublicationException or IOException or UnauthorizedAccessException)
        {
            context.Diagnostics.Report(DiagnosticDescriptors.PublicationFailed, ex.Message);
        }

        return Task.CompletedTask;
    }
}
