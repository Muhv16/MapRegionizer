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
        if (ForceSmallLakeOutlets(mask, elevation, waterBodyTopology, waterSurfaces, lakeCells, lakeIds, flowDirections, accumulation, outlets, options))
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
        var validEndorheicBasins = BuildValidEndorheicBasinSet(basins, elevation);
        var allowedRiverBasins = BuildAllowedRiverBasinSet(basins, validEndorheicBasins);
        var riverCells = SelectRiverCells(mask, elevation, waterBodyTopology, generatedLakes, accumulation, flowDirections, basinIds, allowedRiverBasins, lakeIds, landComponents, options);
        var mouths = new List<RiverMouth>();
        var rivers = ExtractRivers(mask, elevation, waterBodyTopology, waterSurfaces, flowDirections, accumulation, basinIds, riverCells, lakeIds, landComponents, validEndorheicBasins, options, mouths);

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

    private bool ForceSmallLakeOutlets(
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
        var minimumInflow = Math.Clamp(Math.Sqrt(width * mask.Height) * 0.16, 8.0, 42.0) /
                            Math.Max(0.2, options.RiverDensity * options.TributaryDensity);

        foreach (var body in waterSurfaces.Bodies
                     .Where(b => b.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
                     .Where(b => b.CellCount is > 0 and <= 24)
                     .OrderBy(b => b.Id.Value))
        {
            if (!lakeCells.TryGetValue(body.Id.Value, out var cells) || cells.Count == 0)
                continue;

            var inflows = FindLakeInflowCells(mask, topology, lakeIds, flowDirections, accumulation, body.Id.Value, minimumInflow);
            if (inflows.Count == 0)
                continue;
            var hasExistingOutlet = outletByLake.TryGetValue(body.Id.Value, out var existingIndex) && outlets[existingIndex].HasOutlet;
            if (hasExistingOutlet &&
                outlets[existingIndex].OutletCell is { } existingOutlet &&
                inflows.All(inflow => ChebyshevDistance(existingOutlet, inflow) >= 2))
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
            var cost = neighborHeight
                       + uphill * (uphill > 0 ? 2.8 : 0.18)
                       + elevation.GetRoughness(n) * 16.0
                       + ridgeCrossing * 64.0
                       - elevation.GetMountainPassPotential(n) * 34.0
                       - elevation.GetBasinInfluence(n) * 31.0
                       - TerrainValleyBias(terrain)
                       - (isLake ? 18.0 : 0.0)
                       - (isOcean ? 30.0 : 0.0)
                       + diagonalPenalty
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
        if (terrain == TerrainClassKind.DryBasin && basin >= 0.46)
            return true;
        if (terrain == TerrainClassKind.SedimentaryBasin && basin >= 0.68 && options.EndorheicBasinChance >= 0.18)
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

    private static HashSet<int> BuildValidEndorheicBasinSet(IReadOnlyList<DrainageBasin> basins, ElevationMap elevation)
    {
        return basins
            .Where(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin)
            .Where(b => b.CellCount >= 80)
            .Where(b =>
            {
                var terrain = elevation.GetTerrainClass(b.TerminalCell);
                var basin = elevation.GetBasinInfluence(b.TerminalCell);
                return terrain == TerrainClassKind.DryBasin && basin >= 0.46 ||
                       terrain == TerrainClassKind.SedimentaryBasin && basin >= 0.68;
            })
            .Select(b => b.Id)
            .ToHashSet();
    }

    private static HashSet<int> BuildAllowedRiverBasinSet(IReadOnlyList<DrainageBasin> basins, HashSet<int> validEndorheicBasins)
    {
        return basins
            .Where(b => b.TargetKind != DrainageTargetKind.EndorheicDryBasin || validEndorheicBasins.Contains(b.Id))
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

        var baseThreshold = Math.Clamp(Math.Sqrt(width * height) * 0.42, 28.0, 155.0) / Math.Max(0.1, options.RiverDensity * options.TributaryDensity);
        var componentThresholds = BuildComponentRiverThresholds(mask, generatedLakes, accumulation, flowDirections, landComponents, baseThreshold);
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
                if (accumulation[index] >= threshold)
                    MarkRiverCorridor(index, flowDirections, riverCells, basinIds, allowedRiverBasins, lakeIds, mask, topology, width, height);
            }
        }

        return riverCells;
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
                < 180 => 0.995,
                < 750 => 0.988,
                < 2400 => 0.978,
                _ => 0.965
            };
            var local = PercentileSorted(values, percentile);
            thresholds[component.Id] = Math.Clamp(local, baseThreshold * 0.32, baseThreshold * 1.55);
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
        List<RiverMouth> mouths)
    {
        var width = mask.Width;
        var height = mask.Height;
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var mouthsByTerminal = Enumerable.Range(0, riverCells.Length)
            .Where(i => riverCells[i] != 0)
            .Where(i =>
            {
                var downstream = DownstreamIndex(i, flowDirections[i], width, height);
                return downstream < 0 || riverCells[downstream] == 0;
            })
            .OrderByDescending(i => accumulation[i])
            .ToList();
        var visited = new bool[riverCells.Length];
        var rivers = new List<RiverSegment>();
        var maxRivers = Math.Clamp((int)Math.Round(Math.Sqrt(width * height) * 1.35 * Math.Max(0.25, options.MajorRiverCountMultiplier)), 24, 900);
        var componentCounts = new Dictionary<int, int>();
        var componentById = landComponents.Components.ToDictionary(c => c.Id);
        var nextRiverId = 1;

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
                .OrderByDescending(i => accumulation[i])
                .ToList();

            foreach (var mouthIndex in tributaryMouths)
                addedTributary |= TryAddRiverFromMouth(mouthIndex, isTributary: true);
        }

        var remainingBranches = Enumerable.Range(0, riverCells.Length)
            .Where(i => riverCells[i] != 0 && !visited[i])
            .OrderByDescending(i => accumulation[i])
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

        bool TryAddRiverFromMouth(int mouthIndex, bool isTributary)
        {
            if (rivers.Count >= maxRivers || visited[mouthIndex] && !isTributary)
                return false;

            var minSourceAccumulation = Math.Max(5.0, accumulation[mouthIndex] * (isTributary ? 0.05 : 0.012));
            var path = BuildMainstemPath(mouthIndex, upstream, accumulation, visited, riverCells, lakeIds, minSourceAccumulation, width);
            if (path.Count == 0)
                return false;

            path.Reverse();
            var cells = path.Select(i => new GridPoint(i % width, i / width)).ToList();
            var source = cells[0];
            var mouthCell = cells[^1];
            var terminalIndex = FindMouthTargetIndex(mouthIndex, flowDirections, riverCells, lakeIds, mask, topology, width, height);
            var terminal = new GridPoint(terminalIndex % width, terminalIndex / width);
            var target = ResolveTarget(terminal, mask, topology, waterSurfaces, lakeIds);
            if (target.Kind == DrainageTargetKind.EndorheicDryBasin && !validEndorheicBasins.Contains(basinIds[mouthIndex]))
                return false;

            var minLength = target.Kind == DrainageTargetKind.EndorheicDryBasin ? 18 : 12;
            if (cells.Count < minLength && !EndsInWater(mouthCell, mask, topology, lakeIds))
                return false;

            var componentId = landComponents.ComponentIds[source.Y * width + source.X];
            if (!CanAddRiverForComponent(componentId, componentById, componentCounts))
                return false;

            var discharge = accumulation[mouthIndex];
            var meanSlope = ComputeMeanSlope(elevation, cells);
            var kind = ClassifyRiver(elevation, cells, target.Kind, discharge, meanSlope);
            var mouthKind = ClassifyMouth(elevation, terminal, target.Kind, discharge, meanSlope, options);
            var river = new RiverSegment(
                nextRiverId++,
                cells,
                BuildPolyline(elevation, cells, discharge, meanSlope, options),
                source,
                mouthCell,
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
            if (componentId > 0)
                componentCounts[componentId] = componentCounts.GetValueOrDefault(componentId) + 1;

            rivers.Add(river);
            mouths.Add(new RiverMouth(river.Id, terminal, target.Kind, target.TargetId, mouthKind, river.Discharge));
            return true;
        }
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
        double[] accumulation,
        bool[] visited,
        byte[] riverCells,
        int[] lakeIds,
        double minSourceAccumulation,
        int width)
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
                .OrderByDescending(i => accumulation[i])
                .ThenBy(i => Math.Abs((i % width) - (current % width)))
                .FirstOrDefault(-1);
            if (next < 0)
                break;

            current = next;
        }

        return path;
    }

    private static bool CanAddRiverForComponent(
        int componentId,
        Dictionary<int, LandComponent> componentById,
        Dictionary<int, int> componentCounts)
    {
        if (componentId <= 0 || !componentById.TryGetValue(componentId, out var component))
            return true;

        var cap = component.CellCount switch
        {
            < 160 => 0,
            < 750 => 2,
            < 2400 => 5,
            < 9000 => 10,
            _ => Math.Clamp(12 + component.CellCount / 3000, 14, 24)
        };

        return componentCounts.GetValueOrDefault(componentId) < cap;
    }

    private RiverKind ClassifyRiver(ElevationMap elevation, IReadOnlyList<GridPoint> cells, DrainageTargetKind targetKind, double discharge, double meanSlope)
    {
        if (targetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea or DrainageTargetKind.EndorheicDryBasin)
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

    private List<MapPoint> BuildPolyline(ElevationMap elevation, IReadOnlyList<GridPoint> cells, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        if (cells.Count == 0)
            return [];

        var step = cells.Count > 80 ? 2 : 1;
        var points = new List<MapPoint>();
        for (var i = 0; i < cells.Count; i += step)
            points.Add(ToMapPoint(elevation, cells, i, discharge, meanSlope, options));

        if (points.Count == 0 || points[^1] != ToMapPoint(elevation, cells, cells.Count - 1, discharge, meanSlope, options))
            points.Add(ToMapPoint(elevation, cells, cells.Count - 1, discharge, meanSlope, options));

        return points;
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
        var strength = options.MeanderStrength * plain * slopeFactor * dischargeFactor * 0.32;
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
        var noise = Hash01(cell.X, cell.Y, _seed + 7433);
        return new MapPoint(x + sideX * noise * strength, y + sideY * noise * strength);
    }

    private static double ComputeMeanSlope(ElevationMap elevation, IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count < 2)
            return 0;

        var source = elevation.GetHydrologyHeight(cells[0]);
        var mouth = elevation.GetHydrologyHeight(cells[^1]);
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
        TerrainClassKind.DryBasin => 1.7,
        TerrainClassKind.DesertPlateauCandidate => 1.45,
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
