using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed class ElevationGenerator
{
    private readonly int _seed;

    public ElevationGenerator(int seed)
    {
        _seed = seed;
    }

    public ElevationMap Generate(
        MapMask mask,
        CrustFieldMap crustFields,
        PlateDomainMap plateDomains,
        TectonicBoundaryMap boundaries,
        TectonicFeatureMap features,
        ElevationGenerationOptions options)
    {
        var length = mask.Width * mask.Height;
        var elevation = new double[length];
        var baseElevation = new double[length];
        var tectonicElevation = new double[length];
        var roughness = new double[length];
        var erosionMask = new double[length];
        var ridgeMask = new double[length];
        var collisionMask = new double[length];
        var massifMask = new double[length];
        var subductionMask = new double[length];
        var riftMask = new double[length];
        var passiveMask = new double[length];
        var distanceToLand = ComputeDistance(mask, sourceIsLand: true);
        var distanceToWater = ComputeDistance(mask, sourceIsLand: false);
        var domains = plateDomains.Domains.ToDictionary(d => d.Id.Value);
        var minDimension = Math.Max(1, Math.Min(mask.Width, mask.Height));
        var shelfWidth = Math.Max(2.0, minDimension * 0.035 * options.ShelfWidthFactor);
        var inlandScale = Math.Max(4.0, minDimension * 0.16);
        var deepOceanScale = Math.Max(5.0, minDimension * 0.24);

        StampBoundaryMasks(mask, boundaries, ridgeMask, collisionMask, massifMask, subductionMask, riftMask, passiveMask);
        var rawCollisionMask = collisionMask.ToArray();
        ridgeMask = ShapeSignal(SmoothField(ridgeMask, mask.Width, mask.Height, 11), 0.16, 1.65);
        collisionMask = ShapeSignal(SmoothField(collisionMask, mask.Width, mask.Height, 4), 0.10, 1.15);
        massifMask = ShapeSignal(SmoothField(massifMask, mask.Width, mask.Height, 6), 0.05, 1.0);
        var forelandMask = ShapeSignal(SmoothField(rawCollisionMask, mask.Width, mask.Height, 12), 0.04, 1.25);
        subductionMask = ShapeSignal(SmoothField(subductionMask, mask.Width, mask.Height, 7), 0.10, 1.35);
        riftMask = ShapeSignal(SmoothField(riftMask, mask.Width, mask.Height, 8), 0.14, 1.45);
        passiveMask = ShapeSignal(SmoothField(passiveMask, mask.Width, mask.Height, 5), 0.03, 1.0);

        var uplift = BuildTerrainSignal(features, features.GetUplift, 9, 0.24, 1.45);
        var subsidence = BuildTerrainSignal(features, features.GetSubsidence, 8, 0.24, 1.35);
        var volcanism = BuildTerrainSignal(features, features.GetVolcanism, 4, 0.13, 1.15);
        var heatFlow = BuildTerrainSignal(features, features.GetHeatFlow, 8, 0.22, 1.4);
        var sedimentSupply = BuildTerrainSignal(features, features.GetSedimentSupply, 7, 0.24, 1.35);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                var isLand = mask.IsLand(point);
                var crust = crustFields.GetCrust(point);
                var coastal = crustFields.GetCoastalZone(point);
                var domain = domains.TryGetValue(plateDomains.GetPlate(point).Value, out var foundDomain) ? foundDomain : null;
                var activity = domain?.Activity ?? 0.5;
                var coastalInfluence = Math.Clamp(1.0 - distanceToWater[index] / Math.Max(1.0, shelfWidth * 3.2), 0, 1);

                baseElevation[index] = isLand
                    ? ComputeLandBase(distanceToWater[index], inlandScale, crust, coastal)
                    : ComputeSeaBase(distanceToLand[index], shelfWidth, deepOceanScale, crust, coastal, crustFields.GetOceanicAge(point), options);

                tectonicElevation[index] = ComputeTectonicContribution(
                    isLand,
                    crust,
                    coastal,
                    activity,
                    uplift[index],
                    subsidence[index],
                    volcanism[index],
                    heatFlow[index],
                    sedimentSupply[index],
                    ridgeMask[index],
                    collisionMask[index],
                    massifMask[index],
                    forelandMask[index],
                    subductionMask[index],
                    riftMask[index],
                    passiveMask[index],
                    coastalInfluence,
                    options);

                roughness[index] = ComputeRoughness(
                    isLand,
                    crust,
                    coastal,
                    uplift[index],
                    volcanism[index],
                    ridgeMask[index],
                    collisionMask[index],
                    riftMask[index],
                    options);

                var detail = FractalNoise(x, y, scale: 32.0, octaves: 5);
                var broad = FractalNoise(x + 113, y - 47, scale: 96.0, octaves: 3);
                var coastDampening = isLand
                    ? Math.Clamp(distanceToWater[index] / Math.Max(1.0, shelfWidth), 0.25, 1.0)
                    : Math.Clamp(distanceToLand[index] / Math.Max(1.0, shelfWidth), 0.18, 1.0);
                var noiseAmplitude = (isLand ? 430.0 : 120.0) * options.Roughness * roughness[index] * coastDampening;
                var broadWeight = isLand ? 0.45 : 0.25;
                var shapedNoise = detail * noiseAmplitude + broad * noiseAmplitude * broadWeight;

                elevation[index] = (baseElevation[index] + tectonicElevation[index] + shapedNoise) * options.ReliefScale;
            }
        }

        SmoothElevation(mask, elevation, ridgeMask, collisionMask, options, erosionMask);
        LiftInteriorLowlands(mask, elevation, distanceToWater, shelfWidth);
        EnforceConstraints(mask, elevation, options);
        return new ElevationMap(mask.Width, mask.Height, elevation, baseElevation, tectonicElevation, roughness, erosionMask);
    }

    private static double ComputeLandBase(double distanceToWater, double inlandScale, CrustKind crust, CoastalZoneKind coastal)
    {
        var inland = Math.Clamp(distanceToWater / inlandScale, 0, 1);
        var elevation = 24 + Math.Pow(inland, 0.82) * 760;

        elevation += crust switch
        {
            CrustKind.Arc => 360,
            CrustKind.Rift => -130,
            CrustKind.Terrane => 190,
            CrustKind.Shelf => -40,
            _ => 0
        };

        elevation += coastal switch
        {
            CoastalZoneKind.PassiveMargin => -90,
            CoastalZoneKind.ActiveMargin => 90,
            CoastalZoneKind.Shelf => -120,
            CoastalZoneKind.Slope => -70,
            _ => 0
        };

        return elevation;
    }

    private static double ComputeSeaBase(double distanceToLand, double shelfWidth, double deepOceanScale, CrustKind crust, CoastalZoneKind coastal, double oceanicAge, ElevationGenerationOptions options)
    {
        var shelf = Math.Clamp(distanceToLand / Math.Max(1.0, shelfWidth), 0, 1);
        var deep = Math.Clamp(distanceToLand / deepOceanScale, 0, 1);
        var elevation = coastal switch
        {
            CoastalZoneKind.Shelf => -35 - shelf * 120,
            CoastalZoneKind.Slope => -180 - shelf * 650,
            CoastalZoneKind.ShallowSea => -350 - shelf * 500,
            CoastalZoneKind.ActiveMargin => -520 - shelf * 850,
            _ => -1200 - Math.Pow(deep, 1.22) * 3300
        };

        if (crust == CrustKind.Shelf)
            elevation = Math.Max(elevation, -260 - shelf * 180);
        else if (crust == CrustKind.Oceanic && !double.IsNaN(oceanicAge))
            elevation -= Math.Clamp(oceanicAge / 220.0, 0, 1) * 90;
        else if (crust == CrustKind.Arc)
            elevation += 420;

        return elevation * options.SeaDepthScale;
    }

    private static double ComputeTectonicContribution(
        bool isLand,
        CrustKind crust,
        CoastalZoneKind coastal,
        double plateActivity,
        double uplift,
        double subsidence,
        double volcanism,
        double heatFlow,
        double sedimentSupply,
        double ridge,
        double collision,
        double massif,
        double foreland,
        double subduction,
        double rift,
        double passive,
        double coastalInfluence,
        ElevationGenerationOptions options)
    {
        var activity = 0.65 + Math.Clamp(plateActivity, 0, 1.5) * 0.35;
        var mountains = options.Mountaininess * activity;
        var contribution = 0.0;

        var landBasinDampening = isLand ? 0.08 + coastalInfluence * 0.92 : 1.0;

        contribution += Math.Clamp(uplift, 0, 2) * (isLand ? 650 : 35) * mountains;
        contribution += collision * (isLand ? 1350 : 35) * mountains;
        contribution += massif * (isLand ? 2800 : 45) * mountains;
        contribution += foreland * (isLand ? 520 : 0) * mountains;
        contribution += subduction * (isLand ? 470 : -55) * mountains;
        contribution += Math.Clamp(volcanism, 0, 2) * options.VolcanismInfluence * (isLand ? 660 : 90);
        contribution += ridge * (isLand ? 30 : 16);
        contribution += Math.Clamp(heatFlow, 0, 2) * (isLand ? 80 : 8);
        contribution -= Math.Clamp(subsidence, 0, 2) * (isLand ? 230 * landBasinDampening : 130);
        contribution -= Math.Clamp(sedimentSupply, 0, 2) * (isLand ? 45 * coastalInfluence : 10);
        contribution -= rift * options.RiftInfluence * (isLand ? 390 : 45);
        contribution -= passive * (isLand ? 120 * coastalInfluence : 24);

        if (crust == CrustKind.Rift)
            contribution -= options.RiftInfluence * (isLand ? 310 : 55);
        if (crust == CrustKind.Arc)
            contribution += options.VolcanismInfluence * (isLand ? 380 : 60);
        if (coastal == CoastalZoneKind.PassiveMargin)
            contribution -= isLand ? 100 : 28;

        return contribution;
    }

    private static double ComputeRoughness(
        bool isLand,
        CrustKind crust,
        CoastalZoneKind coastal,
        double uplift,
        double volcanism,
        double ridge,
        double collision,
        double rift,
        ElevationGenerationOptions options)
    {
        var roughness = isLand ? 0.35 : 0.22;
        roughness += crust switch
        {
            CrustKind.Arc => 0.24,
            CrustKind.Rift => 0.18,
            CrustKind.Terrane => 0.14,
            CrustKind.Shelf => -0.12,
            CrustKind.Oceanic => -0.06,
            _ => 0
        };
        roughness += coastal is CoastalZoneKind.PassiveMargin or CoastalZoneKind.Shelf ? -0.12 : 0;
        roughness += Math.Clamp(uplift, 0, 1) * 0.22;
        roughness += Math.Clamp(volcanism, 0, 1) * 0.16;
        roughness += ridge * 0.18 + collision * 0.28 + rift * 0.18;

        return Math.Clamp(roughness * (0.55 + options.Roughness), 0.05, 1.0);
    }

    private static void StampBoundaryMasks(
        MapMask mask,
        TectonicBoundaryMap boundaries,
        double[] ridgeMask,
        double[] collisionMask,
        double[] massifMask,
        double[] subductionMask,
        double[] riftMask,
        double[] passiveMask)
    {
        foreach (var segment in boundaries.Segments)
        {
            var isMountainBoundary = IsMountainBoundary(segment.BoundaryMode);
            var radius = segment.BoundaryMode switch
            {
                BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression => 4,
                BoundaryMode.OceanOceanSubduction or BoundaryMode.OceanContinentSubduction or BoundaryMode.ObliqueSubduction => 3,
                BoundaryMode.MidOceanRidge => 3,
                BoundaryMode.ContinentalRift or BoundaryMode.Transtension or BoundaryMode.BackArcSpreading => 3,
                _ => 2
            };
            var strength = Math.Clamp(segment.Activity, 0.15, 1.2);

            foreach (var point in segment.Points)
            {
                var mountainGate = isMountainBoundary
                    ? MountainGate(point, segment.Id, segment.BoundaryMode)
                    : 0;

                foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, radius))
                {
                    var distance = Distance(point, stamped, mask.Width);
                    var falloff = Math.Clamp(1.0 - distance / (radius + 1.0), 0, 1) * strength;
                    var index = stamped.Y * mask.Width + stamped.X;

                    switch (segment.BoundaryMode)
                    {
                        case BoundaryMode.MidOceanRidge:
                            ridgeMask[index] = Math.Max(ridgeMask[index], falloff);
                            break;
                        case BoundaryMode.ContinentContinentCollision:
                        case BoundaryMode.Transpression:
                            if (!mask.IsLand(stamped))
                                break;

                            if (mountainGate < 0.30)
                                break;

                            var gatedFalloff = falloff * (0.35 + mountainGate * 0.85);
                            collisionMask[index] = Math.Max(collisionMask[index], gatedFalloff);
                            if (mountainGate >= 0.68)
                                massifMask[index] = Math.Max(massifMask[index], falloff * (mountainGate - 0.55) / 0.45);
                            break;
                        case BoundaryMode.OceanOceanSubduction:
                        case BoundaryMode.OceanContinentSubduction:
                        case BoundaryMode.ObliqueSubduction:
                        case BoundaryMode.AccretionaryBoundary:
                            subductionMask[index] = Math.Max(subductionMask[index], falloff);
                            break;
                        case BoundaryMode.ContinentalRift:
                        case BoundaryMode.Transtension:
                        case BoundaryMode.BackArcSpreading:
                            riftMask[index] = Math.Max(riftMask[index], falloff);
                            break;
                        case BoundaryMode.PassiveMargin:
                            passiveMask[index] = Math.Max(passiveMask[index], falloff);
                            break;
                    }
                }

                if (!isMountainBoundary || mountainGate < 0.68)
                    continue;

                var massifRadius = radius + (mountainGate >= 0.84 ? 8 : 5);
                foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, massifRadius))
                {
                    if (!mask.IsLand(stamped))
                        continue;

                    var distance = Distance(point, stamped, mask.Width);
                    var falloff = Math.Clamp(1.0 - distance / (massifRadius + 1.0), 0, 1) * strength;
                    var index = stamped.Y * mask.Width + stamped.X;
                    var massifFalloff = Math.Pow(falloff, 1.3) * (mountainGate - 0.58) / 0.42;
                    massifMask[index] = Math.Max(massifMask[index], massifFalloff);
                    collisionMask[index] = Math.Max(collisionMask[index], massifFalloff * 0.42);
                }
            }
        }
    }

    private static bool IsMountainBoundary(BoundaryMode mode) =>
        mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression;

    private static double MountainGate(GridPoint point, int segmentId, BoundaryMode mode)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 17 + 503, mode == BoundaryMode.ContinentContinentCollision ? 34.0 : 26.0);
        var local = SmoothNoise(point.X, point.Y, segmentId * 31 + 907, 11.0);
        var pass = SmoothNoise(point.X, point.Y, segmentId * 43 + 1201, 6.0);
        return Math.Clamp(broad * 0.64 + local * 0.24 + pass * 0.12, 0, 1);
    }

    private static void SmoothElevation(MapMask mask, double[] elevation, double[] ridgeMask, double[] collisionMask, ElevationGenerationOptions options, double[] erosionMask)
    {
        if (options.Erosion <= 0)
            return;

        var width = mask.Width;
        var height = mask.Height;
        var passes = 5;
        for (var pass = 0; pass < passes; pass++)
        {
            var source = elevation.ToArray();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var point = new GridPoint(x, y);
                    var index = y * width + x;
                    var sameSurfaceSum = 0.0;
                    var sameSurfaceWeight = 0.0;
                    var isLand = mask.IsLand(point);

                    foreach (var neighbor in Neighbors8(point, width, height))
                    {
                        if (mask.IsLand(neighbor) != isLand)
                            continue;

                        sameSurfaceSum += source[neighbor.Y * width + neighbor.X];
                        sameSurfaceWeight += 1.0;
                    }

                    if (sameSurfaceWeight <= 0)
                        continue;

                    var average = sameSurfaceSum / sameSurfaceWeight;
                    var protectedRelief = Math.Clamp(collisionMask[index] * 0.7 + ridgeMask[index] * (isLand ? 0.08 : 0.2), 0, 0.75);
                    var waterBoost = isLand ? 0.58 : 1.65;
                    var erosion = options.Erosion * waterBoost * (1.0 - protectedRelief);
                    erosionMask[index] = Math.Max(erosionMask[index], erosion);
                    elevation[index] = source[index] * (1.0 - erosion) + average * erosion;
                }
            }
        }
    }

    private static double[] BuildTerrainSignal(TectonicFeatureMap features, Func<int, int, double> readValue, int passes, double threshold, double gamma)
    {
        var values = new double[features.Width * features.Height];
        for (var y = 0; y < features.Height; y++)
        {
            for (var x = 0; x < features.Width; x++)
                values[y * features.Width + x] = Math.Clamp(readValue(x, y), 0, 2);
        }

        return ShapeSignal(SmoothField(values, features.Width, features.Height, passes), threshold, gamma);
    }

    private static double[] ShapeSignal(double[] values, double threshold, double gamma)
    {
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var normalized = Math.Clamp((values[i] - threshold) / Math.Max(0.0001, 1.0 - threshold), 0, 1);
            result[i] = Math.Pow(normalized, gamma);
        }

        return result;
    }

    private static double[] SmoothField(double[] values, int width, int height, int passes)
    {
        var current = values.ToArray();
        for (var pass = 0; pass < passes; pass++)
        {
            var next = new double[current.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = current[y * width + x] * 4.0;
                    var weight = 4.0;

                    foreach (var neighbor in Neighbors8(new GridPoint(x, y), width, height))
                    {
                        sum += current[neighbor.Y * width + neighbor.X];
                        weight += 1.0;
                    }

                    next[y * width + x] = sum / weight;
                }
            }

            current = next;
        }

        return current;
    }

    private static void EnforceConstraints(MapMask mask, double[] elevation, ElevationGenerationOptions options)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                var value = Math.Clamp(elevation[index], options.MinOceanDepthMeters, options.MaxElevationMeters);

                if (options.PreserveMaskCoastline)
                {
                    value = mask.IsLand(point)
                        ? Math.Max(value, options.MinLandElevationMeters)
                        : Math.Min(value, options.MaxSeaElevationMeters);
                }

                elevation[index] = Math.Clamp(value, options.MinOceanDepthMeters, options.MaxElevationMeters);
            }
        }
    }

    private static void LiftInteriorLowlands(MapMask mask, double[] elevation, double[] distanceToWater, double shelfWidth)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var inland = Math.Clamp((distanceToWater[index] - shelfWidth * 1.25) / Math.Max(1.0, shelfWidth * 4.0), 0, 1);
                if (inland <= 0)
                    continue;

                var floor = 95.0 + inland * 165.0;
                if (elevation[index] >= floor)
                    continue;

                elevation[index] = elevation[index] * 0.35 + floor * 0.65;
            }
        }
    }

    private static double[] ComputeDistance(MapMask mask, bool sourceIsLand)
    {
        var length = mask.Width * mask.Height;
        var distances = Enumerable.Repeat(double.PositiveInfinity, length).ToArray();
        var queue = new PriorityQueue<GridPoint, double>();

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (mask.IsLand(point) != sourceIsLand)
                    continue;

                distances[y * mask.Width + x] = 0;
                queue.Enqueue(point, 0);
            }
        }

        if (queue.Count == 0)
            return Enumerable.Repeat((double)Math.Max(mask.Width, mask.Height), length).ToArray();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distances[current.Y * mask.Width + current.X];
            foreach (var (neighbor, cost) in Neighbors8WithCost(current, mask.Width, mask.Height))
            {
                var index = neighbor.Y * mask.Width + neighbor.X;
                var nextDistance = currentDistance + cost;
                if (nextDistance >= distances[index])
                    continue;

                distances[index] = nextDistance;
                queue.Enqueue(neighbor, nextDistance);
            }
        }

        return distances;
    }

    private double FractalNoise(int x, int y, double scale, int octaves)
    {
        var amplitude = 1.0;
        var frequency = 1.0 / Math.Max(1.0, scale);
        var sum = 0.0;
        var amplitudeSum = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(x * frequency, y * frequency, octave) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.5;
            frequency *= 2.0;
        }

        return amplitudeSum <= 0 ? 0 : sum / amplitudeSum;
    }

    private double ValueNoise(double x, double y, int octave)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);
        var a = HashSigned(x0, y0, octave);
        var b = HashSigned(x0 + 1, y0, octave);
        var c = HashSigned(x0, y0 + 1, octave);
        var d = HashSigned(x0 + 1, y0 + 1, octave);
        return Math.Clamp((Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty) + 1.0) * 0.5, 0, 1);
    }

    private double HashSigned(int x, int y, int octave)
    {
        unchecked
        {
            var hash = _seed;
            hash = hash * 397 ^ x;
            hash = hash * 397 ^ (y * 668265263);
            hash = hash * 397 ^ (octave * 1442695041);
            hash ^= hash >> 13;
            hash *= 1274126177;
            hash ^= hash >> 16;
            return ((hash & 0x7fffffff) / (double)int.MaxValue) * 2.0 - 1.0;
        }
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

    private static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                yield return new GridPoint(WrapX(point.X + dx, width), y);
            }
        }
    }

    private static IEnumerable<(GridPoint Point, double Cost)> Neighbors8WithCost(GridPoint point, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                yield return (new GridPoint(WrapX(point.X + dx, width), y), cost);
            }
        }
    }

    private static double Distance(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, Math.Max(0, width - dx));
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;
}
