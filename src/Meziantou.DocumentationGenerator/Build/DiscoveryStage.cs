using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Packages;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class DiscoveryStage : IBuildStage
{
    public string Name => "Discover package contents";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var discoverer = new PackageDiscoverer(context.Diagnostics);
        context.DiscoveredPackages = Discover(context.Packages, discoverer, context.Diagnostics, cancellationToken);
        context.DiscoveredReferences = Discover(context.References, discoverer, context.Diagnostics, cancellationToken);
        ReportUnconfiguredDependencies(context);
        return Task.CompletedTask;
    }

    private static ImmutableArray<DiscoveredPackage> Discover(ImmutableArray<AcquiredPackage> packages, PackageDiscoverer discoverer, DiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var result = ImmutableArray.CreateBuilder<DiscoveredPackage>();
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (discoverer.Discover(package) is { } discovered)
                {
                    result.Add(discovered);
                }
            }
            catch (PackageArchiveException ex)
            {
                diagnostics.Report(DiagnosticDescriptors.PackageIntegrity, ex.Message, new DiagnosticLocation { Package = package.Configuration.Identity });
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Dependencies are never downloaded implicitly, so that the set of inputs stays explicit and reproducible.
    /// Inherited documentation from a dependency is only available when the dependency is configured in 'references'.
    /// </summary>
    private static void ReportUnconfiguredDependencies(BuildContext context)
    {
        var configured = new HashSet<string>(context.Configuration.Packages.Concat(context.Configuration.References).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var package in context.DiscoveredPackages)
        {
            var dependencies = package.Dependencies
                .SelectMany(group => group.Packages)
                .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                if (!configured.Contains(dependency.Key))
                {
                    var ranges = string.Join(", ", dependency.Select(d => d.VersionRange.ToNormalizedString()).Distinct(StringComparer.Ordinal));
                    context.Diagnostics.Report(DiagnosticDescriptors.DependencyNotConfigured, $"Dependency '{dependency.Key}' ({ranges}) is not configured in 'references'; documentation inherited from it is not available", new DiagnosticLocation { Package = package.Package.Configuration.Identity });
                }
            }
        }
    }
}
