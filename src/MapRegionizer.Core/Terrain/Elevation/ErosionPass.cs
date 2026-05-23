using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class ErosionPass
{
    public ElevationRasterSet Apply(ElevationRasterSet rasters, ElevationInput context, TectonicFields tectonicFields)
    {
        SmoothElevation(context.Mask, rasters.Elevation, tectonicFields.RidgeMask, tectonicFields.CollisionMask, context.Options, rasters.ErosionMask);
        LiftInteriorLowlands(context.Mask, rasters.Elevation, context.DistanceToWater, context.ShelfWidth);
        EnforceConstraints(context.Mask, context.WaterBodyTopology, rasters.Elevation, context.Options);
        return rasters;
    }

    internal static void SmoothElevation(
    MapMask mask,
    double[] elevation,
    double[] ridgeMask,
    double[] collisionMask,
    ElevationGenerationOptions options,
    double[] erosionMask)
    {
        if (options.Erosion <= 0)
            return;

        var width = mask.Width;
        var height = mask.Height;
        var length = width * height;
        var passes = 5;

        var source = elevation.ToArray();
        var target = new double[length];

        for (var pass = 0; pass < passes; pass++)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;

                for (var x = 0; x < width; x++)
                {
                    var point = new GridPoint(x, y);
                    var index = row + x;
                    var isLand = mask.IsLand(point);

                    var sameSurfaceSum = 0.0;
                    var sameSurfaceWeight = 0.0;

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= height)
                            continue;

                        var nrow = ny * width;

                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            var nx = x + dx;
                            if (nx < 0)
                                nx = width - 1;
                            else if (nx >= width)
                                nx = 0;

                            var neighbor = new GridPoint(nx, ny);
                            if (mask.IsLand(neighbor) != isLand)
                                continue;

                            sameSurfaceSum += source[nrow + nx];
                            sameSurfaceWeight += 1.0;
                        }
                    }

                    if (sameSurfaceWeight <= 0)
                    {
                        target[index] = source[index];
                        continue;
                    }

                    var average = sameSurfaceSum / sameSurfaceWeight;
                    var protectedRelief = Math.Clamp(
                        collisionMask[index] * 0.7 + ridgeMask[index] * (isLand ? 0.08 : 0.2),
                        0,
                        0.75);

                    var waterBoost = isLand ? 0.58 : 1.65;
                    var erosion = options.Erosion * waterBoost * (1.0 - protectedRelief);

                    erosionMask[index] = Math.Max(erosionMask[index], erosion);
                    target[index] = source[index] * (1.0 - erosion) + average * erosion;
                }
            }

            (source, target) = (target, source);
        }

        Array.Copy(source, elevation, length);
    }

    internal static void EnforceConstraints(MapMask mask, WaterBodyTopology? waterBodyTopology, double[] elevation, ElevationGenerationOptions options)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                var value = Math.Clamp(elevation[index], options.MinOceanDepthMeters, options.MaxElevationMeters);

                var preserveOcean = options.PreserveOceanCoastline;
                var preserveInlandWater = options.PreserveInlandWaterMask;
                var isLand = mask.IsLand(point);
                var isOceanicWater = !isLand && (waterBodyTopology?.IsOceanicWater(point) ?? true);
                var isInlandWater = !isLand && waterBodyTopology?.IsInlandWater(point) == true;

                if (preserveOcean && isLand)
                    value = Math.Max(value, options.MinLandElevationMeters);
                else if (preserveOcean && isOceanicWater)
                    value = Math.Min(value, options.MaxSeaElevationMeters);
                else if (preserveInlandWater && isInlandWater)
                {
                    value = Math.Min(value, options.MaxElevationMeters);
                }

                elevation[index] = Math.Clamp(value, options.MinOceanDepthMeters, options.MaxElevationMeters);
            }
        }
    }

    internal static void LiftInteriorLowlands(MapMask mask, double[] elevation, double[] distanceToWater, double shelfWidth)
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

}
