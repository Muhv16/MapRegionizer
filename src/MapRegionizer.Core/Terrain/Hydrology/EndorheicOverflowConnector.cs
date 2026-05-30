using System.Buffers;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class EndorheicOverflowConnector
{
    private readonly int _seed;

    public EndorheicOverflowConnector(int seed)
    {
        _seed = seed;
    }

    internal bool ForceEndorheicOverflow(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        IReadOnlyList<DrainageBasin> basins,
        IReadOnlyDictionary<int, EndorheicRiverPolicy> endorheicPolicies,
        IReadOnlyList<LakeOutlet> outlets,
        HashSet<int>? outletLakeIdsCache = null)
    {
        var width = mask.Width;
        var outletLakeIds = outletLakeIdsCache ?? outlets
            .Where(o => o.HasOutlet)
            .Select(o => o.LakeId.Value)
            .ToHashSet();
        var basinById = basins.ToDictionary(b => b.Id);
        var changed = false;

        foreach (var basin in basins
                     .Where(b => endorheicPolicies.TryGetValue(b.Id, out var policy) && policy == EndorheicRiverPolicy.ForceOverflow)
                     .OrderByDescending(b => b.TotalRunoff)
                     .ThenByDescending(b => b.CellCount))
        {
            var terminalIndex = basin.TerminalCell.Y * width + basin.TerminalCell.X;
            if (terminalIndex < 0 || terminalIndex >= flowDirections.Length)
                continue;
            if (flowDirections[terminalIndex] >= 0)
                continue;

            var path = FindEndorheicOverflowPath(
                basin,
                mask,
                elevation,
                topology,
                hydro,
                lakeIds,
                accumulation,
                basinIds,
                basinById,
                outletLakeIds);
            if (path.Count < 2)
                continue;

            for (var i = 0; i < path.Count - 1; i++)
            {
                var from = new GridPoint(path[i] % width, path[i] / width);
                var to = new GridPoint(path[i + 1] % width, path[i + 1] / width);
                var direction = DirectionIndex(from, to, width);
                if (direction >= 0)
                    flowDirections[path[i]] = direction;
            }

            changed = true;
        }

        return changed;
    }

    internal List<int> FindEndorheicOverflowPath(
    DrainageBasin sourceBasin,
    MapMask mask,
    ElevationMap elevation,
    WaterBodyTopology topology,
    double[] hydro,
    int[] lakeIds,
    double[] accumulation,
    int[] basinIds,
    IReadOnlyDictionary<int, DrainageBasin> basinById,
    HashSet<int> outletLakeIds)
    {
        var width = mask.Width;
        var height = mask.Height;
        var start = sourceBasin.TerminalCell.Y * width + sourceBasin.TerminalCell.X;
        var startHeight = hydro[start];

        var startRunoff = Math.Max(
            accumulation[start],
            Math.Sqrt(Math.Max(0.0, sourceBasin.TotalRunoff)));

        var maxSteps = Math.Clamp(
            (int)Math.Round(48.0 + Math.Sqrt(sourceBasin.CellCount) * 3.2 + Math.Sqrt(width * height) * 0.08),
            420,
            2400);

        var maxBarrier = Math.Clamp(
            90.0 + Math.Sqrt(startRunoff) * 14.0 + elevation.GetBasinInfluence(sourceBasin.TerminalCell) * 120.0,
            260.0,
            1200.0);

        var maxCost = Math.Clamp(
            600.0 + Math.Sqrt(startRunoff) * 90.0 + Math.Sqrt(sourceBasin.CellCount) * 55.0,
            1800.0,
            30000.0);

        var costsPool = ArrayPool<double>.Shared.Rent(width * height);
        var stepsPool = ArrayPool<int>.Shared.Rent(width * height);
        var previousPool = ArrayPool<int>.Shared.Rent(width * height);
        var spillHeightsPool = ArrayPool<double>.Shared.Rent(width * height);

        try
        {
            Array.Fill(costsPool, double.PositiveInfinity, 0, width * height);
            Array.Fill(stepsPool, int.MaxValue, 0, width * height);
            Array.Fill(previousPool, -1, 0, width * height);
            Array.Fill(spillHeightsPool, double.PositiveInfinity, 0, width * height);

            var costs = costsPool;
            var steps = stepsPool;
            var previous = previousPool;
            var spillHeights = spillHeightsPool;

            var queue = new PriorityQueue<int, double>();
            costs[start] = 0.0;
            steps[start] = 0;
            spillHeights[start] = startHeight;
            queue.Enqueue(start, 0.0);

            while (queue.Count > 0)
            {
                queue.TryDequeue(out var current, out var currentPriority);
                if (currentPriority > costs[current] + 0.0001)
                    continue;

                if (current != start &&
                    IsOverflowTarget(current, sourceBasin.Id, mask, topology, lakeIds, basinIds, basinById, outletLakeIds))
                {
                    return ReconstructPath(current, previous);
                }

                if (steps[current] >= maxSteps || costs[current] > maxCost)
                    continue;

                var currentPoint = new GridPoint(current % width, current / width);
                foreach (var neighbor in Neighbors8(currentPoint, width, height))
                {
                    var next = neighbor.Y * width + neighbor.X;
                    if (!CanTraverseOverflowCell(next, current, sourceBasin.Id, mask, topology, lakeIds, basinIds, basinById, outletLakeIds))
                        continue;

                    var newSpillHeight = Math.Max(spillHeights[current], hydro[next]);
                    var totalBarrier = Math.Max(0.0, newSpillHeight - startHeight);
                    if (totalBarrier > maxBarrier)
                        continue;

                    var edgeCost = ScoreOverflowStepBySpill(
                        current,
                        next,
                        spillHeights[current],
                        newSpillHeight,
                        startHeight,
                        mask,
                        elevation,
                        topology,
                        hydro,
                        lakeIds,
                        outletLakeIds,
                        width);

                    var newCost = costs[current] + edgeCost;
                    var newSteps = steps[current] + 1;

                    if (newSteps > maxSteps || newCost > maxCost)
                        continue;

                    var better =
                        newCost < costs[next] - 0.0001 ||
                        Math.Abs(newCost - costs[next]) < 0.0001 && newSpillHeight < spillHeights[next];

                    if (!better)
                        continue;

                    costs[next] = newCost;
                    steps[next] = newSteps;
                    spillHeights[next] = newSpillHeight;
                    previous[next] = current;

                    queue.Enqueue(next, newCost);
                }
            }

            return [];
        }
        finally
        {
            ArrayPool<double>.Shared.Return(costsPool);
            ArrayPool<int>.Shared.Return(stepsPool);
            ArrayPool<int>.Shared.Return(previousPool);
            ArrayPool<double>.Shared.Return(spillHeightsPool);
        }
    }

    internal double ScoreOverflowStepBySpill(
    int current,
    int next,
    double currentSpillHeight,
    double nextSpillHeight,
    double startHeight,
    MapMask mask,
    ElevationMap elevation,
    WaterBodyTopology topology,
    double[] hydro,
    int[] lakeIds,
    HashSet<int> outletLakeIds,
    int width)
    {
        var currentPoint = new GridPoint(current % width, current / width);
        var nextPoint = new GridPoint(next % width, next / width);

        var diagonal = Math.Abs(WrappedDeltaX(nextPoint.X - currentPoint.X, width)) != 0 &&
                       nextPoint.Y != currentPoint.Y;

        var terrain = elevation.GetTerrainClass(nextPoint);
        var isOcean = !mask.IsLand(nextPoint) && topology.IsOceanicWater(nextPoint);
        var isOutletLake = lakeIds[next] > 0 && outletLakeIds.Contains(lakeIds[next]);

        var uphill = Math.Max(0.0, hydro[next] - hydro[current]);

        // Only the newly raised spill height is charged.
        var spillRise = Math.Max(0.0, nextSpillHeight - currentSpillHeight);
        var totalBarrier = Math.Max(0.0, nextSpillHeight - startHeight);

        var ridgeCrossing = Math.Max(
            0.0,
            elevation.GetRidgeContinuity(nextPoint) -
            elevation.GetMountainPassPotential(nextPoint) * 0.65);

        var valleyBias =
            TerrainValleyBias(terrain) * 0.32 +
            elevation.GetBasinInfluence(nextPoint) * 11.0;

        var targetBonus = isOcean ? 70.0 : isOutletLake ? 58.0 : 0.0;

        var cost =
            (diagonal ? 1.414 : 1.0) * 4.0
            + uphill * 0.35
            + spillRise * 9.0
            + totalBarrier * 0.05
            + ridgeCrossing * 36.0
            + elevation.GetRoughness(nextPoint) * 10.0
            - elevation.GetMountainPassPotential(nextPoint) * 34.0
            - valleyBias
            - targetBonus
            + HashUnit(nextPoint.X, nextPoint.Y, _seed + 7691) * 1.5;

        return Math.Max(0.25, cost);
    }

    internal static bool IsOverflowTarget(
        int index,
        int sourceBasinId,
        MapMask mask,
        WaterBodyTopology topology,
        int[] lakeIds,
        int[] basinIds,
        IReadOnlyDictionary<int, DrainageBasin> basinById,
        HashSet<int> outletLakeIds)
    {
        var width = mask.Width;
        var point = new GridPoint(index % width, index / width);
        var lakeId = lakeIds[index];
        if (lakeId > 0)
            return outletLakeIds.Contains(lakeId);
        if (!mask.IsLand(point) && topology.IsOceanicWater(point))
            return true;
        if (basinIds[index] == sourceBasinId)
            return false;

        return basinById.TryGetValue(basinIds[index], out var basin) &&
               basin.TargetKind != DrainageTargetKind.EndorheicDryBasin;
    }

    internal static bool CanTraverseOverflowCell(
        int index,
        int current,
        int sourceBasinId,
        MapMask mask,
        WaterBodyTopology topology,
        int[] lakeIds,
        int[] basinIds,
        IReadOnlyDictionary<int, DrainageBasin> basinById,
        HashSet<int> outletLakeIds)
    {
        if (index == current)
            return false;
        if (IsOverflowTarget(index, sourceBasinId, mask, topology, lakeIds, basinIds, basinById, outletLakeIds))
            return true;

        var point = new GridPoint(index % mask.Width, index / mask.Width);
        if (!mask.IsLand(point) || lakeIds[index] > 0)
            return false;

        return !topology.IsInlandWater(point);
    }

    internal double ScoreOverflowStep(
        int current,
        int next,
        double startHeight,
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        HashSet<int> outletLakeIds,
        int width)
    {
        var currentPoint = new GridPoint(current % width, current / width);
        var nextPoint = new GridPoint(next % width, next / width);
        var diagonal = Math.Abs(WrappedDeltaX(nextPoint.X - currentPoint.X, width)) != 0 &&
                       nextPoint.Y != currentPoint.Y;
        var terrain = elevation.GetTerrainClass(nextPoint);
        var isOcean = !mask.IsLand(nextPoint) && topology.IsOceanicWater(nextPoint);
        var isOutletLake = lakeIds[next] > 0 && outletLakeIds.Contains(lakeIds[next]);
        var uphill = Math.Max(0.0, hydro[next] - hydro[current]);
        var barrier = Math.Max(0.0, hydro[next] - startHeight);
        var ridgeCrossing = Math.Max(0.0, elevation.GetRidgeContinuity(nextPoint) - elevation.GetMountainPassPotential(nextPoint) * 0.55);
        var valleyBias = TerrainValleyBias(terrain) * 0.24 + elevation.GetBasinInfluence(nextPoint) * 9.0;
        var targetBonus = isOcean ? 54.0 : isOutletLake ? 44.0 : 0.0;

        var cost = (diagonal ? 1.414 : 1.0) * 5.0
                   + uphill * 1.55
                   + barrier * 0.72
                   + ridgeCrossing * 42.0
                   + elevation.GetRoughness(nextPoint) * 12.0
                   - elevation.GetMountainPassPotential(nextPoint) * 28.0
                   - valleyBias
                   - targetBonus
                   + HashUnit(nextPoint.X, nextPoint.Y, _seed + 7691) * 2.0;
        return Math.Max(0.5, cost);
    }

}
