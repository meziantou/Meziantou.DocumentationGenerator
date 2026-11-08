using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>
/// Layered (Sugiyama) layout of a directed graph: arrows go from one layer to the next, along the main axis (left to
/// right, or top to bottom). The steps are: cycle removal (arrows of a cycle are reversed and drawn the other way),
/// layer assignment (a node with no outgoing arrow is in the last layer), long arrows split by virtual nodes,
/// crossing reduction (barycenter sweeps) and coordinate assignment. Nodes without any arrow are laid out in a grid
/// below the graph. The result only depends on the input order, so the output is deterministic.
/// </summary>
internal static class LayeredGraphLayout
{
    private const double Margin = 4;
    private const double VirtualNodeSize = 8;
    private const double ArrowLength = 8;
    private const double ArrowHalfWidth = 4;
    private const int MaxOrderingSweeps = 8;
    private const int CoordinateIterations = 8;
    private const double IsolatedGap = 32;
    private const double IsolatedMinimumWidth = 640;

    public static LayoutResult Compute(IReadOnlyList<LayoutSize> sizes, IReadOnlyList<(int From, int To)> edges, bool topDown)
    {
        var nodeCount = sizes.Count;
        var layerGap = topDown ? 56d : 72d;
        var nodeGap = topDown ? 20d : 14d;

        var connected = new bool[nodeCount];
        foreach (var (from, to) in edges)
        {
            connected[from] = true;
            connected[to] = true;
        }

        // Primary axis: the direction of the arrows. Secondary axis: across the layers.
        double Primary(LayoutSize s) => topDown ? s.Height : s.Width;
        double Secondary(LayoutSize s) => topDown ? s.Width : s.Height;

        // 1. Cycle removal: edges that close a cycle in a depth-first search are reversed
        var outgoing = new List<int>[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            outgoing[i] = [];
        }

        for (var e = 0; e < edges.Count; e++)
        {
            outgoing[edges[e].From].Add(e);
        }

        var reversed = new bool[edges.Count];
        var state = new byte[nodeCount]; // 0: not visited, 1: on the stack, 2: done
        for (var root = 0; root < nodeCount; root++)
        {
            if (state[root] != 0)
                continue;

            var stack = new Stack<(int Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next < outgoing[node].Count)
                {
                    stack.Push((node, next + 1));
                    var edge = outgoing[node][next];
                    var target = edges[edge].To;
                    if (state[target] == 1)
                    {
                        reversed[edge] = true;
                    }
                    else if (state[target] == 0)
                    {
                        state[target] = 1;
                        stack.Push((target, 0));
                    }
                }
                else
                {
                    state[node] = 2;
                }
            }
        }

        // Edges of the acyclic graph (same direction as the layers)
        var dagEdges = new (int From, int To)[edges.Count];
        var successors = new List<int>[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            successors[i] = [];
        }

        for (var e = 0; e < edges.Count; e++)
        {
            dagEdges[e] = reversed[e] ? (edges[e].To, edges[e].From) : edges[e];
            successors[dagEdges[e].From].Add(dagEdges[e].To);
        }

        // 2. Layers: the height of a node is its longest path to a node without successors; those are in the last layer
        var height = new int[nodeCount];
        Array.Fill(height, -1);
        var order = new List<int>(nodeCount);
        for (var root = 0; root < nodeCount; root++)
        {
            if (height[root] >= 0 || !connected[root])
                continue;

            var stack = new Stack<(int Node, bool Expanded)>();
            stack.Push((root, false));
            while (stack.Count > 0)
            {
                var (node, expanded) = stack.Pop();
                if (height[node] >= 0)
                    continue;

                if (expanded)
                {
                    var h = 0;
                    foreach (var successor in successors[node])
                    {
                        h = Math.Max(h, height[successor] + 1);
                    }

                    height[node] = h;
                    order.Add(node);
                    continue;
                }

                stack.Push((node, true));
                for (var i = successors[node].Count - 1; i >= 0; i--)
                {
                    if (height[successors[node][i]] < 0)
                    {
                        stack.Push((successors[node][i], false));
                    }
                }
            }
        }

        var layerCount = order.Count == 0 ? 0 : order.Max(n => height[n]) + 1;
        var layerOf = new List<int>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            layerOf.Add(connected[i] ? layerCount - 1 - height[i] : -1);
        }

        // 3. Virtual nodes split the edges that span several layers
        var virtualSizes = new List<LayoutSize>(sizes);
        var chains = new List<int>[edges.Count];
        for (var e = 0; e < edges.Count; e++)
        {
            var (from, to) = dagEdges[e];
            var chain = new List<int> { from };
            for (var layer = layerOf[from] + 1; layer < layerOf[to]; layer++)
            {
                chain.Add(virtualSizes.Count);
                layerOf.Add(layer);
                virtualSizes.Add(new LayoutSize(VirtualNodeSize, VirtualNodeSize));
            }

            chain.Add(to);
            chains[e] = chain;
        }

        var totalCount = virtualSizes.Count;
        var upper = new List<int>[totalCount];
        var lower = new List<int>[totalCount];
        for (var i = 0; i < totalCount; i++)
        {
            upper[i] = [];
            lower[i] = [];
        }

        foreach (var chain in chains)
        {
            for (var i = 0; i + 1 < chain.Count; i++)
            {
                lower[chain[i]].Add(chain[i + 1]);
                upper[chain[i + 1]].Add(chain[i]);
            }
        }

        // 4. Crossing reduction
        var layers = new List<int>[layerCount];
        for (var l = 0; l < layerCount; l++)
        {
            layers[l] = [];
        }

        // Initial order: depth-first from the first layer, so that related nodes start close to each other
        var placed = new bool[totalCount];
        void Place(int start)
        {
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (placed[node])
                    continue;

                placed[node] = true;
                layers[layerOf[node]].Add(node);
                for (var i = lower[node].Count - 1; i >= 0; i--)
                {
                    stack.Push(lower[node][i]);
                }
            }
        }

        for (var i = 0; i < nodeCount; i++)
        {
            if (connected[i] && upper[i].Count == 0)
            {
                Place(i);
            }
        }

        for (var i = 0; i < totalCount; i++)
        {
            if (!placed[i] && layerOf[i] >= 0)
            {
                Place(i);
            }
        }

        var position = new int[totalCount];
        void UpdatePositions(List<int>[] current)
        {
            foreach (var layer in current)
            {
                for (var i = 0; i < layer.Count; i++)
                {
                    position[layer[i]] = i;
                }
            }
        }

        UpdatePositions(layers);
        var best = layers.Select(l => l.ToList()).ToArray();
        var bestCrossings = CountCrossings(layers, lower, position);
        for (var sweep = 0; sweep < MaxOrderingSweeps && bestCrossings > 0; sweep++)
        {
            var down = sweep % 2 == 0;
            for (var step = 1; step < layerCount; step++)
            {
                var l = down ? step : layerCount - 1 - step;
                var neighbors = down ? upper : lower;
                var layer = layers[l];
                var barycenters = new Dictionary<int, double>(layer.Count);
                foreach (var node in layer)
                {
                    barycenters[node] = neighbors[node].Count == 0 ? position[node] : neighbors[node].Average(n => (double)position[n]);
                }

                // List.Sort is not stable: ties keep the current order through the position
                layer.Sort((a, b) =>
                {
                    var result = barycenters[a].CompareTo(barycenters[b]);
                    return result != 0 ? result : position[a].CompareTo(position[b]);
                });
                for (var i = 0; i < layer.Count; i++)
                {
                    position[layer[i]] = i;
                }
            }

            var crossings = CountCrossings(layers, lower, position);
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                best = layers.Select(l => l.ToList()).ToArray();
            }
        }

        layers = best;
        UpdatePositions(layers);

        // 5. Coordinates. Main axis: each layer is as deep as its largest node.
        var primaryCenter = new double[totalCount];
        var layerStart = 0d;
        foreach (var layer in layers)
        {
            var depth = layer.Count == 0 ? 0 : layer.Max(n => Primary(virtualSizes[n]));
            foreach (var node in layer)
            {
                primaryCenter[node] = layerStart + (depth / 2);
            }

            layerStart += depth + layerGap;
        }

        // Cross axis: nodes move toward the mean position of their neighbors, keeping their order and spacing
        var secondaryCenter = new double[totalCount];
        foreach (var layer in layers)
        {
            var offset = 0d;
            foreach (var node in layer)
            {
                var size = Secondary(virtualSizes[node]);
                secondaryCenter[node] = offset + (size / 2);
                offset += size + nodeGap;
            }
        }

        double Separation(int a, int b) => ((Secondary(virtualSizes[a]) + Secondary(virtualSizes[b])) / 2) + nodeGap;
        for (var iteration = 0; iteration < CoordinateIterations; iteration++)
        {
            var down = iteration % 2 == 0;
            for (var step = 0; step < layerCount; step++)
            {
                var layer = layers[down ? step : layerCount - 1 - step];
                if (layer.Count == 0)
                    continue;

                var desired = new double[layer.Count];
                for (var i = 0; i < layer.Count; i++)
                {
                    var node = layer[i];
                    var neighbors = upper[node].Concat(lower[node]).ToList();
                    desired[i] = neighbors.Count == 0 ? secondaryCenter[node] : neighbors.Average(n => secondaryCenter[n]);
                }

                // Pushing down from the top and up from the bottom both keep the spacing; so does their mean
                var forward = new double[layer.Count];
                var backward = new double[layer.Count];
                for (var i = 0; i < layer.Count; i++)
                {
                    forward[i] = i == 0 ? desired[i] : Math.Max(desired[i], forward[i - 1] + Separation(layer[i - 1], layer[i]));
                }

                for (var i = layer.Count - 1; i >= 0; i--)
                {
                    backward[i] = i == layer.Count - 1 ? desired[i] : Math.Min(desired[i], backward[i + 1] - Separation(layer[i], layer[i + 1]));
                }

                for (var i = 0; i < layer.Count; i++)
                {
                    secondaryCenter[layer[i]] = (forward[i] + backward[i]) / 2;
                }
            }
        }

        // Normalize: the graph starts at the margin
        var minSecondary = double.MaxValue;
        var maxSecondary = 0d;
        var maxPrimary = 0d;
        for (var i = 0; i < totalCount; i++)
        {
            if (layerOf[i] < 0)
                continue;

            minSecondary = Math.Min(minSecondary, secondaryCenter[i] - (Secondary(virtualSizes[i]) / 2));
        }

        if (minSecondary == double.MaxValue)
        {
            minSecondary = 0;
        }

        var nodes = new LayoutRect[nodeCount];
        for (var i = 0; i < totalCount; i++)
        {
            if (layerOf[i] < 0)
                continue;

            secondaryCenter[i] = secondaryCenter[i] - minSecondary + Margin;
            primaryCenter[i] += Margin;
            maxSecondary = Math.Max(maxSecondary, secondaryCenter[i] + (Secondary(virtualSizes[i]) / 2));
            maxPrimary = Math.Max(maxPrimary, primaryCenter[i] + (Primary(virtualSizes[i]) / 2));
            if (i < nodeCount)
            {
                var size = sizes[i];
                var (cx, cy) = topDown ? (secondaryCenter[i], primaryCenter[i]) : (primaryCenter[i], secondaryCenter[i]);
                nodes[i] = new LayoutRect(cx - (size.Width / 2), cy - (size.Height / 2), size.Width, size.Height);
            }
        }

        var width = topDown ? maxSecondary : maxPrimary;
        var graphHeight = topDown ? maxPrimary : maxSecondary;

        // 6. Edge routes: through the virtual nodes, from the side of the source facing the target
        var routes = ImmutableArray.CreateBuilder<LayoutEdge>(edges.Count);
        for (var e = 0; e < edges.Count; e++)
        {
            var chain = chains[e];
            var points = new List<(double Primary, double Secondary)>(chain.Count);
            for (var i = 0; i < chain.Count; i++)
            {
                var node = chain[i];
                var primary = primaryCenter[node];
                if (i == 0)
                {
                    primary += Primary(virtualSizes[node]) / 2;
                }
                else if (i == chain.Count - 1)
                {
                    primary -= Primary(virtualSizes[node]) / 2;
                }

                points.Add((primary, secondaryCenter[node]));
            }

            if (reversed[e])
            {
                points.Reverse();
            }

            routes.Add(CreateRoute(points, topDown, reversed[e]));
        }

        // Nodes without arrows: a grid below the graph
        var isolated = Enumerable.Range(0, nodeCount).Where(i => !connected[i]).ToList();
        double? isolatedTop = null;
        if (isolated.Count > 0)
        {
            var top = order.Count > 0 ? graphHeight + IsolatedGap : Margin;
            isolatedTop = order.Count > 0 ? top : null;
            var rowWidth = Math.Max(width, IsolatedMinimumWidth);
            var x = Margin;
            var y = top;
            var rowHeight = 0d;
            foreach (var node in isolated)
            {
                var size = sizes[node];
                if (x > Margin && x + size.Width > rowWidth)
                {
                    x = Margin;
                    y += rowHeight + nodeGap;
                    rowHeight = 0;
                }

                nodes[node] = new LayoutRect(x, y, size.Width, size.Height);
                x += size.Width + 12;
                rowHeight = Math.Max(rowHeight, size.Height);
                width = Math.Max(width, x - 12);
            }

            graphHeight = y + rowHeight;
        }

        return new LayoutResult(width + Margin, graphHeight + Margin, [.. nodes], routes.MoveToImmutable(), isolatedTop);
    }

    private static LayoutEdge CreateRoute(List<(double Primary, double Secondary)> points, bool topDown, bool reversed)
    {
        // The line stops at the base of the arrowhead
        var (endPrimary, endSecondary) = points[^1];
        var direction = Math.Sign(endPrimary - points[^2].Primary);
        if (direction == 0)
        {
            direction = 1;
        }

        points[^1] = (endPrimary - (direction * ArrowLength), endSecondary);

        string Point(double primary, double secondary) => topDown ? Format(secondary) + "," + Format(primary) : Format(primary) + "," + Format(secondary);

        var path = new System.Text.StringBuilder();
        path.Append('M').Append(Point(points[0].Primary, points[0].Secondary));
        for (var i = 1; i < points.Count; i++)
        {
            var (p0, s0) = points[i - 1];
            var (p1, s1) = points[i];
            var middle = (p0 + p1) / 2;
            path.Append(" C").Append(Point(middle, s0)).Append(' ').Append(Point(middle, s1)).Append(' ').Append(Point(p1, s1));
        }

        var baseLine = endPrimary - (direction * ArrowLength);
        var arrow = "M" + Point(endPrimary, endSecondary)
            + " L" + Point(baseLine, endSecondary - ArrowHalfWidth)
            + " L" + Point(baseLine, endSecondary + ArrowHalfWidth) + " Z";
        return new LayoutEdge(path.ToString(), arrow, reversed);
    }

    private static string Format(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Number of crossing segments between consecutive layers.</summary>
    internal static int CountCrossings(List<int>[] layers, List<int>[] lower, int[] position)
    {
        var crossings = 0;
        var segments = new List<(int Top, int Bottom)>();
        foreach (var layer in layers)
        {
            segments.Clear();
            foreach (var node in layer)
            {
                foreach (var next in lower[node])
                {
                    segments.Add((position[node], position[next]));
                }
            }

            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = i + 1; j < segments.Count; j++)
                {
                    if ((segments[i].Top < segments[j].Top && segments[i].Bottom > segments[j].Bottom) || (segments[i].Top > segments[j].Top && segments[i].Bottom < segments[j].Bottom))
                    {
                        crossings++;
                    }
                }
            }
        }

        return crossings;
    }
}
