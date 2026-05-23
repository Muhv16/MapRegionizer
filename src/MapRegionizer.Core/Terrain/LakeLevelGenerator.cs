using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed class LakeLevelGenerator
{
    public ElevationMap Generate(
        MapMask mask,
        ElevationMap baseTerrain,
        CrustFieldMap crustFields,
        TectonicBoundaryMap boundaries,
        RiftProvinceMap riftProvinces,
        TectonicFeatureMap features,
        WaterBodyTopology waterBodyTopology,
        GeneratedLakeMap generatedLakes,
        ElevationGenerationOptions options)
    {
        var length = mask.Width * mask.Height;
        var elevation = baseTerrain.ElevationMetersSpan.ToArray();
        var baseElevation = baseTerrain.BaseElevationMetersSpan.ToArray();
        var tectonicElevation = baseTerrain.TectonicElevationMetersSpan.ToArray();
        var roughness = baseTerrain.RoughnessSpan.ToArray();
        var erosionMask = baseTerrain.ErosionMaskSpan.ToArray();
        var terrainClasses = baseTerrain.TerrainClassSpan.ToArray();
        var mountainPassPotential = baseTerrain.MountainPassPotentialSpan.ToArray();
        var ridgeContinuity = baseTerrain.RidgeContinuitySpan.ToArray();
        var foothillInfluence = baseTerrain.FoothillInfluenceSpan.ToArray();
        var basinInfluence = baseTerrain.BasinInfluenceSpan.ToArray();
        var waterSurface = Enumerable.Repeat(double.NaN, length).ToArray();

        var distanceToLand = ElevationGridMath.ComputeDistance(mask, sourceIsLand: true);
        var distanceToWater = ElevationGridMath.ComputeDistance(mask, sourceIsLand: false);
        var landEnclosure = ElevationGridMath.BuildLandEnclosureField(mask);
        var minDimension = Math.Max(1, Math.Min(mask.Width, mask.Height));
        var shelfWidth = Math.Max(2.0, minDimension * 0.035 * options.ShelfWidthFactor);
        var ridgeMask = new double[length];
        var collisionMask = new double[length];
        var massifMask = new double[length];
        var subductionMask = new double[length];
        var passiveMask = new double[length];

        TectonicFieldBuilder.StampBoundaryMasks(mask, boundaries, ridgeMask, collisionMask, massifMask, subductionMask, passiveMask);
        ridgeMask = ElevationSignalMath.ShapeSignal(ElevationSignalMath.SmoothField(ridgeMask, mask.Width, mask.Height, 11), 0.16, 1.65);
        subductionMask = ElevationSignalMath.DiffuseTectonicLineSignal(subductionMask, mask.Width, mask.Height, 8, 11, 0.08, 1.18, 0.24);

        var volcanism = ElevationSignalMath.BuildTerrainSignal(features, features.GetVolcanism, 4, 0.13, 1.15);
        var heatFlow = ElevationSignalMath.BuildTerrainSignal(features, features.GetHeatFlow, 8, 0.22, 1.4);
        var sedimentSupply = ElevationSignalMath.BuildTerrainSignal(features, features.GetSedimentSupply, 7, 0.24, 1.35);
        var riftProvince = ElevationSignalMath.BuildRiftProvinceSignal(riftProvinces, riftProvinces.GetRiftInfluence, 5, 0.035, 0.92);
        var riftGraben = ElevationSignalMath.BuildRiftProvinceSignal(riftProvinces, riftProvinces.GetGrabenMask, 2, 0.05, 1.04);

        var waterSurfaces = ApplyWaterSurfaceLevels(
            mask,
            waterBodyTopology,
            generatedLakes,
            boundaries,
            elevation,
            waterSurface,
            riftProvince,
            riftGraben,
            volcanism,
            heatFlow,
            ridgeContinuity,
            foothillInfluence,
            roughness,
            options);

        Array.Clear(terrainClasses);
        TerrainClassifier.ClassifyTerrain(
            mask,
            crustFields,
            elevation,
            roughness,
            distanceToLand,
            distanceToWater,
            landEnclosure,
            shelfWidth,
            sedimentSupply,
            heatFlow,
            basinInfluence,
            foothillInfluence,
            ridgeContinuity,
            ridgeMask,
            subductionMask,
            riftProvince,
            riftGraben,
            terrainClasses);

        return new ElevationMap(
            mask.Width,
            mask.Height,
            elevation,
            baseElevation,
            tectonicElevation,
            roughness,
            erosionMask,
            terrainClasses,
            mountainPassPotential,
            ridgeContinuity,
            foothillInfluence,
            basinInfluence,
            elevation.ToArray(),
            waterSurface,
            waterSurfaces);
    }

    private static WaterSurfaceMap ApplyWaterSurfaceLevels(
        MapMask mask,
        WaterBodyTopology waterBodyTopology,
        GeneratedLakeMap generatedLakes,
        TectonicBoundaryMap boundaries,
        double[] elevation,
        double[] waterSurface,
        double[] riftProvince,
        double[] riftGraben,
        double[] volcanism,
        double[] heatFlow,
        double[] ridgeContinuity,
        double[] foothillInfluence,
        double[] roughness,
        ElevationGenerationOptions options)
    {
        var faultContext = BuildLakeFaultContext(boundaries, mask.Width, mask.Height);
        var bodyCells = new Dictionary<int, List<GridPoint>>();
        var generatedBodies = generatedLakes.Bodies.ToDictionary(b => b.Id.Value);
        bool IsHydrologyWater(GridPoint point) => !mask.IsLand(point) || generatedLakes.Contains(point);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask.IsLand(new GridPoint(x, y)))
                    continue;

                var id = waterBodyTopology.GetWaterBodyId(x, y)?.Value ?? 1;
                if (!bodyCells.TryGetValue(id, out var cells))
                {
                    cells = [];
                    bodyCells[id] = cells;
                }

                cells.Add(new GridPoint(x, y));
            }
        }

        foreach (var generated in generatedLakes.Bodies)
        {
            if (!bodyCells.TryGetValue(generated.Id.Value, out var cells))
            {
                cells = [];
                bodyCells[generated.Id.Value] = cells;
            }

            cells.AddRange(generated.Cells);
        }

        var surfaces = new List<WaterBodySurface>();
        foreach (var (idValue, cells) in bodyCells)
        {
            var id = new WaterBodyId(idValue);
            var generatedBody = generatedBodies.GetValueOrDefault(idValue);
            var classification = waterBodyTopology.GetClassification(id);
            var kind = generatedBody is not null ? WaterBodyKind.InlandLake : classification?.Kind ?? WaterBodyKind.Ocean;
            var shoreline = FindShoreline(mask, cells, IsHydrologyWater);
            var metrics = ComputeLakeMetrics(
                id,
                kind,
                cells,
                shoreline,
                elevation,
                riftProvince,
                riftGraben,
                volcanism,
                heatFlow,
                ridgeContinuity,
                foothillInfluence,
                roughness,
                faultContext,
                mask.Width,
                options);
            var margin = ComputeLakeSurfaceMargin(cells.Count, mask.Width * mask.Height, options);
            var spillElevation = shoreline.Count > 0
                ? Percentile(shoreline.Select(p => elevation[p.Y * mask.Width + p.X]).ToList(), options.LakeSurfacePercentile)
                : options.MinLandElevationMeters + margin;

            var surface = kind switch
            {
                WaterBodyKind.Ocean => 0.0,
                WaterBodyKind.OceanSea => Math.Min(options.MaxSeaElevationMeters, -1.0),
                _ => Math.Max(options.MinLandElevationMeters, spillElevation - margin)
            };

            var maxDepth = kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea
                ? ComputeLakeMaxDepth(metrics, options)
                : Math.Max(0.0, surface - cells.Min(p => elevation[p.Y * mask.Width + p.X]));
            if (generatedBody is not null)
                maxDepth = Math.Min(maxDepth, Math.Max(options.MinLakeDepthMeters, generatedBody.MaxDepthMeters));

            foreach (var cell in cells)
                waterSurface[cell.Y * mask.Width + cell.X] = surface;

            if (kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
            {
                if (options.PreserveInlandWaterMask || !options.AllowLakeExpansion)
                    LiftShorelineRim(mask, shoreline, elevation, surface + margin, IsHydrologyWater);

                ShapeLakeBed(mask, cells, elevation, surface, maxDepth, metrics, options);
            }

            surfaces.Add(new WaterBodySurface(
                id,
                kind,
                surface,
                spillElevation,
                margin,
                maxDepth,
                shoreline.Count,
                cells.Count,
                metrics.Centroid,
                metrics.Location,
                metrics.Origin,
                metrics.Profile,
                metrics.MeanShorelineElevationMeters,
                metrics.ShorelineReliefMeters,
                metrics.TectonicInfluence,
                metrics.VolcanicInfluence));
        }

        return new WaterSurfaceMap(mask.Width, mask.Height, waterSurface, surfaces);
    }

    private static LakeFaultContext BuildLakeFaultContext(TectonicBoundaryMap boundaries, int width, int height)
    {
        var influence = new double[width * height];
        var axisX = new double[width * height];
        var axisY = new double[width * height];
        var radius = Math.Clamp((int)Math.Round(Math.Min(width, height) * 0.018), 2, 7);

        foreach (var segment in boundaries.Segments)
        {
            if (segment.Points.Count == 0)
                continue;

            var direction = ComputeSegmentDirection(segment.Points, width);
            var modeWeight = segment.BoundaryMode switch
            {
                BoundaryMode.ContinentalRift or BoundaryMode.Transtension => 1.0,
                BoundaryMode.PureTransform or BoundaryMode.Transpression => 0.86,
                BoundaryMode.ObliqueSubduction or BoundaryMode.OceanContinentSubduction or BoundaryMode.OceanOceanSubduction => 0.74,
                BoundaryMode.ContinentContinentCollision => 0.66,
                _ => 0.48
            };
            var strength = Math.Clamp((0.35 + segment.Activity * 0.75) * modeWeight, 0, 1);

            foreach (var point in segment.Points)
            {
                foreach (var stamped in PointsInRadius(width, height, point, radius))
                {
                    var distance = Distance(point, stamped, width);
                    var falloff = Math.Clamp(1.0 - distance / Math.Max(1.0, radius + 0.5), 0, 1);
                    var value = strength * SmoothStep(falloff);
                    var index = stamped.Y * width + stamped.X;
                    if (value <= influence[index])
                        continue;

                    influence[index] = value;
                    axisX[index] = direction.X;
                    axisY[index] = direction.Y;
                }
            }
        }

        return new LakeFaultContext(influence, axisX, axisY);
    }

    private static GridVector ComputeSegmentDirection(IReadOnlyList<GridPoint> points, int width)
    {
        if (points.Count < 2)
            return new GridVector(1, 0);

        var first = points[0];
        var last = points[^1];
        var dx = WrappedDeltaX(last.X - first.X, width);
        var dy = last.Y - first.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length <= 0.0001 ? new GridVector(1, 0) : new GridVector(dx / length, dy / length);
    }

    private static LakeMetrics ComputeLakeMetrics(
        WaterBodyId id,
        WaterBodyKind kind,
        IReadOnlyList<GridPoint> cells,
        IReadOnlyList<GridPoint> shoreline,
        double[] elevation,
        double[] riftProvince,
        double[] riftGraben,
        double[] volcanism,
        double[] heatFlow,
        double[] ridgeContinuity,
        double[] foothillInfluence,
        double[] roughness,
        LakeFaultContext faultContext,
        int width,
        ElevationGenerationOptions options)
    {
        var centroid = ComputeCentroid(cells);
        var meanShoreline = shoreline.Count > 0
            ? shoreline.Select(p => elevation[p.Y * width + p.X]).Average()
            : cells.Select(p => elevation[p.Y * width + p.X]).DefaultIfEmpty(options.MinLandElevationMeters).Average();
        var sortedShoreline = shoreline.Select(p => elevation[p.Y * width + p.X]).Order().ToList();
        var relief = sortedShoreline.Count >= 2
            ? PercentileSorted(sortedShoreline, 0.84) - PercentileSorted(sortedShoreline, 0.16)
            : 0.0;
        var avgRift = AverageCellSignal(cells, riftProvince, width);
        var avgGraben = AverageCellSignal(cells, riftGraben, width);
        var avgVolcanism = AverageCellSignal(cells, volcanism, width);
        var avgHeatFlow = AverageCellSignal(cells, heatFlow, width);
        var avgRidge = AverageCellSignal(shoreline.Count > 0 ? shoreline : cells, ridgeContinuity, width);
        var avgFoothill = AverageCellSignal(shoreline.Count > 0 ? shoreline : cells, foothillInfluence, width);
        var avgRoughness = AverageCellSignal(shoreline.Count > 0 ? shoreline : cells, roughness, width);
        var faultInfluence = AverageCellSignal(cells, faultContext.Influence, width);
        var tectonicInfluence = Math.Clamp(faultInfluence * 0.75 + avgRift * 0.45 + avgGraben * 0.65, 0, 1);
        var volcanicInfluence = Math.Clamp(avgVolcanism * 0.68 + avgHeatFlow * 0.32, 0, 1);
        var shape = ComputeLakeShape(cells, centroid, width);

        var mountainSignal = Math.Clamp(avgRidge * 0.64 + avgFoothill * 0.44 + avgRoughness * 0.36, 0, 1);
        var location = ClassifyLakeLocation(meanShoreline, relief, mountainSignal, volcanicInfluence, options);
        var random = HashUnit(id.Value, cells.Count, 2701);
        var origin = ClassifyLakeOrigin(location, tectonicInfluence, volcanicInfluence, relief, shape.Roundness, cells.Count, random, options);
        var profile = origin switch
        {
            LakeOriginKind.Tectonic => LakeProfileKind.TectonicTrough,
            LakeOriginKind.VolcanicKarst => LakeProfileKind.VolcanicCone,
            LakeOriginKind.Glacial => LakeProfileKind.MountainBowl,
            _ => location == LakeLocationKind.Mountain ? LakeProfileKind.MountainBowl : LakeProfileKind.PlainGaussian
        };

        var axis = ComputeMeanAxis(cells, faultContext, width);
        if (Math.Abs(axis.X) + Math.Abs(axis.Y) <= 0.0001)
            axis = shape.Axis;

        return new LakeMetrics(
            id,
            kind,
            cells.Count,
            centroid,
            location,
            origin,
            profile,
            meanShoreline,
            Math.Max(0.0, relief),
            tectonicInfluence,
            volcanicInfluence,
            avgRift,
            avgGraben,
            shape.SizeNormalized,
            shape.Roundness,
            shape.Elongation,
            axis,
            Lerp(options.LakeDepthRandomnessMin, options.LakeDepthRandomnessMax, HashUnit(id.Value, cells.Count, 2711)));
    }

    private static LakeLocationKind ClassifyLakeLocation(
        double meanShorelineElevation,
        double shorelineRelief,
        double mountainSignal,
        double volcanicInfluence,
        ElevationGenerationOptions options)
    {
        var isHigh = meanShorelineElevation >= options.PlateauLakeElevationMeters;
        var isSteep = shorelineRelief >= options.MountainLakeReliefMeters || mountainSignal >= 0.42;

        if (isHigh && !isSteep)
            return LakeLocationKind.Plateau;

        if (meanShorelineElevation >= options.MountainLakeElevationMeters && (isSteep || mountainSignal >= 0.28))
            return LakeLocationKind.Mountain;

        if (isHigh || (volcanicInfluence >= options.LakeVolcanicInfluenceThreshold * 0.75 && meanShorelineElevation >= options.MountainLakeElevationMeters))
            return LakeLocationKind.Plateau;

        return LakeLocationKind.Plain;
    }

    private static LakeOriginKind ClassifyLakeOrigin(
        LakeLocationKind location,
        double tectonicInfluence,
        double volcanicInfluence,
        double shorelineRelief,
        double roundness,
        int cellCount,
        double random,
        ElevationGenerationOptions options)
    {
        if (tectonicInfluence >= options.LakeTectonicFaultThreshold)
            return LakeOriginKind.Tectonic;

        if (volcanicInfluence >= options.LakeVolcanicInfluenceThreshold && (roundness > 0.38 || cellCount < 260))
            return LakeOriginKind.VolcanicKarst;

        if (location == LakeLocationKind.Mountain && shorelineRelief >= options.MountainLakeReliefMeters * 0.72)
            return LakeOriginKind.Glacial;

        if (location == LakeLocationKind.Plateau && volcanicInfluence >= options.LakeVolcanicInfluenceThreshold * 0.68 && roundness > 0.30)
            return LakeOriginKind.VolcanicKarst;

        if (location == LakeLocationKind.Plain && roundness > 0.56 && random < options.PlainLakeKarstChance)
            return LakeOriginKind.VolcanicKarst;

        return LakeOriginKind.Erosional;
    }

    private static LakeShapeMetrics ComputeLakeShape(IReadOnlyList<GridPoint> cells, GridPoint centroid, int width)
    {
        if (cells.Count <= 1)
            return new LakeShapeMetrics(0, 0, 1, new GridVector(1, 0));

        var xx = 0.0;
        var yy = 0.0;
        var xy = 0.0;
        foreach (var cell in cells)
        {
            var dx = WrappedDeltaX(cell.X - centroid.X, width);
            var dy = cell.Y - centroid.Y;
            xx += dx * dx;
            yy += dy * dy;
            xy += dx * dy;
        }

        xx /= cells.Count;
        yy /= cells.Count;
        xy /= cells.Count;
        var trace = xx + yy;
        var determinant = xx * yy - xy * xy;
        var root = Math.Sqrt(Math.Max(0.0, trace * trace * 0.25 - determinant));
        var major = Math.Max(0.0001, trace * 0.5 + root);
        var minor = Math.Max(0.0001, trace * 0.5 - root);
        var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
        var elongation = Math.Sqrt(major / minor);
        var area = cells.Count;
        var radiusEquivalent = Math.Sqrt(area / Math.PI);
        var roundness = Math.Clamp(1.0 / Math.Max(1.0, elongation) * Math.Clamp(radiusEquivalent / Math.Sqrt(major), 0.2, 1.2), 0, 1);
        var sizeNormalized = Math.Clamp(Math.Sqrt(area) / 120.0, 0, 1);
        var axis = new GridVector(Math.Cos(angle), Math.Sin(angle));
        return new LakeShapeMetrics(sizeNormalized, roundness, elongation, axis);
    }

    private static GridPoint ComputeCentroid(IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count == 0)
            return new GridPoint(0, 0);

        var x = (int)Math.Round(cells.Average(p => p.X));
        var y = (int)Math.Round(cells.Average(p => p.Y));
        return new GridPoint(x, y);
    }

    private static GridVector ComputeMeanAxis(IReadOnlyList<GridPoint> cells, LakeFaultContext faultContext, int width)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var cell in cells)
        {
            var index = cell.Y * width + cell.X;
            x += faultContext.AxisX[index];
            y += faultContext.AxisY[index];
        }

        var length = Math.Sqrt(x * x + y * y);
        return length <= 0.0001 ? new GridVector(0, 0) : new GridVector(x / length, y / length);
    }

    private static double AverageCellSignal(IReadOnlyList<GridPoint> cells, double[] values, int width)
    {
        if (cells.Count == 0)
            return 0;

        var sum = 0.0;
        foreach (var cell in cells)
            sum += values[cell.Y * width + cell.X];

        return sum / cells.Count;
    }

    private static List<GridPoint> FindShoreline(
        MapMask mask,
        IReadOnlyList<GridPoint> waterCells,
        Func<GridPoint, bool> isHydrologyWater)
    {
        var shoreline = new HashSet<GridPoint>();
        foreach (var cell in waterCells)
        {
            foreach (var neighbor in Neighbors4(cell, mask.Width, mask.Height))
            {
                if (!isHydrologyWater(neighbor))
                    shoreline.Add(neighbor);
            }
        }

        return shoreline.ToList();
    }

    private static void LiftShorelineRim(
        MapMask mask,
        IReadOnlyList<GridPoint> shoreline,
        double[] elevation,
        double rimFloor,
        Func<GridPoint, bool> isHydrologyWater)
    {
        foreach (var point in shoreline)
        {
            if (isHydrologyWater(point))
                continue;

            var index = point.Y * mask.Width + point.X;
            elevation[index] = Math.Max(elevation[index], rimFloor);
        }
    }

    private static void ShapeLakeBed(
        MapMask mask,
        IReadOnlyList<GridPoint> waterCells,
        double[] elevation,
        double surface,
        double maxDepth,
        LakeMetrics metrics,
        ElevationGenerationOptions options)
    {
        if (waterCells.Count == 0)
            return;

        var waterSet = waterCells.ToHashSet();
        var distances = waterCells.ToDictionary(p => p, _ => double.PositiveInfinity);
        var queue = new Queue<GridPoint>();
        foreach (var cell in waterCells)
        {
            if (Neighbors4(cell, mask.Width, mask.Height).Any(n => !waterSet.Contains(n)))
            {
                distances[cell] = 0;
                queue.Enqueue(cell);
            }
        }

        if (queue.Count == 0)
        {
            foreach (var cell in waterCells)
            {
                distances[cell] = Distance(cell, metrics.Centroid, mask.Width);
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distances[current];
            foreach (var neighbor in Neighbors4(current, mask.Width, mask.Height))
            {
                if (!waterSet.Contains(neighbor))
                    continue;

                var next = currentDistance + 1;
                if (next >= distances[neighbor])
                    continue;

                distances[neighbor] = next;
                queue.Enqueue(neighbor);
            }
        }

        var maxDistance = Math.Max(1.0, distances.Values.Where(d => !double.IsInfinity(d)).DefaultIfEmpty(1.0).Max());
        var basins = BuildLakeDepressionBasins(waterCells, metrics, maxDistance, options);
        foreach (var cell in waterCells)
        {
            var index = cell.Y * mask.Width + cell.X;
            var normalized = Math.Clamp(distances[cell] / maxDistance, 0, 1);
            var profile = ComputeLakeDepthProfile(cell, normalized, maxDistance, metrics, mask.Width);
            profile = Math.Clamp(profile + ComputeLakeDepressionBoost(cell, basins, mask.Width), 0, 1);
            var noiseScale = metrics.Profile switch
            {
                LakeProfileKind.VolcanicCone => 0.035,
                LakeProfileKind.TectonicTrough => 0.055,
                LakeProfileKind.PlainGaussian => 0.075,
                _ => 0.065
            };
            var noise = (SmoothNoise(cell.X + 31, cell.Y - 17, 2609, 19.0) - 0.5) * maxDepth * noiseScale * Math.Clamp(normalized, 0.15, 1.0);
            var depth = Math.Clamp(options.MinLakeDepthMeters + (maxDepth - options.MinLakeDepthMeters) * profile + noise, options.MinLakeDepthMeters, maxDepth);
            elevation[index] = surface - depth;
        }
    }

    private static double ComputeLakeDepthProfile(GridPoint cell, double normalizedShoreDistance, double maxShoreDistance, LakeMetrics metrics, int width)
    {
        var n = Math.Clamp(normalizedShoreDistance, 0, 1);
        return metrics.Profile switch
        {
            LakeProfileKind.MountainBowl => 1.0 - Math.Exp(-3.1 * Math.Pow(n, 0.82)),
            LakeProfileKind.PlainGaussian => 1.0 - Math.Exp(-2.35 * n * n),
            LakeProfileKind.VolcanicCone => Math.Pow(n, 0.92),
            LakeProfileKind.TectonicTrough => ComputeTectonicTroughProfile(cell, n, maxShoreDistance, metrics, width),
            _ => SmoothStep(n)
        };
    }

    private static double ComputeTectonicTroughProfile(GridPoint cell, double normalizedShoreDistance, double maxShoreDistance, LakeMetrics metrics, int width)
    {
        var dx = WrappedDeltaX(cell.X - metrics.Centroid.X, width);
        var dy = cell.Y - metrics.Centroid.Y;
        var axisX = metrics.Axis.X;
        var axisY = metrics.Axis.Y;
        var side = Math.Abs(dx * -axisY + dy * axisX);
        var trough = 1.0 - Math.Clamp(side / Math.Max(1.0, maxShoreDistance * 0.42), 0, 1);
        return Math.Clamp(0.38 + normalizedShoreDistance * 0.46 + trough * 0.36, 0, 1);
    }

    private static IReadOnlyList<LakeDepressionBasin> BuildLakeDepressionBasins(
        IReadOnlyList<GridPoint> waterCells,
        LakeMetrics metrics,
        double maxShoreDistance,
        ElevationGenerationOptions options)
    {
        if (options.LargeLakeDepressionMinCellCount <= 0 || waterCells.Count < options.LargeLakeDepressionMinCellCount)
            return [];

        var count = Math.Clamp(waterCells.Count / Math.Max(1, options.LargeLakeDepressionMinCellCount), 1, 4);
        var basins = new List<LakeDepressionBasin>(count);
        for (var index = 0; index < count; index++)
        {
            var pick = (int)Math.Floor(HashUnit(metrics.Id.Value, index, 2729) * waterCells.Count);
            var center = waterCells[Math.Clamp(pick, 0, waterCells.Count - 1)];
            var radius = Math.Max(2.0, maxShoreDistance * Lerp(0.18, 0.34, HashUnit(metrics.Id.Value, index, 2731)));
            var strength = Lerp(0.08, 0.20, HashUnit(metrics.Id.Value, index, 2737));
            basins.Add(new LakeDepressionBasin(center, radius, strength));
        }

        return basins;
    }

    private static double ComputeLakeDepressionBoost(GridPoint cell, IReadOnlyList<LakeDepressionBasin> basins, int width)
    {
        var boost = 0.0;
        foreach (var basin in basins)
        {
            var distance = Distance(cell, basin.Center, width);
            var influence = Math.Clamp(1.0 - distance / basin.Radius, 0, 1);
            boost += influence * influence * basin.Strength;
        }

        return boost;
    }

    private static double ComputeLakeSurfaceMargin(int cellCount, int mapArea, ElevationGenerationOptions options)
    {
        var size = Math.Clamp(Math.Sqrt(cellCount / (double)Math.Max(1, mapArea)) * 4.0, 0, 1);
        return Lerp(options.MinLakeSurfaceMarginMeters, options.MaxLakeSurfaceMarginMeters, size);
    }

    private static double ComputeLakeMaxDepth(LakeMetrics metrics, ElevationGenerationOptions options)
    {
        var size = metrics.SizeNormalized;
        var relief = metrics.ShorelineReliefMeters;
        var tectonic = metrics.TectonicInfluence;
        var volcanic = metrics.VolcanicInfluence;

        if (metrics.Kind == WaterBodyKind.InlandSea)
        {
            var inlandSeaCap = metrics.Origin == LakeOriginKind.Tectonic
                ? Math.Max(options.MaxInlandSeaDepthMeters, options.MaxRiftLakeDepthMeters)
                : options.MaxInlandSeaDepthMeters;
            var inlandSeaDepth = Lerp(20.0, inlandSeaCap, Math.Pow(size, 0.72)) + tectonic * 90.0 + relief * 0.025;
            return Math.Clamp(inlandSeaDepth * metrics.DepthRandomness, 20.0, inlandSeaCap);
        }

        if (metrics.CellCount <= 6)
        {
            var pond = Lerp(1.0, 8.0, Math.Clamp(metrics.CellCount / 18.0, 0, 1));
            return Math.Clamp(pond, options.MinLakeDepthMeters, Math.Min(8.0, options.MaxLakeDepthMeters));
        }

        var depth = metrics.Origin switch
        {
            LakeOriginKind.Tectonic => Lerp(18.0, options.MaxRiftLakeDepthMeters, Math.Pow(size, 0.86)) + tectonic * 140.0 + relief * 0.045,
            LakeOriginKind.Glacial => Lerp(9.0, options.MaxLakeDepthMeters * 1.35, Math.Pow(size, 0.78)) + relief * 0.075,
            LakeOriginKind.VolcanicKarst => Lerp(14.0, options.MaxLakeDepthMeters * 1.55, Math.Pow(size, 0.58)) + volcanic * 95.0,
            _ => Lerp(3.0, options.MaxLakeDepthMeters * 0.62, Math.Pow(size, 0.76)) + relief * 0.018
        };

        var cap = metrics.Origin switch
        {
            LakeOriginKind.Tectonic => options.MaxRiftLakeDepthMeters,
            LakeOriginKind.Glacial => Math.Min(options.MaxRiftLakeDepthMeters * 0.62, options.MaxLakeDepthMeters * 1.55),
            LakeOriginKind.VolcanicKarst => Math.Min(options.MaxRiftLakeDepthMeters * 0.70, options.MaxLakeDepthMeters * 2.2),
            _ => options.MaxLakeDepthMeters
        };

        return Math.Clamp(depth * metrics.DepthRandomness, options.MinLakeDepthMeters, cap);
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.Order().ToList();
        return PercentileSorted(sorted, percentile);
    }

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

        return Lerp(sortedValues[lower], sortedValues[upper], position - lower);
    }

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double SmoothNoise(int x, int y, int seed, double scale)
    {
        var sampleX = x / Math.Max(1.0, scale);
        var sampleY = y / Math.Max(1.0, scale);
        var x0 = (int)Math.Floor(sampleX);
        var y0 = (int)Math.Floor(sampleY);
        var tx = SmoothStep(sampleX - x0);
        var ty = SmoothStep(sampleY - y0);
        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);
        return Math.Clamp((Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty) + 1.0) * 0.5, 0, 1);
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

    private static IEnumerable<GridPoint> PointsInRadius(int width, int height, GridPoint center, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = center.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                yield return new GridPoint(WrapX(center.X + dx, width), y);
            }
        }
    }

    private static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height)
    {
        yield return new GridPoint(WrapX(point.X - 1, width), point.Y);
        yield return new GridPoint(WrapX(point.X + 1, width), point.Y);
        if (point.Y > 0) yield return new GridPoint(point.X, point.Y - 1);
        if (point.Y < height - 1) yield return new GridPoint(point.X, point.Y + 1);
    }

    private static double Distance(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, Math.Max(0, width - dx));
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private sealed record LakeFaultContext(double[] Influence, double[] AxisX, double[] AxisY);

    private sealed record LakeShapeMetrics(double SizeNormalized, double Roundness, double Elongation, GridVector Axis);

    private sealed record LakeDepressionBasin(GridPoint Center, double Radius, double Strength);

    private sealed record LakeMetrics(
        WaterBodyId Id,
        WaterBodyKind Kind,
        int CellCount,
        GridPoint Centroid,
        LakeLocationKind Location,
        LakeOriginKind Origin,
        LakeProfileKind Profile,
        double MeanShorelineElevationMeters,
        double ShorelineReliefMeters,
        double TectonicInfluence,
        double VolcanicInfluence,
        double RiftInfluence,
        double GrabenInfluence,
        double SizeNormalized,
        double Roundness,
        double Elongation,
        GridVector Axis,
        double DepthRandomness);
}
