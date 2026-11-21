using System.Globalization;
using System.Text;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.Framework.Diagrams;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>
/// Writes a dependency graph as a figure with an inline SVG drawn by Meziantou.Framework.Diagrams. Each package is a link
/// to its page. Colors come from the site style sheet, and <c>site.js</c> adds the selection, the filter, the pan and the
/// zoom (the graph is readable without it).
/// </summary>
internal static class DependencyGraphSvgWriter
{
    internal const string NodeClass = "dependency-graph-node";
    internal const string ExternalNodeClass = "dependency-graph-node-external";
    internal const string EdgeClass = "dependency-graph-edge";
    internal const string PartialEdgeClass = "dependency-graph-edge-partial";

    // The colors are replaced by the site style sheet, which follows the light and dark themes; the font size is the one
    // used to measure the labels, so the style sheet keeps it.
    private static readonly DiagramTheme Theme = DiagramTheme.Default with
    {
        Background = null,
        FontFamily = "system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        FontSize = 14,
    };

    private static readonly SvgRenderOptions RenderOptions = new()
    {
        IncludeNamespace = false,
        Precision = 1,
    };

    public static string Write(DependencyGraphModel graph, DependencyGraphDirection direction)
    {
        if (graph.Nodes.IsEmpty)
            return "<figure class=\"dependency-graph dependency-graph-empty\"><p>No package matches this dependency graph.</p></figure>";

        var html = new StringBuilder();
        var external = graph.Nodes.Count(n => n.IsExternal);
        var summary = graph.Nodes.Length == 1 ? "1 package" : graph.Nodes.Length.ToString(CultureInfo.InvariantCulture) + " packages";
        summary += graph.Edges.Length == 1 ? ", 1 dependency" : ", " + graph.Edges.Length.ToString(CultureInfo.InvariantCulture) + " dependencies";

        html.Append("<figure class=\"dependency-graph\" data-direction=\"").Append(direction is DependencyGraphDirection.TopDown ? "top-down" : "left-right").Append("\">");
        html.Append("<div class=\"dependency-graph-toolbar\" hidden>");
        html.Append("<input type=\"search\" class=\"dependency-graph-filter\" placeholder=\"Filter packages\" aria-label=\"Filter packages\" autocomplete=\"off\" spellcheck=\"false\">");
        html.Append("<button type=\"button\" class=\"dependency-graph-zoom-out\" aria-label=\"Zoom out\" title=\"Zoom out\">−</button>");
        html.Append("<button type=\"button\" class=\"dependency-graph-zoom-in\" aria-label=\"Zoom in\" title=\"Zoom in\">+</button>");
        html.Append("<button type=\"button\" class=\"dependency-graph-fit\" title=\"Fit the graph in the view\">Fit</button>");
        html.Append("<button type=\"button\" class=\"dependency-graph-reset\">Reset</button>");
        html.Append("</div>");
        html.Append("<div class=\"dependency-graph-viewport\" tabindex=\"0\" role=\"region\" aria-label=\"Dependency graph: ").Append(Encode(summary)).Append("\">");
        html.Append(WriteSvg(graph, direction, summary));
        html.Append("</div>");

        html.Append("<figcaption class=\"dependency-graph-legend\">");
        html.Append("<span>").Append(Encode(summary)).Append(". Arrows point from a package to its dependencies.</span>");
        if (external > 0)
        {
            html.Append(" <span><span class=\"dependency-graph-swatch dependency-graph-swatch-external\"></span>Not documented on this site.</span>");
        }

        if (graph.Edges.Any(e => !e.Frameworks.IsEmpty))
        {
            html.Append(" <span>A dotted arrow is a dependency of some target frameworks only, written on the arrow.</span>");
        }

        html.Append(" <span class=\"dependency-graph-hint\" hidden>Select a package to highlight <span class=\"dependency-graph-swatch dependency-graph-swatch-dependency\"></span>what it depends on and <span class=\"dependency-graph-swatch dependency-graph-swatch-dependent\"></span>what depends on it.</span>");
        html.Append("</figcaption>");
        html.Append("</figure>");
        return html.ToString();
    }

    /// <summary>
    /// Returns the SVG of the graph. Each node is a <c>g.dependency-graph-node</c> whose <c>data-id</c> is <c>n</c>
    /// followed by its index in <see cref="DependencyGraphModel.Nodes"/>, and each edge is a <c>g.dependency-graph-edge</c>
    /// with a class <c>dependency-graph-edge-{from}-{to}</c>, which <c>site.js</c> reads.
    /// </summary>
    internal static string WriteSvg(DependencyGraphModel graph, DependencyGraphDirection direction, string summary)
    {
        var flowchart = new FlowchartDiagram
        {
            AccessibleTitle = "Dependency graph: " + summary,
            Direction = direction is DependencyGraphDirection.TopDown ? DiagramDirection.TopToBottom : DiagramDirection.LeftToRight,
            Theme = Theme,

            // A package identifier has no space, so wrapping would break it in the middle of a word
            WrappingWidth = null,
        };

        var dependencies = new List<string>[graph.Nodes.Length];
        var dependents = new List<string>[graph.Nodes.Length];
        for (var i = 0; i < graph.Nodes.Length; i++)
        {
            dependencies[i] = [];
            dependents[i] = [];
        }

        foreach (var edge in graph.Edges)
        {
            dependencies[edge.From].Add(graph.Nodes[edge.To].Id);
            dependents[edge.To].Add(graph.Nodes[edge.From].Id);
        }

        var elements = new FlowchartNode[graph.Nodes.Length];
        for (var i = 0; i < graph.Nodes.Length; i++)
        {
            var node = graph.Nodes[i];
            var element = flowchart.AddNode(GetNodeId(i), node.Id, FlowchartNodeShape.RoundedRectangle);
            element.Link = node.Url;
            element.Tooltip = GetNodeTitle(node, dependencies[i], dependents[i]);
            element.CssClasses.Add(NodeClass);
            if (node.IsExternal)
            {
                element.CssClasses.Add(ExternalNodeClass);
            }

            elements[i] = element;
        }

        foreach (var edge in graph.Edges)
        {
            var label = edge.Frameworks.IsEmpty ? default : new DiagramText(string.Join(", ", edge.Frameworks));
            var element = flowchart.AddEdge(elements[edge.From], elements[edge.To], label);
            element.CssClasses.Add(EdgeClass);
            element.CssClasses.Add(EdgeClass + "-" + edge.From.ToString(CultureInfo.InvariantCulture) + "-" + edge.To.ToString(CultureInfo.InvariantCulture));
            if (!edge.Frameworks.IsEmpty)
            {
                element.LineStyle = FlowchartLineStyle.Dotted;
                element.CssClasses.Add(PartialEdgeClass);
            }
        }

        return PrepareForHtml(flowchart.ToSvg(RenderOptions));
    }

    internal static string GetNodeId(int index) => "n" + index.ToString(CultureInfo.InvariantCulture);

    // The SVG is a standalone image; inline, it is a group of links (role="img" would hide them from assistive
    // technologies) styled by the site
    private static string PrepareForHtml(string svg)
    {
        var end = svg.IndexOf('>', StringComparison.Ordinal);
        if (!svg.StartsWith("<svg ", StringComparison.Ordinal) || end < 0)
            throw new InvalidOperationException("The diagram is not an SVG element");

        var tag = svg[..end].Replace(" role=\"img\"", "", StringComparison.Ordinal);
        return "<svg class=\"dependency-graph-svg\" role=\"group\"" + tag["<svg".Length..] + svg[end..];
    }

    private static string GetNodeTitle(DependencyGraphNode node, List<string> dependencies, List<string> dependents)
    {
        var title = new StringBuilder(node.Id);
        if (node.Version is not null)
        {
            title.Append(' ').Append(node.Version);
        }

        if (node.IsExternal)
        {
            title.Append("\nNot documented on this site");
        }
        else if (node.Description is { Length: > 0 } description)
        {
            title.Append('\n').Append(description);
        }

        if (dependencies.Count > 0)
        {
            title.Append("\nDepends on ").AppendJoin(", ", dependencies);
        }

        if (dependents.Count > 0)
        {
            title.Append("\nUsed by ").AppendJoin(", ", dependents);
        }

        return title.ToString();
    }

    private static string Encode(string value) => HtmlEncoding.Encode(value);
}
