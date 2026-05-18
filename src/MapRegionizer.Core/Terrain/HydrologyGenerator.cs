using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed class HydrologyGenerator
{
    private static readonly (int Dx, int Dy)[] Directions =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1)
    ];

    private enum EndorheicRiverPolicy
    {
        Suppress,
        EphemeralSmall,
        ForceOverflow
    }

    private readonly int _seed;

    public HydrologyGenerator(int seed)
    {
        _seed = seed;
    }

    public HydrologyMap Generate(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology waterBodyTopology,
        GeneratedLakeMap generatedLakes,
        WaterSurfaceMap waterSurfaces,
        HydrologyGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var length = width * height;
        var hydroSurface = BuildHydroSurface(mask, elevation, waterBodyTopology, generatedLakes);
        var lakeIds = BuildLakeIdRaster(mask, waterBodyTopology, generatedLakes);
        var landComponents = BuildLandComponents(mask, generatedLakes);
        var lakeCells = BuildLakeCells(width, height, lakeIds, waterSurfaces);
        var outlets = BuildLakeOutlets(mask, elevation, waterBodyTopology, waterSurfaces, lakeCells, options);
        var lakeNext = BuildLakeRouting(width, height, lakeCells, outlets);
        var flowDirections = BuildFlowDirections(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, lakeNext, outlets, options);
        ResolveInvalidDryTerminals(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, flowDirections, options);
        BreakCycles(flowDirections, width, height);
        ResolveInvalidDryTerminals(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, flowDirections, options);
        BreakCycles(flowDirections, width, height);
        var localRunoff = BuildLocalRunoff(mask, elevation, waterBodyTopology, generatedLakes);
        var accumulation = AccumulateFlow(flowDirections, localRunoff, width, height);
        if (ForceLakeOutlets(mask, elevation, waterBodyTopology, waterSurfaces, lakeCells, lakeIds, flowDirections, accumulation, outlets, options))
        {
            lakeNext = BuildLakeRouting(width, height, lakeCells, outlets);
            flowDirections = BuildFlowDirections(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, lakeNext, outlets, options);
            ResolveInvalidDryTerminals(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, flowDirections, options);
            BreakCycles(flowDirections, width, height);
            ResolveInvalidDryTerminals(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, flowDirections, options);
            BreakCycles(flowDirections, width, height);
            accumulation = AccumulateFlow(flowDirections, localRunoff, width, height);
        }
        var (basinIds, basins) = BuildBasins(mask, elevation, waterBodyTopology, waterSurfaces, flowDirections, accumulation, lakeIds);
        var endorheicPolicies = BuildEndorheicRiverPolicies(basins, elevation);

        for (var overflowPass = 0; overflowPass < 8; overflowPass++)
        {
            var changed = ForceEndorheicOverflow(
                mask,
                elevation,
                waterBodyTopology,
                hydroSurface,
                lakeIds,
                flowDirections,
                accumulation,
                basinIds,
                basins,
                endorheicPolicies,
                outlets);

            if (!changed)
                break;

            BreakCycles(flowDirections, width, height);
            ResolveInvalidDryTerminals(mask, elevation, waterBodyTopology, generatedLakes, hydroSurface, lakeIds, flowDirections, options);
            BreakCycles(flowDirections, width, height);

            accumulation = AccumulateFlow(flowDirections, localRunoff, width, height);
            (basinIds, basins) = BuildBasins(mask, elevation, waterBodyTopology, waterSurfaces, flowDirections, accumulation, lakeIds);
            endorheicPolicies = BuildEndorheicRiverPolicies(basins, elevation);
        }
        var validEndorheicBasins = BuildValidEndorheicBasinSet(basins, endorheicPolicies);
        var allowedRiverBasins = BuildAllowedRiverBasinSet(basins, validEndorheicBasins);
        var riverCells = SelectRiverCells(mask, elevation, waterBodyTopology, generatedLakes, accumulation, flowDirections, basinIds, allowedRiverBasins, lakeIds, landComponents, options);
        EnsureInlandSeaInflowRiverCells(mask, waterBodyTopology, waterSurfaces, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells);
        var forcedLongPaths = BuildForcedLongRiverPaths(mask, waterBodyTopology, flowDirections, accumulation, basinIds, basins, allowedRiverBasins, lakeIds, riverCells, options);
        AddMajorRiverTributaryCells(mask, waterBodyTopology, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells, forcedLongPaths, options);
        var mouths = new List<RiverMouth>();
        var rivers = ExtractRivers(mask, elevation, waterBodyTopology, waterSurfaces, flowDirections, accumulation, basinIds, riverCells, lakeIds, landComponents, validEndorheicBasins, options, mouths, forcedLongPaths, outlets);

        return new HydrologyMap(
            width,
            height,
            hydroSurface,
            flowDirections,
            accumulation,
            basinIds,
            riverCells,
            rivers,
            mouths,
            outlets,
            basins);
    }

    private static double[] BuildHydroSurface(MapMask mask, ElevationMap elevation, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
    {
        var hydro = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (mask.IsLand(point) && !generatedLakes.Contains(point))
                {
                    hydro[index] = elevation.GetBedElevation(point);
                    continue;
                }

                if (!mask.IsLand(point) && topology.IsOceanicWater(point))
                {
                    hydro[index] = 0.0;
                    continue;
                }

                hydro[index] = elevation.HasWaterSurface(point)
                    ? elevation.GetWaterSurface(point)
                    : Math.Max(0.0, elevation.GetElevation(point));
            }
        }

        return hydro;
    }

    private static int[] BuildLakeIdRaster(MapMask mask, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
    {
        var lakeIds = new int[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (generatedLakes.GetLakeId(point) is { } generatedId)
                {
                    lakeIds[index] = generatedId.Value;
                    continue;
                }

                var id = topology.GetWaterBodyId(point);
                if (!id.HasValue)
                    continue;

                var kind = topology.GetKind(point);
                if (kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
                    lakeIds[index] = id.Value.Value;
            }
        }

        return lakeIds;
    }

    private static LandComponentMap BuildLandComponents(MapMask mask, GeneratedLakeMap generatedLakes)
    {
        var width = mask.Width;
        var height = mask.Height;
        var componentIds = new int[width * height];
        var components = new List<LandComponent>();
        var nextId = 1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = new GridPoint(x, y);
                var startIndex = y * width + x;
                if (!IsRiverSourceLand(mask, generatedLakes, start) || componentIds[startIndex] != 0)
                    continue;

                var id = nextId++;
                var queue = new Queue<GridPoint>();
                var cells = new List<GridPoint>();
                componentIds[startIndex] = id;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cells.Add(current);
                    foreach (var neighbor in Neighbors8(current, width, height))
                    {
                        var index = neighbor.Y * width + neighbor.X;
                        if (componentIds[index] != 0 || !IsRiverSourceLand(mask, generatedLakes, neighbor))
                            continue;

                        componentIds[index] = id;
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(new LandComponent(id, cells.Count));
            }
        }

        return new LandComponentMap(componentIds, components);
    }

    private static bool IsRiverSourceLand(MapMask mask, GeneratedLakeMap generatedLakes, GridPoint point) =>
        mask.IsLand(point) && !generatedLakes.Contains(point);

    private static Dictionary<int, List<GridPoint>> BuildLakeCells(int width, int height, int[] lakeIds, WaterSurfaceMap waterSurfaces)
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

    private List<LakeOutlet> BuildLakeOutlets(
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

    private bool ForceLakeOutlets(
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

    private static bool ShouldForceCrowdedShallowLakeOutlet(
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

    private List<GridPoint> FindLakeInflowCells(
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

    private (GridPoint Downstream, double Score) ScoreLakeOutletDownstream(
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

    private static int[] BuildLakeRouting(int width, int height, Dictionary<int, List<GridPoint>> lakeCells, IReadOnlyList<LakeOutlet> outlets)
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

    private int[] BuildFlowDirections(
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

    private int ChooseDownstreamDirection(
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

    private static double[] BuildLocalRunoff(MapMask mask, ElevationMap elevation, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
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

    private static double[] AccumulateFlow(int[] flowDirections, double[] localRunoff, int width, int height)
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

    private void ResolveInvalidDryTerminals(
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

    private int ChooseSpillDirection(
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

    private static bool IsValidEndorheicTerminal(ElevationMap elevation, GridPoint point, HydrologyGenerationOptions options)
    {
        var terrain = elevation.GetTerrainClass(point);
        var basin = elevation.GetBasinInfluence(point);
        if (terrain == TerrainClassKind.DryBasin && basin >= 0.36)
            return true;
        if (terrain == TerrainClassKind.SedimentaryBasin && basin >= 0.56 && options.EndorheicBasinChance >= 0.14)
            return true;

        return false;
    }

    private static void BreakCycles(int[] flowDirections, int width, int height)
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

    private (int[] BasinIds, IReadOnlyList<DrainageBasin> Basins) BuildBasins(
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

        for (var index = 0; index < basinIds.Length; index++)
        {
            var terminal = FindTerminal(index, flowDirections, width, height);
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

        return (basinIds, basinStats.Values
            .OrderBy(b => b.Id)
            .Select(b => new DrainageBasin(b.Id, b.TargetKind, b.TargetId, b.TerminalCell, b.CellCount, Math.Round(b.TotalRunoff, 3)))
            .ToList());
    }

    private static Dictionary<int, EndorheicRiverPolicy> BuildEndorheicRiverPolicies(IReadOnlyList<DrainageBasin> basins, ElevationMap elevation)
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

    private static HashSet<int> BuildValidEndorheicBasinSet(
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

    private bool ForceEndorheicOverflow(
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
        IReadOnlyList<LakeOutlet> outlets)
    {
        var width = mask.Width;
        var outletLakeIds = outlets
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

    private List<int> FindEndorheicOverflowPath(
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

        var costs = Enumerable.Repeat(double.PositiveInfinity, width * height).ToArray();
        var steps = Enumerable.Repeat(int.MaxValue, width * height).ToArray();
        var previous = Enumerable.Repeat(-1, width * height).ToArray();

        // Новое: максимальная высота “перелива” на пути до клетки.
        var spillHeights = Enumerable.Repeat(double.PositiveInfinity, width * height).ToArray();

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

    private double ScoreOverflowStepBySpill(
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

        // Главное отличие: платим только за новый подъём spill-height.
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

    private static bool IsOverflowTarget(
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

    private static bool CanTraverseOverflowCell(
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

    private double ScoreOverflowStep(
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

    private static List<int> ReconstructPath(int terminal, int[] previous)
    {
        var path = new List<int>();
        var current = terminal;
        while (current >= 0)
        {
            path.Add(current);
            current = previous[current];
        }

        path.Reverse();
        return path;
    }

    private static HashSet<int> BuildAllowedRiverBasinSet(IReadOnlyList<DrainageBasin> basins, HashSet<int> validEndorheicBasins)
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

    private byte[] SelectRiverCells(
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
                if (accumulation[index] < threshold || !IsDistributedHeadwater(index, upstream, accumulation, threshold, basinIds, lakeIds, mask, topology))
                    continue;

                var bucketX = x / bucketSize;
                var bucketY = y / bucketSize;
                var terrainScore = terrain is TerrainClassKind.Mountain or TerrainClassKind.Highland ? 0.92 : 1.0;
                var downstreamLength = CountDownstreamDryLength(index, flowDirections, riverCells, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height, maxLength: 48);
                var lengthFactor = downstreamLength >= desiredVisibleLength
                    ? Math.Clamp(0.92 + downstreamLength / 28.0, 1.0, 2.25)
                    : Math.Clamp(0.24 + downstreamLength / (double)desiredVisibleLength * 0.48, 0.24, 0.72);
                var score = accumulation[index] * terrainScore * lengthFactor * (0.94 + HashUnit(x, y, _seed + 7607) * 0.12);
                candidates.Add(new RiverSourceCandidate(index, componentId, basinIds[index], bucketX, bucketY, downstreamLength, score));
            }
        }

        if (candidates.Count == 0)
            return riverCells;

        var selected = SelectDistributedRiverSources(candidates, width, height, flowDirections, lakeIds, mask, topology, options);
        foreach (var candidate in selected)
            MarkRiverCorridor(candidate.Index, flowDirections, riverCells, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height);

        return riverCells;
    }

    private static bool IsDistributedHeadwater(
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

    private static IReadOnlyList<RiverSourceCandidate> SelectDistributedRiverSources(
        IReadOnlyList<RiverSourceCandidate> candidates,
        int width,
        int height,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        HydrologyGenerationOptions options)
    {
        var areaRoot = Math.Sqrt(width * height);
        var maxSources = Math.Clamp((int)Math.Round(areaRoot * 0.44 * Math.Max(0.2, options.RiverDensity) * Math.Max(0.35, options.TributaryDensity)), 48, 1600);
        var minSpacing = Math.Clamp((int)Math.Round(areaRoot / 72.0 / Math.Max(0.72, Math.Sqrt(Math.Max(0.1, options.RiverDensity)))), 6, 22);
        var selected = new List<RiverSourceCandidate>();
        var reservedCorridors = new bool[width * height];
        var selectedByComponent = new Dictionary<int, int>();
        var selectedByBasin = new Dictionary<int, int>();
        var selectedByBucket = new Dictionary<RiverSourceGroupKey, int>();
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
                        selectedByBucket.GetValueOrDefault(bucketKey) >= bucketCap)
                    {
                        continue;
                    }

                    if (IsFarEnoughFromSelected(candidate, selected, width, minSpacing) &&
                        HasEnoughIndependentDownstreamRun(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height, desiredLength: 4))
                    {
                        selected.Add(candidate);
                        ReserveDownstreamCorridor(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height);
                        selectedByComponent[candidate.ComponentId] = selectedByComponent.GetValueOrDefault(candidate.ComponentId) + 1;
                        selectedByBasin[candidate.BasinId] = selectedByBasin.GetValueOrDefault(candidate.BasinId) + 1;
                        selectedByBucket[bucketKey] = selectedByBucket.GetValueOrDefault(bucketKey) + 1;
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
            foreach (var candidate in candidates.OrderByDescending(c => c.Score).Take(maxSources))
                selected.Add(candidate);
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

    private static void AddSparseBucketRiverSources(
        IReadOnlyList<RiverSourceCandidate> candidates,
        List<RiverSourceCandidate> selected,
        bool[] reservedCorridors,
        Dictionary<int, int> selectedByComponent,
        Dictionary<int, int> selectedByBasin,
        Dictionary<RiverSourceGroupKey, int> selectedByBucket,
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
                selectedByBucket.GetValueOrDefault(bucketKey) > 0)
            {
                continue;
            }

            if (!IsFarEnoughFromSelected(candidate, selected, width, softMinSpacing) ||
                !HasEnoughIndependentDownstreamRun(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height, desiredLength: 2))
            {
                continue;
            }

            selected.Add(candidate);
            ReserveDownstreamCorridor(candidate.Index, reservedCorridors, flowDirections, lakeIds, mask, topology, width, height);
            selectedByComponent[candidate.ComponentId] = selectedByComponent.GetValueOrDefault(candidate.ComponentId) + 1;
            selectedByBasin[candidate.BasinId] = selectedByBasin.GetValueOrDefault(candidate.BasinId) + 1;
            selectedByBucket[bucketKey] = selectedByBucket.GetValueOrDefault(bucketKey) + 1;
            coveredBuckets.Add(new RiverSourceCoverageKey(CoverageComponentId(candidate), candidate.BucketX, candidate.BucketY));
        }
    }

    private static int CoverageComponentId(RiverSourceCandidate candidate) =>
        candidate.ComponentId > 0 ? candidate.ComponentId : -candidate.BasinId;

    private static bool IsFarEnoughFromSelected(RiverSourceCandidate candidate, IReadOnlyList<RiverSourceCandidate> selected, int width, int minSpacing)
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

    private static bool HasEnoughIndependentDownstreamRun(
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

    private static void ReserveDownstreamCorridor(
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

    private static Dictionary<int, double> BuildComponentRiverThresholds(
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

    private static void MarkRiverCorridor(
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

    private static int CountDownstreamDryLength(
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

    private static void EnsureInlandSeaInflowRiverCells(
        MapMask mask,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells)
    {
        var width = mask.Width;
        var height = mask.Height;
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var upstreamDepths = BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height);

        foreach (var body in waterSurfaces.Bodies
                     .Where(b => b.Kind == WaterBodyKind.InlandSea)
                     .OrderByDescending(b => b.CellCount))
        {
            var lakeId = body.Id.Value;
            if (HasVisibleLakeInflow(lakeId, riverCells, flowDirections, upstream, lakeIds, width, height, minimumLength: 8))
                continue;

            var bestPath = Enumerable.Range(0, flowDirections.Length)
                .Where(i => IsLakeInflowMouthCandidate(i, lakeId, flowDirections, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height))
                .Select(i => BuildLongestUpstreamPath(i, upstream, upstreamDepths, accumulation, lakeIds, mask, topology, width))
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

    private static bool HasVisibleLakeInflow(
        int lakeId,
        byte[] riverCells,
        int[] flowDirections,
        List<int>[] upstream,
        int[] lakeIds,
        int width,
        int height,
        int minimumLength)
    {
        for (var index = 0; index < riverCells.Length; index++)
        {
            if (riverCells[index] == 0)
                continue;

            var downstream = DownstreamIndex(index, flowDirections[index], width, height);
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

    private static bool IsLakeInflowMouthCandidate(
        int index,
        int lakeId,
        int[] flowDirections,
        int[] basinIds,
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

        var downstream = DownstreamIndex(index, flowDirections[index], width, height);
        return downstream >= 0 && lakeIds[downstream] == lakeId;
    }

    private static void AddMajorRiverTributaryCells(
        MapMask mask,
        WaterBodyTopology topology,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths,
        HydrologyGenerationOptions options)
    {
        if (options.MajorRiverTributaryMultiplier <= 0 || forcedLongPaths.Count == 0)
            return;

        var width = mask.Width;
        var height = mask.Height;
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var upstreamDepths = BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height);

        foreach (var path in forcedLongPaths.Values.OrderByDescending(p => p.Count))
        {
            if (path.Count < 18)
                continue;

            var mainstem = path.ToHashSet();
            var reservedMouths = new HashSet<int>();
            var targetCount = Math.Clamp((int)Math.Round(path.Count / 14.0 * options.MajorRiverTributaryMultiplier), 2, 22);
            var minAnchorGap = Math.Max(5, path.Count / Math.Max(2, targetCount + 1));
            var added = 0;

            foreach (var anchor in path
                         .Skip(Math.Max(4, path.Count / 10))
                         .Take(Math.Max(0, path.Count - Math.Max(8, path.Count / 5)))
                         .OrderByDescending(i => upstreamDepths[i])
                         .ThenByDescending(i => accumulation[i]))
            {
                if (added >= targetCount)
                    break;
                if (reservedMouths.Any(m => Math.Abs(PathIndexOf(path, m) - PathIndexOf(path, anchor)) < minAnchorGap))
                    continue;

                var tributaryCandidates = upstream[anchor]
                    .Where(i => !mainstem.Contains(i))
                    .Where(i => riverCells[i] == 0 && lakeIds[i] <= 0)
                    .Where(i => IsRenderableRiverLand(new GridPoint(i % width, i / width), mask, topology, lakeIds))
                    .Select(i => BuildLongestTributaryPath(i, upstream, upstreamDepths, accumulation, mainstem, riverCells, lakeIds, mask, topology, width))
                    .Where(p => p.Count >= 4)
                    .ToList();
                var tributary = SelectMajorRiverTributaryPath(tributaryCandidates, accumulation, anchor, width);

                if (tributary is null)
                    continue;
                if (tributary.Any(index => !allowedRiverBasins.Contains(basinIds[index])))
                    continue;

                foreach (var index in tributary)
                    riverCells[index] = 1;

                reservedMouths.Add(anchor);
                added++;
            }
        }
    }

    private static int PathIndexOf(IReadOnlyList<int> path, int value)
    {
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i] == value)
                return i;
        }

        return -1;
    }

    private static List<int>? SelectMajorRiverTributaryPath(
        IReadOnlyList<List<int>> candidates,
        double[] accumulation,
        int anchor,
        int width)
    {
        if (candidates.Count == 0)
            return null;

        var maxLength = candidates.Max(p => p.Count);
        var lengthPreference = HashUnit(anchor % width, anchor / width, 7883);
        var desiredLength = 4.0 + Math.Pow(lengthPreference, 1.65) * Math.Max(0, maxLength - 4);
        return candidates
            .OrderBy(p => Math.Abs(p.Count - desiredLength))
            .ThenByDescending(p => p.Count >= 8)
            .ThenByDescending(p => accumulation[p[0]])
            .First();
    }

    private static List<int> BuildLongestTributaryPath(
        int mouthIndex,
        List<int>[] upstream,
        int[] upstreamDepths,
        double[] accumulation,
        HashSet<int> blockedMainstem,
        byte[] riverCells,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width)
    {
        var path = new List<int>();
        var current = mouthIndex;
        var seen = new HashSet<int>();
        while (current >= 0 && current < upstream.Length && seen.Add(current))
        {
            if (blockedMainstem.Contains(current) || lakeIds[current] > 0)
                break;

            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            path.Add(current);
            var next = upstream[current]
                .Where(i => !blockedMainstem.Contains(i))
                .Where(i => riverCells[i] == 0 && lakeIds[i] <= 0)
                .Where(i =>
                {
                    var upstreamPoint = new GridPoint(i % width, i / width);
                    return IsRenderableRiverLand(upstreamPoint, mask, topology, lakeIds);
                })
                .OrderByDescending(i => upstreamDepths[i])
                .ThenByDescending(i => accumulation[i])
                .FirstOrDefault(-1);

            if (next < 0)
                break;
            current = next;
        }

        return path;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildForcedLongRiverPaths(
        MapMask mask,
        WaterBodyTopology topology,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        IReadOnlyList<DrainageBasin> basins,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        HydrologyGenerationOptions options)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        if (options.LongRiverCountMultiplier <= 0 || options.RiverDensity <= 0)
            return result;

        var width = mask.Width;
        var height = mask.Height;
        var areaRoot = Math.Sqrt(width * height);
        var targetCount = Math.Clamp((int)Math.Round(areaRoot * 0.026 * options.LongRiverCountMultiplier), 4, 48);
        var minLength = Math.Clamp((int)Math.Round(areaRoot * 0.032), 20, 82);
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var upstreamDepths = BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height);
        var basinById = basins.ToDictionary(b => b.Id);

        var candidates = Enumerable.Range(0, flowDirections.Length)
            .Where(i => IsLongRiverMouthCandidate(i, flowDirections, riverCells, basinIds, basinById, allowedRiverBasins, lakeIds, mask, topology, width, height))
            .Select(i => BuildLongestUpstreamPath(i, upstream, upstreamDepths, accumulation, lakeIds, mask, topology, width))
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
                    return Math.Abs(WrappedDeltaX(point.X - mouthPoint.X, width)) < minLength / 3 &&
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

    private static int[] BuildLongestUpstreamDepths(
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

    private static bool IsLongRiverMouthCandidate(
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

    private static List<int> BuildLongestUpstreamPath(
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
        while (current >= 0 && current < upstream.Length && seen.Add(current))
        {
            path.Add(current);
            var next = upstream[current]
                .Where(i => lakeIds[i] <= 0)
                .Where(i =>
                {
                    var point = new GridPoint(i % width, i / width);
                    return IsRenderableRiverLand(point, mask, topology, lakeIds);
                })
                .OrderByDescending(i => upstreamDepths[i])
                .ThenByDescending(i => ScoreLongRiverUpstreamStep(i, current, accumulation, width))
                .FirstOrDefault(-1);

            if (next < 0)
                break;

            current = next;
        }

        return path;
    }

    private static double ScoreLongRiverUpstreamStep(int upstreamIndex, int currentIndex, double[] accumulation, int width)
    {
        var sameColumnPenalty = Math.Abs((upstreamIndex % width) - (currentIndex % width)) == 0 ? 0.06 : 0.0;
        return accumulation[upstreamIndex] + HashUnit(upstreamIndex % width, upstreamIndex / width, 7717) * 0.05 - sameColumnPenalty;
    }

    private List<RiverSegment> ExtractRivers(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        byte[] riverCells,
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
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var upstreamDepths = BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height);
        var mouthsByTerminal = Enumerable.Range(0, riverCells.Length)
            .Where(i => riverCells[i] != 0)
            .Where(i =>
            {
                var downstream = DownstreamIndex(i, flowDirections[i], width, height);
                return downstream < 0 || riverCells[downstream] == 0;
            })
            .OrderByDescending(i => upstreamDepths[i])
            .ThenByDescending(i => accumulation[i])
            .ToList();
        var visited = new bool[riverCells.Length];
        var rivers = new List<RiverSegment>();
        var maxRivers = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) * 2.35 *
                                                    Math.Max(0.25, options.MajorRiverCountMultiplier) *
                                                    (1.0 + Math.Max(0.0, options.MajorRiverTributaryMultiplier) * 0.35)), 48, 2400);
        var componentCounts = new Dictionary<int, int>();
        var componentById = landComponents.Components.ToDictionary(c => c.Id);
        var nextRiverId = 1;
        var usedChannelCells = new int[riverCells.Length];
        var outletLakeIds = outlets.Where(o => o.HasOutlet).Select(o => o.LakeId.Value).ToHashSet();

        foreach (var (mouthIndex, forcedPath) in forcedLongPaths.OrderByDescending(p => p.Value.Count))
            TryAddRiverFromMouth(mouthIndex, isTributary: false, forcedPath, forceLong: true);

        foreach (var mouthIndex in mouthsByTerminal)
            TryAddRiverFromMouth(mouthIndex, isTributary: false);

        var addedTributary = true;
        while (addedTributary && rivers.Count < maxRivers)
        {
            addedTributary = false;
            var tributaryMouths = Enumerable.Range(0, riverCells.Length)
                .Where(i => riverCells[i] != 0 && !visited[i])
                .Where(i =>
                {
                    var downstream = DownstreamIndex(i, flowDirections[i], width, height);
                    return downstream >= 0 && visited[downstream];
                })
                .OrderByDescending(i => upstreamDepths[i])
                .ThenByDescending(i => accumulation[i])
                .ToList();

            foreach (var mouthIndex in tributaryMouths)
                addedTributary |= TryAddRiverFromMouth(mouthIndex, isTributary: true);
        }

        var remainingBranches = Enumerable.Range(0, riverCells.Length)
            .Where(i => riverCells[i] != 0 && !visited[i])
            .Where(i => HasImmediateRenderableOutlet(i, flowDirections, lakeIds, mask, topology, visited, width, height) ||
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
                ? BuildMainstemPath(mouthIndex, upstream, upstreamDepths, accumulation, visited, riverCells, lakeIds, minSourceAccumulation, width, preferDepth: true)
                : forcedPath.Where(i => !visited[i] || i == mouthIndex).ToList();
            if (path.Count == 0)
                return false;

            path.Reverse();
            var cells = path.Select(i => new GridPoint(i % width, i / width)).ToList();
            var source = cells[0];
            var dryMouthCell = cells[^1];
            var segmentOutletIndex = FindSegmentOutletIndex(mouthIndex, flowDirections, lakeIds, mask, topology, visited, width, height);
            var segmentOutlet = new GridPoint(segmentOutletIndex % width, segmentOutletIndex / width);
            var segmentEndsInWater = segmentOutletIndex != mouthIndex && IsWaterTarget(segmentOutlet, mask, topology, lakeIds);
            var segmentEndsInConfluence = segmentOutletIndex != mouthIndex && !segmentEndsInWater;
            var terminalIndex = FindMouthTargetIndex(mouthIndex, flowDirections, riverCells, lakeIds, mask, topology, width, height);
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
            var meanSlope = ComputeMeanSlope(elevation, cells, terminal, target.Kind);
            var targetIsClosedLake = IsClosedLakeTarget(target, outletLakeIds);
            var kind = ClassifyRiver(elevation, cells, target.Kind, targetIsClosedLake, discharge, meanSlope);
            var mouthKind = ClassifyMouth(elevation, terminal, target.Kind, discharge, meanSlope, options);
            var riverMouth = segmentOutlet;
            GridPoint? renderOutlet = segmentOutletIndex == mouthIndex ? null : segmentOutlet;
            var channelCells = BuildChannelPath(mask, elevation, topology, lakeIds, cells, renderOutlet, usedChannelCells, discharge, meanSlope, options);
            var river = new RiverSegment(
                nextRiverId++,
                channelCells,
                BuildPolyline(elevation, channelCells, renderOutlet, discharge, meanSlope, options),
                channelCells[0],
                riverMouth,
                terminal,
                componentId <= 0 ? null : componentId,
                target.Kind,
                target.TargetId,
                Math.Round(discharge, 3),
                Math.Round((double)cells.Count, 2),
                Math.Round(meanSlope, 5),
                kind,
                mouthKind);

            foreach (var index in path)
            {
                visited[index] = true;
                riverCells[index] = 1;
            }
            foreach (var cell in channelCells)
            {
                var channelIndex = cell.Y * width + cell.X;
                if (channelIndex >= 0 && channelIndex < usedChannelCells.Length)
                    usedChannelCells[channelIndex]++;
            }
            if (componentId > 0)
                componentCounts[componentId] = componentCounts.GetValueOrDefault(componentId) + 1;

            rivers.Add(river);
            mouths.Add(new RiverMouth(river.Id, riverMouth, target.Kind, target.TargetId, mouthKind, river.Discharge));
            return true;
        }
    }

    private List<GridPoint> BuildChannelPath(
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
        return path.Count >= 2 ? path : originalCells.ToList();
    }

    private List<GridPoint> TraceChannelPath(
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
    private double? ChannelStepCost(
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

    private static List<GridPoint> ReconstructChannelPath(
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
    private List<GridPoint> RerouteLongStraightRuns(
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

    private bool TryBreakStraightRunWithKink(
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

    private static IEnumerable<int> CandidatePivots(int firstPivot, int offset, StraightRun run, int count)
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

    private static IEnumerable<GridPoint> CardinalKinkCandidates(GridPoint current, (int Dx, int Dy) move, int width, int height)
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

    private static IEnumerable<(GridPoint First, GridPoint Second)> DiagonalKinkCandidates(GridPoint previous, (int Dx, int Dy) move, int width, int height)
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

    private static bool CanUseKinkCell(
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

    private static bool IsAdjacent(GridPoint a, GridPoint b, int width) =>
        Math.Max(Math.Abs(WrappedDeltaX(b.X - a.X, width)), Math.Abs(b.Y - a.Y)) == 1;
    private List<GridPoint> FindLocalChannelPath(
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

    private List<GridPoint> FindLocalChannelPath(
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
    private static IReadOnlyList<StraightRun> DetectLongStraightRuns(IReadOnlyList<GridPoint> cells, int minRun)
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

    private static double ChannelPlainness(TerrainClassKind terrain) => terrain switch
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

    private static double ChannelCurvaturePenalty(int previousDirection, int direction)
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

    private double ChannelCurlBias(GridPoint point, int direction, double strength)
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
    private static List<int>[] BuildUpstreamLists(int[] flowDirections, int width, int height)
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

    private static List<int> BuildMainstemPath(
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
        while (current >= 0 && current < upstream.Length && guard++ < upstream.Length)
        {
            if (visited[current] && current != mouthIndex)
                break;

            path.Add(current);
            var next = upstream[current]
                .Where(i => !visited[i])
                .Where(i => riverCells[i] != 0 && lakeIds[i] <= 0)
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

    private static bool HasImmediateRenderableOutlet(
        int mouthIndex,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        bool[] visited,
        int width,
        int height)
    {
        var downstream = DownstreamIndex(mouthIndex, flowDirections[mouthIndex], width, height);
        if (downstream < 0)
            return true;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        return IsWaterTarget(downstreamPoint, mask, topology, lakeIds) || visited[downstream];
    }

    private static int FindSegmentOutletIndex(
        int mouthIndex,
        int[] flowDirections,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        bool[] visited,
        int width,
        int height)
    {
        var downstream = DownstreamIndex(mouthIndex, flowDirections[mouthIndex], width, height);
        if (downstream < 0)
            return mouthIndex;

        var downstreamPoint = new GridPoint(downstream % width, downstream / width);
        if (IsWaterTarget(downstreamPoint, mask, topology, lakeIds) || visited[downstream])
            return downstream;

        return mouthIndex;
    }

    private static bool CanAddRiverForComponent(
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

    private static bool IsClosedLakeTarget(TargetRef target, HashSet<int> outletLakeIds) =>
        target.Kind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
        (!target.TargetId.HasValue || !outletLakeIds.Contains(target.TargetId.Value));

    private RiverKind ClassifyRiver(ElevationMap elevation, IReadOnlyList<GridPoint> cells, DrainageTargetKind targetKind, bool targetIsClosedLake, double discharge, double meanSlope)
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

    private static RiverMouthKind ClassifyMouth(ElevationMap elevation, GridPoint terminal, DrainageTargetKind targetKind, double discharge, double meanSlope, HydrologyGenerationOptions options)
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

    private List<MapPoint> BuildPolyline(ElevationMap elevation, IReadOnlyList<GridPoint> cells, GridPoint? terminal, double discharge, double meanSlope, HydrologyGenerationOptions options)
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

    private MapPoint? ToSegmentBendPoint(ElevationMap elevation, IReadOnlyList<GridPoint> cells, int index, double discharge, double meanSlope, HydrologyGenerationOptions options)
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

    private MapPoint ToMapPoint(ElevationMap elevation, IReadOnlyList<GridPoint> cells, int index, double discharge, double meanSlope, HydrologyGenerationOptions options)
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

    private static double ComputeMeanSlope(ElevationMap elevation, IReadOnlyList<GridPoint> cells, GridPoint terminal, DrainageTargetKind targetKind)
    {
        if (cells.Count < 2)
            return 0;

        var source = elevation.GetHydrologyHeight(cells[0]);
        var mouth = targetKind == DrainageTargetKind.Ocean
            ? 0.0
            : elevation.GetHydrologyHeight(terminal);
        return Math.Max(0.0, source - mouth) / Math.Max(1, cells.Count - 1);
    }

    private static bool EndsInWater(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        if (lakeIds[index] > 0)
            return true;
        return !mask.IsLand(point) && topology.GetKind(point) is (WaterBodyKind.Ocean or WaterBodyKind.OceanSea);
    }

    private static TargetRef ResolveTarget(GridPoint terminal, MapMask mask, WaterBodyTopology topology, WaterSurfaceMap waterSurfaces, int[] lakeIds)
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

    private static IReadOnlyList<GridPoint> FindShoreline(int width, int height, IReadOnlyList<GridPoint> waterCells, Func<GridPoint, bool> isSameWater)
    {
        var shoreline = new HashSet<GridPoint>();
        foreach (var cell in waterCells)
        {
            foreach (var neighbor in Neighbors8(cell, width, height))
            {
                if (!isSameWater(neighbor))
                    shoreline.Add(neighbor);
            }
        }

        return shoreline.ToList();
    }

    private static double TerrainValleyBias(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.AlluvialPlain => 38.0,
        TerrainClassKind.InteriorLowland => 30.0,
        TerrainClassKind.CoastalPlain => 28.0,
        TerrainClassKind.SedimentaryBasin => 26.0,
        TerrainClassKind.DeltaCandidate => 44.0,
        TerrainClassKind.DryBasin => 18.0,
        TerrainClassKind.Highland => 8.0,
        TerrainClassKind.Mountain => -18.0,
        _ => 0.0
    };

    private static double TerrainRiverThresholdMultiplier(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.Mountain => 0.55,
        TerrainClassKind.Highland => 0.75,
        TerrainClassKind.DeltaCandidate => 0.58,
        TerrainClassKind.AlluvialPlain => 0.82,
        TerrainClassKind.DryBasin => 0.80,
        TerrainClassKind.DesertPlateauCandidate => 0.95,
        _ => 1.0
    };

    private static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static int FindTerminal(int start, int[] flowDirections, int width, int height)
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

    private static int FindMouthTargetIndex(
        int mouthIndex,
        int[] flowDirections,
        byte[] riverCells,
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

    private static bool IsRenderableRiverLand(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        if (lakeIds[index] > 0)
            return false;
        if (!mask.IsLand(point))
            return false;
        return !topology.IsInlandWater(point);
    }

    private static bool IsWaterTarget(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        return lakeIds[index] > 0 || !mask.IsLand(point) && topology.IsOceanicWater(point);
    }

    private static int ChebyshevDistance(GridPoint a, GridPoint b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double Distance(GridPoint a, GridPoint b, int width)
    {
        var dx = WrappedDeltaX(a.X - b.X, width);
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int DownstreamIndex(int index, int direction, int width, int height)
    {
        if (direction < 0 || direction >= Directions.Length || width <= 0)
            return -1;

        var x = index % width;
        var y = index / width;
        var move = Directions[direction];
        var nextY = y + move.Dy;
        if (nextY < 0 || nextY >= height)
            return -1;
        var nextX = WrapX(x + move.Dx, width);
        return nextY * width + nextX;
    }

    private static GridPoint? Move(GridPoint point, int direction, int width, int height)
    {
        var move = Directions[direction];
        var y = point.Y + move.Dy;
        if (y < 0 || y >= height)
            return null;

        return new GridPoint(WrapX(point.X + move.Dx, width), y);
    }

    private static int DirectionIndex(GridPoint from, GridPoint to, int width)
    {
        var dx = WrappedDeltaX(to.X - from.X, width);
        var dy = to.Y - from.Y;
        for (var i = 0; i < Directions.Length; i++)
        {
            if (Directions[i].Dx == Math.Sign(dx) && Directions[i].Dy == Math.Sign(dy))
                return i;
        }

        return -1;
    }

    private static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height)
    {
        for (var i = 0; i < Directions.Length; i++)
        {
            var moved = Move(point, i, width, height);
            if (moved.HasValue)
                yield return moved.Value;
        }
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private static int WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            value = (value << 13) ^ value;
            return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
        }
    }

    private static double HashUnit(int x, int y, int seed) => Math.Clamp((Hash01(x, y, seed) + 1.0) * 0.5, 0, 1);

    private sealed record TerminalKey(DrainageTargetKind Kind, int? TargetId, int TerminalIndex);

    private sealed record TargetRef(DrainageTargetKind Kind, int? TargetId);

    private sealed record LandComponentMap(int[] ComponentIds, IReadOnlyList<LandComponent> Components);

    private sealed record LandComponent(int Id, int CellCount);

    private sealed record RiverSourceCandidate(int Index, int ComponentId, int BasinId, int BucketX, int BucketY, int DownstreamDryLength, double Score);

    private sealed record RiverSourceGroupKey(int ComponentId, int BasinId, int BucketX, int BucketY);

    private sealed record RiverSourceCoverageKey(int ComponentId, int BucketX, int BucketY);

    private sealed class ComponentRiverSourceQueue(int id, IReadOnlyList<RiverSourceCandidate> candidates)
    {
        public int Id { get; } = id;
        public IReadOnlyList<RiverSourceCandidate> Candidates { get; } = candidates;
        public int NextIndex { get; set; }
    }

    private sealed record LongRiverPath(IReadOnlyList<int> Path, double Discharge, int BasinId);

    private readonly record struct StraightRun(int Start, int Length, int Direction);

    private readonly record struct ChannelSearchNode(GridPoint Cell, int PreviousDirection, int StraightRunLength, int DiagonalRunDirection, int DiagonalRunLength);

    private sealed class MutableBasin(int id, DrainageTargetKind targetKind, int? targetId, GridPoint terminalCell)
    {
        public int Id { get; } = id;
        public DrainageTargetKind TargetKind { get; } = targetKind;
        public int? TargetId { get; } = targetId;
        public GridPoint TerminalCell { get; } = terminalCell;
        public int CellCount { get; set; }
        public double TotalRunoff { get; set; }
    }
}

internal static class HydrologyElevationExtensions
{
    public static double GetHydrologyHeight(this ElevationMap elevation, GridPoint point) =>
        elevation.HasWaterSurface(point) ? elevation.GetWaterSurface(point) : elevation.GetBedElevation(point);
}
