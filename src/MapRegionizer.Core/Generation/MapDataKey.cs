namespace MapRegionizer.Core.Generation;

public readonly record struct MapDataKey(string Value)
{
    public override string ToString() => Value;
}

public static class MapDataKeys
{
    public static readonly MapDataKey Mask = new("mask");
    public static readonly MapDataKey Landmasses = new("landmasses");
    public static readonly MapDataKey WaterBodies = new("waterBodies");
    public static readonly MapDataKey RawRegions = new("rawRegions");
    public static readonly MapDataKey Regions = new("regions");
}

public static class MapStageIds
{
    public const string ExtractLandmasses = "extractLandmasses";
    public const string ExtractWaterBodies = "extractWaterBodies";
    public const string GenerateRegions = "generateRegions";
    public const string DistortRegionBoundaries = "distortRegionBoundaries";
}
