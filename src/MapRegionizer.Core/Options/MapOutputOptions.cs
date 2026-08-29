using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Options;

public enum OutputCoordinateSystem
{
    GridMapUnits,
    GeographicLongitudeLatitude,
    WebMercator
}

/// <summary>Presentation/export coordinates. It never participates in generation.</summary>
public sealed record MapOutputOptions
{
    public OutputCoordinateSystem CoordinateSystem { get; init; } = OutputCoordinateSystem.GridMapUnits;

    public void Validate()
    {
        if (!Enum.IsDefined(CoordinateSystem))
            throw new ArgumentOutOfRangeException(nameof(CoordinateSystem), "Unknown output coordinate system.");
    }
}
