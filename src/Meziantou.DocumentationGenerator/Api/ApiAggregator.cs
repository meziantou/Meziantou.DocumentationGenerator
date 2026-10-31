using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework.PublicApiGenerator;
using NuGet.Frameworks;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>
/// Aggregates the builds of each assembly across target frameworks with <see cref="PublicApi.Aggregate"/>.
/// Only builds of the same assembly in the same package are grouped, so unrelated assemblies declaring the same names
/// stay distinct. The result does not depend on the order of the inputs.
/// </summary>
internal sealed class ApiAggregator(ApiSymbolPolicy policy, DiagnosticBag diagnostics)
{
    public ApiCatalog Aggregate(IEnumerable<ApiAssemblyModel> models, IReadOnlyList<DiscoveredPackage> packages)
    {
        var groups = ImmutableArray.CreateBuilder<ApiAssemblyGroup>();
        var byAssembly = models
            .GroupBy(m => (Package: m.Package.Id.ToLowerInvariant(), Assembly: m.Asset.AssemblyName.ToLowerInvariant()))
            .OrderBy(g => g.Key.Package, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Assembly, StringComparer.Ordinal);
        foreach (var group in byAssembly)
        {
            var assemblyGroup = CreateGroup([.. group]);
            if (assemblyGroup is not null)
            {
                groups.Add(assemblyGroup);
            }
        }

        var packageModels = ImmutableArray.CreateBuilder<ApiPackageModel>();
        foreach (var package in packages)
        {
            if (package.Package.Configuration.IsReference)
                continue;

            var packageAssemblies = groups.Where(g => string.Equals(g.Package.Id, package.Id, StringComparison.OrdinalIgnoreCase) && !g.IsReference).ToImmutableArray();
            var namespaces = packageAssemblies
                .SelectMany(a => a.GetAllTypes())
                .GroupBy(t => t.Namespace, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ApiNamespaceModel(g.Key, [.. g.OrderBy(t => t.MetadataName, StringComparer.Ordinal).ThenBy(t => t.Assembly.Scope, StringComparer.Ordinal)]))
                .ToImmutableArray();
            packageModels.Add(new ApiPackageModel { Package = package, Assemblies = packageAssemblies, Namespaces = namespaces });
        }

        return new ApiCatalog(groups.ToImmutable(), packageModels.ToImmutable());
    }

    private ApiAssemblyGroup? CreateGroup(List<ApiAssemblyModel> models)
    {
        var first = models[0];
        var location = new DiagnosticLocation { Package = first.Package.Package.Identity, Assembly = first.Asset.AssemblyName };
        var duplicates = models.GroupBy(m => m.TargetFramework, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidAggregation, $"Assembly '{first.Asset.AssemblyName}' has several assets for {string.Join(", ", duplicates)}; they cannot be aggregated", location);
            return null;
        }

        // Sort by framework precedence so that the result is independent of discovery order
        models = [.. models.OrderBy(m => m.Asset.Framework, NuGetFrameworkSorter.Instance).ThenBy(m => m.TargetFramework, StringComparer.Ordinal)];
        PublicApiAggregatedAssembly aggregate;
        try
        {
            aggregate = PublicApi.Aggregate([.. models.Select(m => m.Assembly)]);
        }
        catch (ArgumentException ex)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidAggregation, $"Cannot aggregate the builds of '{first.Asset.AssemblyName}': {ex.Message}", location);
            return null;
        }

        var targetFrameworks = models.Select(m => m.TargetFramework).ToImmutableArray();
        var group = new ApiAssemblyGroup
        {
            Package = first.Package,
            AssemblyName = first.Asset.AssemblyName,
            Models = [.. models],
            Aggregate = aggregate,
            TargetFrameworks = targetFrameworks,
        };

        group.Types = [.. aggregate.Types
            .Select(t => CreateType(group, t, declaringType: null))
            .Where(t => t is not null)
            .Select(t => t!)
            .OrderBy(t => t.MetadataName, StringComparer.Ordinal)];
        return group;
    }

    private ApiTypeModel? CreateType(ApiAssemblyGroup group, PublicApiAggregatedSymbol aggregate, ApiTypeModel? declaringType)
    {
        var variants = CreateVariants(group, aggregate);
        if (variants.IsEmpty)
            return null;

        var primary = (PublicApiType)variants[0].Symbol;
        var type = new ApiTypeModel
        {
            Assembly = group,
            Aggregate = aggregate,
            Availability = CreateAvailability(group, variants),
            Variants = variants,
            Namespace = declaringType?.Namespace ?? primary.Namespace ?? "",
            DisplayName = (declaringType is null ? "" : declaringType.DisplayName + ".") + GetDisplayName(primary),
            MetadataName = (declaringType is null ? "" : declaringType.MetadataName + ".") + primary.MetadataName,
            DeclaringType = declaringType,
        };

        // C# 14 extension members are shown as declared in their extension block instead of their implementation methods
        type.Members = [.. aggregate.Members
            .Where(m => !IsExtensionImplementation(m))
            .Concat(aggregate.ExtensionMembers)
            .Select(m => CreateMember(group, m, type))
            .Where(m => m is not null)
            .Select(m => m!)
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.DocumentationId, StringComparer.Ordinal)];
        type.NestedTypes = [.. aggregate.NestedTypes
            .Select(t => CreateType(group, t, type))
            .Where(t => t is not null)
            .Select(t => t!)
            .OrderBy(t => t.MetadataName, StringComparer.Ordinal)];
        return type;
    }

    private ApiMemberModel? CreateMember(ApiAssemblyGroup group, PublicApiAggregatedSymbol aggregate, ApiTypeModel declaringType)
    {
        var variants = CreateVariants(group, aggregate);
        if (variants.IsEmpty)
            return null;

        return new ApiMemberModel
        {
            Assembly = group,
            Aggregate = aggregate,
            Availability = CreateAvailability(group, variants),
            Variants = variants,
            DeclaringType = declaringType,
            Category = ApiMemberModel.GetCategory((PublicApiMember)variants[0].Symbol, declaringType.PrimaryType),
        };
    }

    private static bool IsExtensionImplementation(PublicApiAggregatedSymbol member) => member.Symbols.All(s => s is PublicApiMethod { ImplementedExtensionMember: not null });

    /// <summary>Variants restricted to the target frameworks where the symbol is documented, the most common declaration first.</summary>
    private ImmutableArray<ApiVariant> CreateVariants(ApiAssemblyGroup group, PublicApiAggregatedSymbol aggregate)
    {
        var result = new List<ApiVariant>();
        foreach (var variant in aggregate.Variants)
        {
            var targetFrameworks = new List<string>();
            var symbols = new List<PublicApiSymbol>();
            foreach (var targetFramework in group.TargetFrameworks)
            {
                if (!variant.TargetFrameworks.Contains(targetFramework, StringComparer.Ordinal))
                    continue;

                var symbol = aggregate.GetSymbol(targetFramework);
                if (symbol is not null && policy.IsDocumented(symbol))
                {
                    targetFrameworks.Add(targetFramework);
                    symbols.Add(symbol);
                }
            }

            if (targetFrameworks.Count > 0)
            {
                result.Add(new ApiVariant { TargetFrameworks = [.. targetFrameworks], Symbol = symbols[0], Symbols = [.. symbols] });
            }
        }

        return [.. result
            .OrderByDescending(v => v.TargetFrameworks.Length)
            .ThenBy(v => group.TargetFrameworks.IndexOf(v.TargetFrameworks[0]))];
    }

    private static ApiAvailability CreateAvailability(ApiAssemblyGroup group, ImmutableArray<ApiVariant> variants)
    {
        var targetFrameworks = group.TargetFrameworks.Where(tfm => variants.Any(v => v.TargetFrameworks.Contains(tfm, StringComparer.Ordinal))).ToImmutableArray();
        return new ApiAvailability(targetFrameworks, targetFrameworks.Length == group.TargetFrameworks.Length);
    }

    private static string GetDisplayName(PublicApiType type)
    {
        return type.GenericParameters.IsDefaultOrEmpty ? type.Name : type.Name + "<" + string.Join(", ", type.GenericParameters.Select(p => p.Name)) + ">";
    }
}
