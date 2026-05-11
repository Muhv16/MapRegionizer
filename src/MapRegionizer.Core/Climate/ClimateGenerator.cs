using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Climate;

public sealed class ClimateGenerator
{
    private readonly Random _random;

    public ClimateGenerator(int seed)
    {
        _random = new Random(seed);
    }

    public ClimateMap Generate(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology waterBodyTopology,
        WaterSurfaceMap waterSurfaces,
        HydrologyMap hydrology,
        ClimateGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(waterBodyTopology);
        ArgumentNullException.ThrowIfNull(waterSurfaces);
        ArgumentNullException.ThrowIfNull(hydrology);
        ArgumentNullException.ThrowIfNull(options);

        var width = elevation.Width;
        var height = elevation.Height;
        if (mask.Width != width || mask.Height != height)
            throw new ArgumentException("Mask and elevation dimensions must match.", nameof(mask));

        var length = width * height;
        var water = BuildWaterMask(elevation);
        var largeWater = BuildLargeWaterMask(elevation, waterBodyTopology, waterSurfaces, options);
        var distanceToLargeWater = ComputeDistance(width, height, largeWater);
        var distanceToCoast = ComputeDistance(width, height, BuildCoastMask(width, height, water));
        var riverInfluence = BuildRiverInfluence(hydrology);

        var latitudeNorm = new double[length];
        var meanAnnualTemperature = new double[length];
        var summerTemperature = new double[length];
        var winterTemperature = new double[length];
        var seasonality = new double[length];
        var atmosphericMoisture = new double[length];
        var precipitation = new double[length];
        var moisture = new double[length];
        var biomeMoisture = new double[length];
        var rainShadow = new double[length];
        var monsoonInfluence = new double[length];
        var riverValleyInfluence = new double[length];
        var wetlandInfluence = new double[length];
        var snowOverlay = new double[length];
        var mountainOverlay = new double[length];
        var iceScore = new double[length];
        var habitability = new double[length];
        var agriculturalPotential = new double[length];
        var climateClasses = new byte[length];
        var biomes = new byte[length];

        BuildTemperatureFields(
            elevation,
            water,
            distanceToLargeWater,
            latitudeNorm,
            meanAnnualTemperature,
            summerTemperature,
            winterTemperature,
            seasonality,
            options);

        BuildMoistureFields(
            elevation,
            water,
            largeWater,
            distanceToLargeWater,
            riverInfluence,
            meanAnnualTemperature,
            atmosphericMoisture,
            precipitation,
            rainShadow,
            options);

        ApplyContinentalityAndMonsoons(
            elevation,
            water,
            largeWater,
            distanceToLargeWater,
            distanceToCoast,
            riverInfluence,
            latitudeNorm,
            meanAnnualTemperature,
            summerTemperature,
            winterTemperature,
            seasonality,
            atmosphericMoisture,
            precipitation,
            moisture,
            rainShadow,
            monsoonInfluence,
            options);

        SmoothUnitField(moisture, width, height, passes: 1, selfWeight: 0.52);
        SmoothUnitField(precipitation, width, height, passes: 1, selfWeight: 0.66);
        BuildBiomeMoisture(
            elevation,
            water,
            distanceToLargeWater,
            riverInfluence,
            meanAnnualTemperature,
            precipitation,
            moisture,
            rainShadow,
            biomeMoisture,
            options);

        BuildOverlayFields(
            elevation,
            water,
            distanceToLargeWater,
            riverInfluence,
            meanAnnualTemperature,
            summerTemperature,
            precipitation,
            biomeMoisture,
            riverValleyInfluence,
            wetlandInfluence,
            snowOverlay,
            mountainOverlay,
            options);

        ClassifyClimate(
            elevation,
            water,
            riverInfluence,
            wetlandInfluence,
            snowOverlay,
            mountainOverlay,
            latitudeNorm,
            meanAnnualTemperature,
            summerTemperature,
            winterTemperature,
            seasonality,
            precipitation,
            moisture,
            biomeMoisture,
            monsoonInfluence,
            iceScore,
            habitability,
            agriculturalPotential,
            climateClasses,
            biomes,
            options);

        return new ClimateMap(
            width,
            height,
            latitudeNorm,
            meanAnnualTemperature,
            summerTemperature,
            winterTemperature,
            seasonality,
            atmosphericMoisture,
            precipitation,
            moisture,
            biomeMoisture,
            rainShadow,
            monsoonInfluence,
            riverValleyInfluence,
            wetlandInfluence,
            snowOverlay,
            mountainOverlay,
            iceScore,
            habitability,
            agriculturalPotential,
            climateClasses,
            biomes);
    }

    private static bool[] BuildWaterMask(ElevationMap elevation)
    {
        var result = new bool[elevation.Width * elevation.Height];
        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
                result[y * elevation.Width + x] = elevation.HasWaterSurface(x, y);
        }

        return result;
    }

    private static bool[] BuildLargeWaterMask(
        ElevationMap elevation,
        WaterBodyTopology waterBodyTopology,
        WaterSurfaceMap waterSurfaces,
        ClimateGenerationOptions options)
    {
        var result = new bool[elevation.Width * elevation.Height];
        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
            {
                if (!elevation.HasWaterSurface(x, y))
                    continue;

                var kind = waterBodyTopology.GetKind(x, y);
                var bodyId = waterBodyTopology.GetWaterBodyId(x, y);
                var body = bodyId.HasValue ? waterSurfaces.GetBodySurface(bodyId.Value) : null;
                var isLargeBody = body is not null &&
                    (body.Kind is WaterBodyKind.Ocean or WaterBodyKind.OceanSea or WaterBodyKind.InlandSea ||
                     body.CellCount >= options.LargeLakeMinCellCount);
                var isLargeKind = kind.HasValue && (kind.Value is WaterBodyKind.Ocean or WaterBodyKind.OceanSea or WaterBodyKind.InlandSea);
                result[y * elevation.Width + x] = isLargeKind || isLargeBody;
            }
        }

        return result;
    }

    private static bool[] BuildCoastMask(int width, int height, bool[] water)
    {
        var coast = new bool[water.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (water[index])
                    continue;

                foreach (var neighbor in EnumerateNeighbors4(x, y, width, height))
                {
                    if (water[neighbor.Y * width + neighbor.X])
                    {
                        coast[index] = true;
                        break;
                    }
                }
            }
        }

        return coast;
    }

    private static int[] ComputeDistance(int width, int height, bool[] sources)
    {
        var distance = Enumerable.Repeat(int.MaxValue, width * height).ToArray();
        var queue = new Queue<GridPoint>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (!sources[index])
                    continue;

                distance[index] = 0;
                queue.Enqueue(new GridPoint(x, y));
            }
        }

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            var nextDistance = distance[point.Y * width + point.X] + 1;
            foreach (var neighbor in EnumerateNeighbors4(point.X, point.Y, width, height))
            {
                var index = neighbor.Y * width + neighbor.X;
                if (distance[index] <= nextDistance)
                    continue;

                distance[index] = nextDistance;
                queue.Enqueue(neighbor);
            }
        }

        return distance;
    }

    private static double[] BuildRiverInfluence(HydrologyMap hydrology)
    {
        var width = hydrology.Width;
        var height = hydrology.Height;
        var result = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!hydrology.IsRiverCell(x, y))
                    continue;

                var accumulation = hydrology.GetFlowAccumulation(x, y);
                result[y * width + x] = Math.Clamp(Math.Log10(accumulation + 1.0) / 3.2, 0.18, 1.0);
            }
        }

        foreach (var mouth in hydrology.Mouths)
            AddInfluence(result, width, height, mouth.Cell, mouth.Kind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta ? 0.95 : 0.55, 3);

        var spread = result.ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            Array.Copy(spread, result, result.Length);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var best = result[y * width + x];
                    foreach (var neighbor in EnumerateNeighbors8(x, y, width, height))
                        best = Math.Max(best, result[neighbor.Y * width + neighbor.X] * 0.58);

                    spread[y * width + x] = Math.Max(spread[y * width + x], best);
                }
            }
        }

        return spread;
    }

    private static void AddInfluence(double[] field, int width, int height, GridPoint center, double value, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = center.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius)
                    continue;

                var x = WrapX(center.X + dx, width);
                var index = y * width + x;
                field[index] = Math.Max(field[index], value * (1.0 - distance / (radius + 1.0)));
            }
        }
    }

    private static void BuildTemperatureFields(
        ElevationMap elevation,
        bool[] water,
        int[] distanceToLargeWater,
        double[] latitudeNorm,
        double[] meanAnnualTemperature,
        double[] summerTemperature,
        double[] winterTemperature,
        double[] seasonality,
        ClimateGenerationOptions options)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        var maxLatitudeNorm = 1.0 - options.PolarLatitudeMargin;

        for (var y = 0; y < height; y++)
        {
            var rowLatitude = ComputeLatitudeNorm(y, height, maxLatitudeNorm);
            var latitudeCooling = Math.Pow(rowLatitude, options.LatitudeCurveExponent) * options.PoleCoolingCelsius;
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var terrainClass = elevation.GetTerrainClass(x, y);
                var continentalityRaw = NormalizeDistance(distanceToLargeWater[index], options.ContinentalityDistanceCells);
                var continentality = Math.Pow(continentalityRaw, 1.35);
                var temp = options.EquatorTemperatureCelsius - latitudeCooling;
                var exposedElevation = water[index] ? 0.0 : Math.Max(0.0, elevation.GetElevation(x, y));
                temp -= exposedElevation * options.LapseRateCelsiusPerMeter;
                temp += GetTerrainTemperatureModifier(terrainClass);

                if (water[index])
                    temp = temp * 0.86 + (options.EquatorTemperatureCelsius - latitudeCooling * 0.75) * 0.14;

                var localSeasonality = options.BaseSeasonalityCelsius +
                    rowLatitude * options.LatitudeSeasonalityCelsius +
                    continentality * options.ContinentalSeasonalityCelsius;
                if (water[index])
                    localSeasonality *= 0.38;

                latitudeNorm[index] = rowLatitude;
                seasonality[index] = localSeasonality;
                meanAnnualTemperature[index] = temp;
                summerTemperature[index] = temp + localSeasonality * 0.5 + continentality * options.ContinentalSummerBoostCelsius;
                winterTemperature[index] = temp - localSeasonality * 0.5 - continentality * options.ContinentalWinterPenaltyCelsius;
            }
        }
    }

    private static double GetTerrainTemperatureModifier(TerrainClassKind terrainClass) => terrainClass switch
    {
        TerrainClassKind.Mountain => -2.2,
        TerrainClassKind.Highland => -0.9,
        TerrainClassKind.DryBasin => 1.0,
        TerrainClassKind.DesertPlateauCandidate => 1.4,
        TerrainClassKind.SedimentaryBasin => 0.4,
        TerrainClassKind.CoastalPlain => 0.25,
        TerrainClassKind.AlluvialPlain => 0.15,
        _ => 0
    };

    private void BuildMoistureFields(
        ElevationMap elevation,
        bool[] water,
        bool[] largeWater,
        int[] distanceToLargeWater,
        double[] riverInfluence,
        double[] meanAnnualTemperature,
        double[] atmosphericMoisture,
        double[] precipitation,
        double[] rainShadow,
        ClimateGenerationOptions options)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        var outgoing = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            var wind = GetWind(y, height);
            var start = wind.X >= 0 ? 0 : width - 1;
            var end = wind.X >= 0 ? width : -1;
            var step = wind.X >= 0 ? 1 : -1;

            var previous = 0.0;
            for (var x = start; x != end; x += step)
            {
                var index = y * width + x;
                var upwindX = WrapX(x - step, width);
                var upwindIndex = y * width + upwindX;
                var verticalY = Math.Clamp(y - Math.Sign(wind.Y), 0, height - 1);
                var verticalIndex = verticalY * width + x;
                var incoming = previous * options.MoistureRetention +
                    outgoing[verticalIndex] * Math.Abs(wind.Y) * 0.28 +
                    LocalEvaporation(index, water, largeWater, distanceToLargeWater, riverInfluence, meanAnnualTemperature, options);

                var slope = elevation.GetElevation(x, y) - elevation.GetElevation(upwindX, y);
                var terrainClass = elevation.GetTerrainClass(x, y);
                var mountainFactor = terrainClass == TerrainClassKind.Mountain ? 0.38 :
                    terrainClass == TerrainClassKind.Highland ? 0.18 : 0.0;
                mountainFactor += elevation.GetRidgeContinuity(x, y) * 0.32 + elevation.GetFoothillInfluence(x, y) * 0.16;

                var temperatureFactor = Math.Clamp((meanAnnualTemperature[index] + 12.0) / 42.0, 0.12, 1.15);
                var baseRain = incoming * options.BaseRainfallEfficiency * temperatureFactor;
                var orographicRain = Math.Max(0.0, slope) / 1000.0 * options.OrographicStrength * (1.0 + mountainFactor);
                var rain = Math.Min(incoming * 0.52, baseRain + orographicRain);
                var descentDrying = Math.Max(0.0, -slope) / 1100.0 * options.DescentDrying * (1.0 + mountainFactor);
                var shadow = Math.Clamp(rainShadow[upwindIndex] * 0.72 + descentDrying + GetTerrainDryness(terrainClass) * 0.12, 0, 1);

                rain *= 1.0 - shadow * 0.38;
                atmosphericMoisture[index] = Math.Clamp(incoming, 0, 1.8);
                precipitation[index] = Math.Clamp(rain, 0, 1.4);
                rainShadow[index] = shadow;
                outgoing[index] = Math.Clamp(incoming - rain - descentDrying * 0.38, 0, 1.4);
                previous = outgoing[index];
            }
        }

        AddFineClimateNoise(precipitation, 0.035);
    }

    private static double LocalEvaporation(
        int index,
        bool[] water,
        bool[] largeWater,
        int[] distanceToLargeWater,
        double[] riverInfluence,
        double[] meanAnnualTemperature,
        ClimateGenerationOptions options)
    {
        var warmth = Math.Clamp((meanAnnualTemperature[index] + 8.0) / 38.0, 0.08, 1.18);
        if (largeWater[index])
            return options.OceanEvaporation * warmth;
        if (water[index])
            return options.LakeEvaporation * warmth;

        var coastalEvaporation = Math.Max(0.0, 1.0 - distanceToLargeWater[index] / 12.0) * 0.12;
        return (options.LandEvapotranspiration + riverInfluence[index] * 0.08 + coastalEvaporation) * warmth;
    }

    private static double GetTerrainDryness(TerrainClassKind terrainClass) => terrainClass switch
    {
        TerrainClassKind.DryBasin => 0.55,
        TerrainClassKind.DesertPlateauCandidate => 0.62,
        TerrainClassKind.Mountain => 0.20,
        TerrainClassKind.Highland => 0.14,
        _ => 0
    };

    private void ApplyContinentalityAndMonsoons(
        ElevationMap elevation,
        bool[] water,
        bool[] largeWater,
        int[] distanceToLargeWater,
        int[] distanceToCoast,
        double[] riverInfluence,
        double[] latitudeNorm,
        double[] meanAnnualTemperature,
        double[] summerTemperature,
        double[] winterTemperature,
        double[] seasonality,
        double[] atmosphericMoisture,
        double[] precipitation,
        double[] moisture,
        double[] rainShadow,
        double[] monsoonInfluence,
        ClimateGenerationOptions options)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        for (var y = 0; y < height; y++)
        {
            var signedLatitude = ComputeSignedLatitude(y, height);
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var continentality = NormalizeDistance(distanceToLargeWater[index], options.ContinentalityDistanceCells);
                var tropicalBand = Math.Clamp((0.62 - latitudeNorm[index]) / 0.28, 0, 1);
                var warmOceanNearby = Math.Max(0.0, 1.0 - distanceToLargeWater[index] / (double)options.MonsoonOceanDistanceCells) *
                    Math.Clamp((summerTemperature[index] - 16.0) / 12.0, 0, 1);
                var exposure = ComputeCoastalMonsoonExposure(x, y, signedLatitude, width, height, largeWater, options.MonsoonCoastProbeCells);
                var interior = Math.Clamp(distanceToCoast[index] / 12.0, 0.25, 1.0);
                var monsoon = water[index] ? 0.0 : warmOceanNearby * tropicalBand * exposure * interior;
                monsoonInfluence[index] = Math.Clamp(monsoon, 0, 1);

                precipitation[index] = Math.Clamp(precipitation[index] + monsoon * options.MonsoonRainStrength, 0, 1.6);
                seasonality[index] += monsoon * 5.0;
                summerTemperature[index] += monsoon * 0.9;
                winterTemperature[index] -= monsoon * options.DrySeasonStrength * 2.0;

                var coastalHumidityRaw = Math.Max(
                    0.0,
                    1.0 - distanceToLargeWater[index] / Math.Max(1.0, options.ContinentalityDistanceCells * 1.0));

                var coastalHumidity = Math.Sqrt(coastalHumidityRaw);

                var localMoisture = precipitation[index] * 0.66 +
                    Math.Clamp(atmosphericMoisture[index], 0, 1) * 0.24 +
                    coastalHumidity * 0.22 -
                    continentality * options.ContinentalDrying -
                    rainShadow[index] * 0.18 +
                    riverInfluence[index] * options.RiverMoistureBonus;

                if (water[index])
                    localMoisture = Math.Max(localMoisture, largeWater[index] ? 0.82 : 0.62);

                moisture[index] = Math.Clamp(localMoisture, 0, 1);
            }
        }
    }

    private static double ComputeCoastalMonsoonExposure(
        int x,
        int y,
        double signedLatitude,
        int width,
        int height,
        bool[] largeWater,
        int probeCells)
    {
        if (probeCells <= 0)
            return 0.65;

        var eastWater = DirectionalWaterProximity(x, y, 1, 0, width, height, largeWater, probeCells);
        var equatorDy = signedLatitude >= 0 ? 1 : -1;
        var equatorWater = DirectionalWaterProximity(x, y, 0, equatorDy, width, height, largeWater, probeCells);
        var general = Math.Max(eastWater, equatorWater);
        return Math.Clamp(0.35 + general * 0.75, 0, 1);
    }

    private static double DirectionalWaterProximity(int x, int y, int dx, int dy, int width, int height, bool[] water, int maxDistance)
    {
        for (var distance = 1; distance <= maxDistance; distance++)
        {
            var yy = y + dy * distance;
            if (yy < 0 || yy >= height)
                break;

            var xx = WrapX(x + dx * distance, width);
            if (water[yy * width + xx])
                return 1.0 - (distance - 1) / (double)maxDistance;
        }

        return 0;
    }

    private static void BuildBiomeMoisture(
        ElevationMap elevation,
        bool[] water,
        int[] distanceToLargeWater,
        double[] riverInfluence,
        double[] meanAnnualTemperature,
        double[] precipitation,
        double[] moisture,
        double[] rainShadow,
        double[] biomeMoisture,
        ClimateGenerationOptions options)
    {
        Array.Copy(moisture, biomeMoisture, moisture.Length);
        NormalizeLandMoistureByBand(elevation, water, meanAnnualTemperature, biomeMoisture);

        var width = elevation.Width;
        var height = elevation.Height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (water[index])
                    continue;

                var coastalWetness = Math.Max(0.0, 1.0 - distanceToLargeWater[index] / Math.Max(1.0, options.ContinentalityDistanceCells * 0.82));
                var windwardWetness = Math.Clamp(precipitation[index] * 0.28 - rainShadow[index] * 0.12, -0.08, 0.24);
                var physicalWetnessGate = Math.Clamp((moisture[index] - 0.12) / 0.32, 0, 1);
                var riverBiomeBonus = riverInfluence[index] * (0.06 + physicalWetnessGate * 0.10);
                var terrain = elevation.GetTerrainClass(x, y);
                var basinPenalty = terrain is TerrainClassKind.DryBasin or TerrainClassKind.DesertPlateauCandidate ? 0.08 : 0.0;
                biomeMoisture[index] = Math.Clamp(
                    biomeMoisture[index] * 0.74 +
                    moisture[index] * 0.18 +
                    coastalWetness * 0.16 +
                    windwardWetness +
                    riverBiomeBonus -
                    basinPenalty,
                    0,
                    1);
            }
        }
    }

    private static void NormalizeLandMoistureByBand(ElevationMap elevation, bool[] water, double[] meanAnnualTemperature, double[] biomeMoisture)
    {
        var stats = new[] { new TemperatureBandStats(), new TemperatureBandStats(), new TemperatureBandStats(), new TemperatureBandStats() };
        for (var index = 0; index < biomeMoisture.Length; index++)
        {
            if (water[index])
                continue;

            var band = GetTemperatureBand(meanAnnualTemperature[index]);
            stats[band].Values.Add(biomeMoisture[index]);
        }

        var width = elevation.Width;
        for (var band = 0; band < stats.Length; band++)
            stats[band].Prepare();

        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
            {
                var index = y * width + x;
                if (water[index])
                    continue;

                var band = GetTemperatureBand(meanAnnualTemperature[index]);
                var normalized = stats[band].Normalize(biomeMoisture[index]);
                biomeMoisture[index] = Math.Clamp(biomeMoisture[index] * 0.42 + normalized * 0.58, 0, 1);
            }
        }
    }

    private static void BuildOverlayFields(
        ElevationMap elevation,
        bool[] water,
        int[] distanceToLargeWater,
        double[] riverInfluence,
        double[] meanAnnualTemperature,
        double[] summerTemperature,
        double[] precipitation,
        double[] biomeMoisture,
        double[] riverValleyInfluence,
        double[] wetlandInfluence,
        double[] snowOverlay,
        double[] mountainOverlay,
        ClimateGenerationOptions options)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (water[index])
                    continue;

                var terrain = elevation.GetTerrainClass(x, y);
                var elevationMeters = elevation.GetElevation(x, y);
                var ridge = elevation.GetRidgeContinuity(x, y);
                var foothill = elevation.GetFoothillInfluence(x, y);
                var lowland = 1.0 - Math.Clamp((elevationMeters - 120.0) / 520.0, 0, 1);
                var lakeLowland = Math.Max(0.0, 1.0 - distanceToLargeWater[index] / 6.0) * lowland;
                var warmWaterEdge = lakeLowland * Math.Clamp((meanAnnualTemperature[index] - 12.0) / 12.0, 0, 1);
                var river = Math.Pow(Math.Clamp(riverInfluence[index], 0, 1), 0.72);
                riverValleyInfluence[index] = Math.Clamp(river * (0.42 + lowland * 0.42) + warmWaterEdge * 0.22, 0, 1);

                var wetness = biomeMoisture[index];
                wetlandInfluence[index] = Math.Clamp(
                    riverValleyInfluence[index] * Math.Clamp((wetness - 0.42) / 0.34, 0, 1) +
                    lakeLowland * Math.Clamp((wetness - 0.34) / 0.38, 0, 1) +
                    precipitation[index] * lowland * 0.18,
                    0,
                    1);

                var mountainSignal = Math.Clamp(ridge * 0.70 + foothill * 0.32 + Math.Clamp((elevationMeters - 1200.0) / 2100.0, 0, 1), 0, 1);
                mountainOverlay[index] = mountainSignal;
                var coldHeight = Math.Clamp((elevationMeters - 1800.0) / 2200.0, 0, 1);
                var coldSummer = Math.Clamp((8.0 - summerTemperature[index]) / 14.0, 0, 1);
                var snow = Math.Clamp(coldHeight * 0.45 + coldSummer * 0.45 + precipitation[index] * 0.22, 0, 1) * Math.Clamp(mountainSignal * 1.25, 0, 1);
                if (terrain == TerrainClassKind.Mountain && summerTemperature[index] < options.SnowMeltThresholdCelsius + 5.0)
                    snow = Math.Max(snow, Math.Clamp((options.SnowMeltThresholdCelsius + 5.0 - summerTemperature[index]) / 12.0, 0, 1) * 0.62);

                snowOverlay[index] = Math.Clamp(snow, 0, 1);
            }
        }
    }

    private static void ClassifyClimate(
        ElevationMap elevation,
        bool[] water,
        double[] riverInfluence,
        double[] wetlandInfluence,
        double[] snowOverlay,
        double[] mountainOverlay,
        double[] latitudeNorm,
        double[] meanAnnualTemperature,
        double[] summerTemperature,
        double[] winterTemperature,
        double[] seasonality,
        double[] precipitation,
        double[] moisture,
        double[] biomeMoisture,
        double[] monsoonInfluence,
        double[] iceScore,
        double[] habitability,
        double[] agriculturalPotential,
        byte[] climateClasses,
        byte[] biomes,
        ClimateGenerationOptions options)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (water[index])
                {
                    climateClasses[index] = (byte)ClimateClassKind.Ocean;
                    biomes[index] = (byte)BiomeKind.Ocean;
                    moisture[index] = Math.Max(moisture[index], 0.84);
                    habitability[index] = 0;
                    agriculturalPotential[index] = 0;
                    continue;
                }

                var meanTemp = meanAnnualTemperature[index];
                var summer = summerTemperature[index];
                var winter = winterTemperature[index];
                var wetness = biomeMoisture[index];
                var rain = precipitation[index];
                var elevationMeters = elevation.GetElevation(x, y);
                var coldness = Math.Clamp((options.SnowMeltThresholdCelsius - summer) / 14.0, 0, 1);
                var snowAvailability = Math.Clamp(rain / options.SnowPrecipitationScale, 0, 1);
                var localIce = Math.Max(coldness * (0.32 + snowAvailability * 0.68), snowOverlay[index] * 0.72);
                if (elevation.GetTerrainClass(x, y) == TerrainClassKind.Mountain && summer < 6)
                    localIce = Math.Max(localIce, Math.Clamp((6.0 - summer) / 10.0 * (0.35 + rain), 0, 1));

                iceScore[index] = Math.Clamp(localIce, 0, 1);
                var (climateClass, biome) = ClassifyCell(
                    meanTemp,
                    summer,
                    winter,
                    wetness,
                    rain,
                    seasonality[index],
                    monsoonInfluence[index],
                    localIce,
                    elevationMeters,
                    mountainOverlay[index]);
                if (wetlandInfluence[index] > 0.72 && localIce < 0.20)
                    biome = meanTemp > 20 && latitudeNorm[index] < 0.42 ? BiomeKind.Mangrove : BiomeKind.Marsh;
                else if (wetlandInfluence[index] > 0.48 && localIce < 0.18)
                    biome = BiomeKind.Floodplain;
                else if (riverInfluence[index] > 0.58 && wetness > 0.32 && localIce < 0.16)
                    biome = BiomeKind.Floodplain;
                else if (snowOverlay[index] > 0.68 && mountainOverlay[index] > 0.42)
                    biome = BiomeKind.SnowyMountain;

                climateClasses[index] = (byte)climateClass;
                biomes[index] = (byte)biome;

                var temperatureComfort = 1.0 - Math.Clamp(Math.Abs(meanTemp - 15.0) / 26.0, 0, 1);
                var moistureComfort = 1.0 - Math.Clamp(Math.Abs(wetness - 0.52) / 0.52, 0, 1);
                var elevationPenalty = Math.Clamp((elevationMeters - 1800.0) / 2800.0, 0, 1);
                var icePenalty = Math.Clamp(localIce * 1.2, 0, 1);
                var riverBonus = riverInfluence[index] * 0.22;
                habitability[index] = Math.Clamp(temperatureComfort * 0.48 + moistureComfort * 0.38 + riverBonus - elevationPenalty * 0.28 - icePenalty * 0.55, 0, 1);

                var agricultureTemp = 1.0 - Math.Clamp(Math.Abs(meanTemp - 18.0) / 24.0, 0, 1);
                var agricultureWetness = wetness < 0.36 ? wetness / 0.36 : 1.0 - Math.Clamp((wetness - 0.78) / 0.22, 0, 1) * 0.28;
                agriculturalPotential[index] = Math.Clamp(
                    agricultureTemp * 0.36 +
                    agricultureWetness * 0.46 +
                    riverInfluence[index] * options.RiverAgricultureBonus -
                    elevationPenalty * 0.34 -
                    icePenalty * 0.72,
                    0,
                    1);
            }
        }

        ApplySoftBiomeTargets(
            elevation,
            water,
            latitudeNorm,
            meanAnnualTemperature,
            biomeMoisture,
            riverInfluence,
            wetlandInfluence,
            snowOverlay,
            mountainOverlay,
            climateClasses,
            biomes);
    }

    private static (ClimateClassKind ClimateClass, BiomeKind Biome) ClassifyCell(
        double meanTemp,
        double summerTemp,
        double winterTemp,
        double moisture,
        double precipitation,
        double seasonality,
        double monsoon,
        double iceScore,
        double elevationMeters,
        double mountainOverlay)
    {
        if (iceScore > 0.82)
            return (ClimateClassKind.IceCap, BiomeKind.IceSheet);
        if (mountainOverlay > 0.62 && iceScore > 0.46)
            return (ClimateClassKind.Alpine, BiomeKind.SnowyMountain);
        if (summerTemp < 5.0)
            return moisture < 0.22
                ? (ClimateClassKind.PolarDesert, BiomeKind.PolarDesert)
                : (ClimateClassKind.Tundra, BiomeKind.Tundra);
        if (elevationMeters > 2600 && summerTemp < 10.0)
            return (ClimateClassKind.Alpine, BiomeKind.AlpineTundra);

        if (moisture < 0.08)
            return meanTemp >= 17.0 ? (ClimateClassKind.HotArid, BiomeKind.RockyDesert) : (ClimateClassKind.SemiArid, BiomeKind.ColdDesert);
        if (moisture < 0.14)
            return meanTemp >= 17.0 ? (ClimateClassKind.HotArid, BiomeKind.HotDesert) : (ClimateClassKind.SemiArid, BiomeKind.ColdDesert);
        if (moisture < 0.20)
            return (ClimateClassKind.SemiArid, meanTemp >= 14.0 ? BiomeKind.SemiDesert : BiomeKind.Steppe);
        if (moisture < 0.27)
            return (ClimateClassKind.SemiArid, meanTemp >= 14.0 ? BiomeKind.XericShrubland : BiomeKind.Steppe);

        if (meanTemp >= 22.0)
        {
            if (moisture > 0.68)
                return (ClimateClassKind.TropicalWet, BiomeKind.TropicalRainforest);
            if (monsoon > 0.34 || precipitation > 0.52)
                return (ClimateClassKind.TropicalSeasonal, BiomeKind.MonsoonForest);
            if (moisture > 0.48)
                return (ClimateClassKind.TropicalSeasonal, BiomeKind.TropicalSeasonalForest);
            if (moisture > 0.36)
                return (ClimateClassKind.TropicalSeasonal, BiomeKind.DryTropicalForest);
            return (ClimateClassKind.TropicalSeasonal, BiomeKind.Savanna);
        }

        if (meanTemp >= 12.0)
        {
            if (moisture > 0.72)
                return (ClimateClassKind.TemperateWet, BiomeKind.TemperateRainforest);
            if (moisture > 0.38)
                return mountainOverlay > 0.48 ? (ClimateClassKind.WarmTemperate, BiomeKind.MontaneForest) : (ClimateClassKind.WarmTemperate, BiomeKind.TemperateForest);
            if (moisture > 0.29)
                return (ClimateClassKind.WarmTemperate, BiomeKind.OpenWoodland);
            if (seasonality > 18.0 || winterTemp < -3.0)
                return (ClimateClassKind.Continental, BiomeKind.TemperateGrassland);
            return (ClimateClassKind.WarmTemperate, BiomeKind.MediterraneanShrubland);
        }

        if (meanTemp >= 3.0)
        {
            if (moisture > 0.52)
                return mountainOverlay > 0.56
                    ? (ClimateClassKind.Alpine, BiomeKind.MontaneForest)
                    : winterTemp < -8.0 ? (ClimateClassKind.Boreal, BiomeKind.BorealForest) : (ClimateClassKind.TemperateWet, BiomeKind.TemperateForest);
            return (ClimateClassKind.Continental, BiomeKind.TemperateGrassland);
        }

        if (moisture > 0.36)
            return (ClimateClassKind.Boreal, BiomeKind.BorealForest);

        return (ClimateClassKind.Tundra, BiomeKind.Tundra);
    }

    private static void ApplySoftBiomeTargets(
        ElevationMap elevation,
        bool[] water,
        double[] latitudeNorm,
        double[] meanAnnualTemperature,
        double[] biomeMoisture,
        double[] riverInfluence,
        double[] wetlandInfluence,
        double[] snowOverlay,
        double[] mountainOverlay,
        byte[] climateClasses,
        byte[] biomes)
    {
        var landCount = water.Count(isWater => !isWater);
        if (landCount == 0)
            return;

        EnsureMinimumBiome(
            BiomeKind.Marsh,
            Math.Max(24, (int)Math.Round(landCount * 0.006)),
            BuildCandidates(elevation, water, index =>
            {
                var warmEnough = meanAnnualTemperature[index] > -2.0;
                var score = wetlandInfluence[index] * 1.15 + riverInfluence[index] * 0.22 + biomeMoisture[index] * 0.22;
                return warmEnough && wetlandInfluence[index] > 0.24 ? score : -1.0;
            }),
            climateClasses,
            biomes,
            index => meanAnnualTemperature[index] >= 20.0 && latitudeNorm[index] < 0.42 ? BiomeKind.Mangrove : wetlandInfluence[index] > 0.66 ? BiomeKind.Marsh : BiomeKind.Floodplain,
            index => meanAnnualTemperature[index] >= 20.0 ? ClimateClassKind.TropicalWet : ClimateClassKind.TemperateWet);

        var tropicalLand = CountLandWhere(water, meanAnnualTemperature, value => value >= 22.0);
        EnsureMinimumBiome(
            BiomeKind.TropicalRainforest,
            Math.Max(0, (int)Math.Round(tropicalLand * 0.045)),
            BuildCandidates(elevation, water, index =>
            {
                if (meanAnnualTemperature[index] < 22.0 || biomeMoisture[index] < 0.48)
                    return -1.0;

                return biomeMoisture[index] * 0.90 + wetlandInfluence[index] * 0.18 - mountainOverlay[index] * 0.10;
            }),
            climateClasses,
            biomes,
            _ => BiomeKind.TropicalRainforest,
            _ => ClimateClassKind.TropicalWet);

        var temperateWetLand = CountLandWhere(water, meanAnnualTemperature, value => value is >= 5.0 and < 22.0);
        EnsureMinimumBiome(
            BiomeKind.TemperateRainforest,
            Math.Max(0, (int)Math.Round(temperateWetLand * 0.025)),
            BuildCandidates(elevation, water, index =>
            {
                if (meanAnnualTemperature[index] < 5.0 || meanAnnualTemperature[index] >= 22.0 || biomeMoisture[index] < 0.50)
                    return -1.0;

                return biomeMoisture[index] * 0.86 + mountainOverlay[index] * 0.14 + wetlandInfluence[index] * 0.10;
            }),
            climateClasses,
            biomes,
            index => mountainOverlay[index] > 0.52 ? BiomeKind.CloudForest : BiomeKind.TemperateRainforest,
            _ => ClimateClassKind.TemperateWet);

        EnsureMinimumBiome(
            BiomeKind.SnowyMountain,
            Math.Max(0, (int)Math.Round(landCount * 0.004)),
            BuildCandidates(elevation, water, index =>
            {
                var score = snowOverlay[index] * 0.78 + mountainOverlay[index] * 0.42;
                return score > 0.42 ? score : -1.0;
            }),
            climateClasses,
            biomes,
            _ => BiomeKind.SnowyMountain,
            _ => ClimateClassKind.Alpine);

        LimitMediterraneanShare(elevation, water, meanAnnualTemperature, biomeMoisture, mountainOverlay, climateClasses, biomes, landCount);
    }

    private static IReadOnlyList<ClimateCandidate> BuildCandidates(ElevationMap elevation, bool[] water, Func<int, double> score)
    {
        var candidates = new List<ClimateCandidate>();
        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
            {
                var index = y * elevation.Width + x;
                if (water[index])
                    continue;

                var value = score(index);
                if (value > 0)
                    candidates.Add(new ClimateCandidate(index, value));
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates;
    }

    private static void EnsureMinimumBiome(
        BiomeKind countedBiome,
        int targetCount,
        IReadOnlyList<ClimateCandidate> candidates,
        byte[] climateClasses,
        byte[] biomes,
        Func<int, BiomeKind> chooseBiome,
        Func<int, ClimateClassKind> chooseClimateClass)
    {
        if (targetCount <= 0)
            return;

        var current = biomes.Count(value => (BiomeKind)value == countedBiome);
        if (countedBiome == BiomeKind.Marsh)
            current += biomes.Count(value => (BiomeKind)value is BiomeKind.Floodplain or BiomeKind.Mangrove or BiomeKind.Wetland);
        if (current >= targetCount)
            return;

        var needed = targetCount - current;
        foreach (var candidate in candidates)
        {
            var currentBiome = (BiomeKind)biomes[candidate.Index];
            if (currentBiome is BiomeKind.Ocean or BiomeKind.IceSheet or BiomeKind.SnowyMountain)
                continue;

            biomes[candidate.Index] = (byte)chooseBiome(candidate.Index);
            climateClasses[candidate.Index] = (byte)chooseClimateClass(candidate.Index);
            needed--;
            if (needed <= 0)
                break;
        }
    }

    private static void LimitMediterraneanShare(
        ElevationMap elevation,
        bool[] water,
        double[] meanAnnualTemperature,
        double[] biomeMoisture,
        double[] mountainOverlay,
        byte[] climateClasses,
        byte[] biomes,
        int landCount)
    {
        var maxMediterranean = Math.Max(1, (int)Math.Round(landCount * 0.045));
        var mediterranean = new List<ClimateCandidate>();
        for (var index = 0; index < biomes.Length; index++)
        {
            if (water[index] || (BiomeKind)biomes[index] != BiomeKind.MediterraneanShrubland)
                continue;

            var keepScore = 1.0 - Math.Abs(biomeMoisture[index] - 0.33) + Math.Clamp((20.0 - meanAnnualTemperature[index]) / 16.0, 0, 0.25);
            mediterranean.Add(new ClimateCandidate(index, keepScore));
        }

        if (mediterranean.Count <= maxMediterranean)
            return;

        mediterranean.Sort((a, b) => a.Score.CompareTo(b.Score));
        var convertCount = mediterranean.Count - maxMediterranean;
        for (var i = 0; i < convertCount; i++)
        {
            var index = mediterranean[i].Index;
            var replacement = biomeMoisture[index] < 0.30
                ? BiomeKind.XericShrubland
                : mountainOverlay[index] > 0.42 ? BiomeKind.OpenWoodland : BiomeKind.TemperateGrassland;
            biomes[index] = (byte)replacement;
            climateClasses[index] = (byte)(replacement == BiomeKind.TemperateGrassland ? ClimateClassKind.Continental : ClimateClassKind.WarmTemperate);
        }
    }

    private static int CountLandWhere(bool[] water, double[] values, Func<double, bool> predicate)
    {
        var count = 0;
        for (var index = 0; index < values.Length; index++)
        {
            if (!water[index] && predicate(values[index]))
                count++;
        }

        return count;
    }

    private static WindVector GetWind(int y, int height)
    {
        var latitudeNorm = ComputeLatitudeNorm(y, height, 1.0);
        var signedLatitude = ComputeSignedLatitude(y, height);

        if (latitudeNorm < 0.34)
            return new WindVector(-1, signedLatitude >= 0 ? 0.32 : -0.32);
        if (latitudeNorm < 0.67)
            return new WindVector(1, signedLatitude >= 0 ? -0.14 : 0.14);

        return new WindVector(-1, signedLatitude >= 0 ? 0.20 : -0.20);
    }

    private static double ComputeLatitudeNorm(int y, int height, double maxLatitudeNorm)
    {
        if (height <= 1)
            return 0;

        var normalizedY = (y + 0.5) / height;
        return Math.Clamp(Math.Abs(normalizedY * 2.0 - 1.0) * maxLatitudeNorm, 0, 1);
    }

    private static double ComputeSignedLatitude(int y, int height)
    {
        if (height <= 1)
            return 0;

        return Math.Clamp(1.0 - ((y + 0.5) / height) * 2.0, -1, 1);
    }

    private void AddFineClimateNoise(double[] values, double amplitude)
    {
        if (amplitude <= 0)
            return;

        for (var index = 0; index < values.Length; index++)
            values[index] = Math.Clamp(values[index] + (_random.NextDouble() - 0.5) * amplitude, 0, 1.6);
    }

    private static void SmoothUnitField(double[] values, int width, int height, int passes, double selfWeight)
    {
        if (passes <= 0)
            return;

        var scratch = new double[values.Length];
        for (var pass = 0; pass < passes; pass++)
        {
            Array.Copy(values, scratch, values.Length);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = scratch[y * width + x] * selfWeight;
                    var weight = selfWeight;
                    foreach (var neighbor in EnumerateNeighbors8(x, y, width, height))
                    {
                        sum += scratch[neighbor.Y * width + neighbor.X];
                        weight += 1.0;
                    }

                    values[y * width + x] = Math.Clamp(sum / weight, 0, 1);
                }
            }
        }
    }

    private static double NormalizeDistance(int distance, int maxDistance)
    {
        if (distance == int.MaxValue)
            return 1;

        return Math.Clamp(distance / (double)Math.Max(1, maxDistance), 0, 1);
    }

    private static IEnumerable<GridPoint> EnumerateNeighbors4(int x, int y, int width, int height)
    {
        yield return new GridPoint(WrapX(x - 1, width), y);
        yield return new GridPoint(WrapX(x + 1, width), y);
        if (y > 0)
            yield return new GridPoint(x, y - 1);
        if (y + 1 < height)
            yield return new GridPoint(x, y + 1);
    }

    private static IEnumerable<GridPoint> EnumerateNeighbors8(int x, int y, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var yy = y + dy;
            if (yy < 0 || yy >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                yield return new GridPoint(WrapX(x + dx, width), yy);
            }
        }
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private static int GetTemperatureBand(double meanAnnualTemperature)
    {
        if (meanAnnualTemperature >= 22.0)
            return 0;
        if (meanAnnualTemperature >= 12.0)
            return 1;
        if (meanAnnualTemperature >= 3.0)
            return 2;

        return 3;
    }

    private readonly record struct WindVector(double X, double Y);

    private readonly record struct ClimateCandidate(int Index, double Score);

    private sealed class TemperatureBandStats
    {
        private double _low;
        private double _high;

        public List<double> Values { get; } = [];

        public void Prepare()
        {
            if (Values.Count == 0)
            {
                _low = 0;
                _high = 1;
                return;
            }

            Values.Sort();
            _low = Percentile(Values, 0.08);
            _high = Percentile(Values, 0.92);
            if (_high <= _low + 0.001)
            {
                _low = Values[0];
                _high = Values[^1] + 0.001;
            }
        }

        public double Normalize(double value)
        {
            var normalized = Math.Clamp((value - _low) / Math.Max(0.001, _high - _low), 0, 1);
            return Math.Clamp(0.08 + Math.Pow(normalized, 0.82) * 0.84, 0, 1);
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
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
    }
}
