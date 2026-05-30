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
    public bool BoundariesEnabled { get; set; } = true;
    public double BoundaryDetail { get; set; } = 0.25;
    public double MaxOffset { get; set; } = 3.25;
    public double MinLineLengthToCurve { get; set; } = 7;
    public int? Seed { get; set; }
    public MapProjectionMode ProjectionMode { get; set; } = MapProjectionMode.EquirectangularWorld;
    public double OceanSeaMinAreaRatio { get; set; } = 0.12;
    public double InlandSeaMinAreaRatio { get; set; } = 0.015;
    public int OceanSeaNearOceanMaxDistanceCells { get; set; } = 13;
    public int? PlateCount { get; set; }
    public int? HotspotCount { get; set; }
    public double ContinentalSeedRatio { get; set; } = 0.4;
    public double BoundaryNoise { get; set; } = 0.2;
    public double BoundaryNoiseScale { get; set; } = 8.0;
    public double LandWaterTransitionPenalty { get; set; } = 0.2;
    public double Activity { get; set; } = 1.0;
    public double EarthLikeFactor { get; set; } = 0.8;
    public double HistoryDepth { get; set; } = 0.8;
    public double MicroplateRatio { get; set; } = 0.18;
    public double MinMicroplateAreaRatio { get; set; } = 0.0005;
    public double MaxMicroplateAreaRatio { get; set; } = 0.008;
    public int MinBoundarySegmentLength { get; set; } = 16;
    public double ActiveMarginRatio { get; set; } = 0.45;
    public double ShelfWidthFactor { get; set; } = 1.0;
    public double RiftChance { get; set; } = 0.35;
    public bool ValidateGeometry { get; set; } = true;
    public int MaxValidationCycles { get; set; } = 3;
    public int MinPlateSize { get; set; } = 100;
    public double MinPlateSizeRatio { get; set; } = 0.001;
    public double ReliefScale { get; set; } = 1.0;
    public double Mountaininess { get; set; } = 0.8;
    public double Erosion { get; set; } = 0.35;
    public double Roughness { get; set; } = 0.45;
    public double SeaDepthScale { get; set; } = 1.0;
    public double ElevationShelfWidthFactor { get; set; } = 1.0;
    public double VolcanismInfluence { get; set; } = 0.6;
    public double SmallIslandReliefFactor { get; set; } = 0.55;
    public double RiftInfluence { get; set; } = 0.5;
    public bool GenerateSmallLakes { get; set; } = false;
    public double SmallLakeCountMultiplier { get; set; } = 0.3;
    public double SmallLakeScatterMultiplier { get; set; } = 1.0;
    public double SmallLakeSizeMultiplier { get; set; } = 0.5;
    public bool PreserveMaskCoastline { get; set; } = true;
    public bool PreserveOceanCoastline { get; set; } = true;
    public bool PreserveInlandWaterMask { get; set; } = true;
    public bool AllowLakeExpansion { get; set; } = false;
    public bool AllowLakeDrainage { get; set; } = false;
    public double MaxElevationMeters { get; set; } = 8500;
    public double MinOceanDepthMeters { get; set; } = -7000;
    public double MinLandElevationMeters { get; set; } = 1;
    public double MaxSeaElevationMeters { get; set; } = -1;
    public double LakeSurfacePercentile { get; set; } = 0.05;
    public double MinLakeSurfaceMarginMeters { get; set; } = 0.5;
    public double MaxLakeSurfaceMarginMeters { get; set; } = 8.0;
    public double MinLakeDepthMeters { get; set; } = 1.0;
    public double MaxLakeDepthMeters { get; set; } = 80.0;
    public double MaxRiftLakeDepthMeters { get; set; } = 320.0;
    public double MaxInlandSeaDepthMeters { get; set; } = 220.0;
    public double MountainLakeElevationMeters { get; set; } = 900.0;
    public double PlateauLakeElevationMeters { get; set; } = 1400.0;
    public double MountainLakeReliefMeters { get; set; } = 260.0;
    public double LakeTectonicFaultThreshold { get; set; } = 0.28;
    public double LakeVolcanicInfluenceThreshold { get; set; } = 0.34;
    public double PlainLakeKarstChance { get; set; } = 0.12;
    public double LakeDepthRandomnessMin { get; set; } = 0.8;
    public double LakeDepthRandomnessMax { get; set; } = 1.2;
    public int LargeLakeDepressionMinCellCount { get; set; } = 900;
    public double RiverDensity { get; set; } = 1;
    public double MountainRiverDensity { get; set; } = 0.58;
    public int MaxMountainSourcesPerCluster { get; set; }
    public int MinMountainSourceSpacing { get; set; }
    public double MajorRiverCountMultiplier { get; set; } = 1.5;
    public double LongRiverCountMultiplier { get; set; } = 1.3;
    public double TributaryDensity { get; set; } = 1.0;
    public double MajorRiverTributaryMultiplier { get; set; } = 1.0;
    public double LakeOutletInflowForceMultiplier { get; set; } = 0.45;
    public double EndorheicBasinChance { get; set; } = 0.22;
    public double DeltaFrequency { get; set; } = 0.8;
    public double MeanderStrength { get; set; } = 0.65;
    public double LakeOutletStrictness { get; set; } = 0.35;
    public bool PreserveRiverCoastline { get; set; } = true;
    public bool AllowRiverCarving { get; set; } = false;
    public double ClimatePolarLatitudeMargin { get; set; } = 0.05;
    public double ClimateEquatorTemperatureCelsius { get; set; } = 28.0;
    public double ClimatePoleCoolingCelsius { get; set; } = 55.0;
    public double ClimateLatitudeCurveExponent { get; set; } = 1.35;
    public double ClimateLapseRateCelsiusPerMeter { get; set; } = 0.0045;
    public double ClimateBaseSeasonalityCelsius { get; set; } = 6.0;
    public double ClimateLatitudeSeasonalityCelsius { get; set; } = 18.0;
    public double ClimateContinentalSeasonalityCelsius { get; set; } = 13.0;
    public double ClimateContinentalSummerBoostCelsius { get; set; } = 5.0;
    public double ClimateContinentalWinterPenaltyCelsius { get; set; } = 8.0;
    public int ClimateContinentalityDistanceCells { get; set; } = 96;
    public int ClimateLargeLakeMinCellCount { get; set; } = 220;
    public double ClimateOceanEvaporation { get; set; } = 1.30;
    public double ClimateLakeEvaporation { get; set; } = 0.68;
    public double ClimateLandEvapotranspiration { get; set; } = 0.08;
    public double ClimateMoistureRetention { get; set; } = 0.86;
    public double ClimateBaseRainfallEfficiency { get; set; } = 0.22;
    public double ClimateOrographicStrength { get; set; } = 0.84;
    public double ClimateDescentDrying { get; set; } = 0.27;
    public double ClimateContinentalDrying { get; set; } = 0.17;
    public double ClimateRiverMoistureBonus { get; set; } = 0.26;
    public double ClimateRiverAgricultureBonus { get; set; } = 0.54;
    public double ClimateMonsoonRainStrength { get; set; } = 0.38;
    public double ClimateDrySeasonStrength { get; set; } = 0.22;
    public int ClimateMonsoonOceanDistanceCells { get; set; } = 30;
    public int ClimateMonsoonCoastProbeCells { get; set; } = 10;
    public double ClimateSnowMeltThresholdCelsius { get; set; } = 2.0;
    public double ClimateSnowPrecipitationScale { get; set; } = 0.42;
    public TectonicPlateJsonExportMode TectonicJsonMode { get; set; } = TectonicPlateJsonExportMode.Summary;
    public ElevationJsonExportMode ElevationJsonMode { get; set; } = ElevationJsonExportMode.Summary;
    public ClimateJsonExportMode ClimateJsonMode { get; set; } = ClimateJsonExportMode.Summary;
    public bool Debug { get; set; }

    public MapGenerationOptions ToGenerationOptions()
    {
        return new MapGenerationOptions
        {
            PixelSize = PixelSize,
            Seed = Seed,
            Debug = Debug,
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
            WaterBodies = new WaterBodyClassificationOptions
            {
                OceanSeaMinAreaRatio = OceanSeaMinAreaRatio,
                InlandSeaMinAreaRatio = InlandSeaMinAreaRatio,
                OceanSeaNearOceanMaxDistanceCells = OceanSeaNearOceanMaxDistanceCells
            },
            Boundaries = new BoundaryDistortionOptions
            {
                Enabled = BoundariesEnabled,
                Detail = BoundaryDetail,
                MaxOffset = MaxOffset,
                MinLineLengthToCurve = MinLineLengthToCurve
            },
            TectonicPlates = new TectonicPlateGenerationOptions
            {
                PlateCount = PlateCount,
                HotspotCount = HotspotCount,
                ContinentalSeedRatio = ContinentalSeedRatio,
                BoundaryNoise = BoundaryNoise,
                BoundaryNoiseScale = BoundaryNoiseScale,
                LandWaterTransitionPenalty = LandWaterTransitionPenalty,
                Activity = Activity,
                EarthLikeFactor = EarthLikeFactor,
                HistoryDepth = HistoryDepth,
                MicroplateRatio = MicroplateRatio,
                MinMicroplateAreaRatio = MinMicroplateAreaRatio,
                MaxMicroplateAreaRatio = MaxMicroplateAreaRatio,
                MinBoundarySegmentLength = MinBoundarySegmentLength,
                ActiveMarginRatio = ActiveMarginRatio,
                ShelfWidthFactor = ShelfWidthFactor,
                RiftChance = RiftChance,
                ValidateGeometry = ValidateGeometry,
                MaxValidationCycles = MaxValidationCycles,
                MinPlateSize = MinPlateSize,
                MinPlateSizeRatio = MinPlateSizeRatio
            },
            Elevation = new ElevationGenerationOptions
            {
                ReliefScale = ReliefScale,
                Mountaininess = Mountaininess,
                Erosion = Erosion,
                Roughness = Roughness,
                SeaDepthScale = SeaDepthScale,
                ShelfWidthFactor = ElevationShelfWidthFactor,
                VolcanismInfluence = VolcanismInfluence,
                SmallIslandReliefFactor = SmallIslandReliefFactor,
                RiftInfluence = RiftInfluence,
                GenerateSmallLakes = GenerateSmallLakes,
                SmallLakeCountMultiplier = SmallLakeCountMultiplier,
                SmallLakeScatterMultiplier = SmallLakeScatterMultiplier,
                SmallLakeSizeMultiplier = SmallLakeSizeMultiplier,
                PreserveMaskCoastline = PreserveMaskCoastline,
                PreserveOceanCoastline = PreserveOceanCoastline,
                PreserveInlandWaterMask = PreserveInlandWaterMask,
                AllowLakeExpansion = AllowLakeExpansion,
                AllowLakeDrainage = AllowLakeDrainage,
                MaxElevationMeters = MaxElevationMeters,
                MinOceanDepthMeters = MinOceanDepthMeters,
                MinLandElevationMeters = MinLandElevationMeters,
                MaxSeaElevationMeters = MaxSeaElevationMeters,
                LakeSurfacePercentile = LakeSurfacePercentile,
                MinLakeSurfaceMarginMeters = MinLakeSurfaceMarginMeters,
                MaxLakeSurfaceMarginMeters = MaxLakeSurfaceMarginMeters,
                MinLakeDepthMeters = MinLakeDepthMeters,
                MaxLakeDepthMeters = MaxLakeDepthMeters,
                MaxRiftLakeDepthMeters = MaxRiftLakeDepthMeters,
                MaxInlandSeaDepthMeters = MaxInlandSeaDepthMeters,
                MountainLakeElevationMeters = MountainLakeElevationMeters,
                PlateauLakeElevationMeters = PlateauLakeElevationMeters,
                MountainLakeReliefMeters = MountainLakeReliefMeters,
                LakeTectonicFaultThreshold = LakeTectonicFaultThreshold,
                LakeVolcanicInfluenceThreshold = LakeVolcanicInfluenceThreshold,
                PlainLakeKarstChance = PlainLakeKarstChance,
                LakeDepthRandomnessMin = LakeDepthRandomnessMin,
                LakeDepthRandomnessMax = LakeDepthRandomnessMax,
                LargeLakeDepressionMinCellCount = LargeLakeDepressionMinCellCount
            },
            Hydrology = new HydrologyGenerationOptions
            {
                RiverDensity = RiverDensity,
                MountainRiverDensity = MountainRiverDensity,
                MaxMountainSourcesPerCluster = MaxMountainSourcesPerCluster,
                MinMountainSourceSpacing = MinMountainSourceSpacing,
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
            },
            Climate = new ClimateGenerationOptions
            {
                PolarLatitudeMargin = ClimatePolarLatitudeMargin,
                EquatorTemperatureCelsius = ClimateEquatorTemperatureCelsius,
                PoleCoolingCelsius = ClimatePoleCoolingCelsius,
                LatitudeCurveExponent = ClimateLatitudeCurveExponent,
                LapseRateCelsiusPerMeter = ClimateLapseRateCelsiusPerMeter,
                BaseSeasonalityCelsius = ClimateBaseSeasonalityCelsius,
                LatitudeSeasonalityCelsius = ClimateLatitudeSeasonalityCelsius,
                ContinentalSeasonalityCelsius = ClimateContinentalSeasonalityCelsius,
                ContinentalSummerBoostCelsius = ClimateContinentalSummerBoostCelsius,
                ContinentalWinterPenaltyCelsius = ClimateContinentalWinterPenaltyCelsius,
                ContinentalityDistanceCells = ClimateContinentalityDistanceCells,
                LargeLakeMinCellCount = ClimateLargeLakeMinCellCount,
                OceanEvaporation = ClimateOceanEvaporation,
                LakeEvaporation = ClimateLakeEvaporation,
                LandEvapotranspiration = ClimateLandEvapotranspiration,
                MoistureRetention = ClimateMoistureRetention,
                BaseRainfallEfficiency = ClimateBaseRainfallEfficiency,
                OrographicStrength = ClimateOrographicStrength,
                DescentDrying = ClimateDescentDrying,
                ContinentalDrying = ClimateContinentalDrying,
                RiverMoistureBonus = ClimateRiverMoistureBonus,
                RiverAgricultureBonus = ClimateRiverAgricultureBonus,
                MonsoonRainStrength = ClimateMonsoonRainStrength,
                DrySeasonStrength = ClimateDrySeasonStrength,
                MonsoonOceanDistanceCells = ClimateMonsoonOceanDistanceCells,
                MonsoonCoastProbeCells = ClimateMonsoonCoastProbeCells,
                SnowMeltThresholdCelsius = ClimateSnowMeltThresholdCelsius,
                SnowPrecipitationScale = ClimateSnowPrecipitationScale
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
            Debug = Debug,
            TectonicJsonMode = TectonicJsonMode,
            ElevationJsonMode = ElevationJsonMode,
            ClimateJsonMode = ClimateJsonMode
        };
    }
}
