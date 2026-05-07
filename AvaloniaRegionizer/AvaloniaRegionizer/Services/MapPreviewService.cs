using System;
using System.IO;
using Avalonia.Media.Imaging;
using AvaloniaRegionizer.ViewModels;
using MapRegionizer.Core.Generation;
using MapRegionizer.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AvaloniaRegionizer.Services;

public sealed class MapPreviewService
{
    public Bitmap? Render(MapGenerationSession? session, PreviewLayerViewModel? layer)
    {
        if (session is null || layer is null || !session.Has(layer.RequiredKey))
            return null;

        using var image = RenderImage(session, layer);
        return ToBitmap(image);
    }

    public string GetLegend(LocalizationService localization, PreviewLayerViewModel? layer, bool available)
    {
        if (layer is null || !available)
            return localization["LegendNotAvailable"];

        return layer.Kind switch
        {
            PreviewLayerKind.TectonicPlates or PreviewLayerKind.Crust or PreviewLayerKind.TectonicFeatures => localization["LegendTectonics"],
            PreviewLayerKind.Elevation or PreviewLayerKind.ElevationBase or PreviewLayerKind.ElevationTectonic or
                PreviewLayerKind.ElevationRoughness or PreviewLayerKind.ElevationErosion or PreviewLayerKind.ElevationZones or
                PreviewLayerKind.ElevationMountain or PreviewLayerKind.ElevationBasin => localization["LegendElevation"],
            _ => localization["LegendOverview"]
        };
    }

    private static Image<Rgba32> RenderImage(MapGenerationSession session, PreviewLayerViewModel layer)
    {
        var map = session.CurrentMap;
        return layer.Kind switch
        {
            PreviewLayerKind.TectonicPlates => MapImageRenderer.RenderTectonicPlates(map),
            PreviewLayerKind.Crust => MapImageRenderer.RenderCrust(map),
            PreviewLayerKind.TectonicFeatures => MapImageRenderer.RenderTectonicFeatures(map),
            PreviewLayerKind.Elevation or PreviewLayerKind.ElevationBase or PreviewLayerKind.ElevationTectonic or
                PreviewLayerKind.ElevationRoughness or PreviewLayerKind.ElevationErosion or PreviewLayerKind.ElevationZones or
                PreviewLayerKind.ElevationMountain or PreviewLayerKind.ElevationBasin =>
                MapImageRenderer.RenderElevation(map, new ElevationRenderOptions { Mode = layer.ElevationMode!.Value }),
            _ => MapImageRenderer.Render(map)
        };
    }

    private static Bitmap ToBitmap(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
