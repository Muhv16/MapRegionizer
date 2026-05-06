using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;

namespace MapRegionizer.Runner;

public sealed class MapGenerationRunOptions
{
    public string MaskPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public double PixelSize { get; set; } = 1;
    public double SimplifyTolerance { get; set; } = 1;
    public uint TargetArea { get; set; } = 400;
    public double PointsMultiplier { get; set; } = 4;
    public double MinAreaRatio { get; set; } = 0.75;
    public double MaxAreaRatio { get; set; } = 1.75;
    public double BoundaryDetail { get; set; } = 0.25;
    public double MaxOffset { get; set; } = 3.25;
    public double MinLineLengthToCurve { get; set; } = 7;
    public int? Seed { get; set; }
    public MapProjectionMode ProjectionMode { get; set; } = MapProjectionMode.EquirectangularWorld;
    public int? PlateCount { get; set; }
    public int? HotspotCount { get; set; }
    public TectonicPlateJsonExportMode TectonicJsonMode { get; set; } = TectonicPlateJsonExportMode.Summary;
    public ElevationJsonExportMode ElevationJsonMode { get; set; } = ElevationJsonExportMode.Summary;

    public MapGenerationOptions ToGenerationOptions()
    {
        return new MapGenerationOptions
        {
            PixelSize = PixelSize,
            Seed = Seed,
            ProjectionMode = ProjectionMode,
            ShapeExtraction = new ShapeExtractionOptions
            {
                SimplifyTolerance = SimplifyTolerance
            },
            Regions = new RegionGenerationOptions
            {
                TargetArea = TargetArea,
                PointsMultiplier = PointsMultiplier,
                MinAreaRatio = MinAreaRatio,
                MaxAreaRatio = MaxAreaRatio
            },
            Boundaries = new BoundaryDistortionOptions
            {
                Detail = BoundaryDetail,
                MaxOffset = MaxOffset,
                MinLineLengthToCurve = MinLineLengthToCurve
            },
            TectonicPlates = new TectonicPlateGenerationOptions
            {
                PlateCount = PlateCount,
                HotspotCount = HotspotCount
            }
        };
    }
}
