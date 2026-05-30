using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class BasinDelineator
{
    internal HydrologyBasinState Build(HydrologyGenerationContext context, int[] flowDirections, double[] accumulation, int[] lakeIds)
    {
        var (basinIds, basins) = BuildBasins(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, flowDirections, accumulation, lakeIds);
        return new HydrologyBasinState(basinIds, basins);
    }

    internal (int[] BasinIds, IReadOnlyList<DrainageBasin> Basins) BuildBasins(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        int[] flowDirections,
        double[] accumulation,
        int[] lakeIds)
    {
        var width = mask.Width;
        var height = mask.Height;
        var basinIds = new int[width * height];
        var terminals = new Dictionary<TerminalKey, int>();
        var basinStats = new Dictionary<int, MutableBasin>();
        var nextId = 1;

        // Memoized terminal lookup with path compression.
        // Each cell's terminal is found once, then cached for all subsequent lookups.
        var terminalCache = new int[basinIds.Length];
        Array.Fill(terminalCache, -1);

        for (var index = 0; index < basinIds.Length; index++)
        {
            var terminal = terminalCache[index];
            if (terminal < 0)
            {
                var path = new List<int>();
                var current = index;
                var guard = 0;
                while (current >= 0 && terminalCache[current] < 0 && guard++ < basinIds.Length)
                {
                    path.Add(current);
                    var ds = DownstreamIndex(current, flowDirections[current], width, height);
                    if (ds < 0)
                    {
                        terminal = current;
                        break;
                    }
                    current = ds;
                }

                if (terminal < 0)
                    terminal = current >= 0 ? terminalCache[current] : path[^1];

                foreach (var cell in path)
                    terminalCache[cell] = terminal;
            }

            var terminalPoint = new GridPoint(terminal % width, terminal / width);
            var lakeId = lakeIds[terminal];
            var targetKind = DrainageTargetKind.EndorheicDryBasin;
            int? targetId = null;
            if (!mask.IsLand(terminalPoint) && topology.IsOceanicWater(terminalPoint))
            {
                targetKind = DrainageTargetKind.Ocean;
                targetId = topology.GetWaterBodyId(terminalPoint)?.Value;
            }
            else if (lakeId > 0)
            {
                var body = waterSurfaces.GetBodySurface(new WaterBodyId(lakeId));
                targetKind = body?.Kind == WaterBodyKind.InlandSea ? DrainageTargetKind.InlandSea : DrainageTargetKind.Lake;
                targetId = lakeId;
            }

            var terminalKeyIndex = targetKind == DrainageTargetKind.EndorheicDryBasin ? terminal : -1;
            var key = new TerminalKey(targetKind, targetId, terminalKeyIndex);
            if (!terminals.TryGetValue(key, out var basinId))
            {
                basinId = nextId++;
                terminals[key] = basinId;
                basinStats[basinId] = new MutableBasin(basinId, targetKind, targetId, terminalPoint);
            }

            basinIds[index] = basinId;
            basinStats[basinId].CellCount++;
            basinStats[basinId].TotalRunoff += accumulation[index];
        }

        var resultBasins = new List<DrainageBasin>(basinStats.Count);
        foreach (var b in basinStats.Values)
            resultBasins.Add(new DrainageBasin(b.Id, b.TargetKind, b.TargetId, b.TerminalCell, b.CellCount, Math.Round(b.TotalRunoff, 3)));
        resultBasins.Sort((a, b) => a.Id.CompareTo(b.Id));
        return (basinIds, resultBasins);
    }

    internal static Dictionary<int, EndorheicRiverPolicy> BuildEndorheicRiverPolicies(IReadOnlyList<DrainageBasin> basins, ElevationMap elevation)
    {
        var endorheic = basins
            .Where(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin)
            .ToList();
        var policies = new Dictionary<int, EndorheicRiverPolicy>();
        if (endorheic.Count == 0)
            return policies;

        var cellCounts = endorheic.Select(b => (double)b.CellCount).OrderBy(v => v).ToList();
        var runoffValues = endorheic.Select(b => b.TotalRunoff).OrderBy(v => v).ToList();
        var forceCellFloor = Math.Max(140.0, PercentileSorted(cellCounts, 0.72));
        var forceRunoffFloor = Math.Max(220.0, PercentileSorted(runoffValues, 0.72));

        foreach (var basin in endorheic)
        {
            var terrain = elevation.GetTerrainClass(basin.TerminalCell);
            var basinInfluence = elevation.GetBasinInfluence(basin.TerminalCell);
            var plausibleDryTerminal =
                terrain == TerrainClassKind.DryBasin && basinInfluence >= 0.32 ||
                terrain == TerrainClassKind.SedimentaryBasin && basinInfluence >= 0.52 ||
                terrain == TerrainClassKind.InteriorLowland && basinInfluence >= 0.58;

            var strongDryTerminal =
                terrain == TerrainClassKind.DryBasin && basinInfluence >= 0.40 ||
                terrain == TerrainClassKind.SedimentaryBasin && basinInfluence >= 0.60 ||
                terrain == TerrainClassKind.InteriorLowland && basinInfluence >= 0.66;
            var largeEnough = basin.CellCount >= forceCellFloor || basin.TotalRunoff >= forceRunoffFloor;
            var veryLarge = basin.CellCount >= 200 || basin.TotalRunoff >= 900;
            var visibleRiver = basin.CellCount >= 80 || basin.TotalRunoff >= 180.0;

            if (!plausibleDryTerminal && !visibleRiver)
            {
                policies[basin.Id] = EndorheicRiverPolicy.Suppress;
                continue;
            }

            if (veryLarge || largeEnough && !strongDryTerminal)
            {
                policies[basin.Id] = EndorheicRiverPolicy.ForceOverflow;
                continue;
            }

            if (strongDryTerminal && visibleRiver)
            {
                policies[basin.Id] = EndorheicRiverPolicy.ForceOverflow;
                continue;
            }

            policies[basin.Id] = basin.CellCount >= 16 || basin.TotalRunoff >= 32.0
                ? EndorheicRiverPolicy.EphemeralSmall
                : EndorheicRiverPolicy.Suppress;
        }

        return policies;
    }

    internal static HashSet<int> BuildValidEndorheicBasinSet(
        IReadOnlyList<DrainageBasin> basins,
        IReadOnlyDictionary<int, EndorheicRiverPolicy> endorheicPolicies)
    {
        return basins
            .Where(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin)
            .Where(b => endorheicPolicies.TryGetValue(b.Id, out var policy) &&
                        policy != EndorheicRiverPolicy.Suppress)
            .Select(b => b.Id)
            .ToHashSet();
    }

    internal static HashSet<int> BuildAllowedRiverBasinSet(IReadOnlyList<DrainageBasin> basins, HashSet<int> validEndorheicBasins)
    {
        return basins
            .Where(b =>
            b.TargetKind != DrainageTargetKind.EndorheicDryBasin ||
            validEndorheicBasins.Contains(b.Id) ||
            b.CellCount >= 80 ||
            b.TotalRunoff >= 250.0)
        .Select(b => b.Id)
        .ToHashSet();
    }

    internal static int FindTerminal(int start, int[] flowDirections, int width, int height)
    {
        var current = start;
        var guard = 0;
        while (guard++ < flowDirections.Length)
        {
            var downstream = DownstreamIndex(current, flowDirections[current], width, height);
            if (downstream < 0)
                return current;
            current = downstream;
        }

        return current;
    }

    internal sealed record TerminalKey(DrainageTargetKind Kind, int? TargetId, int TerminalIndex);

    internal sealed class MutableBasin(int id, DrainageTargetKind targetKind, int? targetId, GridPoint terminalCell)
    {
        public int Id { get; } = id;
        public DrainageTargetKind TargetKind { get; } = targetKind;
        public int? TargetId { get; } = targetId;
        public GridPoint TerminalCell { get; } = terminalCell;
        public int CellCount { get; set; }
        public double TotalRunoff { get; set; }
    }
}
