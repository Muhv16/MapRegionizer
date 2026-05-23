using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Terrain;

internal static class ElevationMapFactory
{
    public static ElevationMap CreateBaseTerrain(ElevationInput context, ElevationRasterSet rasters)
    {
        var waterSurface = new double[context.Length];
        Array.Fill(waterSurface, double.NaN);
        return new ElevationMap(
            context.Mask.Width,
            context.Mask.Height,
            rasters.Elevation,
            rasters.BaseElevation,
            rasters.TectonicElevation,
            rasters.Roughness,
            rasters.ErosionMask,
            rasters.TerrainClasses,
            rasters.MountainPassPotential,
            rasters.RidgeContinuity,
            rasters.FoothillInfluence,
            rasters.BasinInfluence,
            rasters.Elevation.ToArray(),
            waterSurface);
    }
}
