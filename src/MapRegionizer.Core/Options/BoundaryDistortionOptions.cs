namespace MapRegionizer.Core.Options;

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
