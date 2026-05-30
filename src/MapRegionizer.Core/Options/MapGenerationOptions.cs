namespace MapRegionizer.Core.Options;

public sealed class MapGenerationOptions
{
    public double PixelSize { get; init; } = 1;
    public int? Seed { get; init; }
    public bool Debug { get; init; }
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
