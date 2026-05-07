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
        var terrainClasses = new byte[length];
        var mountainPassPotential = new double[length];
        var ridgeContinuity = new double[length];
        var foothillInfluence = new double[length];
        var basinInfluence = new double[length];
        var ridgeMask = new double[length];
        var collisionMask = new double[length];
        var massifMask = new double[length];
        var subductionMask = new double[length];
        var riftMask = new double[length];
        var passiveMask = new double[length];
        var distanceToLand = ComputeDistance(mask, sourceIsLand: true);
        var distanceToWater = ComputeDistance(mask, sourceIsLand: false);
        var landEnclosure = BuildLandEnclosureField(mask);
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
        subductionMask = DiffuseTectonicLineSignal(subductionMask, mask.Width, mask.Height, 8, 11, 0.08, 1.18, 0.24);
        riftMask = DiffuseTectonicLineSignal(riftMask, mask.Width, mask.Height, 9, 12, 0.10, 1.22, 0.20);
        passiveMask = DiffuseTectonicLineSignal(passiveMask, mask.Width, mask.Height, 6, 10, 0.03, 1.0, 0.26);

        var uplift = BuildTerrainSignal(features, features.GetUplift, 14, 0.18, 1.25);
        var subsidence = BuildTerrainSignal(features, features.GetSubsidence, 8, 0.24, 1.35);
        var volcanism = BuildTerrainSignal(features, features.GetVolcanism, 4, 0.13, 1.15);
        var heatFlow = BuildTerrainSignal(features, features.GetHeatFlow, 8, 0.22, 1.4);
        var sedimentSupply = BuildTerrainSignal(features, features.GetSedimentSupply, 7, 0.24, 1.35);

        BuildMountainNetworkFields(mask, collisionMask, massifMask, forelandMask, ridgeContinuity, mountainPassPotential, foothillInfluence);
        BuildBasinInfluence(mask, distanceToWater, shelfWidth, subsidence, sedimentSupply, passiveMask, riftMask, ridgeContinuity, foothillInfluence, basinInfluence);

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
                    : ComputeSeaBase(x, y, distanceToLand[index], shelfWidth, deepOceanScale, crust, coastal, crustFields.GetOceanicAge(point), options);

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
                    mountainPassPotential[index],
                    ridgeContinuity[index],
                    foothillInfluence[index],
                    basinInfluence[index],
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
                    basinInfluence[index],
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

        ApplyLargeBasins(mask, elevation, basinInfluence, distanceToWater, shelfWidth);
        ApplyMountainCrossSection(mask, elevation, ridgeContinuity, mountainPassPotential, foothillInfluence, basinInfluence, distanceToWater, shelfWidth);
        ApplyBathymetricStructure(mask, elevation, distanceToLand, landEnclosure, shelfWidth, ridgeMask, subductionMask, riftMask, crustFields, options);
        ApplyIslandProfiles(mask, features.Islands, elevation, roughness, distanceToLand);
        SmoothElevation(mask, elevation, ridgeMask, collisionMask, options, erosionMask);
        LiftInteriorLowlands(mask, elevation, distanceToWater, shelfWidth);
        EnforceConstraints(mask, elevation, options);
        ClassifyTerrain(mask, crustFields, elevation, roughness, distanceToLand, distanceToWater, landEnclosure, shelfWidth, sedimentSupply, heatFlow, basinInfluence, foothillInfluence, ridgeContinuity, ridgeMask, subductionMask, riftMask, terrainClasses);
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
            basinInfluence);
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

    private static double ComputeSeaBase(int x, int y, double distanceToLand, double shelfWidth, double deepOceanScale, CrustKind crust, CoastalZoneKind coastal, double oceanicAge, ElevationGenerationOptions options)
    {
        var shelfNoise = SmoothNoise(x, y, 1507, 23.0) * 0.42 + SmoothNoise(x + 53, y - 29, 1511, 57.0) * 0.58;
        var edgeNoise = SmoothNoise(x - 17, y + 31, 1517, 11.0);
        var warpedDistanceToLand = Math.Max(0.0, distanceToLand + (shelfNoise - 0.5) * shelfWidth * 0.78 + (edgeNoise - 0.5) * shelfWidth * 0.34);
        var localShelfWidth = shelfWidth * Math.Clamp(0.68 + shelfNoise * 0.72, 0.62, 1.42);
        var shelf = Math.Clamp(warpedDistanceToLand / Math.Max(1.0, localShelfWidth), 0, 1);
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
        double mountainPassPotential,
        double ridgeContinuity,
        double foothillInfluence,
        double basinInfluence,
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
        var passDampening = isLand ? 1.0 - mountainPassPotential * 0.44 : 1.0;
        var ridgeStrength = isLand ? 0.55 + ridgeContinuity * 0.45 : 1.0;
        var mountainContext = Math.Clamp(collision * 0.55 + massif * 0.85 + ridgeContinuity * 0.32, 0, 1);
        var regionalUpliftMeters = isLand
            ? 280 + mountainContext * 360 + Math.Clamp(volcanism, 0, 2) * 80
            : 35;
        var subductionMeters = isLand
            ? 190 + coastalInfluence * 130 + Math.Clamp(volcanism, 0, 2) * 70
            : -55;

        contribution += Math.Clamp(uplift, 0, 2) * regionalUpliftMeters * mountains;
        contribution += collision * passDampening * ridgeStrength * (isLand ? 1500 : 35) * mountains;
        contribution += massif * passDampening * (0.72 + ridgeContinuity * 0.38) * (isLand ? 3200 : 45) * mountains;
        contribution += Math.Max(foreland, foothillInfluence) * (isLand ? 360 : 0) * mountains;
        contribution += subduction * subductionMeters * mountains;
        contribution += Math.Clamp(volcanism, 0, 2) * options.VolcanismInfluence * (isLand ? 660 : 90);
        contribution += ridge * (isLand ? 30 : 16);
        contribution += Math.Clamp(heatFlow, 0, 2) * (isLand ? 80 : 8);
        contribution -= Math.Clamp(subsidence, 0, 2) * (isLand ? 230 * landBasinDampening : 130);
        contribution -= Math.Clamp(sedimentSupply, 0, 2) * (isLand ? 45 * coastalInfluence : 10);
        contribution -= basinInfluence * (isLand ? 205 : 0);
        contribution -= rift * options.RiftInfluence * (isLand ? 310 : 45);
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
        double basinInfluence,
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
        roughness -= basinInfluence * (isLand ? 0.18 : 0.06);

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
                var localRadius = BoundaryStampRadius(point, segment.Id, segment.BoundaryMode, radius);
                var localStrength = strength * BoundaryStampStrength(point, segment.Id, segment.BoundaryMode);
                var mountainGate = isMountainBoundary
                    ? MountainGate(point, segment.Id, segment.BoundaryMode)
                    : 0;

                foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, localRadius))
                {
                    var distance = Distance(point, stamped, mask.Width);
                    var edgeVariation = 0.84 + SmoothNoise(stamped.X, stamped.Y, segment.Id * 73 + 1907, 8.0) * 0.32;
                    var falloff = SmoothStep(Math.Clamp(1.0 - distance / (localRadius + 1.0), 0, 1)) * localStrength * edgeVariation;
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

                var massifRadius = localRadius + (int)Math.Round((mountainGate >= 0.84 ? 8 : 5) * BoundaryMassifWidth(point, segment.Id));
                foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, massifRadius))
                {
                    if (!mask.IsLand(stamped))
                        continue;

                    var distance = Distance(point, stamped, mask.Width);
                    var edgeVariation = 0.80 + SmoothNoise(stamped.X, stamped.Y, segment.Id * 83 + 2027, 10.0) * 0.36;
                    var falloff = SmoothStep(Math.Clamp(1.0 - distance / (massifRadius + 1.0), 0, 1)) * localStrength * edgeVariation;
                    var index = stamped.Y * mask.Width + stamped.X;
                    var massifFalloff = Math.Pow(falloff, 1.3) * (mountainGate - 0.58) / 0.42;
                    massifMask[index] = Math.Max(massifMask[index], massifFalloff);
                    collisionMask[index] = Math.Max(collisionMask[index], massifFalloff * 0.42);
                }
            }
        }
    }

    private static int BoundaryStampRadius(GridPoint point, int segmentId, BoundaryMode mode, int baseRadius)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 29 + 1301, mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression ? 31.0 : 23.0);
        var local = SmoothNoise(point.X + 17, point.Y - 11, segmentId * 37 + 1307, 9.0);
        var neck = SmoothNoise(point.X - 23, point.Y + 19, segmentId * 41 + 1319, 15.0);
        var width = baseRadius * (0.52 + broad * 0.72 + local * 0.30);

        if (mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression)
            width = Math.Max(width, baseRadius * 0.92);
        else if (neck < 0.28)
            width *= 0.62 + neck;

        return Math.Clamp((int)Math.Round(width), 1, baseRadius + 5);
    }

    private static double BoundaryStampStrength(GridPoint point, int segmentId, BoundaryMode mode)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 53 + 1409, mode is BoundaryMode.PassiveMargin ? 36.0 : 24.0);
        var local = SmoothNoise(point.X - 31, point.Y + 7, segmentId * 59 + 1423, 10.0);
        return Math.Clamp(0.58 + broad * 0.52 + local * 0.20, 0.42, 1.18);
    }

    private static double BoundaryMassifWidth(GridPoint point, int segmentId)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 67 + 1517, 35.0);
        var local = SmoothNoise(point.X + 13, point.Y + 37, segmentId * 71 + 1523, 12.0);
        return Math.Clamp(0.58 + broad * 0.88 + local * 0.28, 0.60, 1.45);
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

    private static void BuildMountainNetworkFields(
        MapMask mask,
        double[] collisionMask,
        double[] massifMask,
        double[] forelandMask,
        double[] ridgeContinuity,
        double[] mountainPassPotential,
        double[] foothillInfluence)
    {
        var length = mask.Width * mask.Height;
        var axis = new double[length];
        for (var i = 0; i < length; i++)
            axis[i] = Math.Max(collisionMask[i], massifMask[i]);

        var broadAxis = SmoothField(axis, mask.Width, mask.Height, 4);
        var broadFoothills = SmoothField(massifMask, mask.Width, mask.Height, 8);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var localAxis = axis[index];
                var continuityNoise = SmoothNoise(x, y, 1601, 18.0);
                ridgeContinuity[index] = Math.Clamp(localAxis * 0.72 + broadAxis[index] * 0.45 + continuityNoise * 0.18 - 0.12, 0, 1);

                var mountainContext = Math.Clamp((forelandMask[index] + broadAxis[index] + localAxis - 0.05) / 1.25, 0, 1);
                var passNoise = SmoothNoise(x, y, 1603, 9.0);
                mountainPassPotential[index] = Math.Clamp(mountainContext * (1.0 - ridgeContinuity[index]) * (0.55 + passNoise * 0.45), 0, 1);

                var foothillWidth = 0.68 + SmoothNoise(x + 47, y - 31, 1607, 28.0) * 0.58;
                var foothillBreak = SmoothNoise(x - 19, y + 53, 1613, 12.0);
                var foothill = forelandMask[index] * (0.58 + foothillWidth * 0.34) + broadFoothills[index] * (0.32 + foothillWidth * 0.28) - localAxis * 0.25;
                if (foothillBreak < 0.24)
                    foothill *= 0.58 + foothillBreak * 1.15;

                foothillInfluence[index] = Math.Clamp(foothill, 0, 1);
            }
        }
    }

    private static void BuildBasinInfluence(
        MapMask mask,
        double[] distanceToWater,
        double shelfWidth,
        double[] subsidence,
        double[] sedimentSupply,
        double[] passiveMask,
        double[] riftMask,
        double[] ridgeContinuity,
        double[] foothillInfluence,
        double[] basinInfluence)
    {
        var raw = new double[basinInfluence.Length];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var interior = Math.Clamp((distanceToWater[index] - shelfWidth * 1.1) / Math.Max(1.0, shelfWidth * 5.0), 0, 1);
                var quietRelief = 1.0 - Math.Clamp(ridgeContinuity[index] * 0.85 + foothillInfluence[index] * 0.35, 0, 1);
                var continentalNoise = SmoothNoise(x, y, 1701, 72.0);
                var widthNoise = SmoothNoise(x - 19, y + 41, 1709, 34.0);
                var breakupNoise = SmoothNoise(x + 67, y - 13, 1711, 15.0);
                var tectonicBasin = subsidence[index] * 0.58 + sedimentSupply[index] * 0.28 + passiveMask[index] * 0.32 + riftMask[index] * 0.12;
                tectonicBasin *= Math.Clamp(0.58 + widthNoise * 0.74, 0.45, 1.25);
                var broadBasin = continentalNoise * interior * (0.48 + widthNoise * 0.36);
                if (breakupNoise < 0.30)
                    tectonicBasin *= 0.55 + breakupNoise;

                raw[index] = Math.Clamp((tectonicBasin + broadBasin) * quietRelief, 0, 1);
            }
        }

        var smooth = SmoothField(raw, mask.Width, mask.Height, 16);
        var broad = SmoothField(smooth, mask.Width, mask.Height, 10);
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var i = y * mask.Width + x;
                var blendNoise = SmoothNoise(x, y, 1721, 46.0);
                var blended = smooth[i] * (0.42 + blendNoise * 0.22) + broad[i] * (0.58 - blendNoise * 0.22);
                if (smooth[i] > broad[i] * 1.22)
                    blended = blended * 0.82 + broad[i] * 0.18;

                var normalized = Math.Clamp((blended - 0.035) / 0.82, 0, 1);
                var eased = SmoothStep(normalized);
                basinInfluence[i] = Math.Pow(eased, 1.08);
            }
        }
    }

    private static void ApplyLargeBasins(MapMask mask, double[] elevation, double[] basinInfluence, double[] distanceToWater, double shelfWidth)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var basin = basinInfluence[index];
                if (basin <= 0.025)
                    continue;

                var interior = Math.Clamp((distanceToWater[index] - shelfWidth) / Math.Max(1.0, shelfWidth * 6.0), 0, 1);
                var target = 150.0 + interior * 290.0;
                var basinProfile = SmoothStep(Math.Clamp((basin - 0.025) / 0.82, 0, 1));
                var flatten = basinProfile * (0.16 + interior * 0.24);
                elevation[index] = elevation[index] * (1.0 - flatten) + target * flatten;
            }
        }
    }

    private static void ApplyMountainCrossSection(
        MapMask mask,
        double[] elevation,
        double[] ridgeContinuity,
        double[] mountainPassPotential,
        double[] foothillInfluence,
        double[] basinInfluence,
        double[] distanceToWater,
        double shelfWidth)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var ridge = ridgeContinuity[index];
                var foothill = foothillInfluence[index];
                var pass = mountainPassPotential[index];
                var basin = basinInfluence[index];
                var interior = Math.Clamp((distanceToWater[index] - shelfWidth * 0.7) / Math.Max(1.0, shelfWidth * 5.5), 0, 1);

                var mainRidge = SmoothStep(Math.Clamp((ridge - 0.56) / 0.34, 0, 1)) * (1.0 - pass * 0.48);
                var steepSlope = SmoothStep(Math.Clamp((ridge * 0.55 + foothill * 0.55 - 0.28) / 0.54, 0, 1)) * (1.0 - mainRidge * 0.62);
                var foothillBelt = SmoothStep(Math.Clamp((foothill - 0.10) / 0.62, 0, 1)) * (1.0 - mainRidge * 0.84) * (1.0 - pass * 0.20);
                var forelandBasin = SmoothStep(Math.Clamp((basin * 0.78 + foothill * 0.32 - ridge * 0.42 - 0.18) / 0.62, 0, 1));

                elevation[index] += mainRidge * 360.0;
                elevation[index] += steepSlope * 180.0;

                if (foothillBelt > 0.02)
                {
                    var foothillTarget = 420.0 + interior * 260.0 + steepSlope * 180.0;
                    var blend = foothillBelt * 0.22;
                    elevation[index] = elevation[index] * (1.0 - blend) + Math.Max(elevation[index], foothillTarget) * blend;
                }

                if (forelandBasin > 0.02)
                {
                    var basinTarget = 180.0 + interior * 260.0;
                    var blend = forelandBasin * (0.10 + interior * 0.14);
                    elevation[index] = elevation[index] * (1.0 - blend) + basinTarget * blend;
                }
            }
        }
    }

    private static void ApplyBathymetricStructure(
        MapMask mask,
        double[] elevation,
        double[] distanceToLand,
        double[] landEnclosure,
        double shelfWidth,
        double[] ridgeMask,
        double[] subductionMask,
        double[] riftMask,
        CrustFieldMap crustFields,
        ElevationGenerationOptions options)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var shelf = Math.Clamp(1.0 - distanceToLand[index] / Math.Max(1.0, shelfWidth * 2.4), 0, 1);
                var deep = Math.Clamp(distanceToLand[index] / Math.Max(1.0, shelfWidth * 7.5), 0, 1);
                var enclosure = landEnclosure[index];
                var channelNoise = SmoothNoise(x + 109, y - 73, 2101, 28.0);
                var bankNoise = SmoothNoise(x - 47, y + 83, 2107, 34.0);
                var basinNoise = SmoothNoise(x + 13, y + 29, 2111, 88.0);

                var submarineRidge = ridgeMask[index] * (0.45 + SmoothNoise(x, y, 2117, 18.0) * 0.35);
                var trench = subductionMask[index] * (0.42 + deep * 0.58);
                var deepChannel = riftMask[index] * 0.45 + Math.Clamp(enclosure - 0.28, 0, 1) * Math.Clamp(channelNoise - 0.42, 0, 1) * 1.7;
                var shallowBank = shelf * Math.Clamp(bankNoise - 0.30, 0, 1) * 1.15;
                var abyssalBasin = deep * Math.Clamp(basinNoise - 0.44, 0, 1) * (1.0 - shelf * 0.55);

                elevation[index] += submarineRidge * 130.0;
                elevation[index] -= trench * 330.0;
                elevation[index] -= deepChannel * 135.0;
                elevation[index] += shallowBank * 85.0;
                elevation[index] -= abyssalBasin * 145.0;

                if (enclosure > 0.50 && distanceToLand[index] <= shelfWidth * 3.0)
                {
                    var inlandSeaTarget = -90.0 - Math.Clamp(distanceToLand[index] / Math.Max(1.0, shelfWidth * 2.0), 0, 1) * 260.0;
                    var blend = Math.Clamp((enclosure - 0.50) / 0.36, 0, 1) * 0.18;
                    elevation[index] = elevation[index] * (1.0 - blend) + inlandSeaTarget * blend;
                }

                elevation[index] = Math.Clamp(elevation[index], options.MinOceanDepthMeters, -1.0);
            }
        }
    }

    private void ApplyIslandProfiles(MapMask mask, IReadOnlyList<TectonicIsland> islands, double[] elevation, double[] roughness, double[] distanceToLand)
    {
        foreach (var island in islands)
        {
            var radius = Math.Clamp(Math.Sqrt(Math.Max(1.0, island.Area)) * 1.65, 3.0, 26.0);
            var stampRadius = (int)Math.Ceiling(radius * 2.2);
            var angle = Hash01(island.Center.X, island.Center.Y, island.PlateId.Value + 1901) * Math.PI * 2.0;
            var axisX = Math.Cos(angle);
            var axisY = Math.Sin(angle);

            foreach (var point in PointsInRadius(mask.Width, mask.Height, island.Center, stampRadius))
            {
                var index = point.Y * mask.Width + point.X;
                var dx = WrappedDeltaX(point.X - island.Center.X, mask.Width);
                var dy = point.Y - island.Center.Y;
                var radialDistance = Math.Sqrt(dx * dx + dy * dy);
                var radial = Math.Clamp(1.0 - radialDistance / Math.Max(1.0, radius), 0, 1);
                var projection = dx * axisX + dy * axisY;
                var sideDistance = Math.Abs(dx * -axisY + dy * axisX);
                var axial = Math.Clamp(1.0 - Math.Abs(projection) / Math.Max(1.0, radius * 1.8), 0, 1);
                var narrowRidge = Math.Clamp(1.0 - sideDistance / Math.Max(1.0, radius * 0.32), 0, 1) * axial;

                switch (island.Kind)
                {
                    case IslandKind.VolcanicArc:
                        if (mask.IsLand(point))
                        {
                            var cone = Math.Pow(radial, 1.55);
                            elevation[index] += cone * 920;
                            roughness[index] = Math.Clamp(roughness[index] + cone * 0.28, 0, 1);
                        }
                        break;
                    case IslandKind.ShelfArchipelago:
                        if (mask.IsLand(point))
                        {
                            var lowTarget = 60 + SmoothNoise(point.X, point.Y, island.PlateId.Value + 1911, 7.0) * 120;
                            elevation[index] = elevation[index] * 0.45 + lowTarget * 0.55;
                            roughness[index] = Math.Clamp(roughness[index] * 0.62, 0.04, 1);
                        }
                        else
                        {
                            var shelfNoise = SmoothNoise(point.X, point.Y, island.PlateId.Value + 1917, 13.0) * 0.45
                                + SmoothNoise(point.X - 37, point.Y + 23, island.PlateId.Value + 1921, 28.0) * 0.55;
                            var lobeNoise = SmoothNoise(point.X + 11, point.Y - 43, island.PlateId.Value + 1927, 7.0);
                            var alongAxis = Math.Clamp(0.72 + axial * 0.42, 0.62, 1.14);
                            var crossAxis = Math.Clamp(1.12 - sideDistance / Math.Max(1.0, radius * 2.1), 0.62, 1.12);
                            var localShelfExtent = radius * (0.72 + shelfNoise * 0.82 + lobeNoise * 0.28) * alongAxis * crossAxis;
                            if (distanceToLand[index] <= localShelfExtent)
                            {
                                var shelf = SmoothStep(Math.Clamp(1.0 - distanceToLand[index] / Math.Max(1.0, localShelfExtent), 0, 1));
                                var targetDepth = -285 + shelf * 225 + (shelfNoise - 0.5) * 42.0;
                                elevation[index] = Math.Max(elevation[index], targetDepth);
                            }
                        }
                        break;
                    case IslandKind.Microcontinent:
                        if (mask.IsLand(point))
                        {
                            var relief = (FractalNoise(point.X + 41, point.Y - 17, 18.0, 4) - 0.35) * 420;
                            elevation[index] += radial * 160 + relief;
                            roughness[index] = Math.Clamp(roughness[index] + 0.10 + radial * 0.08, 0, 1);
                        }
                        break;
                    case IslandKind.UpliftedRidge:
                        if (mask.IsLand(point))
                        {
                            var ridge = Math.Pow(narrowRidge, 1.2);
                            elevation[index] += ridge * 520;
                            roughness[index] = Math.Clamp(roughness[index] + ridge * 0.18, 0, 1);
                        }
                        break;
                    case IslandKind.Hotspot:
                        if (mask.IsLand(point))
                        {
                            var chain = Math.Max(Math.Pow(radial, 1.8), Math.Pow(narrowRidge, 1.35) * 0.72);
                            var ageGradient = Math.Clamp(0.65 + projection / Math.Max(1.0, radius * 3.2), 0.35, 1.15);
                            elevation[index] += chain * ageGradient * 820;
                            roughness[index] = Math.Clamp(roughness[index] + chain * 0.24, 0, 1);
                        }
                        break;
                }
            }
        }
    }

    private static void ClassifyTerrain(
        MapMask mask,
        CrustFieldMap crustFields,
        double[] elevation,
        double[] roughness,
        double[] distanceToLand,
        double[] distanceToWater,
        double[] landEnclosure,
        double shelfWidth,
        double[] sedimentSupply,
        double[] heatFlow,
        double[] basinInfluence,
        double[] foothillInfluence,
        double[] ridgeContinuity,
        double[] ridgeMask,
        double[] subductionMask,
        double[] riftMask,
        byte[] terrainClasses)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (!mask.IsLand(point))
                {
                    terrainClasses[index] = (byte)ClassifyBathymetry(
                        mask,
                        crustFields,
                        elevation[index],
                        x,
                        y,
                        distanceToLand[index],
                        landEnclosure[index],
                        shelfWidth,
                        ridgeMask[index],
                        subductionMask[index],
                        riftMask[index]);
                    continue;
                }

                var value = elevation[index];
                var coastalInfluence = Math.Clamp(1.0 - distanceToWater[index] / Math.Max(1.0, shelfWidth * 3.0), 0, 1);
                var interior = Math.Clamp((distanceToWater[index] - shelfWidth * 1.2) / Math.Max(1.0, shelfWidth * 5.0), 0, 1);
                var sediment = sedimentSupply[index];
                var basin = basinInfluence[index];
                var foothill = foothillInfluence[index];
                var ridge = ridgeContinuity[index];
                var heat = heatFlow[index];
                var aridityNoise = SmoothNoise(x, y, 1801, 54.0);
                var plateauNoise = SmoothNoise(x, y, 1807, 46.0);

                var terrainClass = value switch
                {
                    < 45 => TerrainClassKind.Beach,
                    < 170 when coastalInfluence > 0.55 && (sediment > 0.22 || basin > 0.24) => TerrainClassKind.DeltaCandidate,
                    < 260 when coastalInfluence > 0.34 => TerrainClassKind.CoastalPlain,
                    < 720 when foothill > 0.14 && (sediment > 0.10 || basin > 0.16) => TerrainClassKind.AlluvialPlain,
                    < 560 when sediment > 0.20 && basin > 0.18 && coastalInfluence < 0.45 => TerrainClassKind.AlluvialPlain,
                    < 820 when basin > 0.22 && coastalInfluence < 0.68 && sediment < 0.50 && aridityNoise > 0.60 => TerrainClassKind.DryBasin,
                    < 820 when basin > 0.28 => TerrainClassKind.SedimentaryBasin,
                    < 760 when ridge < 0.35 => TerrainClassKind.InteriorLowland,
                    < 1700 when ridge > 0.58 && value > 1050 => TerrainClassKind.Mountain,
                    < 1600 when value >= 780 && coastalInfluence < 0.64 && ridge < 0.50 && roughness[index] < 0.72 && basin < 0.76 && plateauNoise > 0.54 => TerrainClassKind.DesertPlateauCandidate,
                    < 1750 => TerrainClassKind.Highland,
                    _ => TerrainClassKind.Mountain
                };

                terrainClasses[index] = (byte)terrainClass;
                roughness[index] = AdjustRoughnessForTerrainClass(roughness[index], terrainClass, ridge);
            }
        }
    }

    private static TerrainClassKind ClassifyBathymetry(
        MapMask mask,
        CrustFieldMap crustFields,
        double elevation,
        int x,
        int y,
        double distanceToLand,
        double enclosure,
        double shelfWidth,
        double ridge,
        double subduction,
        double rift)
    {
        var coastal = crustFields.GetCoastalZone(x, y);
        var shelf = Math.Clamp(1.0 - distanceToLand / Math.Max(1.0, shelfWidth * 2.3), 0, 1);
        var bankNoise = SmoothNoise(x - 47, y + 83, 2107, 34.0);
        var channelNoise = SmoothNoise(x + 109, y - 73, 2101, 28.0);
        var basinNoise = SmoothNoise(x + 13, y + 29, 2111, 88.0);

        if (subduction > 0.38 && elevation < -1050)
            return TerrainClassKind.Trench;
        if (ridge > 0.38 && elevation > -2450)
            return TerrainClassKind.SubmarineRidge;
        if (enclosure > 0.64 && distanceToLand <= shelfWidth * 2.8)
            return TerrainClassKind.InlandSeaDepth;
        if (enclosure > 0.42 && rift + Math.Clamp(channelNoise - 0.54, 0, 1) > 0.42 && elevation < -420)
            return TerrainClassKind.StraitDepth;
        if (shelf > 0.34 && elevation > -620 && bankNoise > 0.66)
            return TerrainClassKind.ShallowBank;
        if ((rift > 0.42 || channelNoise > 0.82) && elevation < -850)
            return TerrainClassKind.DeepChannel;
        if (elevation < -3300 && basinNoise > 0.66)
            return TerrainClassKind.AbyssalBasin;
        if (elevation > -900 || coastal is CoastalZoneKind.Shelf or CoastalZoneKind.ShallowSea)
            return TerrainClassKind.ShelfSea;

        return TerrainClassKind.Ocean;
    }

    private static double AdjustRoughnessForTerrainClass(double roughness, TerrainClassKind terrainClass, double ridgeContinuity)
    {
        var target = terrainClass switch
        {
            TerrainClassKind.Beach => 0.07,
            TerrainClassKind.CoastalPlain => 0.14,
            TerrainClassKind.AlluvialPlain => 0.16,
            TerrainClassKind.InteriorLowland => 0.17,
            TerrainClassKind.SedimentaryBasin => 0.10,
            TerrainClassKind.DryBasin => 0.19,
            TerrainClassKind.DeltaCandidate => 0.10,
            TerrainClassKind.DesertPlateauCandidate => 0.22,
            TerrainClassKind.Highland => 0.30,
            TerrainClassKind.Mountain => 0.56 + ridgeContinuity * 0.24,
            _ => roughness
        };

        return Math.Clamp(roughness * 0.45 + target * 0.55, 0.03, 1.0);
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

    private static double[] DiffuseTectonicLineSignal(
        double[] values,
        int width,
        int height,
        int mediumPasses,
        int broadPasses,
        double threshold,
        double gamma,
        double localWeight)
    {
        var medium = SmoothField(values, width, height, mediumPasses);
        var broad = SmoothField(medium, width, height, broadPasses);
        var blended = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
            blended[i] = medium[i] * localWeight + broad[i] * (1.0 - localWeight);

        return ShapeSignal(blended, threshold, gamma);
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

    private static double[] BuildLandEnclosureField(MapMask mask)
    {
        var values = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
                values[y * mask.Width + x] = mask.IsLand(new GridPoint(x, y)) ? 1.0 : 0.0;
        }

        return SmoothField(SmoothField(values, mask.Width, mask.Height, 8), mask.Width, mask.Height, 8);
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

    private static double WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;
}
