using System.Globalization;
using System.Text;
using Meziantou.DocumentationGenerator.Content;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>
/// Writes a dependency graph as a figure with an inline SVG. Each package is a link to its page. Colors come from the
/// site style sheet, and <c>site.js</c> adds the selection, the filter and the zoom (the graph is readable without it).
/// </summary>
internal static class DependencyGraphSvgWriter
{
    private const double NodeHeight = 32;
    private const double CharacterWidth = 7.2;
    private const double NodePadding = 14;
    private const double MinimumNodeWidth = 64;

    public static string Write(DependencyGraphModel graph, DependencyGraphDirection direction)
    {
        var html = new StringBuilder();
        var external = graph.Nodes.Count(n => n.IsExternal);
        var summary = graph.Nodes.Length == 1 ? "1 package" : graph.Nodes.Length.ToString(CultureInfo.InvariantCulture) + " packages";
        summary += graph.Edges.Length == 1 ? ", 1 dependency" : ", " + graph.Edges.Length.ToString(CultureInfo.InvariantCulture) + " dependencies";

        if (graph.Nodes.IsEmpty)
        {
            html.Append("<figure class=\"dependency-graph dependency-graph-empty\"><p>No package matches this dependency graph.</p></figure>");
            return html.ToString();
        }

        var sizes = graph.Nodes.Select(n => new LayoutSize(Math.Max(MinimumNodeWidth, Math.Ceiling((n.Id.Length * CharacterWidth) + (2 * NodePadding))), NodeHeight)).ToList();
        var layout = LayeredGraphLayout.Compute(sizes, [.. graph.Edges.Select(e => (e.From, e.To))], direction is DependencyGraphDirection.TopDown);

        var dependencies = new List<int>[graph.Nodes.Length];
        var dependents = new List<int>[graph.Nodes.Length];
        for (var i = 0; i < graph.Nodes.Length; i++)
        {
            dependencies[i] = [];
            dependents[i] = [];
        }

        foreach (var edge in graph.Edges)
        {
            dependencies[edge.From].Add(edge.To);
            dependents[edge.To].Add(edge.From);
        }

        html.Append("<figure class=\"dependency-graph\" data-direction=\"").Append(direction is DependencyGraphDirection.TopDown ? "top-down" : "left-right").Append("\">");
        html.Append("<div class=\"dependency-graph-toolbar\" hidden>");
        html.Append("<input type=\"search\" class=\"dependency-graph-filter\" placeholder=\"Filter packages\" aria-label=\"Filter packages\" autocomplete=\"off\" spellcheck=\"false\">");
        html.Append("<button type=\"button\" class=\"dependency-graph-zoom-out\" aria-label=\"Zoom out\" title=\"Zoom out\">−</button>");
        html.Append("<button type=\"button\" class=\"dependency-graph-zoom-in\" aria-label=\"Zoom in\" title=\"Zoom in\">+</button>");
        html.Append("<button type=\"button\" class=\"dependency-graph-reset\">Reset</button>");
        html.Append("</div>");
        html.Append("<div class=\"dependency-graph-viewport\" tabindex=\"0\" role=\"region\" aria-label=\"Dependency graph: ").Append(Encode(summary)).Append("\">");
        html.Append("<svg class=\"dependency-graph-svg\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
            .Append(Format(layout.Width)).Append(' ').Append(Format(layout.Height))
            .Append("\" width=\"").Append(Format(layout.Width)).Append("\" height=\"").Append(Format(layout.Height)).Append("\">");

        html.Append("<g class=\"dependency-graph-edges\">");
        for (var e = 0; e < graph.Edges.Length; e++)
        {
            var edge = graph.Edges[e];
            var route = layout.Edges[e];
            html.Append("<g class=\"dependency-graph-edge").Append(route.Reversed ? " dependency-graph-edge-cycle" : "")
                .Append("\" data-from=\"").Append(edge.From.ToString(CultureInfo.InvariantCulture))
                .Append("\" data-to=\"").Append(edge.To.ToString(CultureInfo.InvariantCulture)).Append("\">");
            html.Append("<title>").Append(Encode(GetEdgeTitle(graph, edge))).Append("</title>");
            html.Append("<path class=\"dependency-graph-line\" d=\"").Append(route.Path).Append("\"/>");
            html.Append("<path class=\"dependency-graph-arrow\" d=\"").Append(route.Arrow).Append("\"/>");
            html.Append("</g>");
        }

        html.Append("</g>");

        html.Append("<g class=\"dependency-graph-nodes\">");
        for (var i = 0; i < graph.Nodes.Length; i++)
        {
            var node = graph.Nodes[i];
            var rect = layout.Nodes[i];
            html.Append("<a class=\"dependency-graph-node").Append(node.IsExternal ? " dependency-graph-node-external" : "")
                .Append("\" href=\"").Append(Encode(node.Url))
                .Append("\" data-node=\"").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("\" data-name=\"").Append(Encode(node.Id))
                .Append("\" data-dependencies=\"").Append(string.Join(' ', dependencies[i]))
                .Append("\" data-dependents=\"").Append(string.Join(' ', dependents[i]))
                .Append("\" aria-label=\"").Append(Encode(GetNodeLabel(graph, i, dependencies[i], dependents[i]))).Append("\">");
            html.Append("<title>").Append(Encode(GetNodeTitle(node))).Append("</title>");
            html.Append("<rect x=\"").Append(Format(rect.X)).Append("\" y=\"").Append(Format(rect.Y))
                .Append("\" width=\"").Append(Format(rect.Width)).Append("\" height=\"").Append(Format(rect.Height)).Append("\" rx=\"6\"/>");
            html.Append("<text x=\"").Append(Format(rect.X + (rect.Width / 2))).Append("\" y=\"").Append(Format(rect.Y + (rect.Height / 2)))
                .Append("\" text-anchor=\"middle\" dominant-baseline=\"central\">").Append(Encode(node.Id)).Append("</text>");
            html.Append("</a>");
        }

        html.Append("</g>");
        html.Append("</svg></div>");

        html.Append("<figcaption class=\"dependency-graph-legend\">");
        html.Append("<span>").Append(Encode(summary)).Append(". Arrows point from a package to its dependencies.</span>");
        if (external > 0)
        {
            html.Append(" <span><span class=\"dependency-graph-swatch dependency-graph-swatch-external\"></span>Not documented on this site.</span>");
        }

        if (layout.IsolatedTop is not null)
        {
            html.Append(" <span>Packages below the graph have no dependencies in it.</span>");
        }

        html.Append(" <span class=\"dependency-graph-hint\" hidden>Select a package to highlight <span class=\"dependency-graph-swatch dependency-graph-swatch-dependency\"></span>what it depends on and <span class=\"dependency-graph-swatch dependency-graph-swatch-dependent\"></span>what depends on it.</span>");
        html.Append("</figcaption>");
        html.Append("</figure>");
        return html.ToString();
    }

    private static string GetNodeTitle(DependencyGraphNode node)
    {
        var title = node.Version is null ? node.Id : node.Id + " " + node.Version;
        if (node.IsExternal)
            return title + "\nNot documented on this site";

        return node.Description is { Length: > 0 } description ? title + "\n" + description : title;
    }

    private static string GetNodeLabel(DependencyGraphModel graph, int index, List<int> dependencies, List<int> dependents)
    {
        var label = new StringBuilder(graph.Nodes[index].Id);
        if (graph.Nodes[index].IsExternal)
        {
            label.Append(", not documented on this site");
        }

        if (dependencies.Count > 0)
        {
            label.Append(", depends on ").Append(string.Join(", ", dependencies.Select(d => graph.Nodes[d].Id)));
        }

        if (dependents.Count > 0)
        {
            label.Append(", used by ").Append(string.Join(", ", dependents.Select(d => graph.Nodes[d].Id)));
        }

        return label.ToString();
    }

    private static string GetEdgeTitle(DependencyGraphModel graph, DependencyGraphEdge edge)
    {
        var title = graph.Nodes[edge.From].Id + " → " + graph.Nodes[edge.To].Id + " " + edge.VersionRange;
        return edge.Frameworks.IsEmpty ? title : title + " (" + string.Join(", ", edge.Frameworks) + ")";
    }

    private static string Encode(string value) => HtmlEncoding.Encode(value);

    private static string Format(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
}
