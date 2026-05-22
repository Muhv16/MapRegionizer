using MapRegionizer.Core.Domain;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;

namespace MapRegionizer.Core.Terrain;

internal static class RiverTopologyPlanarityResolver
{
    internal static void ResolveCrossingEdges(
        RiverTopologyGraph graph,
        MapMask mask,
        ElevationMap elevation,
        double[] hydro,
        double[] accumulation,
        int[] basinIds,
        int[] lakeIds)
    {
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            for (var y = 0; y < graph.Height - 1; y++)
            {
                for (var x = 0; x < graph.Width; x++)
                {
                    var eastX = WrapX(x + 1, graph.Width);
                    var a = y * graph.Width + x;
                    var b = y * graph.Width + eastX;
                    var c = (y + 1) * graph.Width + x;
                    var d = (y + 1) * graph.Width + eastX;
                    if (!TryGetEdgeBetween(graph, a, d, out var first) ||
                        !TryGetEdgeBetween(graph, b, c, out var second))
                    {
                        continue;
                    }

                    var firstStrength = EdgeStrength(first, accumulation);
                    var secondStrength = EdgeStrength(second, accumulation);
                    var strong = firstStrength >= secondStrength ? first : second;
                    var weak = firstStrength >= secondStrength ? second : first;
                    if (TryChooseConfluenceTarget(graph, weak, strong, elevation, hydro, accumulation, basinIds, lakeIds, out var target))
                    {
                        graph.SetDownstream(weak.From, target);
                    }
                    else
                    {
                        graph.RemoveCell(weak.From);
                    }

                    changed = true;
                }
            }

            if (!changed)
                break;
        }
    }

    private static bool TryGetEdgeBetween(RiverTopologyGraph graph, int first, int second, out RiverTopologyEdge edge)
    {
        if (graph.Contains(first) && graph.GetDownstream(first) == second)
        {
            edge = new RiverTopologyEdge(first, second);
            return true;
        }

        if (graph.Contains(second) && graph.GetDownstream(second) == first)
        {
            edge = new RiverTopologyEdge(second, first);
            return true;
        }

        edge = default;
        return false;
    }

    private static bool TryChooseConfluenceTarget(
        RiverTopologyGraph graph,
        RiverTopologyEdge weak,
        RiverTopologyEdge strong,
        ElevationMap elevation,
        double[] hydro,
        double[] accumulation,
        int[] basinIds,
        int[] lakeIds,
        out int target)
    {
        var from = graph.ToPoint(weak.From);
        var candidates = new[] { strong.To, strong.From }
            .Concat(Neighbors8(from, graph.Width, graph.Height)
                .Select(p => p.Y * graph.Width + p.X)
                .Where(i => graph.Contains(i) && IsOrthogonalNeighbor(weak.From, i, graph.Width)))
            .Distinct()
            .Where(i => i != weak.From && i != weak.To)
            .Where(i => graph.Contains(i) && lakeIds[i] <= 0)
            .Where(i => DirectionIndex(from, graph.ToPoint(i), graph.Width) >= 0)
            .Select(i =>
            {
                var cyclePenalty = graph.WouldCreateCycle(weak.From, i) ? 10000.0 : 0.0;
                return (Index: i, Cost: ConfluenceCost(graph, weak, strong, i, elevation, hydro, accumulation, basinIds) + cyclePenalty);
            })
            .OrderBy(c => c.Cost)
            .ToList();

        if (candidates.Count == 0)
        {
            target = -1;
            return false;
        }

        target = candidates[0].Index;
        return true;
    }

    private static double ConfluenceCost(
        RiverTopologyGraph graph,
        RiverTopologyEdge weak,
        RiverTopologyEdge strong,
        int target,
        ElevationMap elevation,
        double[] hydro,
        double[] accumulation,
        int[] basinIds)
    {
        var currentHeight = hydro[weak.From];
        var targetHeight = hydro[target];
        var uphillPenalty = Math.Max(0.0, targetHeight - currentHeight) * 3.2;
        var riverAttraction = Math.Min(48.0, Math.Sqrt(Math.Max(0.0, accumulation[target])) * 2.8);
        var newDirection = DirectionIndex(graph.ToPoint(weak.From), graph.ToPoint(target), graph.Width);
        var oldDirection = DirectionIndex(graph.ToPoint(weak.From), graph.ToPoint(weak.To), graph.Width);
        var turnDelta = oldDirection < 0 || newDirection < 0 ? 0 : Math.Abs(oldDirection - newDirection);
        turnDelta = Math.Min(turnDelta, Directions.Length - turnDelta);
        var turnPenalty = turnDelta * 2.4;
        var basinChangePenalty = basinIds[weak.From] != basinIds[target] ? 24.0 : 0.0;
        var strongDownstreamBonus = target == strong.To ? 9.0 : 0.0;
        var terrain = elevation.GetTerrainClass(target % graph.Width, target / graph.Width);
        var valleyBias = TerrainValleyBias(terrain) * 0.18;
        return targetHeight + uphillPenalty - riverAttraction + turnPenalty + basinChangePenalty - strongDownstreamBonus - valleyBias;
    }

    private static double EdgeStrength(RiverTopologyEdge edge, double[] accumulation) =>
        Math.Max(accumulation[edge.From], accumulation[edge.To]);

    private static bool IsOrthogonalNeighbor(int first, int second, int width)
    {
        var dx = Math.Abs(WrappedDeltaX(second % width - first % width, width));
        var dy = Math.Abs(second / width - first / width);
        return dx + dy == 1;
    }
}
