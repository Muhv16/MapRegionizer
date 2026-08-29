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
    public RegionalGenerationMode GenerationMode { get; set; } = RegionalGenerationMode.Legacy;
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

        if (GenerationMode == RegionalGenerationMode.Legacy)
            return MapGenerationRequest.Legacy(mask, generationOptions);

        var requested = new RequestedDomain(mask.Window);
        if (WorkingHaloCells < 0)
            throw new ArgumentOutOfRangeException(nameof(mask), "Working halo cannot be negative.");
        var working = WorkingDomain.ForRequested(requested, WorkingHaloCells);
        var source = worldMaskSource ?? (!string.IsNullOrWhiteSpace(WorldMaskPath)
            ? new ImageMapMaskSource(WorldMaskPath)
            : null);

        return GenerationMode switch
        {
            RegionalGenerationMode.Isolated => MapGenerationRequest.Isolated(requested, mask, generationOptions),
            RegionalGenerationMode.Automatic when source is not null => MapGenerationRequest.Automatic(
                requested,
                working,
                source,
                generationOptions,
                climateBoundary,
                hydrologyBoundary),
            RegionalGenerationMode.Automatic => throw new InvalidOperationException("Automatic regional generation requires --world-mask or an explicit world mask source that covers the working domain."),
            RegionalGenerationMode.Custom when source is not null && climateBoundary is not null && hydrologyBoundary is not null =>
                MapGenerationRequest.Custom(requested, working, source, climateBoundary, hydrologyBoundary, generationOptions),
            RegionalGenerationMode.Custom => throw new InvalidOperationException("Custom regional generation requires a world mask source and both boundary contexts."),
            _ => throw new ArgumentOutOfRangeException(nameof(mask), "Unknown generation mode.")
        };
    }
}
