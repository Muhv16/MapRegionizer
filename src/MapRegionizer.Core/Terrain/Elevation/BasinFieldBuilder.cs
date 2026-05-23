using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class BasinFieldBuilder
{
    public BasinFields Build(ElevationInput context, TectonicFields tectonicFields, MountainFields mountainFields)
    {
        var basinInfluence = new double[context.Length];
        BuildBasinInfluence(
            context.Mask,
            context.DistanceToWater,
            context.ShelfWidth,
            tectonicFields.Subsidence,
            tectonicFields.SedimentSupply,
            tectonicFields.PassiveMask,
            tectonicFields.RiftProvince,
            tectonicFields.RiftGraben,
            mountainFields.RidgeContinuity,
            mountainFields.FoothillInfluence,
            basinInfluence);
        return new BasinFields(basinInfluence);
    }

    internal static void BuildBasinInfluence(
        MapMask mask,
        double[] distanceToWater,
        double shelfWidth,
        double[] subsidence,
        double[] sedimentSupply,
        double[] passiveMask,
        double[] riftProvince,
        double[] riftGraben,
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
                var tectonicBasin = subsidence[index] * 0.52 + sedimentSupply[index] * 0.28 + passiveMask[index] * 0.32 + riftProvince[index] * 0.18 + riftGraben[index] * 0.34;
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

}
