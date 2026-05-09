using MapRegionizer.Core.Domain;

namespace MapRegionizer.Runner;

public sealed record MapGenerationRunResult(
    GeneratedMap Map,
    MapGenerationArtifactPaths Artifacts,
    MapGenerationRunSummary Summary);

public sealed record MapGenerationArtifactPaths(
    string ResultImage,
    string? TectonicPlatesImage,
    string? TectonicCrustImage,
    string? TectonicFeaturesImage,
    string? ElevationImage,
    string? ElevationFinalImage,
    string? ElevationBaseImage,
    string? ElevationTectonicImage,
    string? ElevationRoughnessImage,
    string? ElevationErosionImage,
    string? ElevationTerrainZonesImage,
    string? ElevationMountainImage,
    string? ElevationBasinImage,
    string RegionsGeoJson,
    string LandmassesGeoJson,
    string WaterBodiesGeoJson,
    string? TectonicPlatesJson,
    string? ElevationJson,
    string? LakesJson,
    string SummaryJson);

public sealed record MapGenerationRunSummary(
    DateTimeOffset GeneratedAtUtc,
    string MaskPath,
    string OutputDirectory,
    MapGenerationRunOptionSummary Options,
    MapGenerationMapSummary Map,
    MapGenerationArtifactPaths Artifacts);

public sealed record MapGenerationRunOptionSummary(
    double PixelSize,
    double SimplifyTolerance,
    uint TargetArea,
    double PointsMultiplier,
    double MinAreaRatio,
    double MaxAreaRatio,
    double BoundaryDetail,
    double MaxOffset,
    double MinLineLengthToCurve,
    int? Seed,
    string ProjectionMode,
    int? PlateCount,
    int? HotspotCount,
    bool GenerateSmallLakes,
    double SmallLakeCountMultiplier,
    double SmallLakeScatterMultiplier,
    double SmallLakeSizeMultiplier,
    string TectonicJsonMode,
    string ElevationJsonMode);

public sealed record MapGenerationMapSummary(
    double Width,
    double Height,
    int LandmassCount,
    int WaterBodyCount,
    int RegionCount,
    int? TectonicPlateCount,
    int? TectonicBoundaryCount,
    int? TectonicFeatureCount,
    int? TectonicIslandCount,
    int? ElevationWidth,
    int? ElevationHeight,
    double? MinElevationMeters,
    double? MaxElevationMeters);
