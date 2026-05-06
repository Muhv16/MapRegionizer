namespace MapRegionizer.Core.Options;

public sealed class MapGenerationOptions
{
    public double PixelSize { get; init; } = 1;
    public int? Seed { get; init; }
    public ShapeExtractionOptions ShapeExtraction { get; init; } = new();
    public RegionGenerationOptions Regions { get; init; } = new();
    public BoundaryDistortionOptions Boundaries { get; init; } = new();
    public MapProjectionMode ProjectionMode { get; init; } = MapProjectionMode.EquirectangularWorld;
    public TectonicPlateGenerationOptions TectonicPlates { get; init; } = new();
    public ElevationGenerationOptions Elevation { get; init; } = new();

    public void Validate()
    {
        if (PixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(PixelSize), "Pixel size must be greater than zero.");
        ShapeExtraction.Validate();
        Regions.Validate();
        Boundaries.Validate();
        TectonicPlates.Validate();
        Elevation.Validate();
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
    public double RiftInfluence { get; init; } = 0.5;
    public bool PreserveMaskCoastline { get; init; } = true;
    public double MaxElevationMeters { get; init; } = 8500;
    public double MinOceanDepthMeters { get; init; } = -7000;
    public double MinLandElevationMeters { get; init; } = 1;
    public double MaxSeaElevationMeters { get; init; } = -1;

    public void Validate()
    {
        if (ReliefScale < 0) throw new ArgumentOutOfRangeException(nameof(ReliefScale), "Relief scale cannot be negative.");
        if (Mountaininess < 0) throw new ArgumentOutOfRangeException(nameof(Mountaininess), "Mountaininess cannot be negative.");
        if (Erosion < 0 || Erosion > 1) throw new ArgumentOutOfRangeException(nameof(Erosion), "Erosion must be in [0, 1].");
        if (Roughness < 0 || Roughness > 1) throw new ArgumentOutOfRangeException(nameof(Roughness), "Roughness must be in [0, 1].");
        if (SeaDepthScale < 0) throw new ArgumentOutOfRangeException(nameof(SeaDepthScale), "Sea depth scale cannot be negative.");
        if (ShelfWidthFactor < 0) throw new ArgumentOutOfRangeException(nameof(ShelfWidthFactor), "Shelf width factor cannot be negative.");
        if (VolcanismInfluence < 0) throw new ArgumentOutOfRangeException(nameof(VolcanismInfluence), "Volcanism influence cannot be negative.");
        if (RiftInfluence < 0) throw new ArgumentOutOfRangeException(nameof(RiftInfluence), "Rift influence cannot be negative.");
        if (MaxElevationMeters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "Maximum elevation must be greater than zero.");
        if (MinOceanDepthMeters >= 0) throw new ArgumentOutOfRangeException(nameof(MinOceanDepthMeters), "Minimum ocean depth must be below zero.");
        if (MinLandElevationMeters < 0) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot be negative.");
        if (MaxSeaElevationMeters > 0) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be above zero.");
        if (MinLandElevationMeters > MaxElevationMeters) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot exceed maximum elevation.");
        if (MaxSeaElevationMeters < MinOceanDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be below minimum ocean depth.");
    }
}
