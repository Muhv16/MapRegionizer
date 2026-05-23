using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class MountainFieldBuilder
{
    public MountainFields Build(ElevationInput context, TectonicFields tectonicFields)
    {
        var ridgeContinuity = new double[context.Length];
        var mountainPassPotential = new double[context.Length];
        var foothillInfluence = new double[context.Length];

        BuildMountainNetworkFields(
            context.Mask,
            tectonicFields.CollisionMask,
            tectonicFields.MassifMask,
            tectonicFields.ForelandMask,
            ridgeContinuity,
            mountainPassPotential,
            foothillInfluence);
        ApplyOrogenProvinceFields(
            context.Mask,
            tectonicFields.OrogenProvince,
            tectonicFields.OrogenStrength,
            ridgeContinuity,
            mountainPassPotential,
            foothillInfluence);

        return new MountainFields(ridgeContinuity, mountainPassPotential, foothillInfluence);
    }

    internal static void BuildMountainNetworkFields(
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

    internal static void ApplyOrogenProvinceFields(
        MapMask mask,
        double[] orogenProvince,
        double[] orogenStrength,
        double[] ridgeContinuity,
        double[] mountainPassPotential,
        double[] foothillInfluence)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    continue;

                var index = y * mask.Width + x;
                var province = Math.Clamp(orogenProvince[index], 0, 1.2);
                var strength = Math.Clamp(orogenStrength[index], 0, 1.2);
                if (province <= 0 && strength <= 0)
                    continue;

                var broadNoise = SmoothNoise(x + 23, y - 41, 1631, 30.0);
                var breakupNoise = SmoothNoise(x - 59, y + 17, 1637, 13.0);
                var highland = province * (0.58 + broadNoise * 0.34) + strength * 0.32;
                if (breakupNoise < 0.22)
                    highland *= 0.62 + breakupNoise * 1.15;

                foothillInfluence[index] = Math.Max(foothillInfluence[index], Math.Clamp(highland, 0, 1));
                ridgeContinuity[index] = Math.Max(ridgeContinuity[index], Math.Clamp(strength * 0.26 + province * 0.08, 0, 0.42));
                mountainPassPotential[index] = Math.Max(mountainPassPotential[index], Math.Clamp(province * (1.0 - strength) * 0.28, 0, 0.32));
            }
        }
    }

}
