namespace MapRegionizer.Core.Options;

public sealed class MapGenerationOptions
{
    public double PixelSize { get; init; } = 1;
    public int? Seed { get; init; }
    public ShapeExtractionOptions ShapeExtraction { get; init; } = new();
    public RegionGenerationOptions Regions { get; init; } = new();
    public BoundaryDistortionOptions Boundaries { get; init; } = new();

    public void Validate()
    {
        if (PixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(PixelSize), "Pixel size must be greater than zero.");
        ShapeExtraction.Validate();
        Regions.Validate();
        Boundaries.Validate();
    }
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
