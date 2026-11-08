using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>
/// Selects the packages of a <see cref="DependencyGraphRequest"/>: the documented packages that match a pattern, plus the
/// documented packages they depend on, directly or not, so that the graph is complete. Dependencies that the site does
/// not document are added only when the request asks for them.
/// </summary>
internal static class DependencyGraphBuilder
{
    /// <param name="getPackageUrl">Returns the page of a documented package, or <see langword="null"/> when it has none; the node then links to nuget.org.</param>
    public static DependencyGraphModel Build(DependencyGraphRequest request, PackageDependencyIndex index, Func<string, string?> getPackageUrl)
    {
        var unmatched = ImmutableArray.CreateBuilder<string>();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<PackageDependencyInfo>();
        foreach (var pattern in request.Patterns)
        {
            var matched = false;
            foreach (var package in index.Packages)
            {
                if (!IsMatch(pattern, package.Id))
                    continue;

                matched = true;
                if (selected.Add(package.Id))
                {
                    queue.Enqueue(package);
                }
            }

            if (!matched)
            {
                unmatched.Add(pattern);
            }
        }

        var external = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.TryDequeue(out var package))
        {
            foreach (var dependency in package.Dependencies)
            {
                if (index.TryGet(dependency.Id, out var documented))
                {
                    if (selected.Add(documented.Id))
                    {
                        queue.Enqueue(documented);
                    }
                }
                else if (request.External)
                {
                    external.Add(dependency.Id);
                }
            }
        }

        var nodes = ImmutableArray.CreateBuilder<DependencyGraphNode>();
        foreach (var package in index.Packages.Where(p => selected.Contains(p.Id)))
        {
            var url = getPackageUrl(package.Id) ?? LinkService.GetNuGetOrgPackageUrl(package.Id, package.Version);
            nodes.Add(new DependencyGraphNode(package.Id, package.Version, package.Description, url, IsExternal: false));
        }

        foreach (var id in external)
        {
            nodes.Add(new DependencyGraphNode(id, Version: null, Description: null, LinkService.GetNuGetOrgPackageUrl(id), IsExternal: true));
        }

        nodes.Sort((a, b) => CompareIds(a.Id, b.Id));
        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexById[nodes[i].Id] = i;
        }

        var edges = ImmutableArray.CreateBuilder<DependencyGraphEdge>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsExternal || !index.TryGet(nodes[i].Id, out var package))
                continue;

            foreach (var dependency in package.Dependencies)
            {
                if (indexById.TryGetValue(dependency.Id, out var target) && target != i)
                {
                    edges.Add(new DependencyGraphEdge(i, target, dependency.VersionRange, dependency.Frameworks));
                }
            }
        }

        return new DependencyGraphModel(nodes.ToImmutable(), edges.ToImmutable(), unmatched.ToImmutable());
    }

    private static int CompareIds(string a, string b)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a, b);
    }

    /// <summary>Case-insensitive match where <c>*</c> matches any sequence of characters.</summary>
    internal static bool IsMatch(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] != '*' && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
