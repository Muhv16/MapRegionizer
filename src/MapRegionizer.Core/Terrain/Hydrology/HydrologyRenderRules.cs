using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;

namespace MapRegionizer.Core.Terrain;

internal static class HydrologyRenderRules
{
    public static double ComputeMeanSlope(ElevationMap elevation, IReadOnlyList<GridPoint> cells, GridPoint terminal, DrainageTargetKind targetKind)
    {
        if (cells.Count < 2)
            return 0;

        var source = elevation.GetHydrologyHeight(cells[0]);
        var mouth = targetKind == DrainageTargetKind.Ocean
            ? 0.0
            : elevation.GetHydrologyHeight(terminal);
        return Math.Max(0.0, source - mouth) / Math.Max(1, cells.Count - 1);
    }

    public static RiverMouthKind ClassifyMouth(ElevationMap elevation, GridPoint terminal, DrainageTargetKind targetKind, double discharge, double meanSlope, HydrologyGenerationOptions options)
    {
        if (targetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea or DrainageTargetKind.EndorheicDryBasin)
            return discharge > 110 && meanSlope < 8.0 ? RiverMouthKind.InlandDelta : RiverMouthKind.SimpleMouth;

        var terrain = elevation.GetTerrainClass(Math.Clamp(terminal.X, 0, elevation.Width - 1), Math.Clamp(terminal.Y, 0, elevation.Height - 1));
        var deltaScore = (terrain == TerrainClassKind.DeltaCandidate ? 0.45 : 0.0)
                         + Math.Clamp(discharge / 380.0, 0, 0.55)
                         + (meanSlope < 4.5 ? 0.22 : 0.0)
                         + options.DeltaFrequency * 0.12;
        if (deltaScore >= 0.82)
            return terrain == TerrainClassKind.DeltaCandidate && meanSlope < 2.8 ? RiverMouthKind.MarshDelta : RiverMouthKind.Delta;
        return meanSlope > 8.0 ? RiverMouthKind.Estuary : RiverMouthKind.SimpleMouth;
    }

    public static bool IsClosedLakeTarget(TargetRef target, HashSet<int> outletLakeIds) =>
        target.Kind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
        (!target.TargetId.HasValue || !outletLakeIds.Contains(target.TargetId.Value));

    public static MountainSourceSide RiverTargetSide(RiverSegment river, int width)
    {
        var dx = WrappedDeltaX(river.Mouth.X - river.Source.X, width);
        var dy = river.Mouth.Y - river.Source.Y;
        if (Math.Abs(dx) > Math.Abs(dy))
            return dx < 0 ? MountainSourceSide.West : MountainSourceSide.East;
        if (dy != 0)
            return dy < 0 ? MountainSourceSide.North : MountainSourceSide.South;
        return MountainSourceSide.None;
    }
}