using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class TectonicFieldBuilder
{
    public TectonicFields Build(ElevationInput context)
    {
        var ridgeMask = new double[context.Length];
        var collisionMask = new double[context.Length];
        var massifMask = new double[context.Length];
        var subductionMask = new double[context.Length];
        var passiveMask = new double[context.Length];

        StampBoundaryMasks(context.Mask, context.Boundaries, ridgeMask, collisionMask, massifMask, subductionMask, passiveMask);
        var rawCollisionMask = collisionMask.ToArray();
        ridgeMask = ShapeSignal(SmoothField(ridgeMask, context.Mask.Width, context.Mask.Height, 11), 0.16, 1.65);
        collisionMask = ShapeSignal(SmoothField(collisionMask, context.Mask.Width, context.Mask.Height, 4), 0.10, 1.15);
        massifMask = ShapeSignal(SmoothField(massifMask, context.Mask.Width, context.Mask.Height, 6), 0.05, 1.0);
        var forelandMask = ShapeSignal(SmoothField(rawCollisionMask, context.Mask.Width, context.Mask.Height, 12), 0.04, 1.25);
        subductionMask = DiffuseTectonicLineSignal(subductionMask, context.Mask.Width, context.Mask.Height, 8, 11, 0.08, 1.18, 0.24);
        passiveMask = DiffuseTectonicLineSignal(passiveMask, context.Mask.Width, context.Mask.Height, 6, 10, 0.03, 1.0, 0.26);

        return new TectonicFields(
            ridgeMask,
            collisionMask,
            massifMask,
            forelandMask,
            subductionMask,
            passiveMask,
            BuildTerrainSignal(context.Features, context.Features.GetUplift, 14, 0.18, 1.25),
            BuildTerrainSignal(context.Features, context.Features.GetSubsidence, 8, 0.24, 1.35),
            BuildTerrainSignal(context.Features, context.Features.GetVolcanism, 4, 0.13, 1.15),
            BuildTerrainSignal(context.Features, context.Features.GetHeatFlow, 8, 0.22, 1.4),
            BuildTerrainSignal(context.Features, context.Features.GetSedimentSupply, 7, 0.24, 1.35),
            BuildOrogenProvinceSignal(context.OrogenProvinces, context.OrogenProvinces.GetInfluence, 5, 0.04, 0.95),
            BuildOrogenProvinceSignal(context.OrogenProvinces, context.OrogenProvinces.GetStrength, 3, 0.03, 0.90),
            BuildRiftProvinceSignal(context.RiftProvinces, context.RiftProvinces.GetRiftInfluence, 5, 0.035, 0.92),
            BuildRiftProvinceSignal(context.RiftProvinces, context.RiftProvinces.GetGrabenMask, 2, 0.05, 1.04),
            BuildRiftProvinceSignal(context.RiftProvinces, context.RiftProvinces.GetShoulderUpliftMask, 2, 0.04, 0.96),
            BuildRiftProvinceSignal(context.RiftProvinces, context.RiftProvinces.GetHeatFlowMask, 6, 0.045, 0.9),
            BuildRiftProvinceSignal(context.RiftProvinces, context.RiftProvinces.GetBreakupMask, 2, 0.03, 0.88));
    }

    internal static void StampBoundaryMasks(
        MapMask mask,
        TectonicBoundaryMap boundaries,
        double[] ridgeMask,
        double[] collisionMask,
        double[] massifMask,
        double[] subductionMask,
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

    internal static int BoundaryStampRadius(GridPoint point, int segmentId, BoundaryMode mode, int baseRadius)
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

    internal static double BoundaryStampStrength(GridPoint point, int segmentId, BoundaryMode mode)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 53 + 1409, mode is BoundaryMode.PassiveMargin ? 36.0 : 24.0);
        var local = SmoothNoise(point.X - 31, point.Y + 7, segmentId * 59 + 1423, 10.0);
        return Math.Clamp(0.58 + broad * 0.52 + local * 0.20, 0.42, 1.18);
    }

    internal static double BoundaryMassifWidth(GridPoint point, int segmentId)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 67 + 1517, 35.0);
        var local = SmoothNoise(point.X + 13, point.Y + 37, segmentId * 71 + 1523, 12.0);
        return Math.Clamp(0.58 + broad * 0.88 + local * 0.28, 0.60, 1.45);
    }

    internal static bool IsMountainBoundary(BoundaryMode mode) =>
        mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression;

    internal static double MountainGate(GridPoint point, int segmentId, BoundaryMode mode)
    {
        var broad = SmoothNoise(point.X, point.Y, segmentId * 17 + 503, mode == BoundaryMode.ContinentContinentCollision ? 34.0 : 26.0);
        var local = SmoothNoise(point.X, point.Y, segmentId * 31 + 907, 11.0);
        var pass = SmoothNoise(point.X, point.Y, segmentId * 43 + 1201, 6.0);
        return Math.Clamp(broad * 0.64 + local * 0.24 + pass * 0.12, 0, 1);
    }

}
