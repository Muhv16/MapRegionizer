using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Terrain;

internal static class HydrologyElevationExtensions
{
    public static double GetHydrologyHeight(this ElevationMap elevation, GridPoint point) =>
        elevation.HasWaterSurface(point) ? elevation.GetWaterSurface(point) : elevation.GetBedElevation(point);
}