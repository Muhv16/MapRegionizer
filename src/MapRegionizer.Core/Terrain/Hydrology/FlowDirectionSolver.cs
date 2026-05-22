using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class FlowDirectionSolver
{
    private readonly int _seed;

    public FlowDirectionSolver(int seed)
    {
        _seed = seed;
    }

    internal int[] BuildFlowDirections(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        GeneratedLakeMap generatedLakes,
        double[] hydro,
        int[] lakeIds,
        int[] lakeNext,
        IReadOnlyList<LakeOutlet> outlets,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var directions = Enumerable.Repeat(-1, width * height).ToArray();
        var outletCells = outlets.Where(o => o.HasOutlet && o.OutletCell.HasValue).Select(o => o.OutletCell!.Value).ToHashSet();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * width + x;
                if (!mask.IsLand(point) && topology.IsOceanicWater(point))
                    continue;

                if (lakeIds[index] > 0)
                {
                    directions[index] = lakeNext[index];
                    continue;
                }

                if (generatedLakes.Contains(point))
                    continue;

                directions[index] = ChooseDownstreamDirection(point, mask, elevation, topology, hydro, lakeIds, outletCells, options);
            }
        }

        return directions;
    }

    internal int ChooseDownstreamDirection(
        GridPoint point,
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        HashSet<GridPoint> outletCells,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var currentIndex = point.Y * width + point.X;
        var currentHeight = hydro[currentIndex];
        var bestDirection = -1;
        var bestCost = double.PositiveInfinity;
        for (var direction = 0; direction < Directions.Length; direction++)
        {
            var neighbor = Move(point, direction, width, mask.Height);
            if (neighbor is null)
                continue;

            var n = neighbor.Value;
            var neighborIndex = n.Y * width + n.X;
            var neighborHeight = hydro[neighborIndex];
            var neighborKind = topology.GetKind(n);
            var isOcean = !mask.IsLand(n) && neighborKind is (WaterBodyKind.Ocean or WaterBodyKind.OceanSea);
            var isLake = lakeIds[neighborIndex] > 0;
            var diagonalPenalty = Directions[direction].Dx != 0 && Directions[direction].Dy != 0 ? 1.414 : 1.0;
            var uphill = neighborHeight - currentHeight;
            var maxBreach = 44.0 + elevation.GetMountainPassPotential(point) * 92.0 + elevation.GetBasinInfluence(point) * 44.0;
            if (!isOcean && uphill > maxBreach && !outletCells.Contains(point))
                continue;

            var terrain = elevation.GetTerrainClass(n);
            var ridgeCrossing = Math.Max(0.0, elevation.GetRidgeContinuity(n) - elevation.GetMountainPassPotential(n) * 0.45);
            var plainness = terrain is TerrainClassKind.AlluvialPlain or TerrainClassKind.InteriorLowland or TerrainClassKind.CoastalPlain or TerrainClassKind.SedimentaryBasin or TerrainClassKind.DeltaCandidate
                ? 1.0
                : terrain == TerrainClassKind.Highland ? 0.35 : 0.15;
            var lowSlope = Math.Clamp(1.0 - Math.Abs(uphill) / 38.0, 0, 1);
            var bendX = Hash01(point.X / 13, point.Y / 13, _seed + 7391) * 0.62 +
                        Hash01(point.X / 29, point.Y / 19, _seed + 7393) * 0.38;
            var bendY = Hash01(point.X / 17, point.Y / 17, _seed + 7397) * 0.58 +
                        Hash01(point.X / 23, point.Y / 31, _seed + 7399) * 0.42;
            var bendLen = Math.Sqrt(bendX * bendX + bendY * bendY);
            var bendBias = bendLen <= 0.001
                ? 0.0
                : -((Directions[direction].Dx * bendX + Directions[direction].Dy * bendY) / bendLen) * plainness * lowSlope * 5.2;
            var axisPenalty = Directions[direction].Dx == 0 || Directions[direction].Dy == 0
                ? plainness * lowSlope * 1.15
                : 0.0;
            var targetBiasScale = Math.Clamp(1.0 - plainness * lowSlope * 0.48, 0.42, 1.0);
            var cost = neighborHeight
                       + uphill * (uphill > 0 ? 2.8 : 0.18)
                       + elevation.GetRoughness(n) * 16.0
                       + ridgeCrossing * 64.0
                       - elevation.GetMountainPassPotential(n) * 34.0
                       - elevation.GetBasinInfluence(n) * 31.0
                       - TerrainValleyBias(terrain)
                       - (isLake ? 12.0 * targetBiasScale : 0.0)
                       - (isOcean ? 22.0 * targetBiasScale : 0.0)
                       + diagonalPenalty
                       + axisPenalty
                       + bendBias
                       + HashUnit(n.X, n.Y, _seed + 7321) * 3.5;

            if (cost >= bestCost)
                continue;

            bestCost = cost;
            bestDirection = direction;
        }

        return bestDirection;
    }

    internal static bool WouldCreateCycle(int from, int target, int[] flowDirections, int width, int height)
    {
        var current = target;
        var guard = 0;
        while (current >= 0 && current < flowDirections.Length && guard++ < flowDirections.Length)
        {
            if (current == from)
                return true;

            current = DownstreamIndex(current, flowDirections[current], width, height);
        }

        return false;
    }

    internal static bool IsOrthogonalNeighbor(int first, int second, int width)
    {
        var dx = Math.Abs(WrappedDeltaX(second % width - first % width, width));
        var dy = Math.Abs(second / width - first / width);
        return dx + dy == 1;
    }

    internal void ResolveInvalidDryTerminals(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        GeneratedLakeMap generatedLakes,
        double[] hydro,
        int[] lakeIds,
        int[] flowDirections,
        HydrologyGenerationOptions options)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            for (var index = 0; index < flowDirections.Length; index++)
            {
                if (flowDirections[index] >= 0)
                    continue;

                var point = new GridPoint(index % mask.Width, index / mask.Width);
                if (!IsRiverSourceLand(mask, generatedLakes, point) || lakeIds[index] > 0)
                    continue;
                if (IsValidEndorheicTerminal(elevation, point, options))
                    continue;

                var direction = ChooseSpillDirection(point, mask, elevation, topology, hydro, lakeIds, flowDirections, options);
                if (direction < 0)
                    continue;

                flowDirections[index] = direction;
                changed = true;
            }

            if (!changed)
                break;
        }
    }

    internal int ChooseSpillDirection(
        GridPoint point,
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        int[] flowDirections,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var current = point.Y * width + point.X;
        var currentHeight = hydro[current];
        var bestDirection = -1;
        var bestCost = double.PositiveInfinity;

        for (var direction = 0; direction < Directions.Length; direction++)
        {
            var neighbor = Move(point, direction, width, mask.Height);
            if (neighbor is null)
                continue;

            var n = neighbor.Value;
            var neighborIndex = n.Y * width + n.X;
            var neighborKind = topology.GetKind(n);
            var isOcean = !mask.IsLand(n) && neighborKind is (WaterBodyKind.Ocean or WaterBodyKind.OceanSea);
            var isLake = lakeIds[neighborIndex] > 0;
            var uphill = Math.Max(0.0, hydro[neighborIndex] - currentHeight);
            var maxSpill = 145.0 + elevation.GetMountainPassPotential(point) * 120.0 + elevation.GetBasinInfluence(point) * 70.0;
            if (!isOcean && !isLake && uphill > maxSpill)
                continue;

            var terminalPenalty = flowDirections[neighborIndex] < 0 && !isOcean && !isLake ? 42.0 : 0.0;
            var ridgePenalty = elevation.GetRidgeContinuity(n) * 48.0;
            var cost = hydro[neighborIndex]
                       + uphill * 2.4
                       + terminalPenalty
                       + ridgePenalty
                       + elevation.GetRoughness(n) * 16.0
                       - elevation.GetBasinInfluence(n) * 32.0
                       - elevation.GetMountainPassPotential(n) * 38.0
                       - (isOcean || isLake ? 70.0 : 0.0)
                       + HashUnit(n.X, n.Y, _seed + 7523) * 2.0;

            if (cost >= bestCost)
                continue;

            bestCost = cost;
            bestDirection = direction;
        }

        return bestDirection;
    }

    internal static bool IsValidEndorheicTerminal(ElevationMap elevation, GridPoint point, HydrologyGenerationOptions options)
    {
        var terrain = elevation.GetTerrainClass(point);
        var basin = elevation.GetBasinInfluence(point);
        if (terrain == TerrainClassKind.DryBasin && basin >= 0.36)
            return true;
        if (terrain == TerrainClassKind.SedimentaryBasin && basin >= 0.56 && options.EndorheicBasinChance >= 0.14)
            return true;

        return false;
    }

    internal static void BreakCycles(int[] flowDirections, int width, int height)
    {
        var length = flowDirections.Length;
        var state = new byte[length];
        for (var start = 0; start < length; start++)
        {
            if (state[start] != 0)
                continue;

            var path = new List<int>();
            var seen = new Dictionary<int, int>();
            var current = start;
            while (current >= 0 && current < length)
            {
                if (state[current] == 2)
                    break;
                if (seen.TryGetValue(current, out var cycleStart))
                {
                    var breakIndex = path.Skip(cycleStart).OrderBy(i => flowDirections[i]).First();
                    flowDirections[breakIndex] = -1;
                    break;
                }
                if (state[current] == 1)
                    break;

                state[current] = 1;
                seen[current] = path.Count;
                path.Add(current);
                current = DownstreamIndex(current, flowDirections[current], width, height);
            }

            foreach (var index in path)
                state[index] = 2;
        }
    }

}
