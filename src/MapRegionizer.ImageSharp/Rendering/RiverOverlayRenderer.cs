using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MapRegionizer.ImageSharp;

internal static class RiverOverlayRenderer
{
    internal static void RenderElevationRiversToFile(GeneratedMap map, string filePath, RiverRenderOptions? options = null)
    {
        using var image = RenderElevationRivers(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderElevationRivers(GeneratedMap map, RiverRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Hydrology is null)
            throw new InvalidOperationException("The map does not contain hydrology data.");

        options ??= new RiverRenderOptions();
        var image = ElevationMapRenderer.RenderElevation(map, new ElevationRenderOptions
        {
            Scale = options.Scale,
            Mode = ElevationRenderMode.FinalElevation,
            DrawHillshade = true
        });

        DrawRivers(image, map.Hydrology, map.Bounds.PixelSize, options);
        return image;
    }

    internal static void DrawRivers(Image<Rgba32> image, HydrologyMap hydrology, double pixelSize, RiverRenderOptions options)
    {
        var widthScale = RiverRendering.BuildRiverWidthScale(hydrology.Rivers, options);
        foreach (var river in hydrology.Rivers.OrderBy(r => r.Discharge))
        {
            if (river.Polyline.Count < 2)
                continue;

            var color = ApplyRiverOpacity(GetRiverColor(river, options), options);
            var width = RiverRendering.GetRiverWidth(river, options, widthScale);
            image.Mutate(ctx =>
            {
                ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = true });
                foreach (var run in RiverRendering.BuildRiverRenderRuns(river.Polyline, hydrology.Width, pixelSize, options.Scale))
                    ctx.DrawLine(color, width, run);
            });

            if (options.DrawDebugMarkers && river.MouthKind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta)
                DrawMouthMarker(image, river, pixelSize, options);
        }

        if (options.DrawDebugMarkers)
        {
            foreach (var outlet in hydrology.LakeOutlets.Where(o => o.HasOutlet && o.OutletCell.HasValue))
                TectonicFeatureRenderer.DrawPointMarker(image, outlet.OutletCell!.Value, pixelSize, Math.Max(1, (int)Math.Round(options.OutletMarkerRadius)), options.OutletColor, options.Scale);
        }
    }

    internal static void DrawMouthMarker(Image<Rgba32> image, RiverSegment river, double pixelSize, RiverRenderOptions options)
    {
        var radius = Math.Clamp((int)Math.Round(options.MaxRiverWidth + 1.0), 2, 6);
        var color = river.MouthKind switch
        {
            RiverMouthKind.MarshDelta => options.MarshDeltaColor,
            RiverMouthKind.InlandDelta => options.InlandDeltaColor,
            _ => options.DeltaColor
        };
        color = ApplyRiverOpacity(color, options);
        TectonicFeatureRenderer.DrawPointMarker(image, river.Mouth, pixelSize, radius, color, options.Scale);
    }

    internal static Color GetRiverColor(RiverSegment river, RiverRenderOptions options) => river.Kind switch
    {
        RiverKind.Mountain => options.MountainRiverColor,
        RiverKind.Rift => options.RiftRiverColor,
        RiverKind.Deltaic => options.DeltaRiverColor,
        RiverKind.Endorheic => options.EndorheicRiverColor,
        _ => options.PlainRiverColor
    };

    internal static Color ApplyRiverOpacity(Color color, RiverRenderOptions options)
    {
        if (options.Opacity >= 0.999)
            return color;

        var rgba = color.ToPixel<Rgba32>();
        var alpha = (byte)Math.Clamp((int)Math.Round(rgba.A * Math.Clamp(options.Opacity, 0.0, 1.0)), 0, 255);
        return Color.FromRgba(rgba.R, rgba.G, rgba.B, alpha);
    }

    internal static void DrawRiverValleyAccents(Image<Rgba32> image, HydrologyMap hydrology, double pixelSize, ClimateRenderOptions options)
    {
        var widthScale = RiverRendering.BuildRiverWidthScale(hydrology.Rivers, new RiverRenderOptions
        {
            WidthLowPercentile = options.RiverWidthLowPercentile,
            WidthHighPercentile = options.RiverWidthHighPercentile,
            WidthGamma = options.RiverWidthGamma,
            MinRiverWidth = options.RiverValleyAccentMinWidth,
            MaxRiverWidth = options.RiverValleyAccentMaxWidth
        });

        foreach (var river in hydrology.Rivers.OrderBy(r => r.Discharge))
        {
            if (river.Polyline.Count < 2)
                continue;

            var normalized = Math.Clamp((river.Discharge - widthScale.Low) / Math.Max(0.0001, widthScale.High - widthScale.Low), 0, 1);
            normalized = Math.Pow(normalized, options.RiverValleyAccentGamma);
            var width = (float)((options.RiverValleyAccentMinWidth + (options.RiverValleyAccentMaxWidth - options.RiverValleyAccentMinWidth) * normalized) * RiverRendering.GetRiverStrokeScale(options.Scale));
            var color = Color.FromPixel(ColorBlending.Blend(options.RiverValleyAccentColor.ToPixel<Rgba32>(), options.RiverValleyMajorAccentColor.ToPixel<Rgba32>(), normalized));
            image.Mutate(ctx =>
            {
                ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = true });
                foreach (var run in RiverRendering.BuildRiverRenderRuns(river.Polyline, hydrology.Width, pixelSize, options.Scale))
                    ctx.DrawLine(color, width, run);
            });
        }
    }
}
