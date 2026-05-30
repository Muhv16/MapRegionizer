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
            var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
            var gridLinePenalty = isDiagonal
                ? plainness * lowSlope * 0.58
                : plainness * lowSlope * 0.74;
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
                       + gridLinePenalty
                       + bendBias
                       + HashUnit(n.X, n.Y, _seed + 7321) * 3.5;

            if (cost >= bestCost)
                continue;

            bestCost = cost;
            bestDirection = direction;
        }

        return bestDirection;
    }

    internal bool RegularizeLongStraightRuns(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        int[] flowDirections,
        double[] accumulation,
        HydrologyGenerationOptions options)
    {
        const int minRun = 6;

        var width = mask.Width;
        var height = mask.Height;
        var visibleFloor = Math.Clamp(Math.Sqrt(width * height) * 0.032 / Math.Max(0.35, options.RiverDensity), 12.0, 42.0);
        var candidates = Enumerable.Range(0, flowDirections.Length)
            .Where(i => accumulation[i] >= visibleFloor)
            .Where(i => IsRenderableRiverLand(new GridPoint(i % width, i / width), mask, topology, lakeIds))
            .OrderByDescending(i => accumulation[i])
            .Take(Math.Clamp(flowDirections.Length / 180, 900, 2400))
            .ToList();
        var changed = false;
        var attemptedRuns = new HashSet<long>();
        var maxChanges = Math.Clamp(flowDirections.Length / 4200, 48, 160);
        var changes = 0;

        for (var pass = 0; pass < 3 && changes < maxChanges; pass++)
        {
            var passChanged = false;
            foreach (var start in candidates)
            {
                if (changes >= maxChanges)
                    break;

                var path = TraceRenderableDownstreamPath(start, flowDirections, lakeIds, mask, topology, maxLength: 180);
                if (path.Count < minRun + 1)
                    continue;

                var run = DetectLongestRun(path, width, minRun);
                if (!run.HasValue)
                    continue;

                var runValue = run.Value;
                var fromIndex = path[runValue.Start];
                var toIndex = path[runValue.Start + runValue.Length];
                var key = ((long)Math.Min(fromIndex, toIndex) << 32) | (uint)Math.Max(fromIndex, toIndex);
                if (!attemptedRuns.Add(key))
                    continue;

                var segment = path.Skip(runValue.Start).Take(runValue.Length + 1).ToList();
                var replacement = FindLocalFlowPath(mask, elevation, topology, hydro, lakeIds, segment, flowDirections, accumulation, options, runValue.Direction);
                if (replacement.Count <= 2 || replacement.SequenceEqual(segment))
                    continue;
                if (!TryCommitFlowPath(replacement, flowDirections, width, height))
                    continue;

                passChanged = true;
                changed = true;
                changes++;
            }

            if (!passChanged)
                break;
        }

        return changed;
    }

    private List<int> TraceRenderableDownstreamPath(
        int start,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int maxLength)
    {
        var path = new List<int>();
        var seen = new HashSet<int>();
        var current = start;
        while (current >= 0 && current < flowDirections.Length && path.Count < maxLength && seen.Add(current))
        {
            var point = new GridPoint(current % mask.Width, current / mask.Width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            path.Add(current);
            var downstream = DownstreamIndex(current, flowDirections[current], mask.Width, mask.Height);
            if (downstream < 0)
                break;

            current = downstream;
        }

        return path;
    }

    private static StraightRun? DetectLongestRun(IReadOnlyList<int> path, int width, int minRun)
    {
        if (path.Count < minRun + 1)
            return null;

        var best = default(StraightRun);
        var bestLength = 0;
        var direction = DirectionBetween(path[0], path[1], width);
        var start = 0;
        var length = 1;
        for (var i = 1; i < path.Count - 1; i++)
        {
            var nextDirection = DirectionBetween(path[i], path[i + 1], width);
            if (nextDirection == direction)
            {
                length++;
                continue;
            }

            if (direction >= 0 && length >= minRun && length > bestLength)
            {
                best = new StraightRun(start, length, direction);
                bestLength = length;
            }

            direction = nextDirection;
            start = i;
            length = 1;
        }

        if (direction >= 0 && length >= minRun && length > bestLength)
            best = new StraightRun(start, length, direction);

        return best.Length == 0 ? null : best;
    }

    private List<int> FindLocalFlowPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        IReadOnlyList<int> segment,
        int[] flowDirections,
        double[] accumulation,
        HydrologyGenerationOptions options,
        int forbiddenDirection)
    {
        var width = mask.Width;
        var height = mask.Height;
        var start = segment[0];
        var target = segment[^1];
        var segmentPoints = segment.Select(i => new GridPoint(i % width, i / width)).ToList();
        var baseRadius = Math.Clamp(segment.Count / 4 + 2, 4, 8);

        for (var radius = baseRadius; radius <= 10; radius += 2)
        {
            var path = FindLocalFlowPath(mask, elevation, topology, hydro, lakeIds, segmentPoints, start, target, flowDirections, accumulation, options, forbiddenDirection, radius);
            if (path.Count >= 2 && !path.SequenceEqual(segment))
                return path;
        }

        return segment.ToList();
    }

    private List<int> FindLocalFlowPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        double[] hydro,
        int[] lakeIds,
        IReadOnlyList<GridPoint> segmentPoints,
        int start,
        int target,
        int[] flowDirections,
        double[] accumulation,
        HydrologyGenerationOptions options,
        int forbiddenDirection,
        int radius)
    {
        var width = mask.Width;
        var startPoint = new GridPoint(start % width, start / width);
        var targetPoint = new GridPoint(target % width, target / width);
        var open = new PriorityQueue<FlowSearchNode, double>();
        var first = new FlowSearchNode(start, -1, 0, -1, 0);
        var bestCosts = new Dictionary<FlowSearchNode, double> { [first] = 0.0 };
        var previous = new Dictionary<FlowSearchNode, FlowSearchNode>();
        open.Enqueue(first, Distance(startPoint, targetPoint, width));
        FlowSearchNode? found = null;
        var maxExpansions = Math.Clamp(segmentPoints.Count * radius * 130, 2400, 64000);
        var expansions = 0;

        while (open.Count > 0 && expansions++ < maxExpansions)
        {
            var node = open.Dequeue();
            var baseCost = bestCosts[node];
            if (node.Index == target)
            {
                found = node;
                break;
            }

            var current = new GridPoint(node.Index % width, node.Index / width);
            for (var direction = 0; direction < Directions.Length; direction++)
            {
                var moved = Move(current, direction, width, mask.Height);
                if (!moved.HasValue)
                    continue;

                var nextPoint = moved.Value;
                var next = nextPoint.Y * width + nextPoint.X;
                if (next != target && !IsRenderableRiverLand(nextPoint, mask, topology, lakeIds))
                    continue;

                var pathDistance = DistanceToPath(nextPoint, segmentPoints, width, radius + 0.75);
                if (pathDistance > radius && next != target)
                    continue;

                var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
                var straightRun = direction == node.PreviousDirection ? node.StraightRunLength + 1 : 1;
                var diagonalRun = isDiagonal && direction == node.DiagonalRunDirection ? node.DiagonalRunLength + 1 : isDiagonal ? 1 : 0;
                var nearTarget = Distance(nextPoint, targetPoint, width) <= 2.01;
                if (!nearTarget && straightRun > 5)
                    continue;
                if (!nearTarget && isDiagonal && diagonalRun > 4)
                    continue;
                if (!nearTarget && direction == forbiddenDirection && straightRun >= 3)
                    continue;
                if (!nearTarget && isDiagonal && direction == forbiddenDirection && diagonalRun >= 3)
                    continue;
                if (!nearTarget && IsBacktrackLikeTurn(node.PreviousDirection, direction))
                    continue;

                var stepCost = FlowRegularizationStepCost(
                    current,
                    nextPoint,
                    targetPoint,
                    direction,
                    node.PreviousDirection,
                    straightRun,
                    diagonalRun,
                    pathDistance,
                    hydro,
                    accumulation,
                    elevation,
                    options);
                if (!stepCost.HasValue)
                    continue;

                var nextNode = new FlowSearchNode(next, direction, straightRun, isDiagonal ? direction : -1, diagonalRun);
                var cost = baseCost + stepCost.Value;
                if (bestCosts.TryGetValue(nextNode, out var oldCost) && oldCost <= cost)
                    continue;

                bestCosts[nextNode] = cost;
                previous[nextNode] = node;
                var heuristic = Distance(nextPoint, targetPoint, width) * 2.6 + pathDistance * 0.75;
                open.Enqueue(nextNode, cost + heuristic);
            }
        }

        return found.HasValue ? ReconstructFlowPath(found.Value, previous) : [];
    }

    private double? FlowRegularizationStepCost(
        GridPoint current,
        GridPoint next,
        GridPoint target,
        int direction,
        int previousDirection,
        int straightRun,
        int diagonalRun,
        double pathDistance,
        double[] hydro,
        double[] accumulation,
        ElevationMap elevation,
        HydrologyGenerationOptions options)
    {
        var width = elevation.Width;
        var currentIndex = current.Y * width + current.X;
        var nextIndex = next.Y * width + next.X;
        var uphill = hydro[nextIndex] - hydro[currentIndex];
        var maxBreach = 11.0 + elevation.GetMountainPassPotential(next) * 34.0 + elevation.GetBasinInfluence(next) * 18.0;
        if (uphill > maxBreach)
            return null;

        var terrain = elevation.GetTerrainClass(next);
        var plainness = FlowPlainness(terrain);
        var lateralStrength = Math.Clamp(plainness + elevation.GetFoothillInfluence(next) * 0.32 + options.MeanderStrength * 0.25, 0.15, 1.0);
        var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
        var turnDelta = previousDirection < 0 ? 1 : Math.Abs(direction - previousDirection);
        turnDelta = Math.Min(turnDelta, Directions.Length - turnDelta);
        var turnCost = turnDelta switch
        {
            0 => 2.8 + lateralStrength * 3.5,
            1 => -1.4,
            2 => 0.4,
            3 => 6.0,
            _ => 10.0
        };
        var straightPenalty = straightRun >= 3 ? Math.Pow(straightRun - 1, 1.9) * (7.0 + lateralStrength * 8.0) : 0.0;
        var diagonalPenalty = isDiagonal && diagonalRun >= 3 ? Math.Pow(diagonalRun - 1, 2.0) * (8.5 + lateralStrength * 9.0) : 0.0;
        var targetDistance = Distance(next, target, width);

        return 1.0
               + hydro[nextIndex] * 0.035
               + Math.Max(0.0, uphill) * 2.9
               + elevation.GetRoughness(next) * 9.0
               + Math.Max(0.0, elevation.GetRidgeContinuity(next) - elevation.GetMountainPassPotential(next) * 0.45) * 42.0
               - elevation.GetBasinInfluence(next) * 22.0
               - TerrainValleyBias(terrain) * 0.18
               - Math.Clamp(accumulation[nextIndex] / 180.0, 0.0, 4.0)
               + pathDistance * 4.8
               + targetDistance * 2.2
               + turnCost
               + straightPenalty
               + diagonalPenalty
               + HashUnit(next.X, next.Y, _seed + 7901) * 2.6;
    }

    private static bool TryCommitFlowPath(IReadOnlyList<int> path, int[] flowDirections, int width, int height)
    {
        if (path.Count < 2 || path.Count != path.Distinct().Count())
            return false;

        var testDirections = flowDirections.ToArray();
        for (var i = 0; i < path.Count - 1; i++)
        {
            var from = path[i];
            var to = path[i + 1];
            var direction = DirectionBetween(from, to, width);
            if (direction < 0 || DownstreamIndex(from, direction, width, height) != to)
                return false;

            testDirections[from] = direction;
            if (WouldCreateCycle(from, to, testDirections, width, height))
                return false;
        }

        for (var i = 0; i < path.Count - 1; i++)
            flowDirections[path[i]] = DirectionBetween(path[i], path[i + 1], width);

        return true;
    }

    private static List<int> ReconstructFlowPath(
        FlowSearchNode found,
        IReadOnlyDictionary<FlowSearchNode, FlowSearchNode> previous)
    {
        var path = new List<int>();
        var current = found;
        while (true)
        {
            path.Add(current.Index);
            if (!previous.TryGetValue(current, out var parent))
                break;
            current = parent;
        }

        path.Reverse();
        return path;
    }

    private static double DistanceToPath(GridPoint point, IReadOnlyList<GridPoint> cells, int width, double maxStopDistance)
    {
        var best = double.PositiveInfinity;
        foreach (var cell in cells)
        {
            var distance = Distance(point, cell, width);
            if (distance < best)
                best = distance;
            if (best <= 0.001 || best <= maxStopDistance * 0.35)
                break;
        }

        return best;
    }

    private static int DirectionBetween(int from, int to, int width)
    {
        var fromPoint = new GridPoint(from % width, from / width);
        var toPoint = new GridPoint(to % width, to / width);
        return DirectionIndex(fromPoint, toPoint, width);
    }

    private static bool IsBacktrackLikeTurn(int previousDirection, int direction)
    {
        if (previousDirection < 0 || direction < 0)
            return false;

        var delta = Math.Abs(direction - previousDirection);
        return Math.Min(delta, Directions.Length - delta) >= 4;
    }

    private static double FlowPlainness(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.AlluvialPlain => 1.0,
        TerrainClassKind.InteriorLowland => 0.95,
        TerrainClassKind.CoastalPlain => 0.88,
        TerrainClassKind.SedimentaryBasin => 0.82,
        TerrainClassKind.DeltaCandidate => 1.0,
        TerrainClassKind.DryBasin => 0.68,
        TerrainClassKind.Highland => 0.48,
        TerrainClassKind.Mountain => 0.18,
        _ => 0.22
    };

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

    private readonly record struct StraightRun(int Start, int Length, int Direction);

    private readonly record struct FlowSearchNode(int Index, int PreviousDirection, int StraightRunLength, int DiagonalRunDirection, int DiagonalRunLength);
}
