using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class ForcedLongRiverPlanner
{
    internal static void EnsureInlandSeaInflowRiverCells(
        MapMask mask,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        HydrologyGenerationOptions options,
        List<int>[]? upstreamCache = null,
        int[]? upstreamDepthsCache = null,
        IGridTopology? gridTopology = null)
    {
        if (options.RiverDensity <= 0)
            return;

        var width = mask.Width;
        var height = mask.Height;
        gridTopology ??= new CylindricalXTopology(width, height);
        var upstream = upstreamCache ?? BuildUpstreamLists(flowDirections, width, height, gridTopology);
        var upstreamDepths = upstreamDepthsCache ?? BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height, gridTopology);

        foreach (var body in waterSurfaces.Bodies
                     .Where(b => b.Kind == WaterBodyKind.InlandSea)
                     .OrderByDescending(b => b.CellCount))
        {
            var lakeId = body.Id.Value;
            if (HasVisibleLakeInflow(lakeId, riverCells, flowDirections, upstream, lakeIds, width, height, minimumLength: 8, gridTopology))
                continue;

            var bestPath = Enumerable.Range(0, flowDirections.Length)
                .Where(i => IsLakeInflowMouthCandidate(i, lakeId, flowDirections, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height, gridTopology))
                .Select(i => BuildLongestUpstreamPath(i, upstream, upstreamDepths, accumulation, lakeIds, mask, topology, width, gridTopology))
                .Where(path => path.Count >= 8)
                .OrderByDescending(path => path.Count)
                .ThenByDescending(path => accumulation[path[0]])
                .FirstOrDefault();

            if (bestPath is null)
                continue;

            foreach (var index in bestPath)
                riverCells[index] = 1;
        }
    }

    internal static bool HasVisibleLakeInflow(
        int lakeId,
        byte[] riverCells,
        int[] flowDirections,
        List<int>[] upstream,
        int[] lakeIds,
        int width,
        int height,
        int minimumLength,
        IGridTopology? gridTopology = null)
    {
        gridTopology ??= new CylindricalXTopology(width, height);
        for (var index = 0; index < riverCells.Length; index++)
        {
            if (riverCells[index] == 0)
                continue;

            var downstream = DownstreamIndex(index, flowDirections[index], gridTopology);
            if (downstream < 0 || lakeIds[downstream] != lakeId)
                continue;

            var stack = new Stack<(int Index, int Length)>();
            var seen = new HashSet<int>();
            stack.Push((index, 1));
            while (stack.Count > 0)
            {
                var (current, length) = stack.Pop();
                if (!seen.Add(current) || riverCells[current] == 0)
                    continue;
                if (length >= minimumLength)
                    return true;

                foreach (var nextUpstream in upstream[current].Where(i => riverCells[i] != 0))
                    stack.Push((nextUpstream, length + 1));
            }
        }

        return false;
    }

    internal static bool IsLakeInflowMouthCandidate(
        int index,
        int lakeId,
        int[] flowDirections,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height,
        IGridTopology? gridTopology = null)
    {
        gridTopology ??= new CylindricalXTopology(width, height);
        var point = new GridPoint(index % width, index / width);
        if (!IsRenderableRiverLand(point, mask, topology, lakeIds) || !allowedRiverBasins.Contains(basinIds[index]))
            return false;

        var downstream = DownstreamIndex(index, flowDirections[index], gridTopology);
        return downstream >= 0 && lakeIds[downstream] == lakeId;
    }

    internal static void MarkForcedLongRiverCells(IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths, byte[] riverCells, int[] lakeIds)
    {
        foreach (var path in forcedLongPaths.Values)
        {
            foreach (var index in path)
            {
                if (index >= 0 && index < riverCells.Length && lakeIds[index] <= 0)
                    riverCells[index] = 1;
            }
        }
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildForcedLongRiverPaths(
        MapMask mask,
        WaterBodyTopology topology,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        IReadOnlyList<DrainageBasin> basins,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        HydrologyGenerationOptions options,
        List<int>[]? upstreamCache = null,
        int[]? upstreamDepthsCache = null,
        IGridTopology? gridTopology = null)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        if (options.LongRiverCountMultiplier <= 0 || options.RiverDensity <= 0)
            return result;

        var width = mask.Width;
        var height = mask.Height;
        gridTopology ??= new CylindricalXTopology(width, height);
        var areaRoot = Math.Sqrt(width * height);
        var targetCount = Math.Clamp((int)Math.Round(areaRoot * 0.026 * options.LongRiverCountMultiplier * Math.Max(0.2, options.RiverDensity)), 2, 48);
        var minLength = Math.Clamp((int)Math.Round(areaRoot * 0.032), 20, 82);
        var upstream = upstreamCache ?? BuildUpstreamLists(flowDirections, width, height, gridTopology);
        var upstreamDepths = upstreamDepthsCache ?? BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height, gridTopology);
        var basinById = basins.ToDictionary(b => b.Id);

        var candidates = Enumerable.Range(0, flowDirections.Length)
            .Where(i => IsLongRiverMouthCandidate(i, flowDirections, riverCells, basinIds, basinById, allowedRiverBasins, lakeIds, mask, topology, width, height, gridTopology))
            .Select(i => BuildLongestUpstreamPath(i, upstream, upstreamDepths, accumulation, lakeIds, mask, topology, width, gridTopology))
            .Where(path => path.Count >= minLength)
            .Select(path => new LongRiverPath(path, accumulation[path[0]], basinIds[path[0]]))
            .OrderByDescending(p => p.Path.Count)
            .ThenByDescending(p => p.Discharge)
            .ToList();

        var chosenBasinCounts = new Dictionary<int, int>();
        var chosenMouths = new List<int>();
        foreach (var candidate in candidates)
        {
            var basinCount = chosenBasinCounts.GetValueOrDefault(candidate.BasinId);
            var perBasinCap = Math.Max(3, targetCount / 3);
            if (basinCount >= perBasinCap)
                continue;

            var mouthIndex = candidate.Path[0];
            var mouthPoint = new GridPoint(mouthIndex % width, mouthIndex / width);
            if (chosenMouths.Any(i =>
                {
                    var point = new GridPoint(i % width, i / width);
                    return Math.Abs(GridTopologyMath.WrappedDeltaX(gridTopology, point.X - mouthPoint.X)) < minLength / 3 &&
                           Math.Abs(point.Y - mouthPoint.Y) < minLength / 3;
                }))
            {
                continue;
            }

            result[mouthIndex] = candidate.Path;
            chosenBasinCounts[candidate.BasinId] = basinCount + 1;
            chosenMouths.Add(mouthIndex);
            if (chosenMouths.Count >= targetCount)
                break;
        }

        return result;
    }

    internal sealed record LongRiverPath(IReadOnlyList<int> Path, double Discharge, int BasinId);
}
