using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverSourceSelector
{
    private readonly int _seed;

    public RiverSourceSelector(int seed)
    {
        _seed = seed;
    }

    internal byte[] SelectRiverCells(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        GeneratedLakeMap generatedLakes,
        double[] accumulation,
        int[] flowDirections,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        LandComponentMap landComponents,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var riverCells = new byte[width * height];
        if (options.RiverDensity <= 0 || options.TributaryDensity <= 0)
            return riverCells;

        var baseThreshold = Math.Clamp(Math.Sqrt(width * height) * 0.22, 18.0, 96.0) / Math.Max(0.1, options.RiverDensity * options.TributaryDensity);
        var componentThresholds = BuildComponentRiverThresholds(mask, generatedLakes, accumulation, flowDirections, landComponents, baseThreshold);
        var mountainSources = BuildMountainSourceBudget(mask, elevation, generatedLakes, landComponents, options);
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var candidates = new List<RiverSourceCandidate>();
        var bucketSize = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) / 30.0), 14, 44);
        const int desiredVisibleLength = 6;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * width + x;
                if (flowDirections[index] < 0 || !IsRenderableRiverLand(point, mask, topology, lakeIds))
                    continue;
                if (!allowedRiverBasins.Contains(basinIds[index]))
                    continue;

                var terrain = elevation.GetTerrainClass(point);
                var componentId = landComponents.ComponentIds[index];
                var componentThreshold = componentId > 0 && componentThresholds.TryGetValue(componentId, out var localThreshold)
                    ? localThreshold
                    : baseThreshold;
                var threshold = Math.Min(baseThreshold, componentThreshold) * TerrainRiverThresholdMultiplier(terrain);
                var mountainInfo = mountainSources.InfoByCell[index];
                var mountainSide = mountainInfo is null
                    ? MountainSourceSide.None
                    : TraceMountainSourceSide(index, flowDirections, width, height, maxSteps: 18);
                if (mountainInfo is not null)
                    threshold /= Math.Max(0.25, options.MountainRiverDensity);
                if (accumulation[index] < threshold || !IsDistributedHeadwater(index, upstream, accumulation, threshold, basinIds, lakeIds, mask, topology))
                    continue;

                var bucketX = x / bucketSize;
                var bucketY = y / bucketSize;
                var terrainScore = terrain is TerrainClassKind.Mountain or TerrainClassKind.Highland ? 0.86 : 1.0;
                var downstreamLength = CountDownstreamDryLength(index, flowDirections, riverCells, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height, maxLength: 48);
                var lengthFactor = downstreamLength >= desiredVisibleLength
                    ? Math.Clamp(0.92 + downstreamLength / 28.0, 1.0, 2.25)
                    : Math.Clamp(0.24 + downstreamLength / (double)desiredVisibleLength * 0.48, 0.24, 0.72);
                var score = accumulation[index] * terrainScore * lengthFactor * (0.94 + HashUnit(x, y, _seed + 7607) * 0.12);
                candidates.Add(new RiverSourceCandidate(index, componentId, basinIds[index], bucketX, bucketY, downstreamLength, score, mountainInfo?.ClusterId, mountainSide));
            }
        }

        if (candidates.Count == 0)
            return riverCells;

        var selected = SelectDistributedRiverSources(candidates, width, height, flowDirections, lakeIds, mask, topology, options, mountainSources);
        foreach (var candidate in selected)
            MarkRiverCorridor(candidate.Index, flowDirections, riverCells, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height);

        return riverCells;
    }

    internal static bool IsDistributedHeadwater(
        int index,
        List<int>[] upstream,
        double[] accumulation,
        double threshold,
        int[] basinIds,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology)
    {
        var width = mask.Width;
        var basinId = basinIds[index];
        foreach (var upstreamIndex in upstream[index])
        {
            if (lakeIds[upstreamIndex] > 0 || basinIds[upstreamIndex] != basinId)
                continue;

            var point = new GridPoint(upstreamIndex % width, upstreamIndex / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                continue;

            if (accumulation[upstreamIndex] >= threshold * 1.10)
                return false;
        }

        return true;
    }

    internal static MountainSourceBudget BuildMountainSourceBudget(
        MapMask mask,
        ElevationMap elevation,
        GeneratedLakeMap generatedLakes,
        LandComponentMap landComponents,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var infoByCell = new MountainSourceInfo?[width * height];
        var clusterCaps = new Dictionary<int, int>();
        var sideCaps = new Dictionary<MountainSideBudgetKey, int>();
        var visited = new bool[width * height];
        var nextClusterId = 1;
        var spacing = options.MinMountainSourceSpacing > 0
            ? options.MinMountainSourceSpacing
            : Math.Clamp((int)Math.Round(Math.Sqrt(width * height) / 96.0), 6, 14);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = new GridPoint(x, y);
                var startIndex = y * width + x;
                if (visited[startIndex] || !IsMountainSourceLand(start, mask, elevation, generatedLakes))
                    continue;

                var componentId = landComponents.ComponentIds[startIndex];
                var queue = new Queue<GridPoint>();
                var cells = new List<GridPoint>();
                visited[startIndex] = true;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cells.Add(current);
                    foreach (var neighbor in Neighbors8(current, width, height))
                    {
                        var index = neighbor.Y * width + neighbor.X;
                        if (visited[index] || landComponents.ComponentIds[index] != componentId || !IsMountainSourceLand(neighbor, mask, elevation, generatedLakes))
                            continue;

                        visited[index] = true;
                        queue.Enqueue(neighbor);
                    }
                }

                if (cells.Count < 5)
                    continue;

                var clusterId = nextClusterId++;
                var density = Math.Max(0.0, options.RiverDensity) * Math.Max(0.0, options.MountainRiverDensity);
                var cap = 2 + (int)Math.Round(Math.Sqrt(cells.Count) * 0.16 * density);
                cap = Math.Clamp(cap, 1, Math.Max(1, options.MaxMountainSourcesPerCluster > 0 ? options.MaxMountainSourcesPerCluster : 18));
                var sideCap = Math.Clamp((int)Math.Ceiling(cap * 0.42), 1, Math.Max(1, cap));

                clusterCaps[clusterId] = cap;
                foreach (var side in new[] { MountainSourceSide.North, MountainSourceSide.South, MountainSourceSide.West, MountainSourceSide.East })
                    sideCaps[new MountainSideBudgetKey(clusterId, side)] = sideCap;

                foreach (var cell in cells)
                    infoByCell[cell.Y * width + cell.X] = new MountainSourceInfo(clusterId, cells.Count, spacing);
            }
        }

        return new MountainSourceBudget(infoByCell, clusterCaps, sideCaps);
    }

    internal static bool IsMountainSourceLand(GridPoint point, MapMask mask, ElevationMap elevation, GeneratedLakeMap generatedLakes)
    {
        if (!IsRiverSourceLand(mask, generatedLakes, point))
            return false;

        return elevation.GetTerrainClass(point) is TerrainClassKind.Mountain or TerrainClassKind.Highland;
    }

    internal static MountainSourceSide TraceMountainSourceSide(int start, int[] flowDirections, int width, int height, int maxSteps)
    {
        var startX = start % width;
        var startY = start / width;
        var current = start;
        var dx = 0;
        var dy = 0;
        var seen = new HashSet<int>();
        for (var step = 0; step < maxSteps && current >= 0 && current < flowDirections.Length && seen.Add(current); step++)
        {
            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                break;

            var nextX = downstream % width;
            var nextY = downstream / width;
            dx += WrappedDeltaX(nextX - (current % width), width);
            dy += nextY - (current / width);
            current = downstream;
        }

        if (dx == 0 && dy == 0)
        {
            dx = WrappedDeltaX((current % width) - startX, width);
            dy = current / width - startY;
        }

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx < 0 ? MountainSourceSide.West : MountainSourceSide.East;
        if (dy != 0)
            return dy < 0 ? MountainSourceSide.North : MountainSourceSide.South;
        return MountainSourceSide.None;
    }
    internal static IReadOnlyList<RiverSourceCandidate> SelectDistributedRiverSources(
        IReadOnlyList<RiverSourceCandidate> candidates,
        int width,
        int height,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        HydrologyGenerationOptions options,
        MountainSourceBudget mountainSources)
    {
        var areaRoot = Math.Sqrt(width * height);
        var maxSources = Math.Clamp((int)Math.Round(areaRoot * 0.44 * Math.Max(0.2, options.RiverDensity) * Math.Max(0.35, options.TributaryDensity)), 48, 1600);
        var minSpacing = Math.Clamp((int)Math.Round(areaRoot / 72.0 / Math.Max(0.72, Math.Sqrt(Math.Max(0.1, options.RiverDensity)))), 6, 22);
        var selected = new List<RiverSourceCandidate>();
        var reservedCorridors = new bool[width * height];
        var selectedByComponent = new Dictionary<int, int>();
        var selectedByBasin = new Dictionary<int, int>();
        var selectedByBucket = new Dictionary<RiverSourceGroupKey, int>();
        var selectedByMountainCluster = new Dictionary<int, int>();
        var selectedByMountainSide = new Dictionary<MountainSideBudgetKey, int>();
        var groupsByComponent = candidates
            .GroupBy(c => c.ComponentId > 0 ? c.ComponentId : -c.BasinId)
            .OrderByDescending(g => g.Max(c => c.Score))
            .Select(g => new ComponentRiverSourceQueue(
                g.Key,
                g.GroupBy(c => new RiverSourceGroupKey(c.ComponentId, c.BasinId, c.BucketX, c.BucketY))
                    .SelectMany(bucket => bucket
                        .OrderByDescending(c => c.DownstreamDryLength >= 6)
                        .ThenByDescending(c => c.Score)
                        .Take(3))
                    .OrderByDescending(c => c.Score)
                    .ToList()))
            .Where(g => g.Candidates.Count > 0)
            .ToList();
        var componentCap = groupsByComponent.Count <= 1
            ? maxSources
            : Math.Clamp(maxSources / Math.Max(2, groupsByComponent.Count / 4), 14, 48);
        var basinCap = Math.Clamp(maxSources / 4, 14, 52);
        const int bucketCap = 3;

        var progress = true;
        while (selected.Count < maxSources && progress)
        {
            progress = false;
            foreach (var group in groupsByComponent)
            {
                while (group.NextIndex < group.Candidates.Count)
                {
                    var candidate = group.Candidates[group.NextIndex++];
                    var bucketKey = new RiverSourceGroupKey(candidate.ComponentId, candidate.BasinId, candidate.BucketX, candidate.BucketY);
                    if (selectedByComponent.GetValueOrDefault(candidate.ComponentId) >= componentCap ||
                        selectedByBasin.GetValueOrDefault(candidate.BasinId) >= basinCap ||
                        selectedByBucket.GetValueOrDefault(bucketKey) >= bucketCap ||
                        !CanSelectMountainSource(candidate, mountainSources, selectedByMountainCluster, selectedByMountainSide))
                    {
                        continue;
                    }

                    if (IsFarEnoughFromSelected(candidate, selected, width, SourceSpacing(candidate, mountainSources, minSpacing)) &&
                        HasEnoughIndependentDownstreamRun(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height, desiredLength: 4))
                    {
                        selected.Add(candidate);
                        ReserveDownstreamCorridor(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height);
                        selectedByComponent[candidate.ComponentId] = selectedByComponent.GetValueOrDefault(candidate.ComponentId) + 1;
                        selectedByBasin[candidate.BasinId] = selectedByBasin.GetValueOrDefault(candidate.BasinId) + 1;
                        selectedByBucket[bucketKey] = selectedByBucket.GetValueOrDefault(bucketKey) + 1;
                        RegisterMountainSource(candidate, selectedByMountainCluster, selectedByMountainSide);
                        progress = true;
                        break;
                    }
                }

                if (selected.Count >= maxSources)
                    break;
            }
        }

        if (selected.Count == 0)
        {
            foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            {
                if (selected.Count >= maxSources)
                    break;
                if (!CanSelectMountainSource(candidate, mountainSources, selectedByMountainCluster, selectedByMountainSide) ||
                    !IsFarEnoughFromSelected(candidate, selected, width, SourceSpacing(candidate, mountainSources, minSpacing)))
                {
                    continue;
                }

                selected.Add(candidate);
                RegisterMountainSource(candidate, selectedByMountainCluster, selectedByMountainSide);
            }
        }
        else
        {
            AddSparseBucketRiverSources(
                candidates,
                selected,
                reservedCorridors,
                selectedByComponent,
                selectedByBasin,
                selectedByBucket,
                selectedByMountainCluster,
                selectedByMountainSide,
                mountainSources,
                maxSources,
                componentCap,
                basinCap,
                minSpacing,
                flowDirections,
                lakeIds,
                mask,
                topology,
                width,
                height);
        }

        return selected;
    }

    internal static int SourceSpacing(RiverSourceCandidate candidate, MountainSourceBudget mountainSources, int defaultSpacing)
    {
        if (!candidate.MountainClusterId.HasValue)
            return defaultSpacing;

        var info = candidate.Index >= 0 && candidate.Index < mountainSources.InfoByCell.Length
            ? mountainSources.InfoByCell[candidate.Index]
            : null;
        return info is null ? defaultSpacing : Math.Max(defaultSpacing, info.MinSpacing);
    }
    internal static bool CanSelectMountainSource(
        RiverSourceCandidate candidate,
        MountainSourceBudget mountainSources,
        IReadOnlyDictionary<int, int> selectedByCluster,
        IReadOnlyDictionary<MountainSideBudgetKey, int> selectedBySide)
    {
        if (!candidate.MountainClusterId.HasValue)
            return true;

        var clusterId = candidate.MountainClusterId.Value;
        if (!mountainSources.ClusterCaps.TryGetValue(clusterId, out var clusterCap))
            return true;
        if (selectedByCluster.GetValueOrDefault(clusterId) >= clusterCap)
            return false;

        if (candidate.MountainSide == MountainSourceSide.None)
            return true;

        var sideKey = new MountainSideBudgetKey(clusterId, candidate.MountainSide);
        return !mountainSources.SideCaps.TryGetValue(sideKey, out var sideCap) ||
               selectedBySide.GetValueOrDefault(sideKey) < sideCap;
    }

    internal static void RegisterMountainSource(
        RiverSourceCandidate candidate,
        Dictionary<int, int> selectedByCluster,
        Dictionary<MountainSideBudgetKey, int> selectedBySide)
    {
        if (!candidate.MountainClusterId.HasValue)
            return;

        var clusterId = candidate.MountainClusterId.Value;
        selectedByCluster[clusterId] = selectedByCluster.GetValueOrDefault(clusterId) + 1;
        if (candidate.MountainSide != MountainSourceSide.None)
        {
            var sideKey = new MountainSideBudgetKey(clusterId, candidate.MountainSide);
            selectedBySide[sideKey] = selectedBySide.GetValueOrDefault(sideKey) + 1;
        }
    }
    internal static void AddSparseBucketRiverSources(
        IReadOnlyList<RiverSourceCandidate> candidates,
        List<RiverSourceCandidate> selected,
        bool[] reservedCorridors,
        Dictionary<int, int> selectedByComponent,
        Dictionary<int, int> selectedByBasin,
        Dictionary<RiverSourceGroupKey, int> selectedByBucket,
        Dictionary<int, int> selectedByMountainCluster,
        Dictionary<MountainSideBudgetKey, int> selectedByMountainSide,
        MountainSourceBudget mountainSources,
        int maxSources,
        int componentCap,
        int basinCap,
        int minSpacing,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var supplementalBudget = Math.Clamp((int)Math.Round(maxSources * 0.18), 8, 220);
        var supplementalLimit = Math.Min(candidates.Count, maxSources + supplementalBudget);
        var softMinSpacing = Math.Clamp((int)Math.Round(minSpacing * 0.55), 3, minSpacing);
        var softComponentCap = componentCap + Math.Clamp((int)Math.Round(componentCap * 0.45), 4, 28);
        var softBasinCap = basinCap + Math.Clamp((int)Math.Round(basinCap * 0.35), 4, 36);
        var coveredBuckets = selected
            .Select(c => new RiverSourceCoverageKey(CoverageComponentId(c), c.BucketX, c.BucketY))
            .ToHashSet();

        var sparseBucketCandidates = candidates
            .GroupBy(c => new RiverSourceCoverageKey(CoverageComponentId(c), c.BucketX, c.BucketY))
            .Where(g => !coveredBuckets.Contains(g.Key))
            .Select(g => g
                .OrderByDescending(c => c.DownstreamDryLength >= 3)
                .ThenByDescending(c => c.Score)
                .First())
            .OrderBy(c => selectedByComponent.GetValueOrDefault(c.ComponentId))
            .ThenBy(c => selectedByBasin.GetValueOrDefault(c.BasinId))
            .ThenByDescending(c => c.DownstreamDryLength)
            .ThenByDescending(c => c.Score)
            .ToList();

        foreach (var candidate in sparseBucketCandidates)
        {
            if (selected.Count >= supplementalLimit)
                break;

            var bucketKey = new RiverSourceGroupKey(candidate.ComponentId, candidate.BasinId, candidate.BucketX, candidate.BucketY);
            if (selectedByComponent.GetValueOrDefault(candidate.ComponentId) >= softComponentCap ||
                selectedByBasin.GetValueOrDefault(candidate.BasinId) >= softBasinCap ||
                selectedByBucket.GetValueOrDefault(bucketKey) > 0 ||
                !CanSelectMountainSource(candidate, mountainSources, selectedByMountainCluster, selectedByMountainSide))
            {
                continue;
            }

            if (!IsFarEnoughFromSelected(candidate, selected, width, SourceSpacing(candidate, mountainSources, softMinSpacing)) ||
                !HasEnoughIndependentDownstreamRun(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height, desiredLength: 2))
            {
                continue;
            }

            selected.Add(candidate);
            ReserveDownstreamCorridor(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height);
            selectedByComponent[candidate.ComponentId] = selectedByComponent.GetValueOrDefault(candidate.ComponentId) + 1;
            selectedByBasin[candidate.BasinId] = selectedByBasin.GetValueOrDefault(candidate.BasinId) + 1;
            selectedByBucket[bucketKey] = selectedByBucket.GetValueOrDefault(bucketKey) + 1;
            RegisterMountainSource(candidate, selectedByMountainCluster, selectedByMountainSide);
            coveredBuckets.Add(new RiverSourceCoverageKey(CoverageComponentId(candidate), candidate.BucketX, candidate.BucketY));
        }
    }

    internal static int CoverageComponentId(RiverSourceCandidate candidate) =>
        candidate.ComponentId > 0 ? candidate.ComponentId : -candidate.BasinId;

    internal static bool IsFarEnoughFromSelected(RiverSourceCandidate candidate, IReadOnlyList<RiverSourceCandidate> selected, int width, int minSpacing)
    {
        var candidatePoint = new GridPoint(candidate.Index % width, candidate.Index / width);
        foreach (var other in selected)
        {
            if (other.ComponentId != candidate.ComponentId && other.BasinId != candidate.BasinId)
                continue;

            var otherPoint = new GridPoint(other.Index % width, other.Index / width);
            var dx = Math.Abs(WrappedDeltaX(candidatePoint.X - otherPoint.X, width));
            var dy = Math.Abs(candidatePoint.Y - otherPoint.Y);
            if (Math.Sqrt(dx * dx + dy * dy) < minSpacing)
                return false;
        }

        return true;
    }

    internal static bool HasEnoughIndependentDownstreamRun(
        int start,
        bool[] reservedCorridors,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height,
        int desiredLength)
    {
        var current = start;
        var length = 0;
        var seen = new HashSet<int>();
        while (current >= 0 && current < flowDirections.Length && seen.Add(current))
        {
            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                return length >= desiredLength;
            if (reservedCorridors[current])
                return length >= desiredLength;

            length++;
            if (length >= desiredLength)
                return true;

            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                return length >= desiredLength;

            current = downstream;
        }

        return length >= desiredLength;
    }

    internal static void ReserveDownstreamCorridor(
        int start,
        bool[] reservedCorridors,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var current = start;
        var seen = new HashSet<int>();
        while (current >= 0 && current < flowDirections.Length && seen.Add(current))
        {
            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            reservedCorridors[current] = true;
            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                break;

            current = downstream;
        }
    }

    internal static Dictionary<int, double> BuildComponentRiverThresholds(
        MapMask mask,
        GeneratedLakeMap generatedLakes,
        double[] accumulation,
        int[] flowDirections,
        LandComponentMap landComponents,
        double baseThreshold)
    {
        var valuesByComponent = new Dictionary<int, List<double>>();
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                var componentId = landComponents.ComponentIds[index];
                if (componentId <= 0 || flowDirections[index] < 0 || !IsRiverSourceLand(mask, generatedLakes, point))
                    continue;

                if (!valuesByComponent.TryGetValue(componentId, out var values))
                {
                    values = [];
                    valuesByComponent[componentId] = values;
                }

                values.Add(accumulation[index]);
            }
        }

        var thresholds = new Dictionary<int, double>();
        foreach (var component in landComponents.Components)
        {
            if (!valuesByComponent.TryGetValue(component.Id, out var values) || values.Count == 0)
                continue;

            values.Sort();
            var percentile = component.CellCount switch
            {
                < 180 => 0.962,
                < 750 => 0.952,
                < 2400 => 0.938,
                _ => 0.842
            };
            var local = PercentileSorted(values, percentile);
            thresholds[component.Id] = Math.Clamp(local, baseThreshold * 0.18, baseThreshold * 1.22);
        }

        return thresholds;
    }

    internal static void MarkRiverCorridor(
        int start,
        int[] flowDirections,
        byte[] riverCells,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height)
    {
        var current = start;
        var basinId = basinIds[start];
        for (var guard = 0; guard < flowDirections.Length && current >= 0 && current < flowDirections.Length; guard++)
        {
            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            riverCells[current] = 1;
            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
            {
                if (!allowedRiverBasins.Contains(basinIds[current]))
                    riverCells[current] = 0;
                break;
            }

            var downstreamPoint = new GridPoint(downstream % width, downstream / width);
            if (!IsRenderableRiverLand(downstreamPoint, mask, topology, lakeIds))
                break;

            current = downstream;
            if (basinIds[current] != basinId && !allowedRiverBasins.Contains(basinId))
                break;
        }
    }

    internal static int CountDownstreamDryLength(
        int start,
        int[] flowDirections,
        byte[] riverCells,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width,
        int height,
        int maxLength)
    {
        var current = start;
        var basinId = basinIds[start];
        var length = 0;
        var seen = new HashSet<int>();
        while (current >= 0 && current < flowDirections.Length && seen.Add(current) && length < maxLength)
        {
            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            length++;
            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                break;

            var downstreamPoint = new GridPoint(downstream % width, downstream / width);
            if (!IsRenderableRiverLand(downstreamPoint, mask, topology, lakeIds))
                break;
            if (riverCells[downstream] != 0)
                break;
            if (basinIds[downstream] != basinId && !allowedRiverBasins.Contains(basinId))
                break;

            current = downstream;
        }

        return length;
    }

    internal sealed record MountainSourceInfo(int ClusterId, int Area, int MinSpacing);

    internal sealed record MountainSideBudgetKey(int ClusterId, MountainSourceSide Side);

    internal sealed record MountainSourceBudget(
        MountainSourceInfo?[] InfoByCell,
        IReadOnlyDictionary<int, int> ClusterCaps,
        IReadOnlyDictionary<MountainSideBudgetKey, int> SideCaps);

    internal sealed record RiverSourceCandidate(
        int Index,
        int ComponentId,
        int BasinId,
        int BucketX,
        int BucketY,
        int DownstreamDryLength,
        double Score,
        int? MountainClusterId,
        MountainSourceSide MountainSide);

    internal sealed record RiverSourceGroupKey(int ComponentId, int BasinId, int BucketX, int BucketY);

    internal sealed record RiverSourceCoverageKey(int ComponentId, int BucketX, int BucketY);

    internal sealed class ComponentRiverSourceQueue(int id, IReadOnlyList<RiverSourceCandidate> candidates)
    {
        public int Id { get; } = id;
        public IReadOnlyList<RiverSourceCandidate> Candidates { get; } = candidates;
        public int NextIndex { get; set; }
    }

}
