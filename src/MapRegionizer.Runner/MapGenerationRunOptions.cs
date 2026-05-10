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
    public double SmallIslandReliefFactor { get; set; } = 0.55;
    public bool GenerateSmallLakes { get; set; } = true;
    public double SmallLakeCountMultiplier { get; set; } = 0.5;
    public double SmallLakeScatterMultiplier { get; set; } = 0.5;
    public double SmallLakeSizeMultiplier { get; set; } = 0.2;
    public double RiverDensity { get; set; } = 10;
    public double MajorRiverCountMultiplier { get; set; } = 1.5;
    public double LongRiverCountMultiplier { get; set; } = 1.3;
    public double TributaryDensity { get; set; } = 3.5;
    public double MajorRiverTributaryMultiplier { get; set; } = 1000.0;
    public double LakeOutletInflowForceMultiplier { get; set; } = 1000.0;
    public double EndorheicBasinChance { get; set; } = 0.22;
    public double DeltaFrequency { get; set; } = 0.8;
    public double MeanderStrength { get; set; } = 0.65;
    public double LakeOutletStrictness { get; set; } = 0.55;
    public bool PreserveRiverCoastline { get; set; } = true;
    public bool AllowRiverCarving { get; set; } = false;
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
            },
            Elevation = new ElevationGenerationOptions
            {
                SmallIslandReliefFactor = SmallIslandReliefFactor,
                GenerateSmallLakes = GenerateSmallLakes,
                SmallLakeCountMultiplier = SmallLakeCountMultiplier,
                SmallLakeScatterMultiplier = SmallLakeScatterMultiplier,
                SmallLakeSizeMultiplier = SmallLakeSizeMultiplier
            },
            Hydrology = new HydrologyGenerationOptions
            {
                RiverDensity = RiverDensity,
                MajorRiverCountMultiplier = MajorRiverCountMultiplier,
                LongRiverCountMultiplier = LongRiverCountMultiplier,
                TributaryDensity = TributaryDensity,
                MajorRiverTributaryMultiplier = MajorRiverTributaryMultiplier,
                LakeOutletInflowForceMultiplier = LakeOutletInflowForceMultiplier,
                EndorheicBasinChance = EndorheicBasinChance,
                DeltaFrequency = DeltaFrequency,
                MeanderStrength = MeanderStrength,
                LakeOutletStrictness = LakeOutletStrictness,
                PreserveCoastline = PreserveRiverCoastline,
                AllowRiverCarving = AllowRiverCarving
            }
        };
    }

    public MapGenerationRequestOptions ToRequestOptions()
    {
        return new MapGenerationRequestOptions
        {
            MaskPath = MaskPath,
            OutputDirectory = OutputDirectory,
            GenerationOptions = ToGenerationOptions(),
            TectonicJsonMode = TectonicJsonMode,
            ElevationJsonMode = ElevationJsonMode
        };
    }
}
