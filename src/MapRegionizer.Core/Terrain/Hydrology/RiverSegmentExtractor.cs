using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverSegmentExtractor
{
    private readonly ChannelPathTracer _channelPathTracer;

    public RiverSegmentExtractor(ChannelPathTracer channelPathTracer)
    {
        _channelPathTracer = channelPathTracer;
    }

    internal static List<RiverSegment> FinalizeVisibleRivers(IReadOnlyList<RiverSegment> rivers, int width, int height)
    {
        if (rivers.Count == 0)
            return [];

        var radius = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) / 82.0), 6, 12);
        var accepted = new List<RiverSegment>();
        foreach (var river in rivers.OrderByDescending(r => r.Discharge).ThenByDescending(r => r.Cells.Count))
        {
            if (ShouldSuppressNearStrongerRiver(river, accepted, width, radius))
                continue;

            accepted.Add(river);
        }

        var byId = accepted.ToDictionary(r => r.Id);
        var parentById = new Dictionary<int, int?>();
        var childrenById = accepted.ToDictionary(r => r.Id, _ => new List<int>());
        foreach (var river in accepted)
        {
            var parent = accepted
                .Where(other => other.Id != river.Id)
                .Where(other => other.Cells.Any(c => Distance(c, river.Mouth, width) <= 1.01))
                .OrderByDescending(other => other.Discharge)
                .FirstOrDefault();
            parentById[river.Id] = parent?.Id;
            if (parent is not null)
                childrenById[parent.Id].Add(river.Id);
        }

        var orderById = new Dictionary<int, int>();
        int RiverOrder(int id)
        {
            if (orderById.TryGetValue(id, out var cached))
                return cached;
            if (!childrenById.TryGetValue(id, out var children) || children.Count == 0)
                return orderById[id] = 1;

            var childOrders = children.Select(RiverOrder).OrderDescending().ToList();
            var max = childOrders[0];
            var order = childOrders.Count(o => o == max) >= 2 ? max + 1 : max;
            return orderById[id] = Math.Clamp(order, 1, 9);
        }

        foreach (var river in accepted)
            RiverOrder(river.Id);

        var maxDischarge = Math.Max(1.0, accepted.Max(r => r.Discharge));
        var maxLength = Math.Max(1, accepted.Max(r => r.Cells.Count));
        var shortLimit = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) / 34.0), 5, 8);
        return accepted
            .Select(r =>
            {
                var order = orderById.GetValueOrDefault(r.Id, 1);
                var rank = Math.Clamp(
                    Math.Sqrt(r.Discharge / maxDischarge) * 0.62 +
                    Math.Sqrt(r.Cells.Count / (double)maxLength) * 0.24 +
                    Math.Clamp(order / 4.0, 0.0, 1.0) * 0.14,
                    0.0,
                    1.0);
                var isMajor = r.Discharge >= 220.0 && r.Cells.Count > shortLimit && !(r.MeanSlope > 16.0 && r.Discharge < 297.0) || order >= 3 || rank >= 0.72;
                return r with
                {
                    Order = order,
                    IsMajor = isMajor,
                    VisibleRank = Math.Round(rank, 4),
                    ParentRiverId = parentById.GetValueOrDefault(r.Id),
                    TributaryIds = childrenById.GetValueOrDefault(r.Id) ?? []
                };
            })
            .OrderByDescending(r => r.Discharge)
            .ThenBy(r => r.Id)
            .ToList();
    }

    internal static bool ShouldSuppressNearStrongerRiver(RiverSegment candidate, IReadOnlyList<RiverSegment> strongerRivers, int width, int radius)
    {
        if (candidate.Cells.Count < 6)
            return false;

        var candidateSide = RiverTargetSide(candidate, width);
        foreach (var stronger in strongerRivers)
        {
            if (candidate.LandComponentId != stronger.LandComponentId)
                continue;
            if (candidate.Discharge > stronger.Discharge * 0.72)
                continue;
            if (candidateSide != RiverTargetSide(stronger, width))
                continue;

            var near = 0;
            foreach (var cell in candidate.Cells)
            {
                if (stronger.Cells.Any(other => Distance(cell, other, width) <= radius))
                    near++;
            }

            if (near / (double)candidate.Cells.Count >= 0.56)
                return true;
        }

        return false;
    }

    internal static MountainSourceSide RiverTargetSide(RiverSegment river, int width)
    {
        var dx = WrappedDeltaX(river.Mouth.X - river.Source.X, width);
        var dy = river.Mouth.Y - river.Source.Y;
        if (Math.Abs(dx) > Math.Abs(dy))
            return dx < 0 ? MountainSourceSide.West : MountainSourceSide.East;
        if (dy != 0)
            return dy < 0 ? MountainSourceSide.North : MountainSourceSide.South;
        return MountainSourceSide.None;
    }
    internal List<RiverSegment> ExtractRivers(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        RiverTopologyGraph riverTopology,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        int[] lakeIds,
        LandComponentMap landComponents,
        HashSet<int> validEndorheicBasins,
        HydrologyGenerationOptions options,
        List<RiverMouth> mouths,
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths,
        IReadOnlyList<LakeOutlet> outlets)
    {
        var width = mask.Width;
        var height = mask.Height;
        var upstream = riverTopology.BuildUpstreamLists();
        var upstreamDepths = riverTopology.BuildLongestUpstreamDepths(upstream);
        var mouthsByTerminal = Enumerable.Range(0, riverTopology.CellsSpan.Length)
            .Where(riverTopology.Contains)
            .Where(i => riverTopology.GetDownstream(i) < 0)
            .OrderByDescending(i => upstreamDepths[i])
            .ThenByDescending(i => accumulation[i])
            .ToList();
        var visited = new bool[riverTopology.CellsSpan.Length];
        var rivers = new List<RiverSegment>();
        var maxRivers = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) * 2.35 *
                                                    Math.Max(0.25, options.MajorRiverCountMultiplier) *
                                                    (1.0 + Math.Max(0.0, options.MajorRiverTributaryMultiplier) * 0.35)), 48, 2400);
        var componentCounts = new Dictionary<int, int>();
        var componentById = landComponents.Components.ToDictionary(c => c.Id);
        var nextRiverId = 1;
        var outletLakeIds = outlets.Where(o => o.HasOutlet).Select(o => o.LakeId.Value).ToHashSet();
        var usedChannelCells = new int[width * height];

        foreach (var (mouthIndex, forcedPath) in forcedLongPaths.OrderByDescending(p => p.Value.Count))
            TryAddRiverFromMouth(mouthIndex, isTributary: false, forcedPath, forceLong: true);

        foreach (var mouthIndex in mouthsByTerminal)
            TryAddRiverFromMouth(mouthIndex, isTributary: false);

        var addedTributary = true;
        while (addedTributary && rivers.Count < maxRivers)
        {
            addedTributary = false;
            var tributaryMouths = Enumerable.Range(0, riverTopology.CellsSpan.Length)
                .Where(i => riverTopology.Contains(i) && !visited[i])
                .Where(i =>
                {
                    var downstream = riverTopology.GetDownstream(i);
                    return downstream >= 0 && visited[downstream];
                })
                .OrderByDescending(i => upstreamDepths[i])
                .ThenByDescending(i => accumulation[i])
                .ToList();

            foreach (var mouthIndex in tributaryMouths)
                addedTributary |= TryAddRiverFromMouth(mouthIndex, isTributary: true);
        }

        var remainingBranches = Enumerable.Range(0, riverTopology.CellsSpan.Length)
            .Where(i => riverTopology.Contains(i) && !visited[i])
            .Where(i => HasImmediateRenderableOutlet(i, riverTopology, flowDirections, lakeIds, mask, topology, visited, width, height) ||
                        upstreamDepths[i] >= 6)
            .OrderByDescending(i => upstreamDepths[i])
            .ThenByDescending(i => accumulation[i])
            .ToList();
        foreach (var mouthIndex in remainingBranches)
        {
            if (rivers.Count >= maxRivers)
                break;
            TryAddRiverFromMouth(mouthIndex, isTributary: true);
        }

        return rivers
            .OrderByDescending(r => r.Discharge)
            .ThenBy(r => r.Id)
            .ToList();

        bool TryAddRiverFromMouth(int mouthIndex, bool isTributary, IReadOnlyList<int>? forcedPath = null, bool forceLong = false)
        {
            if (rivers.Count >= maxRivers || visited[mouthIndex] && !isTributary)
                return false;

            var minSourceAccumulation = Math.Max(3.0, accumulation[mouthIndex] * (isTributary ? 0.022 : 0.008));
            var path = forcedPath is null
                ? BuildMainstemPath(mouthIndex, upstream, upstreamDepths, accumulation, visited, riverTopology, lakeIds, minSourceAccumulation, width, preferDepth: true)
                : forcedPath.Where(i => riverTopology.Contains(i) && (!visited[i] || i == mouthIndex)).ToList();
            if (forcedPath is not null && !IsValidMouthToSourcePath(path, riverTopology))
                path = BuildMainstemPath(mouthIndex, upstream, upstreamDepths, accumulation, visited, riverTopology, lakeIds, minSourceAccumulation, width, preferDepth: true);
            if (path.Count == 0)
                return false;

            path.Reverse();
            var cells = path.Select(i => new GridPoint(i % width, i / width)).ToList();
            var source = cells[0];
            var dryMouthCell = cells[^1];
            var segmentOutletIndex = FindSegmentOutletIndex(mouthIndex, riverTopology, flowDirections, lakeIds, mask, topology, visited, width, height);
            var segmentOutlet = new GridPoint(segmentOutletIndex % width, segmentOutletIndex / width);
            var segmentEndsInWater = segmentOutletIndex != mouthIndex && IsWaterTarget(segmentOutlet, mask, topology, lakeIds);
            var segmentEndsInConfluence = segmentOutletIndex != mouthIndex && !segmentEndsInWater;
            var terminalIndex = FindMouthTargetIndex(mouthIndex, flowDirections, lakeIds, mask, topology, width, height);
            var terminal = new GridPoint(terminalIndex % width, terminalIndex / width);
            var target = ResolveTarget(terminal, mask, topology, waterSurfaces, lakeIds);
            if (target.Kind == DrainageTargetKind.EndorheicDryBasin &&
                !validEndorheicBasins.Contains(basinIds[mouthIndex]) &&
                accumulation[mouthIndex] < 70.0)
                return false;

            var minLength = target.Kind == DrainageTargetKind.EndorheicDryBasin ? 8 : 12;
            if (cells.Count < minLength && target.Kind == DrainageTargetKind.EndorheicDryBasin && !EndsInWater(dryMouthCell, mask, topology, lakeIds))
                return false;
            if (!forceLong && isTributary && cells.Count < 4 && !segmentEndsInWater)
                return false;
            if (!forceLong && cells.Count < 3 && segmentEndsInWater && accumulation[mouthIndex] < 70.0)
                return false;
            if (!forceLong && cells.Count < 3 && !segmentEndsInWater && !segmentEndsInConfluence)
                return false;
            if (!forceLong && cells.Count <= 2 && Distance(source, segmentOutlet, width) > 2.25)
                return false;
            var dynamicShortLimit = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) / 34.0), 5, 8);
            if (!forceLong && cells.Count <= dynamicShortLimit && accumulation[mouthIndex] < 120.0 && !segmentEndsInConfluence)
                return false;

            var componentId = landComponents.ComponentIds[source.Y * width + source.X];
            if (!CanAddRiverForComponent(componentId, componentById, componentCounts, options))
                return false;

            var discharge = accumulation[mouthIndex];
            GridPoint? renderOutlet = segmentOutletIndex == mouthIndex ? null : segmentOutlet;
            var originalMeanSlope = ComputeMeanSlope(elevation, cells, terminal, target.Kind);
            var channelCells = _channelPathTracer.BuildChannelPath(mask, elevation, topology, lakeIds, cells, renderOutlet, usedChannelCells, discharge, originalMeanSlope, options);
            if (!IsValidChannelPath(channelCells, cells))
                channelCells = cells;

            source = channelCells[0];
            dryMouthCell = channelCells[^1];
            var meanSlope = ComputeMeanSlope(elevation, channelCells, terminal, target.Kind);
            var targetIsClosedLake = IsClosedLakeTarget(target, outletLakeIds);
            var kind = ClassifyRiver(elevation, channelCells, target.Kind, targetIsClosedLake, discharge, meanSlope);
            var mouthKind = ClassifyMouth(elevation, terminal, target.Kind, discharge, meanSlope, options);
            var riverMouth = segmentOutlet;
            var river = new RiverSegment(
                nextRiverId++,
                channelCells,
                _channelPathTracer.BuildPolyline(elevation, channelCells, renderOutlet, discharge, meanSlope, options),
                source,
                riverMouth,
                terminal,
                componentId <= 0 ? null : componentId,
                target.Kind,
                target.TargetId,
                Math.Round(discharge, 3),
                Math.Round((double)channelCells.Count, 2),
                Math.Round(meanSlope, 5),
                kind,
                mouthKind);

            foreach (var index in path)
                visited[index] = true;
            if (componentId > 0)
                componentCounts[componentId] = componentCounts.GetValueOrDefault(componentId) + 1;

            MarkAcceptedChannel(channelCells, river.Id, usedChannelCells, width);
            rivers.Add(river);
            mouths.Add(new RiverMouth(river.Id, riverMouth, target.Kind, target.TargetId, mouthKind, river.Discharge));
            return true;
        }
    }

    internal static bool IsValidChannelPath(
        IReadOnlyList<GridPoint> channelCells,
        IReadOnlyList<GridPoint> originalCells)
    {
        if (channelCells.Count < 2 || originalCells.Count < 2)
            return false;
        return channelCells[0] == originalCells[0] && channelCells[^1] == originalCells[^1];
    }

    internal static void MarkAcceptedChannel(
        IReadOnlyList<GridPoint> cells,
        int riverId,
        int[] usedChannelCells,
        int width)
    {
        foreach (var cell in cells)
            usedChannelCells[cell.Y * width + WrapX(cell.X, width)] = riverId;
    }

    internal static bool HasImmediateRenderableOutlet(
        int mouthIndex,
        RiverTopologyGraph riverTopology,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        bool[] visited,
        int width,
        int height)
    {
        var topologyDownstream = riverTopology.GetDownstream(mouthIndex);
        if (topologyDownstream >= 0)
            return visited[topologyDownstream];

        var downstream = DownstreamIndex(mouthIndex, flowDirections[mouthIndex], width, height);
        if (downstream < 0)
            return true;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        return IsWaterTarget(downstreamPoint, mask, topology, lakeIds) || visited[downstream];
    }

    internal static int FindSegmentOutletIndex(
        int mouthIndex,
        RiverTopologyGraph riverTopology,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        bool[] visited,
        int width,
        int height)
    {
        var topologyDownstream = riverTopology.GetDownstream(mouthIndex);
        if (topologyDownstream >= 0 && visited[topologyDownstream])
            return topologyDownstream;

        var downstream = DownstreamIndex(mouthIndex, flowDirections[mouthIndex], width, height);
        if (downstream < 0)
            return mouthIndex;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        if (IsWaterTarget(downstreamPoint, mask, topology, lakeIds) || visited[downstream])
            return downstream;

        return mouthIndex;
    }

    internal static List<int> BuildMainstemPath(
        int mouthIndex,
        List<int>[] upstream,
        int[] upstreamDepths,
        double[] accumulation,
        bool[] visited,
        RiverTopologyGraph riverTopology,
        int[] lakeIds,
        double minSourceAccumulation,
        int width,
        bool preferDepth)
    {
        var path = new List<int>();
        var current = mouthIndex;
        var guard = 0;
        while (current >= 0 && current < upstream.Length && guard++ < upstream.Length)
        {
            if (visited[current] && current != mouthIndex)
                break;

            path.Add(current);
            var next = upstream[current]
                .Where(i => !visited[i])
                .Where(i => riverTopology.Contains(i) && lakeIds[i] <= 0)
                .Where(i => accumulation[i] >= minSourceAccumulation)
                .OrderByDescending(i => preferDepth ? upstreamDepths[i] : accumulation[i])
                .ThenByDescending(i => preferDepth ? accumulation[i] : upstreamDepths[i])
                .ThenBy(i => Math.Abs((i % width) - (current % width)))
                .FirstOrDefault(-1);
            if (next < 0)
                break;

            current = next;
        }

        return path;
    }

    internal static bool IsValidMouthToSourcePath(IReadOnlyList<int> path, RiverTopologyGraph riverTopology)
    {
        if (path.Count == 0)
            return false;

        for (var i = 1; i < path.Count; i++)
        {
            if (riverTopology.GetDownstream(path[i]) != path[i - 1])
                return false;
        }

        return true;
    }

    internal static bool CanAddRiverForComponent(
        int componentId,
        Dictionary<int, LandComponent> componentById,
        Dictionary<int, int> componentCounts,
        HydrologyGenerationOptions options)
    {
        if (componentId <= 0 || !componentById.TryGetValue(componentId, out var component))
            return true;

        var cap = component.CellCount switch
        {
            < 160 => 1,
            < 750 => 6,
            < 2400 => 14,
            < 9000 => 32,
            _ => Math.Clamp(18 + component.CellCount / 1400, 48, 120)
        };
        cap = (int)Math.Round(cap * (1.0 + Math.Max(0.0, options.MajorRiverTributaryMultiplier) * 0.35));

        return componentCounts.GetValueOrDefault(componentId) < cap;
    }

    internal static bool IsClosedLakeTarget(TargetRef target, HashSet<int> outletLakeIds) =>
        target.Kind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
        (!target.TargetId.HasValue || !outletLakeIds.Contains(target.TargetId.Value));

    internal RiverKind ClassifyRiver(ElevationMap elevation, IReadOnlyList<GridPoint> cells, DrainageTargetKind targetKind, bool targetIsClosedLake, double discharge, double meanSlope)
    {
        if (targetKind == DrainageTargetKind.EndorheicDryBasin || targetIsClosedLake)
            return RiverKind.Endorheic;

        var mountain = cells.Count == 0 ? 0 : cells.Average(p =>
            elevation.GetTerrainClass(p) is TerrainClassKind.Mountain or TerrainClassKind.Highland ? 1.0 : 0.0);
        var delta = cells.Count > 0 && elevation.GetTerrainClass(cells[^1]) == TerrainClassKind.DeltaCandidate;
        if (delta && discharge > 150)
            return RiverKind.Deltaic;
        if (mountain > 0.45 || meanSlope > 16.0)
            return RiverKind.Mountain;
        var riftish = cells.Count == 0 ? 0 : cells.Average(p => elevation.GetBasinInfluence(p) * 0.45 + elevation.GetFoothillInfluence(p) * 0.20);
        return riftish > 0.46 && discharge > 80 ? RiverKind.Rift : RiverKind.Plain;
    }

    internal static RiverMouthKind ClassifyMouth(ElevationMap elevation, GridPoint terminal, DrainageTargetKind targetKind, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        if (targetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea or DrainageTargetKind.EndorheicDryBasin)
            return discharge > 110 && meanSlope < 8.0 ? RiverMouthKind.InlandDelta : RiverMouthKind.SimpleMouth;

        var terrain = elevation.GetTerrainClass(Math.Clamp(terminal.X, 0, elevation.Width - 1), Math.Clamp(terminal.Y, 0, elevation.Height - 1));
        var deltaScore = (terrain == TerrainClassKind.DeltaCandidate ? 0.45 : 0.0)
                         + Math.Clamp(discharge / 380.0, 0, 0.55)
                         + (meanSlope < 4.5 ? 0.22 : 0.0)
                         + options.DeltaFrequency * 0.12;
        if (deltaScore >= 0.82)
            return terrain == TerrainClassKind.DeltaCandidate && meanSlope < 2.8 ? RiverMouthKind.MarshDelta : RiverMouthKind.Delta;
        return meanSlope > 8.0 ? RiverMouthKind.Estuary : RiverMouthKind.SimpleMouth;
    }

    internal static double ComputeMeanSlope(ElevationMap elevation, IReadOnlyList<GridPoint> cells, GridPoint terminal, DrainageTargetKind targetKind)
    {
        if (cells.Count < 2)
            return 0;

        var source = elevation.GetHydrologyHeight(cells[0]);
        var mouth = targetKind == DrainageTargetKind.Ocean
            ? 0.0
            : elevation.GetHydrologyHeight(terminal);
        return Math.Max(0.0, source - mouth) / Math.Max(1, cells.Count - 1);
    }

    internal static bool EndsInWater(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        if (lakeIds[index] > 0)
            return true;
        return !mask.IsLand(point) && topology.GetKind(point) is (WaterBodyKind.Ocean or WaterBodyKind.OceanSea);
    }

    internal static TargetRef ResolveTarget(GridPoint terminal, MapMask mask, WaterBodyTopology topology, WaterSurfaceMap waterSurfaces, int[] lakeIds)
    {
        var x = Math.Clamp(terminal.X, 0, mask.Width - 1);
        var y = Math.Clamp(terminal.Y, 0, mask.Height - 1);
        var point = new GridPoint(x, y);
        var lakeId = lakeIds[y * mask.Width + x];
        if (lakeId > 0)
        {
            var body = waterSurfaces.GetBodySurface(new WaterBodyId(lakeId));
            return new TargetRef(body?.Kind == WaterBodyKind.InlandSea ? DrainageTargetKind.InlandSea : DrainageTargetKind.Lake, lakeId);
        }

        if (!mask.IsLand(point) && topology.IsOceanicWater(point))
            return new TargetRef(DrainageTargetKind.Ocean, topology.GetWaterBodyId(point)?.Value);

        return new TargetRef(DrainageTargetKind.EndorheicDryBasin, null);
    }

    internal static int FindMouthTargetIndex(
        int mouthIndex,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var downstream = DownstreamIndex(mouthIndex, flowDirections[mouthIndex], width, height);
        if (downstream < 0)
            return mouthIndex;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        if (IsWaterTarget(downstreamPoint, mask, topology, lakeIds))
            return downstream;

        var current = downstream;
        var guard = 0;
        while (guard++ < flowDirections.Length)
        {
            var point = new GridPoint(current % width, current / width);
            if (IsWaterTarget(point, mask, topology, lakeIds))
                return current;

            downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                return current;
            current = downstream;
        }

        return current;
    }

}
