using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class ChannelPathTracer
{
    private readonly int _seed;

    public ChannelPathTracer(int seed)
    {
        _seed = seed;
    }

    internal List<GridPoint> BuildChannelPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds,
        IReadOnlyList<GridPoint> originalCells,
        GridPoint? renderOutlet,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options)
    {
        if (originalCells.Count <= 2)
            return originalCells.ToList();

        var path = TraceChannelPath(mask, elevation, topology, lakeIds, originalCells, usedChannelCells, discharge, meanSlope, options);
        if (path.Count < 2)
            path = originalCells.ToList();

        path = RerouteLongStraightRuns(mask, elevation, topology, lakeIds, path, usedChannelCells, discharge, meanSlope, options);
        path = SmoothAlternatingZigZags(path, mask.Width);
        return path.Count >= 2 ? path : originalCells.ToList();
    }

    internal List<GridPoint> TraceChannelPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds,
        IReadOnlyList<GridPoint> originalCells,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var target = originalCells[^1];
        var corridorRadius = Math.Clamp((int)Math.Round(Math.Sqrt(originalCells.Count) * 0.72), 4, 8);
        var start = originalCells[0];
        var open = new PriorityQueue<ChannelSearchNode, double>();
        var first = new ChannelSearchNode(start, -1, 0, -1, 0);
        var bestCosts = new Dictionary<ChannelSearchNode, double> { [first] = 0.0 };
        var previous = new Dictionary<ChannelSearchNode, ChannelSearchNode>();
        open.Enqueue(first, Distance(start, target, width));
        ChannelSearchNode? found = null;
        var maxExpansions = Math.Clamp(originalCells.Count * 520, 2400, 52000);
        var expansions = 0;

        while (open.Count > 0 && expansions++ < maxExpansions)
        {
            var node = open.Dequeue();
            var baseCost = bestCosts[node];
            if (node.Cell == target)
            {
                found = node;
                break;
            }

            for (var direction = 0; direction < Directions.Length; direction++)
            {
                var moved = Move(node.Cell, direction, width, mask.Height);
                if (!moved.HasValue)
                    continue;

                var next = moved.Value;
                if (next != target && !IsRenderableRiverLand(next, mask, topology, lakeIds))
                    continue;

                var pathDistance = DistanceToPath(next, originalCells, width, corridorRadius + 1.0);
                if (pathDistance > corridorRadius && next != target)
                    continue;

                var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
                var straightRun = direction == node.PreviousDirection ? node.StraightRunLength + 1 : 1;
                var diagonalRun = isDiagonal && direction == node.DiagonalRunDirection ? node.DiagonalRunLength + 1 : isDiagonal ? 1 : 0;
                var nearTarget = Distance(next, target, width) <= 2.01;
                if (!nearTarget && straightRun > 5)
                    continue;
                if (!nearTarget && isDiagonal && diagonalRun > 4)
                    continue;
                if (!nearTarget && IsBacktrackLikeTurn(node.PreviousDirection, direction))
                    continue;

                var stepCost = ChannelStepCost(
                    node.Cell,
                    next,
                    target,
                    direction,
                    node.PreviousDirection,
                    straightRun,
                    isDiagonal ? direction : -1,
                    diagonalRun,
                    pathDistance,
                    originalCells.Count,
                    mask,
                    elevation,
                    usedChannelCells,
                    discharge,
                    meanSlope,
                    options,
                    hardLimitMode: true);
                if (!stepCost.HasValue)
                    continue;

                var nextNode = new ChannelSearchNode(next, direction, straightRun, isDiagonal ? direction : -1, diagonalRun);
                var cost = baseCost + stepCost.Value;
                if (bestCosts.TryGetValue(nextNode, out var oldCost) && oldCost <= cost)
                    continue;

                bestCosts[nextNode] = cost;
                previous[nextNode] = node;
                var heuristic = Distance(next, target, width) * 2.4 + pathDistance * 0.8;
                open.Enqueue(nextNode, cost + heuristic);
            }
        }

        if (!found.HasValue)
            return originalCells.ToList();

        return ReconstructChannelPath(found.Value, previous);
    }
    internal double? ChannelStepCost(
        GridPoint current,
        GridPoint next,
        GridPoint target,
        int direction,
        int previousDirection,
        int straightRunLength,
        int diagonalRunDirection,
        int diagonalRunLength,
        double pathDistance,
        int originalLength,
        MapMask mask,
        ElevationMap elevation,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options,
        bool hardLimitMode)
    {
        var width = mask.Width;
        var currentHeight = elevation.GetHydrologyHeight(current);
        var nextHeight = elevation.GetHydrologyHeight(next);
        var uphill = nextHeight - currentHeight;
        var maxBreach = 9.0 + elevation.GetMountainPassPotential(next) * 26.0 + elevation.GetBasinInfluence(next) * 12.0;
        if (uphill > maxBreach)
            return null;

        var terrain = elevation.GetTerrainClass(next);
        var plainness = ChannelPlainness(terrain);
        var slopeFactor = Math.Clamp(1.0 - meanSlope / 34.0, 0.0, 1.0);
        var lateralStrength = Math.Clamp(0.22 + plainness * slopeFactor * 0.78 + elevation.GetFoothillInfluence(next) * 0.18, 0.16, 1.0);
        var targetDistance = Distance(next, target, width);
        var ridgePenalty = Math.Max(0.0, elevation.GetRidgeContinuity(next) - elevation.GetMountainPassPotential(next) * 0.42) * 46.0;
        var usedAttraction = usedChannelCells[next.Y * width + next.X] > 0 ? -Math.Clamp(discharge / 180.0, 0.7, 3.4) * 7.5 : 0.0;
        var curvaturePenalty = ChannelCurvaturePenalty(previousDirection, direction);
        var longStraightPenalty = straightRunLength >= 3
            ? Math.Pow(straightRunLength - 1, 1.75) * (4.8 + lateralStrength * 7.5)
            : 0.0;
        var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
        var diagonalRunPenalty = isDiagonal && diagonalRunLength >= 3
            ? Math.Pow(diagonalRunLength - 1, 1.85) * (6.8 + lateralStrength * 8.5)
            : 0.0;
        if (hardLimitMode && straightRunLength >= 5)
            longStraightPenalty += 95.0 * (straightRunLength - 4);
        if (hardLimitMode && isDiagonal && diagonalRunLength >= 4)
            diagonalRunPenalty += 120.0 * (diagonalRunLength - 3);

        var curlBias = ChannelCurlBias(next, direction, lateralStrength);
        var meanderNoise = Hash01(next.X / 3, next.Y / 3, _seed + 7817) * lateralStrength * options.MeanderStrength * 3.2;
        var targetBias = targetDistance * (3.1 + Math.Clamp(originalLength / 120.0, 0.0, 2.4));
        return 1.0
            + nextHeight * 0.045
            + Math.Max(0.0, uphill) * 2.7
            + elevation.GetRoughness(next) * 8.0
            + ridgePenalty
            - elevation.GetBasinInfluence(next) * 24.0
            - TerrainValleyBias(terrain) * 0.22
            + pathDistance * 4.6
            + targetBias
            + usedAttraction
            + curvaturePenalty
            + longStraightPenalty
            + diagonalRunPenalty
            + curlBias
            + meanderNoise;
    }

    internal static List<GridPoint> ReconstructChannelPath(
        ChannelSearchNode found,
        IReadOnlyDictionary<ChannelSearchNode, ChannelSearchNode> previous)
    {
        var path = new List<GridPoint>();
        var current = found;
        while (true)
        {
            path.Add(current.Cell);
            if (!previous.TryGetValue(current, out var parent))
                break;
            current = parent;
        }

        path.Reverse();
        return path;
    }
    internal static List<GridPoint> SmoothAlternatingZigZags(IReadOnlyList<GridPoint> cells, int width)
    {
        if (cells.Count < 5)
            return cells.ToList();

        var result = cells.ToList();
        var changed = true;
        for (var pass = 0; pass < 3 && changed; pass++)
        {
            changed = false;
            for (var i = 1; i < result.Count - 2; i++)
            {
                var d0 = DirectionIndex(result[i - 1], result[i], width);
                var d1 = DirectionIndex(result[i], result[i + 1], width);
                var d2 = DirectionIndex(result[i + 1], result[i + 2], width);
                if (d0 < 0 || d1 < 0 || d2 < 0 || d0 != d2 || d0 == d1)
                    continue;

                if (IsAdjacent(result[i - 1], result[i + 1], width))
                {
                    result.RemoveAt(i);
                    changed = true;
                    break;
                }

                if (i + 3 < result.Count)
                {
                    var d3 = DirectionIndex(result[i + 2], result[i + 3], width);
                    if (d1 == d3 && IsAdjacent(result[i], result[i + 2], width))
                    {
                        result.RemoveAt(i + 1);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return result;
    }
    internal List<GridPoint> RerouteLongStraightRuns(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds,
        List<GridPoint> cells,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options)
    {
        const int minRun = 6;
        var result = cells.ToList();
        for (var pass = 0; pass < 3; pass++)
        {
            var run = DetectLongStraightRuns(result, minRun)
                .OrderByDescending(r => r.Length)
                .FirstOrDefault();
            if (run == default)
                break;

            var anchorStart = Math.Max(0, run.Start - 1);
            var anchorEnd = Math.Min(result.Count - 1, run.Start + run.Length + 1);
            if (anchorEnd <= anchorStart)
                break;

            var segment = result.Skip(anchorStart).Take(anchorEnd - anchorStart + 1).ToList();
            var rerouted = FindLocalChannelPath(mask, elevation, topology, lakeIds, segment, usedChannelCells, discharge, meanSlope, options, run.Direction);
            if (rerouted.Count <= 2 || rerouted.SequenceEqual(segment))
            {
                if (!TryBreakStraightRunWithKink(result, run, mask, elevation, topology, lakeIds))
                    break;
                continue;
            }

            result.RemoveRange(anchorStart, anchorEnd - anchorStart + 1);
            result.InsertRange(anchorStart, rerouted);
        }

        return result;
    }

    internal bool TryBreakStraightRunWithKink(
        List<GridPoint> cells,
        StraightRun run,
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds)
    {
        if (run.Direction < 0 || run.Direction >= Directions.Length || cells.Count < 3)
            return false;

        var width = mask.Width;
        var move = Directions[run.Direction];
        var firstPivot = Math.Clamp(run.Start + run.Length / 2, 1, cells.Count - 2);
        for (var offset = 0; offset <= Math.Max(1, run.Length / 2); offset++)
        {
            foreach (var pivot in CandidatePivots(firstPivot, offset, run, cells.Count))
            {
                var prev = cells[pivot - 1];
                var next = cells[pivot + 1];
                if (move.Dx == 0 || move.Dy == 0)
                {
                    foreach (var candidate in CardinalKinkCandidates(cells[pivot], move, width, mask.Height))
                    {
                        if (!CanUseKinkCell(candidate, prev, next, cells, pivot, mask, elevation, topology, lakeIds))
                            continue;

                        cells[pivot] = candidate;
                        return true;
                    }
                }
                else
                {
                    foreach (var pair in DiagonalKinkCandidates(prev, move, width, mask.Height))
                    {
                        if (!CanUseKinkCell(pair.First, prev, pair.Second, cells, pivot, mask, elevation, topology, lakeIds) ||
                            !CanUseKinkCell(pair.Second, pair.First, next, cells, pivot, mask, elevation, topology, lakeIds))
                        {
                            continue;
                        }

                        cells.RemoveAt(pivot);
                        cells.Insert(pivot, pair.Second);
                        cells.Insert(pivot, pair.First);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    internal static IEnumerable<int> CandidatePivots(int firstPivot, int offset, StraightRun run, int count)
    {
        var min = Math.Max(1, run.Start + 1);
        var max = Math.Min(count - 2, run.Start + run.Length - 1);
        if (offset == 0)
        {
            if (firstPivot >= min && firstPivot <= max)
                yield return firstPivot;
            yield break;
        }

        var left = firstPivot - offset;
        if (left >= min && left <= max)
            yield return left;
        var right = firstPivot + offset;
        if (right >= min && right <= max)
            yield return right;
    }

    internal static IEnumerable<GridPoint> CardinalKinkCandidates(GridPoint current, (int Dx, int Dy) move, int width, int height)
    {
        if (move.Dx == 0)
        {
            yield return new GridPoint(WrapX(current.X + 1, width), current.Y);
            yield return new GridPoint(WrapX(current.X - 1, width), current.Y);
        }
        else
        {
            if (current.Y + 1 < height)
                yield return new GridPoint(current.X, current.Y + 1);
            if (current.Y - 1 >= 0)
                yield return new GridPoint(current.X, current.Y - 1);
        }
    }

    internal static IEnumerable<(GridPoint First, GridPoint Second)> DiagonalKinkCandidates(GridPoint previous, (int Dx, int Dy) move, int width, int height)
    {
        var a1 = new GridPoint(WrapX(previous.X + move.Dx, width), previous.Y);
        var b1Y = previous.Y + move.Dy;
        if (b1Y >= 0 && b1Y < height)
            yield return (a1, new GridPoint(WrapX(previous.X + move.Dx * 2, width), b1Y));

        var a2Y = previous.Y + move.Dy;
        var b2Y = previous.Y + move.Dy * 2;
        if (a2Y >= 0 && a2Y < height && b2Y >= 0 && b2Y < height)
            yield return (new GridPoint(previous.X, a2Y), new GridPoint(WrapX(previous.X + move.Dx, width), b2Y));
    }

    internal static bool CanUseKinkCell(
        GridPoint candidate,
        GridPoint previous,
        GridPoint next,
        IReadOnlyList<GridPoint> cells,
        int replaceIndex,
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds)
    {
        if (!IsAdjacent(previous, candidate, mask.Width) || !IsAdjacent(candidate, next, mask.Width))
            return false;
        if (!IsRenderableRiverLand(candidate, mask, topology, lakeIds))
            return false;
        for (var i = 0; i < cells.Count; i++)
        {
            if (i == replaceIndex)
                continue;
            if (cells[i] == candidate)
                return false;
        }

        var breach = elevation.GetHydrologyHeight(candidate) - elevation.GetHydrologyHeight(previous);
        var maxBreach = 10.0 + elevation.GetMountainPassPotential(candidate) * 24.0 + elevation.GetBasinInfluence(candidate) * 12.0;
        return breach <= maxBreach;
    }

    internal static bool IsAdjacent(GridPoint a, GridPoint b, int width) =>
        Math.Max(Math.Abs(WrappedDeltaX(b.X - a.X, width)), Math.Abs(b.Y - a.Y)) == 1;
    internal List<GridPoint> FindLocalChannelPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds,
        IReadOnlyList<GridPoint> segment,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options,
        int forbiddenStraightDirection)
    {
        var baseRadius = Math.Clamp(segment.Count / 2 + 2, 3, 8);
        for (var radius = baseRadius; radius <= 10; radius += 2)
        {
            var path = FindLocalChannelPath(mask, elevation, topology, lakeIds, segment, usedChannelCells, discharge, meanSlope, options, forbiddenStraightDirection, radius);
            if (!path.SequenceEqual(segment))
                return path;
        }

        return segment.ToList();
    }

    internal List<GridPoint> FindLocalChannelPath(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        int[] lakeIds,
        IReadOnlyList<GridPoint> segment,
        int[] usedChannelCells,
        double discharge,
        double meanSlope,
        HydrologyGenerationOptions options,
        int forbiddenStraightDirection,
        int radius)
    {
        var width = mask.Width;
        var start = segment[0];
        var target = segment[^1];
        var open = new PriorityQueue<ChannelSearchNode, double>();
        var first = new ChannelSearchNode(start, -1, 0, -1, 0);
        var bestCosts = new Dictionary<ChannelSearchNode, double> { [first] = 0.0 };
        var previous = new Dictionary<ChannelSearchNode, ChannelSearchNode>();
        open.Enqueue(first, Distance(start, target, width));
        ChannelSearchNode? found = null;
        var expansions = 0;
        var maxExpansions = Math.Clamp(segment.Count * radius * 96, 1800, 18000);

        while (open.Count > 0 && expansions++ < maxExpansions)
        {
            var node = open.Dequeue();
            var baseCost = bestCosts[node];
            if (node.Cell == target)
            {
                found = node;
                break;
            }

            for (var direction = 0; direction < Directions.Length; direction++)
            {
                var moved = Move(node.Cell, direction, width, mask.Height);
                if (!moved.HasValue)
                    continue;

                var next = moved.Value;
                if (next != target && !IsRenderableRiverLand(next, mask, topology, lakeIds))
                    continue;

                var pathDistance = DistanceToPath(next, segment, width, radius + 0.75);
                if (pathDistance > radius && next != target)
                    continue;

                var isDiagonal = Directions[direction].Dx != 0 && Directions[direction].Dy != 0;
                var straightRun = direction == node.PreviousDirection ? node.StraightRunLength + 1 : 1;
                var diagonalRun = isDiagonal && direction == node.DiagonalRunDirection ? node.DiagonalRunLength + 1 : isDiagonal ? 1 : 0;
                var nearTarget = Distance(next, target, width) <= 2.01;
                if (!nearTarget && straightRun > 5)
                    continue;
                if (!nearTarget && isDiagonal && diagonalRun > 4)
                    continue;
                if (!nearTarget && direction == forbiddenStraightDirection && straightRun >= 3)
                    continue;
                if (!nearTarget && isDiagonal && direction == forbiddenStraightDirection && diagonalRun >= 3)
                    continue;
                if (!nearTarget && IsBacktrackLikeTurn(node.PreviousDirection, direction))
                    continue;

                var stepCost = ChannelStepCost(
                    node.Cell,
                    next,
                    target,
                    direction,
                    node.PreviousDirection,
                    straightRun,
                    isDiagonal ? direction : -1,
                    diagonalRun,
                    pathDistance,
                    segment.Count,
                    mask,
                    elevation,
                    usedChannelCells,
                    discharge,
                    meanSlope,
                    options,
                    hardLimitMode: true);
                if (!stepCost.HasValue)
                    continue;

                var nextNode = new ChannelSearchNode(next, direction, straightRun, isDiagonal ? direction : -1, diagonalRun);
                var cost = baseCost + stepCost.Value;
                if (bestCosts.TryGetValue(nextNode, out var oldCost) && oldCost <= cost)
                    continue;

                bestCosts[nextNode] = cost;
                previous[nextNode] = node;
                open.Enqueue(nextNode, cost + Distance(next, target, width) * 2.2 + pathDistance * 0.8);
            }
        }

        return found.HasValue ? ReconstructChannelPath(found.Value, previous) : segment.ToList();
    }
    internal static IReadOnlyList<StraightRun> DetectLongStraightRuns(IReadOnlyList<GridPoint> cells, int minRun)
    {
        var runs = new List<StraightRun>();
        if (cells.Count < minRun + 1)
            return runs;

        var runDirection = DirectionIndex(cells[0], cells[1], int.MaxValue / 4);
        var runStart = 0;
        var runLength = 1;
        for (var i = 1; i < cells.Count - 1; i++)
        {
            var direction = DirectionIndex(cells[i], cells[i + 1], int.MaxValue / 4);
            if (direction == runDirection)
            {
                runLength++;
                continue;
            }

            if (runDirection >= 0 && runLength >= minRun)
                runs.Add(new StraightRun(runStart, runLength, runDirection));
            runDirection = direction;
            runStart = i;
            runLength = 1;
        }

        if (runDirection >= 0 && runLength >= minRun)
            runs.Add(new StraightRun(runStart, runLength, runDirection));
        return runs;
    }

    internal static double DistanceToPath(GridPoint point, IReadOnlyList<GridPoint> cells, int width, double maxStopDistance)
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

    internal static double ChannelPlainness(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.AlluvialPlain => 1.0,
        TerrainClassKind.InteriorLowland => 0.95,
        TerrainClassKind.CoastalPlain => 0.9,
        TerrainClassKind.SedimentaryBasin => 0.82,
        TerrainClassKind.DeltaCandidate => 1.0,
        TerrainClassKind.DryBasin => 0.72,
        TerrainClassKind.Highland => 0.36,
        _ => 0.14
    };

    internal static double ChannelCurvaturePenalty(int previousDirection, int direction)
    {
        if (previousDirection < 0)
            return 0.0;

        var delta = Math.Abs(direction - previousDirection);
        delta = Math.Min(delta, Directions.Length - delta);
        return delta switch
        {
            0 => 0.35,
            1 => -0.65,
            2 => 0.45,
            3 => 4.4,
            _ => 8.5
        };
    }

    internal static bool IsBacktrackLikeTurn(int previousDirection, int direction)
    {
        if (previousDirection < 0 || direction < 0)
            return false;

        var delta = Math.Abs(direction - previousDirection);
        return Math.Min(delta, Directions.Length - delta) >= 4;
    }

    internal double ChannelCurlBias(GridPoint point, int direction, double strength)
    {
        if (strength <= 0.001)
            return 0.0;

        var a = Hash01(point.X / 23, point.Y / 23, _seed + 7867);
        var b = Hash01((point.X + 11) / 31, (point.Y - 7) / 31, _seed + 7879);
        var angle = (a * 0.65 + b * 0.35) * Math.PI;
        var curlX = Math.Cos(angle);
        var curlY = Math.Sin(angle);
        var alignment = Directions[direction].Dx * curlX + Directions[direction].Dy * curlY;
        return -alignment * Math.Clamp(strength, 0.0, 1.0) * 4.8;
    }
    internal List<MapPoint> BuildPolyline(ElevationMap elevation, IReadOnlyList<GridPoint> cells, GridPoint? terminal, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        if (cells.Count == 0)
            return [];

        var points = new List<MapPoint>();
        for (var i = 0; i < cells.Count; i++)
        {
            points.Add(ToMapPoint(elevation, cells, i, discharge, meanSlope, options));
            if (i < cells.Count - 1)
            {
                var bend = ToSegmentBendPoint(elevation, cells, i, discharge, meanSlope, options);
                if (bend.HasValue)
                    points.Add(bend.Value);
            }
        }

        if (terminal.HasValue && terminal.Value != cells[^1])
            points.Add(new MapPoint(terminal.Value.X + 0.5, terminal.Value.Y + 0.5));

        return points;
    }

    internal MapPoint? ToSegmentBendPoint(ElevationMap elevation, IReadOnlyList<GridPoint> cells, int index, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        var current = cells[index];
        var next = cells[index + 1];
        var rawDx = next.X - current.X;
        if (Math.Abs(rawDx) > elevation.Width / 2.0)
            return null;

        var dx = rawDx;
        var dy = next.Y - current.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001)
            return null;

        var terrain = elevation.GetTerrainClass(current);
        var plain = terrain is TerrainClassKind.AlluvialPlain or TerrainClassKind.InteriorLowland or TerrainClassKind.CoastalPlain or TerrainClassKind.SedimentaryBasin or TerrainClassKind.DeltaCandidate ? 1.0 :
            terrain is TerrainClassKind.Highland ? 0.45 : 0.28;
        var slopeFactor = Math.Clamp(1.0 - meanSlope / 30.0, 0.18, 1.0);
        var dischargeFactor = Math.Clamp(discharge / 260.0, 0.25, 1.0);
        var cartographicFloor = cells.Count > 24 ? 0.72 : 0.56;
        var strength = Math.Clamp(cartographicFloor + options.MeanderStrength * plain * slopeFactor * dischargeFactor * 0.18, 0.0, 0.86);
        if (strength <= 0.01)
            return null;

        var sideX = -dy / len;
        var sideY = dx / len;
        var wave = index % 2 == 0 ? 1.0 : -1.0;
        var noise = wave * (0.82 + HashUnit(current.X, current.Y, _seed + 7487) * 0.18);
        var t = 0.38;
        return new MapPoint(
            current.X + 0.5 + dx * t + sideX * noise * strength,
            current.Y + 0.5 + dy * t + sideY * noise * strength);
    }

    internal MapPoint ToMapPoint(ElevationMap elevation, IReadOnlyList<GridPoint> cells, int index, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        var cell = cells[index];
        var x = cell.X + 0.5;
        var y = cell.Y + 0.5;
        if (index <= 0 || index >= cells.Count - 1)
            return new MapPoint(x, y);

        var terrain = elevation.GetTerrainClass(cell);
        var plain = terrain is TerrainClassKind.AlluvialPlain or TerrainClassKind.InteriorLowland or TerrainClassKind.CoastalPlain or TerrainClassKind.SedimentaryBasin or TerrainClassKind.DeltaCandidate ? 1.0 : 0.25;
        var slopeFactor = Math.Clamp(1.0 - meanSlope / 18.0, 0, 1);
        var dischargeFactor = Math.Clamp(discharge / 260.0, 0, 1);
        var strength = options.MeanderStrength * plain * slopeFactor * dischargeFactor * 0.58;
        if (strength <= 0.01)
            return new MapPoint(x, y);

        var prev = cells[index - 1];
        var next = cells[index + 1];
        var dx = WrappedDeltaX(next.X - prev.X, elevation.Width);
        var dy = next.Y - prev.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001)
            return new MapPoint(x, y);

        var sideX = -dy / len;
        var sideY = dx / len;
        var phase = HashUnit(cells[0].X, cells[0].Y, _seed + 7439) * Math.PI * 2.0;
        var wave = Math.Sin(index * 0.58 + phase) * 0.72 + Math.Sin(index * 0.23 + phase * 1.7) * 0.28;
        var noise = Math.Clamp(wave + Hash01(cell.X, cell.Y, _seed + 7433) * 0.22, -1.0, 1.0);
        return new MapPoint(x + sideX * noise * strength, y + sideY * noise * strength);
    }

    internal readonly record struct StraightRun(int Start, int Length, int Direction);

    internal readonly record struct ChannelSearchNode(GridPoint Cell, int PreviousDirection, int StraightRunLength, int DiagonalRunDirection, int DiagonalRunLength);
}
