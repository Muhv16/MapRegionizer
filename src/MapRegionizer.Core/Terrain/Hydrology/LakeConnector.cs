using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class LakeConnector
{
    private readonly int _seed;

    public LakeConnector(int seed)
    {
        _seed = seed;
    }

    internal static Dictionary<int, List<GridPoint>> BuildLakeCells(int width, int height, int[] lakeIds, WaterSurfaceMap waterSurfaces)
    {
        var result = new Dictionary<int, List<GridPoint>>();
        var inlandIds = waterSurfaces.Bodies
            .Where(b => b.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
            .Select(b => b.Id.Value)
            .ToHashSet();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var id = lakeIds[y * width + x];
                if (id <= 0 || !inlandIds.Contains(id))
                    continue;

                if (!result.TryGetValue(id, out var cells))
                {
                    cells = [];
                    result[id] = cells;
                }

                cells.Add(new GridPoint(x, y));
            }
        }

        return result;
    }

    internal List<LakeOutlet> BuildLakeOutlets(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        Dictionary<int, List<GridPoint>> lakeCells,
        HydrologyGenerationOptions options)
    {
        var result = new List<LakeOutlet>();
        foreach (var body in waterSurfaces.Bodies.Where(b => b.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea).OrderBy(b => b.Id.Value))
        {
            if (!lakeCells.TryGetValue(body.Id.Value, out var cells) || cells.Count == 0)
                continue;

            var waterSet = cells.ToHashSet();
            var shoreline = FindShoreline(mask.Width, mask.Height, cells, waterSet.Contains);
            var candidates = new List<(GridPoint Cell, GridPoint Downstream, double Score, double Breach)>();
            foreach (var cell in shoreline)
            {
                var (bestDownstream, bestScore) = ScoreLakeOutletDownstream(mask, elevation, body, waterSet, cell);
                var breach = Math.Max(0.0, elevation.GetBedElevation(cell) - body.SpillElevationMeters);
                var shorelineScore = elevation.GetBedElevation(cell) + elevation.GetRidgeContinuity(cell) * 58.0 - elevation.GetBasinInfluence(cell) * 24.0 + bestScore * 0.18;
                candidates.Add((cell, bestDownstream, shorelineScore, breach));
            }

            var best = candidates.OrderBy(c => c.Score).FirstOrDefault();
            var originChance = body.LakeOrigin switch
            {
                LakeOriginKind.Glacial => 0.78,
                LakeOriginKind.Tectonic => 0.70,
                LakeOriginKind.Erosional => 0.58,
                LakeOriginKind.VolcanicKarst => 0.28,
                _ => 0.52
            };
            var sizeBoost = Math.Clamp(Math.Sqrt(body.CellCount) / 90.0, 0, 0.22);
            var kindBoost = body.Kind == WaterBodyKind.InlandSea ? 0.16 : 0.0;
            var strictness = Math.Clamp(options.LakeOutletStrictness, 0, 1);
            var endorheicBias = options.EndorheicBasinChance * (body.LakeLocation == LakeLocationKind.Plain ? 0.42 : 0.22);
            var chance = Math.Clamp(originChance + sizeBoost + kindBoost - strictness * 0.25 - endorheicBias, 0.05, 0.92);
            var hasOutlet = candidates.Count > 0 && HashUnit(body.Id.Value, body.CellCount, _seed + 7193) < chance;
            if (body.LakeOrigin == LakeOriginKind.VolcanicKarst && best.Breach > 8.0 + strictness * 18.0)
                hasOutlet = false;
            if (body.LakeLocation == LakeLocationKind.Plain && best.Cell != default && elevation.GetTerrainClass(best.Cell) == TerrainClassKind.DryBasin)
                hasOutlet = hasOutlet && HashUnit(body.Id.Value, body.CellCount, _seed + 7211) > options.EndorheicBasinChance;

            result.Add(new LakeOutlet(
                body.Id,
                hasOutlet,
                hasOutlet ? best.Cell : null,
                hasOutlet ? best.Downstream : null,
                body.SpillElevationMeters,
                candidates.Count == 0 ? 0 : Math.Round(best.Breach, 3),
                candidates.Count == 0 ? 0 : Math.Round(1.0 / Math.Max(1.0, best.Score - body.SurfaceElevationMeters + 1.0), 6)));
        }

        return result;
    }

    internal bool ForceLakeOutlets(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        Dictionary<int, List<GridPoint>> lakeCells,
        int[] lakeIds,
        int[] flowDirections,
        double[] accumulation,
        List<LakeOutlet> outlets,
        HydrologyGenerationOptions options)
    {
        var changed = false;
        var width = mask.Width;
        var outletByLake = outlets
            .Select((outlet, index) => (outlet, index))
            .ToDictionary(x => x.outlet.LakeId.Value, x => x.index);
        var minimumInflow = Math.Clamp(Math.Sqrt(width * mask.Height) * 0.08, 4.0, 28.0) /
                            Math.Max(0.2, options.RiverDensity * options.TributaryDensity);

        foreach (var body in waterSurfaces.Bodies
                     .Where(b => b.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
                     .Where(b => b.CellCount > 0)
                     .OrderBy(b => b.Id.Value))
        {
            if (!lakeCells.TryGetValue(body.Id.Value, out var cells) || cells.Count == 0)
                continue;

            var inflows = FindLakeInflowCells(mask, topology, lakeIds, flowDirections, accumulation, body.Id.Value, minimumInflow);
            if (inflows.Count == 0)
                continue;
            var hasExistingOutlet = outletByLake.TryGetValue(body.Id.Value, out var existingIndex) && outlets[existingIndex].HasOutlet;
            var isSmallLakeOutletFix = body.CellCount <= 24;
            var isCrowdedShallowLake = ShouldForceCrowdedShallowLakeOutlet(body, inflows.Count, options);
            if (!isSmallLakeOutletFix && !isCrowdedShallowLake)
                continue;

            var requiredOutletSeparation = isCrowdedShallowLake
                ? Math.Max(3, Math.Min(18, (int)Math.Round(Math.Sqrt(body.CellCount) * 0.35)))
                : 2;
            if (hasExistingOutlet &&
                outlets[existingIndex].OutletCell is { } existingOutlet &&
                inflows.All(inflow => ChebyshevDistance(existingOutlet, inflow) >= requiredOutletSeparation))
            {
                continue;
            }

            var waterSet = cells.ToHashSet();
            var inflowSet = inflows.ToHashSet();
            var shoreline = FindShoreline(width, mask.Height, cells, waterSet.Contains)
                .Where(cell => !inflowSet.Contains(cell))
                .ToList();
            var separated = shoreline
                .Where(cell => inflows.All(inflow => ChebyshevDistance(cell, inflow) >= 2))
                .ToList();
            if (separated.Count > 0)
                shoreline = separated;
            if (shoreline.Count == 0)
                continue;

            var best = shoreline
                .Select(cell =>
                {
                    var (downstream, downstreamScore) = ScoreLakeOutletDownstream(mask, elevation, body, waterSet, cell);
                    var breach = Math.Max(0.0, elevation.GetBedElevation(cell) - body.SpillElevationMeters);
                    var separationBonus = Math.Min(8.0, inflows.Min(inflow => ChebyshevDistance(cell, inflow))) * 5.0;
                    if (isCrowdedShallowLake)
                        separationBonus += Math.Min(24.0, inflows.Average(inflow => Distance(cell, inflow, width))) * 3.5;
                    var score = elevation.GetBedElevation(cell)
                                + elevation.GetRidgeContinuity(cell) * 58.0
                                - elevation.GetBasinInfluence(cell) * 24.0
                                + downstreamScore * 0.18
                                - separationBonus;
                    return (Cell: cell, Downstream: downstream, Score: score, Breach: breach);
                })
                .OrderBy(c => c.Score)
                .First();

            var forced = new LakeOutlet(
                body.Id,
                true,
                best.Cell,
                best.Downstream,
                body.SpillElevationMeters,
                Math.Round(best.Breach, 3),
                Math.Round(1.0 / Math.Max(1.0, best.Score - body.SurfaceElevationMeters + 1.0), 6));

            if (outletByLake.TryGetValue(body.Id.Value, out existingIndex))
                outlets[existingIndex] = forced;
            else
                outlets.Add(forced);
            changed = true;
        }

        return changed;
    }

    internal static bool ShouldForceCrowdedShallowLakeOutlet(
        WaterBodySurface body,
        int inflowCount,
        HydrologyGenerationOptions options)
    {
        if (body.Kind != WaterBodyKind.InlandLake || body.CellCount <= 24)
            return false;

        var shallowDepthLimit = Math.Clamp(8.0 + Math.Sqrt(body.CellCount) * 0.12, 10.0, 24.0);
        if (body.MaxDepthMeters > shallowDepthLimit)
            return false;

        var forceMultiplier = Math.Max(0.05, options.LakeOutletInflowForceMultiplier);
        var inflowThreshold = Math.Clamp((int)Math.Round((2.0 + Math.Sqrt(body.CellCount) / 18.0) * forceMultiplier), 3, 28);
        return inflowCount >= inflowThreshold;
    }

    internal List<GridPoint> FindLakeInflowCells(
        MapMask mask,
        WaterBodyTopology topology,
        int[] lakeIds,
        int[] flowDirections,
        double[] accumulation,
        int lakeId,
        double minimumInflow)
    {
        var inflows = new List<GridPoint>();
        for (var index = 0; index < flowDirections.Length; index++)
        {
            if (lakeIds[index] > 0 || accumulation[index] < minimumInflow)
                continue;
            var point = new GridPoint(index % mask.Width, index / mask.Width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                continue;
            var downstream = DownstreamIndex(index, flowDirections[index], mask.Width, mask.Height);
            if (downstream >= 0 && lakeIds[downstream] == lakeId)
                inflows.Add(point);
        }

        return inflows;
    }

    internal (GridPoint Downstream, double Score) ScoreLakeOutletDownstream(
        MapMask mask,
        ElevationMap elevation,
        WaterBodySurface body,
        HashSet<GridPoint> waterSet,
        GridPoint cell)
    {
        var bestDownstream = cell;
        var bestScore = double.PositiveInfinity;
        foreach (var neighbor in Neighbors8(cell, mask.Width, mask.Height))
        {
            if (waterSet.Contains(neighbor))
                continue;

            var terrain = elevation.GetTerrainClass(neighbor);
            var hydro = elevation.HasWaterSurface(neighbor) ? elevation.GetWaterSurface(neighbor) : elevation.GetBedElevation(neighbor);
            var ridgePenalty = elevation.GetRidgeContinuity(neighbor) * 52.0;
            var roughnessPenalty = elevation.GetRoughness(neighbor) * 20.0;
            var passBias = elevation.GetMountainPassPotential(neighbor) * 42.0;
            var basinBias = elevation.GetBasinInfluence(neighbor) * 28.0 + TerrainValleyBias(terrain);
            var downhillBias = Math.Max(0.0, body.SurfaceElevationMeters - hydro) * 0.45;
            var noise = HashUnit(cell.X, cell.Y, body.Id.Value + _seed + 7103) * 4.0;
            var score = hydro + ridgePenalty + roughnessPenalty - passBias - basinBias - downhillBias + noise;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestDownstream = neighbor;
        }

        return (bestDownstream, bestScore);
    }

    internal static int[] BuildLakeRouting(int width, int height, Dictionary<int, List<GridPoint>> lakeCells, IReadOnlyList<LakeOutlet> outlets)
    {
        var lakeNext = Enumerable.Repeat(-1, width * height).ToArray();
        var byLake = outlets.Where(o => o.HasOutlet && o.OutletCell.HasValue).ToDictionary(o => o.LakeId.Value);
        foreach (var (lakeId, cells) in lakeCells)
        {
            if (!byLake.TryGetValue(lakeId, out var outlet))
                continue;

            var waterSet = cells.ToHashSet();
            var outletCell = outlet.OutletCell!.Value;
            var distances = cells.ToDictionary(c => c, _ => int.MaxValue);
            var queue = new Queue<GridPoint>();
            foreach (var cell in cells)
            {
                if (!Neighbors8(cell, width, height).Contains(outletCell))
                    continue;

                distances[cell] = 0;
                lakeNext[cell.Y * width + cell.X] = DirectionIndex(cell, outletCell, width);
                queue.Enqueue(cell);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = distances[current];
                foreach (var neighbor in Neighbors8(current, width, height))
                {
                    if (!waterSet.Contains(neighbor) || distances[neighbor] <= currentDistance + 1)
                        continue;

                    distances[neighbor] = currentDistance + 1;
                    lakeNext[neighbor.Y * width + neighbor.X] = DirectionIndex(neighbor, current, width);
                    queue.Enqueue(neighbor);
                }
            }
        }

        return lakeNext;
    }

}
