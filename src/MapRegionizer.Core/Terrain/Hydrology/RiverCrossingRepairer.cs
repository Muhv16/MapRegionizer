using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverCrossingRepairer
{
    internal static bool ResolveCrossingRiverEdges(
        MapMask mask,
        ElevationMap elevation,
        double[] hydro,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        byte[] riverCells,
        int[] lakeIds)
    {
        var changed = false;
        var width = mask.Width;
        var height = mask.Height;
        for (var pass = 0; pass < 4; pass++)
        {
            var passChanged = false;
            for (var y = 0; y < height - 1; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var eastX = WrapX(x + 1, width);
                    var a = y * width + x;
                    var b = y * width + eastX;
                    var c = (y + 1) * width + x;
                    var d = (y + 1) * width + eastX;
                    if (!TryGetVisibleEdgeBetween(a, d, flowDirections, riverCells, width, height, out var first) ||
                        !TryGetVisibleEdgeBetween(b, c, flowDirections, riverCells, width, height, out var second))
                    {
                        continue;
                    }

                    var strong = accumulation[first.From] >= accumulation[second.From] ? first : second;
                    var weak = accumulation[first.From] >= accumulation[second.From] ? second : first;
                    if (!TryChooseCrossingConfluenceTarget(weak, strong, mask, elevation, hydro, flowDirections, accumulation, basinIds, riverCells, lakeIds, out var target))
                        continue;

                    var direction = DirectionIndex(new GridPoint(weak.From % width, weak.From / width), new GridPoint(target % width, target / width), width);
                    if (direction < 0 || direction == flowDirections[weak.From])
                        continue;

                    flowDirections[weak.From] = direction;
                    passChanged = true;
                    changed = true;
                }
            }

            if (!passChanged)
                break;
        }

        return changed;
    }

    internal static bool TryGetVisibleEdgeBetween(int first, int second, int[] flowDirections, byte[] riverCells, int width, int height, out RiverEdge edge)
    {
        if (riverCells[first] != 0 && riverCells[second] != 0 && DownstreamIndex(first, flowDirections[first], width, height) == second)
        {
            edge = new RiverEdge(first, second);
            return true;
        }

        if (riverCells[first] != 0 && riverCells[second] != 0 && DownstreamIndex(second, flowDirections[second], width, height) == first)
        {
            edge = new RiverEdge(second, first);
            return true;
        }

        edge = default;
        return false;
    }

    internal static bool TryChooseCrossingConfluenceTarget(
        RiverEdge weak,
        RiverEdge strong,
        MapMask mask,
        ElevationMap elevation,
        double[] hydro,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        byte[] riverCells,
        int[] lakeIds,
        out int target)
    {
        var width = mask.Width;
        var height = mask.Height;
        var fromPoint = new GridPoint(weak.From % width, weak.From / width);
        var candidates = new[] { strong.To, strong.From }
            .Concat(Neighbors8(fromPoint, width, height)
                .Select(p => p.Y * width + p.X)
                .Where(i => riverCells[i] != 0 && IsOrthogonalNeighbor(weak.From, i, width)))
            .Distinct()
            .Where(i => i != weak.From && i != weak.To)
            .Where(i => riverCells[i] != 0 && lakeIds[i] <= 0)
            .Where(i => DirectionIndex(fromPoint, new GridPoint(i % width, i / width), width) >= 0)
            .Select(i =>
            {
                var cyclePenalty = WouldCreateCycle(weak.From, i, flowDirections, width, height) ? 10000.0 : 0.0;
                return (Index: i, Cost: CrossingConfluenceCost(weak, strong, i, elevation, hydro, accumulation, basinIds, flowDirections, width) + cyclePenalty);
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

    internal static double CrossingConfluenceCost(
        RiverEdge weak,
        RiverEdge strong,
        int target,
        ElevationMap elevation,
        double[] hydro,
        double[] accumulation,
        int[] basinIds,
        int[] flowDirections,
        int width)
    {
        var currentHeight = hydro[weak.From];
        var targetHeight = hydro[target];
        var uphillPenalty = Math.Max(0.0, targetHeight - currentHeight) * 3.2;
        var riverAttraction = Math.Min(48.0, Math.Sqrt(Math.Max(0.0, accumulation[target])) * 2.8);
        var newDirection = DirectionIndex(new GridPoint(weak.From % width, weak.From / width), new GridPoint(target % width, target / width), width);
        var oldDirection = flowDirections[weak.From];
        var turnDelta = oldDirection < 0 || newDirection < 0 ? 0 : Math.Abs(oldDirection - newDirection);
        turnDelta = Math.Min(turnDelta, Directions.Length - turnDelta);
        var turnPenalty = turnDelta * 2.4;
        var basinChangePenalty = basinIds[weak.From] != basinIds[target] ? 24.0 : 0.0;
        var strongDownstreamBonus = target == strong.To ? 9.0 : 0.0;
        var terrain = elevation.GetTerrainClass(target % width, target / width);
        var valleyBias = TerrainValleyBias(terrain) * 0.18;
        return targetHeight + uphillPenalty - riverAttraction + turnPenalty + basinChangePenalty - strongDownstreamBonus - valleyBias;
    }

    internal readonly record struct RiverEdge(int From, int To);
}
