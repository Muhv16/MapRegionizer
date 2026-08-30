#pragma warning disable CS0618

namespace MapRegionizer.Core.Options;

public sealed class MapGenerationOptions
{
    private MapSpatialOptions _spatial = MapSpatialOptions.LegacyDefault();
    private bool _spatialWasConfigured;

    /// <summary>Generation spatial semantics. Output coordinates are configured separately.</summary>
    public MapSpatialOptions Spatial
    {
        get => _spatial;
        init
        {
            _spatial = value ?? throw new ArgumentNullException(nameof(value));
            _spatialWasConfigured = true;
        }
    }

    /// <summary>
    /// Legacy alias for <see cref="MapSpatialOptions.UnitsPerCell"/>. It remains
    /// source-compatible while the spatial model migrates away from pixel terminology.
    /// </summary>
    [Obsolete("Use Spatial.UnitsPerCell. This alias is retained for compatibility.")]
    public double PixelSize
    {
        get => Spatial.UnitsPerCell;
        init => _spatial = _spatial with { UnitsPerCell = value };
    }
    public int? Seed { get; init; }
    public bool Debug { get; init; }
    public ShapeExtractionOptions ShapeExtraction { get; init; } = new();
    public WaterBodyClassificationOptions WaterBodies { get; init; } = new();
    public RegionGenerationOptions Regions { get; init; } = new();
    public BoundaryDistortionOptions Boundaries { get; init; } = new();
    [Obsolete("Use Spatial.Projection is represented by WorldModel, Coverage, GridMapping, and Topology.")]
    public MapProjectionMode ProjectionMode { get; init; } = MapProjectionMode.EquirectangularWorld;
    /// <summary>
    /// Stable seed for world-context generation.  Legacy callers may continue
    /// using <see cref="Seed"/>; when both are supplied WorldSeed governs
    /// automatic world identity while Seed keeps local compatibility paths
    /// reproducible.
    /// </summary>
    public int? WorldSeed { get; init; }
    public TectonicPlateGenerationOptions TectonicPlates { get; init; } = new();
    public ElevationGenerationOptions Elevation { get; init; } = new();
    public HydrologyGenerationOptions Hydrology { get; init; } = new();
    public ClimateGenerationOptions Climate { get; init; } = new();

    /// <summary>
    /// Returns a copy with the supplied generation-space descriptor. Output
    /// coordinate choices are intentionally not part of this object.
    /// </summary>
    public MapGenerationOptions WithSpatial(MapSpatialOptions spatial)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        return new MapGenerationOptions
        {
            Spatial = spatial,
            Seed = Seed,
            WorldSeed = WorldSeed,
            Debug = Debug,
            ShapeExtraction = ShapeExtraction,
            WaterBodies = WaterBodies,
            Regions = Regions,
            Boundaries = Boundaries,
            ProjectionMode = ProjectionMode,
            TectonicPlates = TectonicPlates,
            Elevation = Elevation,
            Hydrology = Hydrology,
            Climate = Climate
        };
    }

    /// <summary>
    /// Returns spatial options while honoring an explicitly supplied legacy
    /// projection when no new spatial configuration was provided.
    /// </summary>
    public MapSpatialOptions EffectiveSpatial =>
        !_spatialWasConfigured && ProjectionMode != MapProjectionMode.EquirectangularWorld
            ? MapSpatialOptions.FromLegacy(ProjectionMode, Spatial.UnitsPerCell)
            : Spatial;

    public void Validate()
    {
        EffectiveSpatial.Validate();
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
