using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal static class HydrologyTerrainRules
{
    public static bool IsRiverSourceLand(MapMask mask, GeneratedLakeMap generatedLakes, GridPoint point) =>
        mask.IsLand(point) && !generatedLakes.Contains(point);

    public static bool IsRenderableRiverLand(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        if (lakeIds[index] > 0)
            return false;
        if (!mask.IsLand(point))
            return false;
        return !topology.IsInlandWater(point);
    }

    public static bool IsWaterTarget(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        return lakeIds[index] > 0 || !mask.IsLand(point) && topology.IsOceanicWater(point);
    }

    public static bool EndsInWater(GridPoint point, MapMask mask, WaterBodyTopology topology, int[] lakeIds)
    {
        var index = point.Y * mask.Width + point.X;
        if (lakeIds[index] > 0)
            return true;
        return !mask.IsLand(point) && topology.GetKind(point) is (WaterBodyKind.Ocean or WaterBodyKind.OceanSea);
    }

    public static double TerrainValleyBias(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.AlluvialPlain => 38.0,
        TerrainClassKind.InteriorLowland => 30.0,
        TerrainClassKind.CoastalPlain => 28.0,
        TerrainClassKind.SedimentaryBasin => 26.0,
        TerrainClassKind.DeltaCandidate => 44.0,
        TerrainClassKind.DryBasin => 18.0,
        TerrainClassKind.Highland => 8.0,
        TerrainClassKind.Mountain => -18.0,
        _ => 0.0
    };

    public static double TerrainRiverThresholdMultiplier(TerrainClassKind terrain) => terrain switch
    {
        TerrainClassKind.Mountain => 0.55,
        TerrainClassKind.Highland => 0.75,
        TerrainClassKind.DeltaCandidate => 0.58,
        TerrainClassKind.AlluvialPlain => 0.82,
        TerrainClassKind.DryBasin => 0.80,
        TerrainClassKind.DesertPlateauCandidate => 0.95,
        _ => 1.0
    };

    public static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    public static TargetRef ResolveTarget(GridPoint terminal, MapMask mask, WaterBodyTopology topology, WaterSurfaceMap waterSurfaces, int[] lakeIds)
    {
        var x = Math.Clamp(terminal.X, 0, mask.Width - 1);
        var y = Math.Clamp(terminal.Y, 0, mask.Height - 1);
        var point = new GridPoint(x, y);
        var lakeId = lakeIds[y * mask.Width + x];
        if (lakeId > 0)
        {
            var body = waterSurfaces.GetBodySurface(new WaterBodyId(lakeId));
            return new TargetRef(body?.Kind == WaterBodyKind.InlandSea ? DrainageTargetKind.InlandSea : DrainageTargetKind.Lake, lakeId);
        }

        if (!mask.IsLand(point) && topology.IsOceanicWater(point))
            return new TargetRef(DrainageTargetKind.Ocean, topology.GetWaterBodyId(point)?.Value);

        return new TargetRef(DrainageTargetKind.EndorheicDryBasin, null);
    }
}