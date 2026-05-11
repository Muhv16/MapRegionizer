namespace MapRegionizer.Core.Options;

public sealed class MapGenerationOptions
{
    public double PixelSize { get; init; } = 1;
    public int? Seed { get; init; }
    public ShapeExtractionOptions ShapeExtraction { get; init; } = new();
    public WaterBodyClassificationOptions WaterBodies { get; init; } = new();
    public RegionGenerationOptions Regions { get; init; } = new();
    public BoundaryDistortionOptions Boundaries { get; init; } = new();
    public MapProjectionMode ProjectionMode { get; init; } = MapProjectionMode.EquirectangularWorld;
    public TectonicPlateGenerationOptions TectonicPlates { get; init; } = new();
    public ElevationGenerationOptions Elevation { get; init; } = new();
    public HydrologyGenerationOptions Hydrology { get; init; } = new();
    public ClimateGenerationOptions Climate { get; init; } = new();

    public void Validate()
    {
        if (PixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(PixelSize), "Pixel size must be greater than zero.");
        ShapeExtraction.Validate();
        WaterBodies.Validate();
        Regions.Validate();
        Boundaries.Validate();
        TectonicPlates.Validate();
        Elevation.Validate();
        Hydrology.Validate();
        Climate.Validate();
    }
}

public sealed class WaterBodyClassificationOptions
{
    public double OceanSeaMinAreaRatio { get; init; } = 0.12;
    public double InlandSeaMinAreaRatio { get; init; } = 0.015;
    public int OceanSeaNearOceanMaxDistanceCells { get; init; } = 13;

    public void Validate()
    {
        if (OceanSeaMinAreaRatio < 0 || OceanSeaMinAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(OceanSeaMinAreaRatio), "Ocean-sea minimum area ratio must be in [0, 1].");
        if (InlandSeaMinAreaRatio < 0 || InlandSeaMinAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(InlandSeaMinAreaRatio), "Inland-sea minimum area ratio must be in [0, 1].");
        if (OceanSeaMinAreaRatio < InlandSeaMinAreaRatio) throw new ArgumentOutOfRangeException(nameof(OceanSeaMinAreaRatio), "Ocean-sea minimum area ratio should be greater than or equal to inland-sea minimum area ratio.");
        if (OceanSeaNearOceanMaxDistanceCells < 0) throw new ArgumentOutOfRangeException(nameof(OceanSeaNearOceanMaxDistanceCells), "Ocean-sea near-ocean distance cannot be negative.");
    }
}

public enum MapProjectionMode
{
    EquirectangularWorld,
    Flat,
    Regional
}

public sealed class ShapeExtractionOptions
{
    public double SimplifyTolerance { get; init; } = 1;

    public void Validate()
    {
        if (SimplifyTolerance < 0) throw new ArgumentOutOfRangeException(nameof(SimplifyTolerance), "Simplify tolerance cannot be negative.");
    }
}

public sealed class RegionGenerationOptions
{
    public uint TargetArea { get; init; } = 400;
    public double PointsMultiplier { get; init; } = 4;
    public double MinAreaRatio { get; init; } = 0.75;
    public double MaxAreaRatio { get; init; } = 1.75;

    public void Validate()
    {
        if (TargetArea == 0) throw new ArgumentOutOfRangeException(nameof(TargetArea), "Target area must be greater than zero.");
        if (PointsMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(PointsMultiplier), "Points multiplier must be greater than zero.");
        if (MinAreaRatio <= 0 || MinAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(MinAreaRatio), "Minimum area ratio must be in (0, 1].");
        if (MaxAreaRatio < 1) throw new ArgumentOutOfRangeException(nameof(MaxAreaRatio), "Maximum area ratio must be at least 1.");
    }
}

public sealed class BoundaryDistortionOptions
{
    public bool Enabled { get; init; } = true;
    public double Detail { get; init; } = 0.25;
    public double MaxOffset { get; init; } = 3.25;
    public double MinLineLengthToCurve { get; init; } = 7;

    public void Validate()
    {
        if (Detail <= 0 || Detail > 1) throw new ArgumentOutOfRangeException(nameof(Detail), "Detail must be in (0, 1].");
        if (MaxOffset < 0) throw new ArgumentOutOfRangeException(nameof(MaxOffset), "Max offset cannot be negative.");
        if (MinLineLengthToCurve < 0) throw new ArgumentOutOfRangeException(nameof(MinLineLengthToCurve), "Minimum line length cannot be negative.");
    }
}

public sealed class TectonicPlateGenerationOptions
{
    public int? PlateCount { get; init; }
    public double ContinentalSeedRatio { get; init; } = 0.4;
    public double BoundaryNoise { get; init; } = 0.2;
    public double BoundaryNoiseScale { get; init; } = 8.0;
    public double LandWaterTransitionPenalty { get; init; } = 0.2;
    public double Activity { get; init; } = 1.0;
    public double EarthLikeFactor { get; init; } = 0.8;
    public double HistoryDepth { get; init; } = 0.8;
    public double MicroplateRatio { get; init; } = 0.18;
    public double MinMicroplateAreaRatio { get; init; } = 0.0005;
    public double MaxMicroplateAreaRatio { get; init; } = 0.008;
    public int MinBoundarySegmentLength { get; init; } = 16;
    public double ActiveMarginRatio { get; init; } = 0.45;
    public double ShelfWidthFactor { get; init; } = 1.0;
    public int? HotspotCount { get; init; }
    public double RiftChance { get; init; } = 0.35;
    public bool ValidateGeometry { get; init; } = true;
    public int MaxValidationCycles { get; init; } = 3;
    public int MinPlateSize { get; init; } = 100;
    public double MinPlateSizeRatio { get; init; } = 0.001;  // 0.1% of map area

    public void Validate()
    {
        if (PlateCount <= 0) throw new ArgumentOutOfRangeException(nameof(PlateCount), "Plate count must be greater than zero.");
        if (ContinentalSeedRatio < 0 || ContinentalSeedRatio > 1) throw new ArgumentOutOfRangeException(nameof(ContinentalSeedRatio), "Continental seed ratio must be in [0, 1].");
        if (BoundaryNoise < 0 || BoundaryNoise > 1) throw new ArgumentOutOfRangeException(nameof(BoundaryNoise), "Boundary noise must be in [0, 1].");
        if (BoundaryNoiseScale < 0) throw new ArgumentOutOfRangeException(nameof(BoundaryNoiseScale), "Boundary noise scale cannot be negative.");
        if (LandWaterTransitionPenalty < 0) throw new ArgumentOutOfRangeException(nameof(LandWaterTransitionPenalty), "Land-water transition penalty cannot be negative.");
        if (Activity < 0) throw new ArgumentOutOfRangeException(nameof(Activity), "Activity cannot be negative.");
        if (EarthLikeFactor < 0 || EarthLikeFactor > 1) throw new ArgumentOutOfRangeException(nameof(EarthLikeFactor), "Earth-like factor must be in [0, 1].");
        if (HistoryDepth < 0 || HistoryDepth > 1) throw new ArgumentOutOfRangeException(nameof(HistoryDepth), "History depth must be in [0, 1].");
        if (MicroplateRatio < 0 || MicroplateRatio > 1) throw new ArgumentOutOfRangeException(nameof(MicroplateRatio), "Microplate ratio must be in [0, 1].");
        if (MinMicroplateAreaRatio < 0 || MinMicroplateAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(MinMicroplateAreaRatio), "Minimum microplate area ratio must be in [0, 1].");
        if (MaxMicroplateAreaRatio < 0 || MaxMicroplateAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(MaxMicroplateAreaRatio), "Maximum microplate area ratio must be in [0, 1].");
        if (MaxMicroplateAreaRatio > 0 && MaxMicroplateAreaRatio < MinMicroplateAreaRatio) throw new ArgumentOutOfRangeException(nameof(MaxMicroplateAreaRatio), "Maximum microplate area ratio must be greater than or equal to minimum microplate area ratio.");
        if (MinBoundarySegmentLength < 1) throw new ArgumentOutOfRangeException(nameof(MinBoundarySegmentLength), "Minimum boundary segment length must be at least 1.");
        if (ActiveMarginRatio < 0 || ActiveMarginRatio > 1) throw new ArgumentOutOfRangeException(nameof(ActiveMarginRatio), "Active margin ratio must be in [0, 1].");
        if (ShelfWidthFactor < 0) throw new ArgumentOutOfRangeException(nameof(ShelfWidthFactor), "Shelf width factor cannot be negative.");
        if (HotspotCount < 0) throw new ArgumentOutOfRangeException(nameof(HotspotCount), "Hotspot count cannot be negative.");
        if (RiftChance < 0 || RiftChance > 1) throw new ArgumentOutOfRangeException(nameof(RiftChance), "Rift chance must be in [0, 1].");
        if (MaxValidationCycles < 0) throw new ArgumentOutOfRangeException(nameof(MaxValidationCycles), "Max validation cycles cannot be negative.");
        if (MinPlateSize < 0) throw new ArgumentOutOfRangeException(nameof(MinPlateSize), "Minimum plate size cannot be negative.");
        if (MinPlateSizeRatio < 0 || MinPlateSizeRatio > 1) throw new ArgumentOutOfRangeException(nameof(MinPlateSizeRatio), "Min plate size ratio must be in [0, 1].");
    }
}

public sealed class ElevationGenerationOptions
{
    public double ReliefScale { get; init; } = 1.0;
    public double Mountaininess { get; init; } = 0.8;
    public double Erosion { get; init; } = 0.35;
    public double Roughness { get; init; } = 0.45;
    public double SeaDepthScale { get; init; } = 1.0;
    public double ShelfWidthFactor { get; init; } = 1.0;
    public double VolcanismInfluence { get; init; } = 0.6;
    public double SmallIslandReliefFactor { get; init; } = 0.55;
    public double RiftInfluence { get; init; } = 0.5;
    public bool GenerateSmallLakes { get; init; } = true;
    public double SmallLakeCountMultiplier { get; init; } = 0.3;
    public double SmallLakeScatterMultiplier { get; init; } = 1.0;
    public double SmallLakeSizeMultiplier { get; init; } = 0.4;
    public bool PreserveMaskCoastline { get; init; } = true;
    public bool PreserveOceanCoastline { get; init; } = true;
    public bool PreserveInlandWaterMask { get; init; } = true;
    public bool AllowLakeExpansion { get; init; } = false;
    public bool AllowLakeDrainage { get; init; } = false;
    public double MaxElevationMeters { get; init; } = 8500;
    public double MinOceanDepthMeters { get; init; } = -7000;
    public double MinLandElevationMeters { get; init; } = 1;
    public double MaxSeaElevationMeters { get; init; } = -1;
    public double LakeSurfacePercentile { get; init; } = 0.05;
    public double MinLakeSurfaceMarginMeters { get; init; } = 0.5;
    public double MaxLakeSurfaceMarginMeters { get; init; } = 8.0;
    public double MinLakeDepthMeters { get; init; } = 1.0;
    public double MaxLakeDepthMeters { get; init; } = 80.0;
    public double MaxRiftLakeDepthMeters { get; init; } = 320.0;
    public double MaxInlandSeaDepthMeters { get; init; } = 220.0;
    public double MountainLakeElevationMeters { get; init; } = 900.0;
    public double PlateauLakeElevationMeters { get; init; } = 1400.0;
    public double MountainLakeReliefMeters { get; init; } = 260.0;
    public double LakeTectonicFaultThreshold { get; init; } = 0.28;
    public double LakeVolcanicInfluenceThreshold { get; init; } = 0.34;
    public double PlainLakeKarstChance { get; init; } = 0.12;
    public double LakeDepthRandomnessMin { get; init; } = 0.8;
    public double LakeDepthRandomnessMax { get; init; } = 1.2;
    public int LargeLakeDepressionMinCellCount { get; init; } = 900;

    public void Validate()
    {
        if (ReliefScale < 0) throw new ArgumentOutOfRangeException(nameof(ReliefScale), "Relief scale cannot be negative.");
        if (Mountaininess < 0) throw new ArgumentOutOfRangeException(nameof(Mountaininess), "Mountaininess cannot be negative.");
        if (Erosion < 0 || Erosion > 1) throw new ArgumentOutOfRangeException(nameof(Erosion), "Erosion must be in [0, 1].");
        if (Roughness < 0 || Roughness > 1) throw new ArgumentOutOfRangeException(nameof(Roughness), "Roughness must be in [0, 1].");
        if (SeaDepthScale < 0) throw new ArgumentOutOfRangeException(nameof(SeaDepthScale), "Sea depth scale cannot be negative.");
        if (ShelfWidthFactor < 0) throw new ArgumentOutOfRangeException(nameof(ShelfWidthFactor), "Shelf width factor cannot be negative.");
        if (VolcanismInfluence < 0) throw new ArgumentOutOfRangeException(nameof(VolcanismInfluence), "Volcanism influence cannot be negative.");
        if (SmallIslandReliefFactor < 0) throw new ArgumentOutOfRangeException(nameof(SmallIslandReliefFactor), "Small island relief factor cannot be negative.");
        if (RiftInfluence < 0) throw new ArgumentOutOfRangeException(nameof(RiftInfluence), "Rift influence cannot be negative.");
        if (SmallLakeCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeCountMultiplier), "Small lake count multiplier cannot be negative.");
        if (SmallLakeScatterMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeScatterMultiplier), "Small lake scatter multiplier cannot be negative.");
        if (SmallLakeSizeMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeSizeMultiplier), "Small lake size multiplier must be greater than zero.");
        if (LakeSurfacePercentile < 0 || LakeSurfacePercentile > 1) throw new ArgumentOutOfRangeException(nameof(LakeSurfacePercentile), "Lake surface percentile must be in [0, 1].");
        if (MinLakeSurfaceMarginMeters < 0) throw new ArgumentOutOfRangeException(nameof(MinLakeSurfaceMarginMeters), "Minimum lake surface margin cannot be negative.");
        if (MaxLakeSurfaceMarginMeters < MinLakeSurfaceMarginMeters) throw new ArgumentOutOfRangeException(nameof(MaxLakeSurfaceMarginMeters), "Maximum lake surface margin cannot be below minimum margin.");
        if (MinLakeDepthMeters <= 0) throw new ArgumentOutOfRangeException(nameof(MinLakeDepthMeters), "Minimum lake depth must be greater than zero.");
        if (MaxLakeDepthMeters < MinLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxLakeDepthMeters), "Maximum lake depth cannot be below minimum lake depth.");
        if (MaxRiftLakeDepthMeters < MaxLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxRiftLakeDepthMeters), "Maximum rift lake depth cannot be below maximum lake depth.");
        if (MaxInlandSeaDepthMeters < MaxLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxInlandSeaDepthMeters), "Maximum inland sea depth cannot be below maximum lake depth.");
        if (MountainLakeElevationMeters < 0) throw new ArgumentOutOfRangeException(nameof(MountainLakeElevationMeters), "Mountain lake elevation threshold cannot be negative.");
        if (PlateauLakeElevationMeters < MountainLakeElevationMeters) throw new ArgumentOutOfRangeException(nameof(PlateauLakeElevationMeters), "Plateau lake elevation threshold should be greater than or equal to mountain lake elevation threshold.");
        if (MountainLakeReliefMeters < 0) throw new ArgumentOutOfRangeException(nameof(MountainLakeReliefMeters), "Mountain lake relief threshold cannot be negative.");
        if (LakeTectonicFaultThreshold < 0 || LakeTectonicFaultThreshold > 1) throw new ArgumentOutOfRangeException(nameof(LakeTectonicFaultThreshold), "Lake tectonic fault threshold must be in [0, 1].");
        if (LakeVolcanicInfluenceThreshold < 0 || LakeVolcanicInfluenceThreshold > 1) throw new ArgumentOutOfRangeException(nameof(LakeVolcanicInfluenceThreshold), "Lake volcanic influence threshold must be in [0, 1].");
        if (PlainLakeKarstChance < 0 || PlainLakeKarstChance > 1) throw new ArgumentOutOfRangeException(nameof(PlainLakeKarstChance), "Plain lake karst chance must be in [0, 1].");
        if (LakeDepthRandomnessMin <= 0) throw new ArgumentOutOfRangeException(nameof(LakeDepthRandomnessMin), "Minimum lake depth randomness must be greater than zero.");
        if (LakeDepthRandomnessMax < LakeDepthRandomnessMin) throw new ArgumentOutOfRangeException(nameof(LakeDepthRandomnessMax), "Maximum lake depth randomness cannot be below minimum randomness.");
        if (LargeLakeDepressionMinCellCount < 0) throw new ArgumentOutOfRangeException(nameof(LargeLakeDepressionMinCellCount), "Large lake depression threshold cannot be negative.");
        if (MaxElevationMeters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "Maximum elevation must be greater than zero.");
        if (MinOceanDepthMeters >= 0) throw new ArgumentOutOfRangeException(nameof(MinOceanDepthMeters), "Minimum ocean depth must be below zero.");
        if (MinLandElevationMeters < 0) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot be negative.");
        if (MaxSeaElevationMeters > 0) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be above zero.");
        if (MinLandElevationMeters > MaxElevationMeters) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot exceed maximum elevation.");
        if (MaxSeaElevationMeters < MinOceanDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be below minimum ocean depth.");
    }
}

public sealed class HydrologyGenerationOptions
{
    public double RiverDensity { get; init; } = 10;
    public double MajorRiverCountMultiplier { get; init; } = 1.5;
    public double LongRiverCountMultiplier { get; init; } = 1.3;
    public double TributaryDensity { get; init; } = 3.5;
    public double MajorRiverTributaryMultiplier { get; init; } = 1.0;
    public double LakeOutletInflowForceMultiplier { get; init; } = 1.0;
    public double EndorheicBasinChance { get; init; } = 0.22;
    public double DeltaFrequency { get; init; } = 0.8;
    public double MeanderStrength { get; init; } = 0.65;
    public double LakeOutletStrictness { get; init; } = 0.55;
    public bool PreserveCoastline { get; init; } = true;
    public bool AllowRiverCarving { get; init; } = false;

    public void Validate()
    {
        if (RiverDensity < 0) throw new ArgumentOutOfRangeException(nameof(RiverDensity), "River density cannot be negative.");
        if (MajorRiverCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(MajorRiverCountMultiplier), "Major river count multiplier cannot be negative.");
        if (LongRiverCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(LongRiverCountMultiplier), "Long river count multiplier cannot be negative.");
        if (TributaryDensity < 0) throw new ArgumentOutOfRangeException(nameof(TributaryDensity), "Tributary density cannot be negative.");
        if (MajorRiverTributaryMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(MajorRiverTributaryMultiplier), "Major river tributary multiplier cannot be negative.");
        if (LakeOutletInflowForceMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(LakeOutletInflowForceMultiplier), "Lake outlet inflow force multiplier cannot be negative.");
        if (EndorheicBasinChance < 0 || EndorheicBasinChance > 1) throw new ArgumentOutOfRangeException(nameof(EndorheicBasinChance), "Endorheic basin chance must be in [0, 1].");
        if (DeltaFrequency < 0) throw new ArgumentOutOfRangeException(nameof(DeltaFrequency), "Delta frequency cannot be negative.");
        if (MeanderStrength < 0 || MeanderStrength > 1) throw new ArgumentOutOfRangeException(nameof(MeanderStrength), "Meander strength must be in [0, 1].");
        if (LakeOutletStrictness < 0 || LakeOutletStrictness > 1) throw new ArgumentOutOfRangeException(nameof(LakeOutletStrictness), "Lake outlet strictness must be in [0, 1].");
    }
}

public sealed class ClimateGenerationOptions
{
    public double PolarLatitudeMargin { get; init; } = 0.05;
    public double EquatorTemperatureCelsius { get; init; } = 28.0;
    public double PoleCoolingCelsius { get; init; } = 55.0;
    public double LatitudeCurveExponent { get; init; } = 1.35;
    public double LapseRateCelsiusPerMeter { get; init; } = 0.0045;
    public double BaseSeasonalityCelsius { get; init; } = 6.0;
    public double LatitudeSeasonalityCelsius { get; init; } = 18.0;
    public double ContinentalSeasonalityCelsius { get; init; } = 13.0;
    public double ContinentalSummerBoostCelsius { get; init; } = 5.0;
    public double ContinentalWinterPenaltyCelsius { get; init; } = 8.0;
    public int ContinentalityDistanceCells { get; init; } = 96;
    public int LargeLakeMinCellCount { get; init; } = 220;
    public double OceanEvaporation { get; init; } = 1.30;
    public double LakeEvaporation { get; init; } = 0.68;
    public double LandEvapotranspiration { get; init; } = 0.08;
    public double MoistureRetention { get; init; } = 0.86;
    public double BaseRainfallEfficiency { get; init; } = 0.22;
    public double OrographicStrength { get; init; } = 0.84;
    public double DescentDrying { get; init; } = 0.27;
    public double ContinentalDrying { get; init; } = 0.17;
    public double RiverMoistureBonus { get; init; } = 0.26;
    public double RiverAgricultureBonus { get; init; } = 0.54;
    public double MonsoonRainStrength { get; init; } = 0.38;
    public double DrySeasonStrength { get; init; } = 0.22;
    public int MonsoonOceanDistanceCells { get; init; } = 30;
    public int MonsoonCoastProbeCells { get; init; } = 10;
    public double SnowMeltThresholdCelsius { get; init; } = 2.0;
    public double SnowPrecipitationScale { get; init; } = 0.42;

    public void Validate()
    {
        if (PolarLatitudeMargin < 0 || PolarLatitudeMargin >= 1) throw new ArgumentOutOfRangeException(nameof(PolarLatitudeMargin), "Polar latitude margin must be in [0, 1).");
        if (LatitudeCurveExponent <= 0) throw new ArgumentOutOfRangeException(nameof(LatitudeCurveExponent), "Latitude curve exponent must be greater than zero.");
        if (LapseRateCelsiusPerMeter < 0) throw new ArgumentOutOfRangeException(nameof(LapseRateCelsiusPerMeter), "Lapse rate cannot be negative.");
        if (ContinentalityDistanceCells <= 0) throw new ArgumentOutOfRangeException(nameof(ContinentalityDistanceCells), "Continentality distance must be greater than zero.");
        if (LargeLakeMinCellCount < 0) throw new ArgumentOutOfRangeException(nameof(LargeLakeMinCellCount), "Large lake threshold cannot be negative.");
        if (OceanEvaporation < 0) throw new ArgumentOutOfRangeException(nameof(OceanEvaporation), "Ocean evaporation cannot be negative.");
        if (LakeEvaporation < 0) throw new ArgumentOutOfRangeException(nameof(LakeEvaporation), "Lake evaporation cannot be negative.");
        if (LandEvapotranspiration < 0) throw new ArgumentOutOfRangeException(nameof(LandEvapotranspiration), "Land evapotranspiration cannot be negative.");
        if (MoistureRetention < 0 || MoistureRetention > 1) throw new ArgumentOutOfRangeException(nameof(MoistureRetention), "Moisture retention must be in [0, 1].");
        if (BaseRainfallEfficiency < 0) throw new ArgumentOutOfRangeException(nameof(BaseRainfallEfficiency), "Base rainfall efficiency cannot be negative.");
        if (OrographicStrength < 0) throw new ArgumentOutOfRangeException(nameof(OrographicStrength), "Orographic strength cannot be negative.");
        if (DescentDrying < 0) throw new ArgumentOutOfRangeException(nameof(DescentDrying), "Descent drying cannot be negative.");
        if (ContinentalDrying < 0) throw new ArgumentOutOfRangeException(nameof(ContinentalDrying), "Continental drying cannot be negative.");
        if (RiverMoistureBonus < 0) throw new ArgumentOutOfRangeException(nameof(RiverMoistureBonus), "River moisture bonus cannot be negative.");
        if (RiverAgricultureBonus < 0) throw new ArgumentOutOfRangeException(nameof(RiverAgricultureBonus), "River agriculture bonus cannot be negative.");
        if (MonsoonRainStrength < 0) throw new ArgumentOutOfRangeException(nameof(MonsoonRainStrength), "Monsoon rain strength cannot be negative.");
        if (DrySeasonStrength < 0) throw new ArgumentOutOfRangeException(nameof(DrySeasonStrength), "Dry-season strength cannot be negative.");
        if (MonsoonOceanDistanceCells <= 0) throw new ArgumentOutOfRangeException(nameof(MonsoonOceanDistanceCells), "Monsoon ocean distance must be greater than zero.");
        if (MonsoonCoastProbeCells < 0) throw new ArgumentOutOfRangeException(nameof(MonsoonCoastProbeCells), "Monsoon coast probe cannot be negative.");
        if (SnowPrecipitationScale <= 0) throw new ArgumentOutOfRangeException(nameof(SnowPrecipitationScale), "Snow precipitation scale must be greater than zero.");
    }
}
