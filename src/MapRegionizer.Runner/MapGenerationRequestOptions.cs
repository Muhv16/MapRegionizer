using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;

namespace MapRegionizer.Runner;

public sealed class MapGenerationRequestOptions
{
    public string MaskPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public MapGenerationOptions GenerationOptions { get; set; } = new();
    public bool Debug { get; set; }
    public bool RasterizeRegions { get; set; }
    public string? RegionDraftPath { get; set; }
    public string? RegionDraftOutputPath { get; set; }
    public bool? RegionDraftDistortionEnabled { get; set; }
    public TectonicPlateJsonExportMode TectonicJsonMode { get; set; } = TectonicPlateJsonExportMode.Summary;
    public ElevationJsonExportMode ElevationJsonMode { get; set; } = ElevationJsonExportMode.Summary;
    public ClimateJsonExportMode ClimateJsonMode { get; set; } = ClimateJsonExportMode.Summary;
}
