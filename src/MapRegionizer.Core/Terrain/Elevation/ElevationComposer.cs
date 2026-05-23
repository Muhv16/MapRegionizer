using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class ElevationComposer
{
    private readonly ElevationNoise _noise;

    public ElevationComposer(ElevationNoise noise)
    {
        _noise = noise;
    }

    public ElevationRasterSet Compose(
        ElevationInput context,
        TectonicFields tectonicFields,
        CoastalFields coastalFields,
        MountainFields mountainFields,
        BasinFields basinFields)
    {
        var elevation = new double[context.Length];
        var baseElevation = new double[context.Length];
        var tectonicElevation = new double[context.Length];
        var roughness = new double[context.Length];
        var erosionMask = new double[context.Length];
        var terrainClasses = new byte[context.Length];

        for (var y = 0; y < context.Mask.Height; y++)
        {
            for (var x = 0; x < context.Mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * context.Mask.Width + x;
                var isLand = context.Mask.IsLand(point);
                var isInlandWater = !isLand && context.WaterBodyTopology?.IsInlandWater(point) == true;
                var isLandLike = isLand || isInlandWater;
                var crust = context.CrustFields.GetCrust(point);
                var coastal = context.CrustFields.GetCoastalZone(point);
                var domain = context.Domains.TryGetValue(context.PlateDomains.GetPlate(point).Value, out var foundDomain) ? foundDomain : null;
                var activity = domain?.Activity ?? 0.5;
                var coastalInfluence = Math.Clamp(1.0 - context.DistanceToWater[index] / Math.Max(1.0, coastalFields.ShelfWidth * 3.2), 0, 1);

                baseElevation[index] = isLandLike
                    ? ComputeLandBase(context.DistanceToWater[index], coastalFields.InlandScale, crust, coastal)
                    : ComputeSeaBase(x, y, context.DistanceToLand[index], coastalFields.ShelfWidth, coastalFields.DeepOceanScale, crust, coastal, context.CrustFields.GetOceanicAge(point), context.Options);

                tectonicElevation[index] = ComputeTectonicContribution(
                    isLandLike,
                    crust,
                    coastal,
                    activity,
                    tectonicFields.Uplift[index],
                    tectonicFields.Subsidence[index],
                    tectonicFields.Volcanism[index],
                    tectonicFields.HeatFlow[index],
                    tectonicFields.SedimentSupply[index],
                    tectonicFields.RidgeMask[index],
                    tectonicFields.CollisionMask[index],
                    tectonicFields.MassifMask[index],
                    tectonicFields.ForelandMask[index],
                    tectonicFields.OrogenProvince[index],
                    tectonicFields.OrogenStrength[index],
                    mountainFields.MountainPassPotential[index],
                    mountainFields.RidgeContinuity[index],
                    mountainFields.FoothillInfluence[index],
                    basinFields.BasinInfluence[index],
                    tectonicFields.SubductionMask[index],
                    tectonicFields.RiftProvince[index],
                    tectonicFields.RiftGraben[index],
                    tectonicFields.RiftShoulder[index],
                    tectonicFields.RiftHeat[index],
                    tectonicFields.RiftBreakup[index],
                    tectonicFields.PassiveMask[index],
                    coastalInfluence,
                    context.Options);

                roughness[index] = ComputeRoughness(
                    isLandLike,
                    crust,
                    coastal,
                    tectonicFields.Uplift[index],
                    tectonicFields.Volcanism[index],
                    tectonicFields.RidgeMask[index],
                    tectonicFields.CollisionMask[index],
                    tectonicFields.OrogenProvince[index],
                    tectonicFields.RiftGraben[index],
                    tectonicFields.RiftShoulder[index],
                    basinFields.BasinInfluence[index],
                    context.Options);

                var detail = _noise.FractalNoise(x, y, scale: 32.0, octaves: 5);
                var broad = _noise.FractalNoise(x + 113, y - 47, scale: 96.0, octaves: 3);
                var coastDampening = isLandLike
                    ? Math.Clamp(context.DistanceToWater[index] / Math.Max(1.0, coastalFields.ShelfWidth), 0.25, 1.0)
                    : Math.Clamp(context.DistanceToLand[index] / Math.Max(1.0, coastalFields.ShelfWidth), 0.18, 1.0);
                var noiseAmplitude = (isLandLike ? 430.0 : 120.0) * context.Options.Roughness * roughness[index] * coastDampening;
                var broadWeight = isLandLike ? 0.45 : 0.25;
                var shapedNoise = detail * noiseAmplitude + broad * noiseAmplitude * broadWeight;

                elevation[index] = (baseElevation[index] + tectonicElevation[index] + shapedNoise) * context.Options.ReliefScale;
            }
        }

        ApplyLargeBasins(context.Mask, elevation, basinFields.BasinInfluence, context.DistanceToWater, coastalFields.ShelfWidth);
        ApplyMountainCrossSection(context.Mask, elevation, mountainFields.RidgeContinuity, mountainFields.MountainPassPotential, mountainFields.FoothillInfluence, basinFields.BasinInfluence, context.DistanceToWater, coastalFields.ShelfWidth);
        ApplyBathymetricStructure(context.Mask, context.WaterBodyTopology, elevation, context.DistanceToLand, context.LandEnclosure, coastalFields.ShelfWidth, tectonicFields.RidgeMask, tectonicFields.SubductionMask, tectonicFields.RiftProvince, tectonicFields.RiftGraben, context.CrustFields, context.Options);
        ApplyIslandProfiles(context.Mask, context.Features.Islands, elevation, roughness, context.DistanceToLand, context.Options);

        return new ElevationRasterSet(
            elevation,
            baseElevation,
            tectonicElevation,
            roughness,
            erosionMask,
            terrainClasses,
            mountainFields.MountainPassPotential,
            mountainFields.RidgeContinuity,
            mountainFields.FoothillInfluence,
            basinFields.BasinInfluence);
    }

    internal static double ComputeLandBase(double distanceToWater, double inlandScale, CrustKind crust, CoastalZoneKind coastal)
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

    internal static double ComputeSeaBase(int x, int y, double distanceToLand, double shelfWidth, double deepOceanScale, CrustKind crust, CoastalZoneKind coastal, double oceanicAge, ElevationGenerationOptions options)
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

    internal static double ComputeTectonicContribution(
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
        double orogenProvince,
        double orogenStrength,
        double mountainPassPotential,
        double ridgeContinuity,
        double foothillInfluence,
        double basinInfluence,
        double subduction,
        double riftProvince,
        double riftGraben,
        double riftShoulder,
        double riftHeat,
        double riftBreakup,
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
        var provinceHighland = Math.Clamp(orogenProvince * 0.62 + orogenStrength * 0.82, 0, 1.4);
        var mountainContext = Math.Clamp(collision * 0.55 + massif * 0.85 + provinceHighland * 0.38 + ridgeContinuity * 0.32, 0, 1);
        var regionalUpliftMeters = isLand
            ? 280 + mountainContext * 360 + Math.Clamp(volcanism, 0, 2) * 80
            : 35;
        var subductionMeters = isLand
            ? 190 + coastalInfluence * 130 + Math.Clamp(volcanism, 0, 2) * 70
            : -55;

        contribution += Math.Clamp(uplift, 0, 2) * regionalUpliftMeters * mountains;
        contribution += collision * passDampening * ridgeStrength * (isLand ? 1500 : 35) * mountains;
        contribution += massif * passDampening * (0.72 + ridgeContinuity * 0.38) * (isLand ? 3200 : 45) * mountains;
        contribution += provinceHighland * (isLand ? 620 : 0) * mountains;
        contribution += Math.Max(foreland, foothillInfluence) * (isLand ? 360 : 0) * mountains;
        contribution += subduction * subductionMeters * mountains;
        contribution += Math.Clamp(volcanism, 0, 2) * options.VolcanismInfluence * (isLand ? 660 : 90);
        contribution += ridge * (isLand ? 30 : 16);
        contribution += Math.Clamp(heatFlow, 0, 2) * (isLand ? 80 : 8);
        contribution += Math.Clamp(riftShoulder, 0, 1.4) * options.RiftInfluence * (isLand ? 145 : 18);
        contribution += Math.Clamp(riftHeat, 0, 1.6) * options.RiftInfluence * (isLand ? 36 : 8);
        contribution -= Math.Clamp(subsidence, 0, 2) * (isLand ? 230 * landBasinDampening : 130);
        contribution -= Math.Clamp(sedimentSupply, 0, 2) * (isLand ? 45 * coastalInfluence : 10);
        contribution -= basinInfluence * (isLand ? 205 : 0);
        contribution -= Math.Clamp(riftGraben, 0, 1.6) * options.RiftInfluence * (isLand ? 360 : 92);
        contribution -= Math.Clamp(riftProvince, 0, 1.4) * options.RiftInfluence * (isLand ? 82 : 38);
        contribution -= Math.Clamp(riftBreakup, 0, 1.2) * options.RiftInfluence * (isLand ? 26 : 16);
        contribution -= passive * (isLand ? 120 * coastalInfluence : 24);

        if (crust == CrustKind.Rift)
            contribution -= options.RiftInfluence * (isLand ? 80 : 24);
        if (crust == CrustKind.Arc)
            contribution += options.VolcanismInfluence * (isLand ? 380 : 60);
        if (coastal == CoastalZoneKind.PassiveMargin)
            contribution -= isLand ? 100 : 28;

        return contribution;
    }

    internal static double ComputeRoughness(
        bool isLand,
        CrustKind crust,
        CoastalZoneKind coastal,
        double uplift,
        double volcanism,
        double ridge,
        double collision,
        double orogenProvince,
        double riftGraben,
        double riftShoulder,
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
        roughness += ridge * 0.18 + collision * 0.28 + orogenProvince * 0.18 + riftShoulder * 0.16 + riftGraben * 0.08;
        roughness -= basinInfluence * (isLand ? 0.18 : 0.06);

        return Math.Clamp(roughness * (0.55 + options.Roughness), 0.05, 1.0);
    }

    internal static void ApplyLargeBasins(MapMask mask, double[] elevation, double[] basinInfluence, double[] distanceToWater, double shelfWidth)
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

    internal static void ApplyMountainCrossSection(
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

    internal static void ApplyBathymetricStructure(
        MapMask mask,
        WaterBodyTopology? waterBodyTopology,
        double[] elevation,
        double[] distanceToLand,
        double[] landEnclosure,
        double shelfWidth,
        double[] ridgeMask,
        double[] subductionMask,
        double[] riftProvince,
        double[] riftGraben,
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
                if (waterBodyTopology is not null && !waterBodyTopology.IsOceanicWater(point))
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
                var riftChannel = riftProvince[index] * 0.25 + riftGraben[index] * 0.62;
                var deepChannel = riftChannel + Math.Clamp(enclosure - 0.28, 0, 1) * Math.Clamp(channelNoise - 0.42, 0, 1) * 1.7;
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

    internal void ApplyIslandProfiles(MapMask mask, IReadOnlyList<TectonicIsland> islands, double[] elevation, double[] roughness, double[] distanceToLand, ElevationGenerationOptions options)
    {
        var smallIslandAreaScale = Math.Max(1.0, Math.Min(mask.Width, mask.Height) * 0.10);

        foreach (var island in islands)
        {
            var islandSize = Math.Clamp(Math.Sqrt(Math.Max(1.0, island.Area)) / smallIslandAreaScale, 0, 1);
            var reliefFactor = Lerp(options.SmallIslandReliefFactor, 1.0, islandSize);
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
                var peakNoise = SmoothStep(Math.Clamp((SmoothNoise(point.X + 19, point.Y - 31, island.PlateId.Value + 1931, 6.5) - 0.42) / 0.40, 0, 1));
                var largeIslandPeakGate = SmoothStep(Math.Clamp((islandSize - 0.24) / 0.77, 0, 1));

                switch (island.Kind)
                {
                    case IslandKind.VolcanicArc:
                        if (mask.IsLand(point))
                        {
                            var cone = Math.Pow(radial, 1.55);
                            elevation[index] += cone * 920 * reliefFactor;
                            roughness[index] = Math.Clamp(roughness[index] + cone * 0.28 * reliefFactor, 0, 1);
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
                            var relief = (_noise.FractalNoise(point.X + 41, point.Y - 17, 18.0, 4) - 0.35) * 420 * reliefFactor;
                            elevation[index] += (radial * 160 * reliefFactor) + relief;
                            roughness[index] = Math.Clamp(roughness[index] + (0.10 + radial * 0.08) * reliefFactor, 0, 1);
                        }
                        break;
                    case IslandKind.UpliftedRidge:
                        if (mask.IsLand(point))
                        {
                            var ridge = Math.Pow(narrowRidge, 1.2);
                            elevation[index] += ridge * 520 * reliefFactor;
                            roughness[index] = Math.Clamp(roughness[index] + ridge * 0.18 * reliefFactor, 0, 1);
                        }
                        break;
                    case IslandKind.Hotspot:
                        if (mask.IsLand(point))
                        {
                            var chain = Math.Max(Math.Pow(radial, 1.8), Math.Pow(narrowRidge, 1.35) * 0.72);
                            var ageGradient = Math.Clamp(0.65 + projection / Math.Max(1.0, radius * 3.2), 0.35, 1.15);
                            elevation[index] += chain * ageGradient * 820 * reliefFactor;
                            roughness[index] = Math.Clamp(roughness[index] + chain * 0.24 * reliefFactor, 0, 1);
                        }
                        break;
                }

                if (mask.IsLand(point))
                {
                    var peakPotential = ComputeIslandPeakPotential(island.Kind, radial, narrowRidge, peakNoise) * largeIslandPeakGate;
                    ApplySmallIslandReliefCeiling(island.Kind, islandSize, options.SmallIslandReliefFactor, peakPotential, index, elevation);
                }
            }
        }
    }

    internal static double ComputeIslandPeakPotential(IslandKind kind, double radial, double narrowRidge, double peakNoise)
    {
        return kind switch
        {
            IslandKind.VolcanicArc => Math.Pow(radial, 2.5) * (0.30 + peakNoise * 0.70),
            IslandKind.Hotspot => Math.Max(Math.Pow(radial, 2.8), Math.Pow(narrowRidge, 2.1) * 0.82) * (0.34 + peakNoise * 0.66),
            IslandKind.Microcontinent => Math.Pow(radial, 2.2) * peakNoise * 0.48,
            IslandKind.UpliftedRidge => Math.Pow(narrowRidge, 1.7) * (0.42 + peakNoise * 0.58),
            _ => 0.0
        };
    }

    internal static void ApplySmallIslandReliefCeiling(IslandKind kind, double islandSize, double smallIslandReliefFactor, double peakPotential, int index, double[] elevation)
    {
        var ceilingSizeStrength = 1.0 - SmoothStep(Math.Clamp((islandSize - 0.35) / 0.35, 0, 1));
        var reliefCeilingStrength = Math.Clamp(1.0 - smallIslandReliefFactor, 0, 1);
        var ceilingStrength = ceilingSizeStrength * reliefCeilingStrength;
        if (ceilingStrength <= 0)
            return;

        var broadCeiling = kind switch
        {
            IslandKind.VolcanicArc => 1150.0 + islandSize * 1850.0,
            IslandKind.Hotspot => 1250.0 + islandSize * 1750.0,
            IslandKind.Microcontinent => 850.0 + islandSize * 1450.0,
            IslandKind.UpliftedRidge => 720.0 + islandSize * 1250.0,
            IslandKind.ShelfArchipelago => 280.0 + islandSize * 520.0,
            _ => 900.0 + islandSize * 1300.0
        };
        var peakAllowance = kind switch
        {
            IslandKind.VolcanicArc => 4600.0,
            IslandKind.Hotspot => 3300.0,
            IslandKind.Microcontinent => 2950.0,
            IslandKind.UpliftedRidge => 1500.0,
            _ => 0.0
        };
        var ceiling = broadCeiling + peakAllowance * Math.Clamp(peakPotential, 0, 1);

        if (elevation[index] <= ceiling)
            return;

        var unrestrictedExcessScale = 1.0;
        var restrictedExcessScale = Math.Clamp(smallIslandReliefFactor * Lerp(0.35, 0.62, peakPotential), 0.02, 1.0);
        var excessScale = Lerp(unrestrictedExcessScale, restrictedExcessScale, ceilingStrength);
        elevation[index] = ceiling + (elevation[index] - ceiling) * excessScale;
    }

}
