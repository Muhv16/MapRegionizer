using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

public static class MapImageRenderer
{
    public static void RenderToFile(GeneratedMap map, string filePath, MapRenderOptions? options = null)
        => BasicMapRenderer.RenderToFile(map, filePath, options);

    public static Image<Rgba32> Render(GeneratedMap map, MapRenderOptions? options = null)
        => BasicMapRenderer.Render(map, options);

    public static void RenderTectonicPlatesToFile(GeneratedMap map, string filePath, TectonicPlateRenderOptions? options = null)
        => TectonicPlateRenderer.RenderTectonicPlatesToFile(map, filePath, options);

    public static Image<Rgba32> RenderTectonicPlates(GeneratedMap map, TectonicPlateRenderOptions? options = null)
        => TectonicPlateRenderer.RenderTectonicPlates(map, options);

    public static void RenderCrustToFile(GeneratedMap map, string filePath, CrustRenderOptions? options = null)
        => CrustRenderer.RenderCrustToFile(map, filePath, options);

    public static Image<Rgba32> RenderCrust(GeneratedMap map, CrustRenderOptions? options = null)
        => CrustRenderer.RenderCrust(map, options);

    public static void RenderTectonicFeaturesToFile(GeneratedMap map, string filePath, TectonicFeatureRenderOptions? options = null)
        => TectonicFeatureRenderer.RenderTectonicFeaturesToFile(map, filePath, options);

    public static Image<Rgba32> RenderTectonicFeatures(GeneratedMap map, TectonicFeatureRenderOptions? options = null)
        => TectonicFeatureRenderer.RenderTectonicFeatures(map, options);

    public static void RenderElevationToFile(GeneratedMap map, string filePath, ElevationRenderOptions? options = null)
        => ElevationMapRenderer.RenderElevationToFile(map, filePath, options);

    public static Image<Rgba32> RenderElevation(GeneratedMap map, ElevationRenderOptions? options = null)
        => ElevationMapRenderer.RenderElevation(map, options);

    public static void RenderElevationRiversToFile(GeneratedMap map, string filePath, RiverRenderOptions? options = null)
        => RiverOverlayRenderer.RenderElevationRiversToFile(map, filePath, options);

    public static Image<Rgba32> RenderElevationRivers(GeneratedMap map, RiverRenderOptions? options = null)
        => RiverOverlayRenderer.RenderElevationRivers(map, options);

    public static void RenderClimateToFile(GeneratedMap map, string filePath, ClimateRenderOptions? options = null)
        => ClimateMapRenderer.RenderClimateToFile(map, filePath, options);

    public static Image<Rgba32> RenderClimate(GeneratedMap map, ClimateRenderOptions? options = null)
        => ClimateMapRenderer.RenderClimate(map, options);

    public static void RenderElevationDebugToFiles(
        GeneratedMap map,
        string outputDirectory,
        string prefix = "elevation",
        ElevationRenderOptions? options = null,
        RiverRenderOptions? riverOptions = null)
        => ElevationMapRenderer.RenderElevationDebugToFiles(map, outputDirectory, prefix, options, riverOptions);
}
