using MapRegionizer.Core.Options;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Terrain;
using MapRegionizer.GeoJson;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.Runner;

public sealed class MapGenerationRequestOptions
{
    public string MaskPath { get; set; } = string.Empty;
    /// <summary>Optional wider world mask used by Automatic/Custom requests.</summary>
    public string? WorldMaskPath { get; set; }
    public int RequestedOriginX { get; set; }
    public int RequestedOriginY { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public MapGenerationOptions GenerationOptions { get; set; } = new();
    private WorldContextMode _worldContextMode = WorldContextMode.Isolated;
    private bool _legacyCompatibilityEnabled = true;
    private bool _legacyCompatibilityExplicit;

    /// <summary>Canonical policy for obtaining world and boundary context.</summary>
    public WorldContextMode WorldContextMode
    {
        get => _worldContextMode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _worldContextMode = value;
            _legacyCompatibilityEnabled = false;
            _legacyCompatibilityExplicit = true;
        }
    }

    /// <summary>
    /// Whether the old MapMask compatibility adapter should be used. This is
    /// separate from world context so legacy spatial behavior cannot be
    /// mistaken for a context strategy.
    /// </summary>
    public bool LegacyCompatibilityEnabled
    {
        get => _legacyCompatibilityEnabled;
        set
        {
            _legacyCompatibilityEnabled = value;
            _legacyCompatibilityExplicit = true;
        }
    }

    [Obsolete("Use WorldContextMode and LegacyCompatibilityEnabled.")]
    public RegionalGenerationMode GenerationMode
    {
        get => _legacyCompatibilityEnabled
            ? RegionalGenerationMode.Legacy
            : _worldContextMode.ToRegionalGenerationMode();
        set
        {
            _worldContextMode = value.ToWorldContextMode();
            _legacyCompatibilityEnabled = value == RegionalGenerationMode.Legacy;
            _legacyCompatibilityExplicit = true;
        }
    }

    public bool UsesLegacyCompatibility => _legacyCompatibilityEnabled &&
        (_legacyCompatibilityExplicit || !SpatialConfigurationEnabled);
    public int WorkingHaloCells { get; set; }
    public bool SpatialConfigurationEnabled { get; set; }
    public MapOutputOptions OutputOptions { get; set; } = new();
    public bool Debug { get; set; }
    public bool RasterizeRegions { get; set; }
    public string? RegionDraftPath { get; set; }
    public string? RegionDraftOutputPath { get; set; }
    public bool? RegionDraftDistortionEnabled { get; set; }
    public TectonicPlateJsonExportMode TectonicJsonMode { get; set; } = TectonicPlateJsonExportMode.Summary;
    public ElevationJsonExportMode ElevationJsonMode { get; set; } = ElevationJsonExportMode.Summary;
    public ClimateJsonExportMode ClimateJsonMode { get; set; } = ClimateJsonExportMode.Summary;

    /// <summary>
    /// Builds the Core request from a materialized mask.  The Runner owns the
    /// file adapter; all spatial/topology semantics are still validated by
    /// Core request construction.
    /// </summary>
    public MapGenerationRequest ToGenerationRequest(
        MapMask mask,
        MapGenerationOptions? generationOptions = null,
        IMapMaskSource? worldMaskSource = null,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null)
    {
        ArgumentNullException.ThrowIfNull(mask);

        generationOptions ??= GenerationOptions;

        if (UsesLegacyCompatibility)
            return MapGenerationRequest.Legacy(mask, generationOptions);

        var requested = new RequestedDomain(mask.Window);
        if (WorkingHaloCells < 0)
            throw new ArgumentOutOfRangeException(nameof(mask), "Working halo cannot be negative.");
        var working = WorkingDomain.ForRequested(requested, WorkingHaloCells);
        var source = worldMaskSource ?? (!string.IsNullOrWhiteSpace(WorldMaskPath)
            ? new ImageMapMaskSource(WorldMaskPath)
            : null);

        return WorldContextMode switch
        {
            WorldContextMode.Isolated => MapGenerationRequest.Isolated(requested, mask, generationOptions),
            WorldContextMode.Automatic when source is not null => MapGenerationRequest.Automatic(
                requested,
                working,
                source,
                generationOptions,
                climateBoundary,
                hydrologyBoundary),
            WorldContextMode.Automatic => throw new InvalidOperationException("Automatic world context requires --world-mask or an explicit world mask source that covers the working domain."),
            WorldContextMode.Custom when source is not null && climateBoundary is not null && hydrologyBoundary is not null =>
                MapGenerationRequest.Custom(requested, working, source, climateBoundary, hydrologyBoundary, generationOptions),
            WorldContextMode.Custom => throw new InvalidOperationException("Custom world context requires a world mask source and both boundary contexts."),
            _ => throw new ArgumentOutOfRangeException(nameof(mask), "Unknown generation mode.")
        };
    }
}
