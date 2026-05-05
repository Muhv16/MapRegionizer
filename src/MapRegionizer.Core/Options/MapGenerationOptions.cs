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

    public void Validate()
    {
        if (PixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(PixelSize), "Pixel size must be greater than zero.");
        ShapeExtraction.Validate();
        Regions.Validate();
        Boundaries.Validate();
        TectonicPlates.Validate();
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
    public double BoundaryNoise { get; init; } = 0.3;
    public double BoundaryNoiseScale { get; init; } = 12.0;
    public double LandWaterTransitionPenalty { get; init; } = 0.2;
    public double Activity { get; init; } = 1.0;
    public double EarthLikeFactor { get; init; } = 0.8;
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
        if (MaxValidationCycles < 0) throw new ArgumentOutOfRangeException(nameof(MaxValidationCycles), "Max validation cycles cannot be negative.");
        if (MinPlateSize < 0) throw new ArgumentOutOfRangeException(nameof(MinPlateSize), "Minimum plate size cannot be negative.");
        if (MinPlateSizeRatio < 0 || MinPlateSizeRatio > 1) throw new ArgumentOutOfRangeException(nameof(MinPlateSizeRatio), "Min plate size ratio must be in [0, 1].");
    }
}
