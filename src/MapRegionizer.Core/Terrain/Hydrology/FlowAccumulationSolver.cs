using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class FlowAccumulationSolver
{
    internal static double[] BuildLocalRunoff(MapMask mask, ElevationMap elevation, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
    {
        var runoff = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (!mask.IsLand(point) && topology.IsOceanicWater(point))
                    continue;

                if (elevation.HasWaterSurface(point) && !generatedLakes.Contains(point) && !mask.IsLand(point))
                {
                    runoff[index] = 0.25;
                    continue;
                }

                var height = Math.Max(0.0, elevation.GetBedElevation(point));
                var terrain = elevation.GetTerrainClass(point);
                var altitude = Math.Clamp(height / 2600.0, 0, 1) * 0.78;
                var foothill = elevation.GetFoothillInfluence(point) * 0.58;
                var pass = elevation.GetMountainPassPotential(point) * 0.42;
                var rough = Math.Clamp(1.0 - Math.Abs(elevation.GetRoughness(point) - 0.48) * 1.45, 0.12, 1.0) * 0.32;
                var basinEdge = elevation.GetBasinInfluence(point) * (terrain is TerrainClassKind.DryBasin ? -0.34 : 0.24);
                var ridgePenalty = elevation.GetRidgeContinuity(point) * (terrain == TerrainClassKind.Mountain ? 0.22 : 0.38);
                var terrainBonus = terrain switch
                {
                    TerrainClassKind.Mountain => 0.36,
                    TerrainClassKind.Highland => 0.24,
                    TerrainClassKind.AlluvialPlain => 0.16,
                    TerrainClassKind.DeltaCandidate => 0.18,
                    TerrainClassKind.DryBasin => -0.45,
                    TerrainClassKind.DesertPlateauCandidate => -0.22,
                    _ => 0.0
                };
                runoff[index] = Math.Clamp(0.42 + altitude + foothill + pass + rough + basinEdge + terrainBonus - ridgePenalty, 0.04, 2.75);
            }
        }

        return runoff;
    }

    internal static double[] AccumulateFlow(int[] flowDirections, double[] localRunoff, int width, int height)
    {
        var length = flowDirections.Length;
        var indegree = new int[length];
        for (var index = 0; index < length; index++)
        {
            var downstream = DownstreamIndex(index, flowDirections[index], width, height);
            if (downstream >= 0)
                indegree[downstream]++;
        }

        var accumulation = localRunoff.ToArray();
        var queue = new Queue<int>();
        for (var index = 0; index < length; index++)
        {
            if (indegree[index] == 0)
                queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var downstream = DownstreamIndex(index, flowDirections[index], width, height);
            if (downstream < 0)
                continue;

            accumulation[downstream] += accumulation[index];
            indegree[downstream]--;
            if (indegree[downstream] == 0)
                queue.Enqueue(downstream);
        }

        return accumulation;
    }

    internal static int[] BuildLongestUpstreamDepths(
        int[] flowDirections,
        List<int>[] upstream,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var depth = new int[flowDirections.Length];
        var remaining = new int[flowDirections.Length];
        var queue = new Queue<int>();
        for (var index = 0; index < flowDirections.Length; index++)
        {
            var point = new GridPoint(index % width, index / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                continue;

            var validUpstream = 0;
            foreach (var upstreamIndex in upstream[index])
            {
                var upstreamPoint = new GridPoint(upstreamIndex % width, upstreamIndex / width);
                if (IsRenderableRiverLand(upstreamPoint, mask, topology, lakeIds))
                    validUpstream++;
            }

            remaining[index] = validUpstream;
            if (validUpstream == 0)
                queue.Enqueue(index);
            depth[index] = 1;
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var downstream = DownstreamIndex(index, flowDirections[index], width, height);
            if (downstream < 0)
                continue;

            var downstreamPoint = new GridPoint(downstream % width, downstream / width);
            if (!IsRenderableRiverLand(downstreamPoint, mask, topology, lakeIds))
                continue;

            depth[downstream] = Math.Max(depth[downstream], depth[index] + 1);
            remaining[downstream]--;
            if (remaining[downstream] == 0)
                queue.Enqueue(downstream);
        }

        return depth;
    }

    internal static bool IsLongRiverMouthCandidate(
        int index,
        int[] flowDirections,
        byte[] riverCells,
        int[] basinIds,
        Dictionary<int, DrainageBasin> basinById,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var point = new GridPoint(index % width, index / width);
        if (!IsRenderableRiverLand(point, mask, topology, lakeIds) || !allowedRiverBasins.Contains(basinIds[index]))
            return false;
        if (!basinById.TryGetValue(basinIds[index], out var basin) ||
            basin.TargetKind is DrainageTargetKind.EndorheicDryBasin)
            return false;

        var downstream = DownstreamIndex(index, flowDirections[index], width, height);
        if (downstream < 0)
            return false;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        if (IsWaterTarget(downstreamPoint, mask, topology, lakeIds))
            return true;

        return riverCells[index] != 0 && riverCells[downstream] == 0;
    }

    internal static List<int> BuildLongestUpstreamPath(
        int mouthIndex,
        List<int>[] upstream,
        int[] upstreamDepths,
        double[] accumulation,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width)
    {
        var path = new List<int>();
        var current = mouthIndex;
        var seen = new HashSet<int>();
        var previousDirection = -1;
        var straightRunLength = 0;
        var diagonalRunDirection = -1;
        var diagonalRunLength = 0;
        while (current >= 0 && current < upstream.Length && seen.Add(current))
        {
            path.Add(current);
            var next = SelectShapeAwareUpstream(
                upstream[current]
                .Where(i => lakeIds[i] <= 0)
                .Where(i =>
                {
                    var point = new GridPoint(i % width, i / width);
                    return IsRenderableRiverLand(point, mask, topology, lakeIds);
                }),
                current,
                upstreamDepths,
                accumulation,
                width,
                preferDepth: true,
                previousDirection,
                straightRunLength,
                diagonalRunDirection,
                diagonalRunLength);

            if (next < 0)
                break;

            UpdateShapeState(next, current, width, ref previousDirection, ref straightRunLength, ref diagonalRunDirection, ref diagonalRunLength);
            current = next;
        }

        return path;
    }

    internal static double ScoreLongRiverUpstreamStep(int upstreamIndex, int currentIndex, double[] accumulation, int width)
    {
        var sameColumnPenalty = Math.Abs((upstreamIndex % width) - (currentIndex % width)) == 0 ? 0.06 : 0.0;
        return accumulation[upstreamIndex] + HashUnit(upstreamIndex % width, upstreamIndex / width, 7717) * 0.05 - sameColumnPenalty;
    }

    internal static int SelectShapeAwareUpstream(
        IEnumerable<int> candidates,
        int currentIndex,
        int[] upstreamDepths,
        double[] accumulation,
        int width,
        bool preferDepth,
        int previousDirection,
        int straightRunLength,
        int diagonalRunDirection,
        int diagonalRunLength)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
            return -1;

        if (preferDepth)
        {
            var maxDepth = list.Max(i => upstreamDepths[i]);
            list = list.Where(i => upstreamDepths[i] >= maxDepth - 3).ToList();
        }
        else
        {
            var maxAccumulation = list.Max(i => accumulation[i]);
            list = list.Where(i => accumulation[i] >= maxAccumulation * 0.82).ToList();
        }

        return list
            .OrderByDescending(i => ShapeAwareUpstreamScore(i, currentIndex, upstreamDepths, accumulation, width, preferDepth, previousDirection, straightRunLength, diagonalRunDirection, diagonalRunLength))
            .FirstOrDefault(-1);
    }

    internal static double ShapeAwareUpstreamScore(
        int upstreamIndex,
        int currentIndex,
        int[] upstreamDepths,
        double[] accumulation,
        int width,
        bool preferDepth,
        int previousDirection,
        int straightRunLength,
        int diagonalRunDirection,
        int diagonalRunLength)
    {
        var direction = DirectionBetween(upstreamIndex, currentIndex, width);
        var isDiagonal = direction >= 0 && Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
        var nextStraightRun = direction == previousDirection ? straightRunLength + 1 : 1;
        var nextDiagonalRun = isDiagonal && direction == diagonalRunDirection ? diagonalRunLength + 1 : isDiagonal ? 1 : 0;
        var turnDelta = previousDirection < 0 || direction < 0 ? 1 : Math.Abs(direction - previousDirection);
        turnDelta = Math.Min(turnDelta, Directions.Length - turnDelta);
        var turnScore = turnDelta switch
        {
            0 => -8.0,
            1 => 10.0,
            2 => 3.0,
            3 => -18.0,
            _ => -32.0
        };
        var straightPenalty = nextStraightRun >= 3 ? Math.Pow(nextStraightRun - 2, 2.0) * 26.0 : 0.0;
        var diagonalPenalty = nextDiagonalRun >= 3 ? Math.Pow(nextDiagonalRun - 2, 2.0) * 34.0 : 0.0;
        if (nextStraightRun >= 6)
            straightPenalty += 700.0;
        if (nextDiagonalRun >= 5)
            diagonalPenalty += 900.0;

        var depthScore = upstreamDepths[upstreamIndex] * (preferDepth ? 130.0 : 22.0);
        var accumulationScore = Math.Log(Math.Max(1.0, accumulation[upstreamIndex])) * (preferDepth ? 18.0 : 44.0);
        var sameColumnPenalty = Math.Abs((upstreamIndex % width) - (currentIndex % width)) == 0 ? 6.0 : 0.0;
        return depthScore
               + accumulationScore
               + turnScore
               - straightPenalty
               - diagonalPenalty
               - sameColumnPenalty
               + HashUnit(upstreamIndex % width, upstreamIndex / width, 7717) * 3.0;
    }

    internal static void UpdateShapeState(
        int upstreamIndex,
        int currentIndex,
        int width,
        ref int previousDirection,
        ref int straightRunLength,
        ref int diagonalRunDirection,
        ref int diagonalRunLength)
    {
        var direction = DirectionBetween(upstreamIndex, currentIndex, width);
        var isDiagonal = direction >= 0 && Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
        straightRunLength = direction == previousDirection ? straightRunLength + 1 : 1;
        diagonalRunLength = isDiagonal && direction == diagonalRunDirection ? diagonalRunLength + 1 : isDiagonal ? 1 : 0;
        diagonalRunDirection = isDiagonal ? direction : -1;
        previousDirection = direction;
    }

    internal static int DirectionBetween(int from, int to, int width)
    {
        var fromPoint = new GridPoint(from % width, from / width);
        var toPoint = new GridPoint(to % width, to / width);
        return DirectionIndex(fromPoint, toPoint, width);
    }

    internal static List<int>[] BuildUpstreamLists(int[] flowDirections, int width, int height)
    {
        var upstream = Enumerable.Range(0, flowDirections.Length).Select(_ => new List<int>()).ToArray();
        for (var index = 0; index < flowDirections.Length; index++)
        {
            var downstream = DownstreamIndex(index, flowDirections[index], width, height);
            if (downstream >= 0 && downstream < flowDirections.Length)
                upstream[downstream].Add(index);
        }

        return upstream;
    }

    internal static List<int> BuildMainstemPath(
        int mouthIndex,
        List<int>[] upstream,
        int[] upstreamDepths,
        double[] accumulation,
        bool[] visited,
        byte[] riverCells,
        int[] lakeIds,
        double minSourceAccumulation,
        int width,
        bool preferDepth)
    {
        var path = new List<int>();
        var current = mouthIndex;
        var guard = 0;
        var previousDirection = -1;
        var straightRunLength = 0;
        var diagonalRunDirection = -1;
        var diagonalRunLength = 0;
        while (current >= 0 && current < upstream.Length && guard++ < upstream.Length)
        {
            if (visited[current] && current != mouthIndex)
                break;

            path.Add(current);
            var next = SelectShapeAwareUpstream(
                upstream[current]
                .Where(i => !visited[i])
                .Where(i => riverCells[i] != 0 && lakeIds[i] <= 0)
                .Where(i => accumulation[i] >= minSourceAccumulation),
                current,
                upstreamDepths,
                accumulation,
                width,
                preferDepth,
                previousDirection,
                straightRunLength,
                diagonalRunDirection,
                diagonalRunLength);
            if (next < 0)
                break;

            UpdateShapeState(next, current, width, ref previousDirection, ref straightRunLength, ref diagonalRunDirection, ref diagonalRunLength);
            current = next;
        }

        return path;
    }

}
