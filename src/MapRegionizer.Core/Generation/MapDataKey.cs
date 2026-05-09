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
    public static readonly MapDataKey WaterBodyTopology = new("waterBodyTopology");
    public static readonly MapDataKey RawRegions = new("rawRegions");
    public static readonly MapDataKey Regions = new("regions");
    public static readonly MapDataKey TectonicHistory = new("tectonicHistory");
    public static readonly MapDataKey CrustFields = new("crustFields");
    public static readonly MapDataKey PlateDomains = new("plateDomains");
    public static readonly MapDataKey TectonicBoundaries = new("tectonicBoundaries");
    public static readonly MapDataKey OrogenProvinces = new("orogenProvinces");
    public static readonly MapDataKey RiftProvinces = new("riftProvinces");
    public static readonly MapDataKey TectonicFeatures = new("tectonicFeatures");
    public static readonly MapDataKey BaseTerrain = new("baseTerrain");
    public static readonly MapDataKey Elevation = new("elevation");
    public static readonly MapDataKey WaterSurfaces = new("waterSurfaces");
    public static readonly MapDataKey TectonicPlates = new("tectonicPlates");
}

public static class MapStageIds
{
    public const string ExtractLandmasses = "extractLandmasses";
    public const string ExtractWaterBodies = "extractWaterBodies";
    public const string ClassifyWaterBodies = "classifyWaterBodies";
    public const string GenerateRegions = "generateRegions";
    public const string DistortRegionBoundaries = "distortRegionBoundaries";
    public const string GenerateTectonicHistory = "generateTectonicHistory";
    public const string GenerateCrustFields = "generateCrustFields";
    public const string GeneratePlateDomains = "generatePlateDomains";
    public const string GenerateTectonicBoundaries = "generateTectonicBoundaries";
    public const string GenerateOrogenProvinces = "generateOrogenProvinces";
    public const string GenerateRiftProvinces = "generateRiftProvinces";
    public const string GenerateTectonicFeatures = "generateTectonicFeatures";
    public const string GenerateElevation = "generateElevation";
    public const string GenerateLakeLevels = "generateLakeLevels";
    public const string GenerateTectonicPlates = "generateTectonicPlates";
}
