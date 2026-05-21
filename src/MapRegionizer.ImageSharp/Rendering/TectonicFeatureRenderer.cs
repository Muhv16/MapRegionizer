using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MapRegionizer.ImageSharp;

internal static class TectonicFeatureRenderer
{
    internal static void RenderTectonicFeaturesToFile(GeneratedMap map, string filePath, TectonicFeatureRenderOptions? options = null)
    {
        using var image = RenderTectonicFeatures(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderTectonicFeatures(GeneratedMap map, TectonicFeatureRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var tectonics = map.TectonicPlates ?? throw new InvalidOperationException("The map does not contain tectonic plate data.");
        var featureMap = tectonics.Features ?? throw new InvalidOperationException("The map does not contain tectonic feature data.");

        options ??= new TectonicFeatureRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, featureMap.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, featureMap.Width - 1);
                    row[x] = GetFeatureFieldColor(featureMap, sourceX, sourceY, options).ToPixel<Rgba32>();
                }
            }
        });

        foreach (var feature in featureMap.Features
            .Where(f => ShouldDrawFeature(f, options))
            .OrderBy(f => GetFeatureDrawOrder(f.Kind)))
            DrawFeature(image, feature, featureMap.Width, map.Bounds.PixelSize, options);

        foreach (var island in featureMap.Islands)
            DrawIslandMarker(image, island, map.Bounds.PixelSize, options);

        if (options.DrawPlateBoundaries)
            TectonicPlateRenderer.DrawPlateBoundaries(image, tectonics, map.Bounds.PixelSize, options);

        return image;
    }

    internal static Color GetFeatureFieldColor(TectonicFeatureMap features, int x, int y, TectonicFeatureRenderOptions options)
    {
        if (!options.DrawFeatureFields)
            return options.BackgroundColor;

        var uplift = Math.Clamp(features.GetUplift(x, y), 0, 1);
        var subsidence = Math.Clamp(features.GetSubsidence(x, y), 0, 1);
        var volcanism = Math.Clamp(features.GetVolcanism(x, y), 0, 1);
        var seismicity = Math.Clamp(features.GetSeismicity(x, y), 0, 1);
        var heatFlow = Math.Clamp(features.GetHeatFlow(x, y), 0, 1);

        var color = options.BackgroundColor.ToPixel<Rgba32>();
        if (options.Mode == TectonicFeatureRenderMode.Diagnostic)
        {
            color = ColorBlending.Blend(color, options.SubsidenceColor.ToPixel<Rgba32>(), subsidence * options.FieldIntensity);
            color = ColorBlending.Blend(color, options.HeatFlowColor.ToPixel<Rgba32>(), heatFlow * options.FieldIntensity);
            color = ColorBlending.Blend(color, options.UpliftColor.ToPixel<Rgba32>(), uplift * options.FieldIntensity);
            color = ColorBlending.Blend(color, options.VolcanismColor.ToPixel<Rgba32>(), volcanism * options.FieldIntensity);
            color = ColorBlending.Blend(color, options.SeismicityColor.ToPixel<Rgba32>(), seismicity * options.FieldIntensity * 0.8);
            return color;
        }

        var normalizedSubsidence = NormalizeFeatureField(subsidence, options.SubsidenceFieldThreshold, options.FieldSaturation, options.FieldGamma);
        var normalizedHeatFlow = NormalizeFeatureField(heatFlow, options.HeatFlowFieldThreshold, options.FieldSaturation, options.FieldGamma);
        var normalizedUplift = options.DrawUpliftFieldInSummary
            ? NormalizeFeatureField(uplift, options.UpliftFieldThreshold, options.FieldSaturation, options.FieldGamma)
            : 0;
        var normalizedVolcanism = NormalizeFeatureField(volcanism, options.VolcanismFieldThreshold, options.FieldSaturation, options.FieldGamma);
        var normalizedSeismicity = options.DrawSeismicityFieldInSummary
            ? NormalizeFeatureField(seismicity, options.SeismicityFieldThreshold, options.FieldSaturation, options.FieldGamma)
            : 0;
        var strongest = Math.Max(
            Math.Max(normalizedSubsidence, normalizedHeatFlow),
            Math.Max(Math.Max(normalizedUplift, normalizedVolcanism), normalizedSeismicity));

        if (strongest < options.MinimumVisibleFieldStrength)
            return color;

        color = ColorBlending.Blend(color, options.SubsidenceColor.ToPixel<Rgba32>(), normalizedSubsidence * options.FieldIntensity);
        color = ColorBlending.Blend(color, options.HeatFlowColor.ToPixel<Rgba32>(), normalizedHeatFlow * options.FieldIntensity);
        color = ColorBlending.Blend(color, options.UpliftColor.ToPixel<Rgba32>(), normalizedUplift * options.FieldIntensity);
        color = ColorBlending.Blend(color, options.VolcanismColor.ToPixel<Rgba32>(), normalizedVolcanism * options.FieldIntensity);
        color = ColorBlending.Blend(color, options.SeismicityColor.ToPixel<Rgba32>(), normalizedSeismicity * options.SeismicityFieldIntensity);
        return color;
    }

    internal static double NormalizeFeatureField(double value, double threshold, double saturation, double gamma)
    {
        if (value <= threshold)
            return 0;

        var range = Math.Max(0.0001, saturation - threshold);
        var normalized = Math.Clamp((value - threshold) / range, 0, 1);
        return Math.Pow(normalized, gamma);
    }

    internal static void DrawFeature(Image<Rgba32> image, TectonicFeature feature, int mapWidth, double pixelSize, TectonicFeatureRenderOptions options)
    {
        if (feature.Points.Count == 0)
            return;

        var color = GetFeatureColor(feature.Kind, options);
        var width = GetFeatureWidth(feature.Kind, options);
        var radius = Math.Max(1, (int)Math.Round(width / 2));
        var drawAsLine = ShouldDrawFeatureAsLine(feature, mapWidth, options);

        if (!drawAsLine)
        {
            DrawSparseFeatureMarkers(image, feature, pixelSize, radius, color, options);
            return;
        }

        image.Mutate(ctx =>
        {
            for (var index = 1; index < feature.Points.Count; index++)
            {
                var previous = feature.Points[index - 1];
                var current = feature.Points[index];
                if (!CanConnectFeaturePoints(previous, current, mapWidth, options.MaxConnectedFeatureStep))
                    continue;

                ctx.DrawLine(
                    color,
                    width,
                    RenderingGeometry.ToPixelCenter(previous, pixelSize, options.Scale),
                    RenderingGeometry.ToPixelCenter(current, pixelSize, options.Scale));
            }
        });

        if (feature.SourceSegmentId.HasValue && options.Mode == TectonicFeatureRenderMode.Diagnostic)
        {
            var pointStep = Math.Max(1, feature.Points.Count / options.MaxBoundaryDerivedPointMarkers);
            for (var index = 0; index < feature.Points.Count; index += pointStep)
                DrawPointMarker(image, feature.Points[index], pixelSize, radius, color, options.Scale);
        }
    }

    internal static bool ShouldDrawFeature(TectonicFeature feature, TectonicFeatureRenderOptions options)
    {
        if (options.Mode == TectonicFeatureRenderMode.Diagnostic)
            return true;

        if (!IsSummaryFeatureKind(feature.Kind))
            return false;

        if (!feature.SourceSegmentId.HasValue)
            return IsHistoricalSummaryFeatureKind(feature.Kind);

        return IsImportantBoundaryFeatureKind(feature.Kind) &&
               feature.Points.Count >= options.MinBoundaryDerivedLinePoints;
    }

    internal static bool ShouldDrawFeatureAsLine(TectonicFeature feature, int mapWidth, TectonicFeatureRenderOptions options)
    {
        if (feature.Points.Count < 2)
            return false;

        if (options.Mode == TectonicFeatureRenderMode.Diagnostic)
            return true;

        if (feature.Kind is TectonicFeatureKind.Hotspot)
            return false;

        if (feature.SourceSegmentId.HasValue)
        {
            return IsImportantBoundaryFeatureKind(feature.Kind) &&
                   feature.Points.Count >= options.MinBoundaryDerivedLinePoints &&
                   ConnectedStepRatio(feature.Points, mapWidth, options.MaxConnectedFeatureStep) >= options.MinConnectedStepRatio;
        }

        if (feature.Kind == TectonicFeatureKind.Orogen)
            return false;

        return IsSummaryLineamentKind(feature.Kind) &&
               feature.Points.Count >= options.MinHistoricalLinePoints &&
               ConnectedStepRatio(feature.Points, mapWidth, options.MaxConnectedFeatureStep) >= options.MinConnectedStepRatio;
    }

    internal static void DrawSparseFeatureMarkers(Image<Rgba32> image, TectonicFeature feature, double pixelSize, int radius, Color color, TectonicFeatureRenderOptions options)
    {
        var maxMarkers = feature.SourceSegmentId.HasValue
            ? options.MaxBoundaryDerivedSummaryMarkers
            : feature.Kind == TectonicFeatureKind.Hotspot
                ? options.MaxHotspotSummaryMarkers
            : options.MaxHistoricalSummaryMarkers;

        var step = Math.Max(1, feature.Points.Count / Math.Max(1, maxMarkers));
        for (var index = 0; index < feature.Points.Count; index += step)
            DrawPointMarker(image, feature.Points[index], pixelSize, radius + 1, color, options.Scale);
    }

    internal static bool IsSummaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate;

    internal static bool IsSummaryLineamentKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Microplate;

    internal static bool IsHistoricalSummaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate;

    internal static bool IsImportantBoundaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Ridge;

    internal static double ConnectedStepRatio(IReadOnlyList<GridPoint> points, int mapWidth, int maxStep)
    {
        if (points.Count < 2)
            return 1;

        var connected = 0;
        for (var index = 1; index < points.Count; index++)
        {
            if (CanConnectFeaturePoints(points[index - 1], points[index], mapWidth, maxStep))
                connected++;
        }

        return connected / (double)(points.Count - 1);
    }

    internal static bool CanConnectFeaturePoints(GridPoint previous, GridPoint current, int mapWidth, int maxStep)
    {
        var dx = Math.Abs(previous.X - current.X);
        var wrappedDx = Math.Min(dx, Math.Max(0, mapWidth - dx));
        var dy = Math.Abs(previous.Y - current.Y);
        return Math.Max(wrappedDx, dy) <= maxStep && dx <= maxStep;
    }

    internal static void DrawIslandMarker(Image<Rgba32> image, TectonicIsland island, double pixelSize, TectonicFeatureRenderOptions options)
    {
        var color = island.Kind switch
        {
            IslandKind.VolcanicArc => options.ArcColor,
            IslandKind.Hotspot => options.HotspotColor,
            IslandKind.Microcontinent => options.CratonColor,
            IslandKind.UpliftedRidge => options.RidgeColor,
            IslandKind.ShelfArchipelago => options.PassiveMarginColor,
            _ => options.IslandColor
        };

        var radius = Math.Clamp((int)Math.Round(Math.Sqrt(Math.Max(1, island.Area)) / 4), 2, 6);
        DrawPointMarker(image, island.Center, pixelSize, radius, color, options.Scale);
    }

    internal static void DrawPointMarker(Image<Rgba32> image, GridPoint point, double pixelSize, int radius, Color color, float scale)
    {
        var center = RenderingGeometry.ToPixelCenter(point, pixelSize, scale);
        var size = radius * 2 + 1;
        image.Mutate(ctx => ctx.Fill(color, new RectangleF(center.X - radius, center.Y - radius, size, size)));
    }

    internal static Color GetFeatureColor(TectonicFeatureKind kind, TectonicFeatureRenderOptions options) => kind switch
    {
        TectonicFeatureKind.Ridge => options.RidgeColor,
        TectonicFeatureKind.Trench => options.TrenchColor,
        TectonicFeatureKind.Arc => options.ArcColor,
        TectonicFeatureKind.Rift => options.RiftColor,
        TectonicFeatureKind.Suture => options.SutureColor,
        TectonicFeatureKind.Orogen => options.OrogenColor,
        TectonicFeatureKind.Craton => options.CratonColor,
        TectonicFeatureKind.PassiveMargin => options.PassiveMarginColor,
        TectonicFeatureKind.Hotspot => options.HotspotColor,
        TectonicFeatureKind.SedimentaryBasin => options.SedimentaryBasinColor,
        TectonicFeatureKind.Microplate => options.MicroplateColor,
        TectonicFeatureKind.BackArcBasin => options.BackArcBasinColor,
        _ => options.UnknownFeatureColor
    };

    internal static float GetFeatureWidth(TectonicFeatureKind kind, TectonicFeatureRenderOptions options) => kind switch
    {
        TectonicFeatureKind.Trench => options.MajorFeatureWidth,
        TectonicFeatureKind.Ridge => options.MajorFeatureWidth,
        TectonicFeatureKind.Microplate => options.MajorFeatureWidth,
        TectonicFeatureKind.Hotspot => options.PointFeatureWidth,
        _ => options.FeatureWidth
    };

    internal static int GetFeatureDrawOrder(TectonicFeatureKind kind) => kind switch
    {
        TectonicFeatureKind.Craton => 0,
        TectonicFeatureKind.SedimentaryBasin => 1,
        TectonicFeatureKind.PassiveMargin => 2,
        TectonicFeatureKind.BackArcBasin => 3,
        TectonicFeatureKind.Ridge => 4,
        TectonicFeatureKind.Rift => 5,
        TectonicFeatureKind.Suture => 6,
        TectonicFeatureKind.Orogen => 7,
        TectonicFeatureKind.Trench => 8,
        TectonicFeatureKind.Arc => 9,
        TectonicFeatureKind.Microplate => 10,
        TectonicFeatureKind.Hotspot => 11,
        _ => 12
    };
}
