using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Options;

public enum OutputCoordinateSystem
{
    GridMapUnits,
    GeographicLongitudeLatitude,
    /// <summary>Compatibility name for the EPSG:3857 output.</summary>
    WebMercator,
    WebMercator3857
}

/// <summary>How output projection handles geographic latitudes outside its domain.</summary>
public enum LatitudeOverflowPolicy
{
    Reject,
    Clip
}

/// <summary>How output geometry handles antimeridian-crossing paths.</summary>
public enum AntimeridianOutputPolicy
{
    /// <summary>Keep legacy geographic coordinates unwrapped; split projected output.</summary>
    Auto,
    /// <summary>Keep each path continuous in an unwrapped world.</summary>
    Unwrap,
    /// <summary>Cut geometry at world seams and normalize each output part.</summary>
    Split
}

/// <summary>Presentation/export coordinates. It never participates in generation.</summary>
public sealed record MapOutputOptions
{
    public OutputCoordinateSystem CoordinateSystem { get; init; } = OutputCoordinateSystem.GridMapUnits;
    public LatitudeOverflowPolicy LatitudeOverflowPolicy { get; init; } = LatitudeOverflowPolicy.Reject;
    public AntimeridianOutputPolicy AntimeridianPolicy { get; init; } = AntimeridianOutputPolicy.Auto;

    /// <summary>
    /// Enables adaptive subdivision before a nonlinear projected transform.
    /// This is an output-copy policy and never affects generated data.
    /// </summary>
    public bool EnableAdaptiveDensification { get; init; } = true;

    /// <summary>Maximum projected midpoint deviation in output coordinate units.</summary>
    public double ProjectionErrorTolerance { get; init; } = 1.0;

    /// <summary>Maximum recursive subdivision depth for one source segment.</summary>
    public int MaxDensificationDepth { get; init; } = 12;

    /// <summary>Segments shorter than this projected length are not subdivided.</summary>
    public double MinDensificationSegmentLength { get; init; } = 0.01;

    // Friendly aliases for callers that use the shorter terminology.
    public bool DensifyProjectedGeometry
    {
        get => EnableAdaptiveDensification;
        init => EnableAdaptiveDensification = value;
    }

    public double DensificationTolerance
    {
        get => ProjectionErrorTolerance;
        init => ProjectionErrorTolerance = value;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(CoordinateSystem))
            throw new ArgumentOutOfRangeException(nameof(CoordinateSystem), "Unknown output coordinate system.");
        if (!Enum.IsDefined(LatitudeOverflowPolicy))
            throw new ArgumentOutOfRangeException(nameof(LatitudeOverflowPolicy), "Unknown latitude overflow policy.");
        if (!Enum.IsDefined(AntimeridianPolicy))
            throw new ArgumentOutOfRangeException(nameof(AntimeridianPolicy), "Unknown antimeridian output policy.");
        if (!double.IsFinite(ProjectionErrorTolerance) || ProjectionErrorTolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(ProjectionErrorTolerance), "Projection error tolerance must be finite and greater than zero.");
        if (MaxDensificationDepth < 0 || MaxDensificationDepth > 24)
            throw new ArgumentOutOfRangeException(nameof(MaxDensificationDepth), "Densification depth must be between 0 and 24.");
        if (!double.IsFinite(MinDensificationSegmentLength) || MinDensificationSegmentLength < 0)
            throw new ArgumentOutOfRangeException(nameof(MinDensificationSegmentLength), "Minimum densification segment length must be finite and non-negative.");
    }
}
