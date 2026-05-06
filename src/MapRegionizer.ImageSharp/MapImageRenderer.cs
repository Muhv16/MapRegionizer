using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace MapRegionizer.ImageSharp;

public static class MapImageRenderer
{
    public static void RenderToFile(GeneratedMap map, string filePath, MapRenderOptions? options = null)
    {
        using var image = Render(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> Render(GeneratedMap map, MapRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        options ??= new MapRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);

        foreach (var water in map.WaterBodies)
            FillPolygon(image, water.Shape, options.WaterColor, options.Scale);

        foreach (var landmass in map.Landmasses)
            FillPolygon(image, landmass.Shape, options.LandColor, options.Scale);

        foreach (var region in map.Regions)
            DrawPolygonBoundary(image, region.Shape, options.RegionBorderColor, options.BorderWidth, options.Scale);

        return image;
    }

    public static void RenderTectonicPlatesToFile(GeneratedMap map, string filePath, TectonicPlateRenderOptions? options = null)
    {
        using var image = RenderTectonicPlates(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderTectonicPlates(GeneratedMap map, TectonicPlateRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.TectonicPlates is null)
            throw new InvalidOperationException("The map does not contain tectonic plate data.");

        options ??= new TectonicPlateRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);

        foreach (var water in map.WaterBodies)
            FillPolygon(image, water.Shape, options.WaterColor, options.Scale);

        foreach (var landmass in map.Landmasses)
            FillPolygon(image, landmass.Shape, options.LandColor, options.Scale);

        DrawPlateBoundaries(image, map.TectonicPlates, map.Bounds.PixelSize, options);
        DrawPlateIds(image, map.TectonicPlates, map.Bounds.PixelSize, options);

        return image;
    }

    public static void RenderCrustToFile(GeneratedMap map, string filePath, CrustRenderOptions? options = null)
    {
        using var image = RenderCrust(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderCrust(GeneratedMap map, CrustRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var tectonics = map.TectonicPlates ?? throw new InvalidOperationException("The map does not contain tectonic plate data.");
        var crustFields = tectonics.CrustFields ?? throw new InvalidOperationException("The map does not contain crust field data.");

        options ??= new CrustRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, crustFields.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, crustFields.Width - 1);
                    var crust = GetDominantCrust(crustFields, sourceX, sourceY, options.CrustSmoothingRadius);
                    var color = GetCrustColor(crust, options);
                    if (options.CoastalZoneTintStrength > 0)
                    {
                        var coastalZone = GetDominantCoastalZone(crustFields, sourceX, sourceY, options.CrustSmoothingRadius);
                        color = ApplyCoastalZoneTint(color, coastalZone, options);
                    }

                    row[x] = color.ToPixel<Rgba32>();
                }
            }
        });

        if (options.DrawPlateBoundaries)
            DrawPlateBoundaries(image, tectonics, map.Bounds.PixelSize, options);

        return image;
    }

    public static void RenderTectonicFeaturesToFile(GeneratedMap map, string filePath, TectonicFeatureRenderOptions? options = null)
    {
        using var image = RenderTectonicFeatures(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderTectonicFeatures(GeneratedMap map, TectonicFeatureRenderOptions? options = null)
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
            DrawPlateBoundaries(image, tectonics, map.Bounds.PixelSize, options);

        return image;
    }

    private static void FillPolygon(Image<Rgba32> image, NtsPolygon polygon, Color color, float scale)
    {
        var path = BuildPath(polygon, scale);
        image.Mutate(ctx => ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = false }).Fill(color, path));
    }

    private static void DrawPolygonBoundary(Image<Rgba32> image, NtsPolygon polygon, Color color, float width, float scale)
    {
        var path = BuildPath(polygon, scale);
        image.Mutate(ctx => ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = false }).Draw(color, width, path));
    }

    private static IPath BuildPath(NtsPolygon polygon, float scale)
    {
        var builder = new SixLabors.ImageSharp.Drawing.PathBuilder().SetTransform(Matrix3x2.CreateScale(scale, scale));
        builder.AddLines(polygon.ExteriorRing.Coordinates.Select(c => new PointF((float)c.X, (float)c.Y)));
        builder.CloseFigure();

        foreach (var interior in polygon.InteriorRings)
        {
            builder.AddLines(interior.Coordinates.Select(c => new PointF((float)c.X, (float)c.Y)));
            builder.CloseFigure();
        }

        return builder.Build();
    }

    private static void DrawPlateBoundaries(Image<Rgba32> image, TectonicPlateMap tectonics, double pixelSize, TectonicPlateRenderOptions options)
    {
        for (var y = 0; y < tectonics.Height; y++)
        {
            for (var x = 0; x < tectonics.Width; x++)
            {
                var point = new GridPoint(x, y);
                var currentPlate = tectonics.Raster.GetPlate(x, y);
                var right = new GridPoint(x + 1 == tectonics.Width ? 0 : x + 1, y);

                if (tectonics.Raster.GetPlate(right) != currentPlate)
                {
                    if (x == tectonics.Width - 1)
                    {
                        DrawLine(image, 0, y * pixelSize, 0, (y + 1) * pixelSize, options);
                        DrawLine(image, tectonics.Width * pixelSize, y * pixelSize, tectonics.Width * pixelSize, (y + 1) * pixelSize, options);
                    }
                    else
                    {
                        var xPos = (x + 1) * pixelSize;
                        DrawLine(image, xPos, y * pixelSize, xPos, (y + 1) * pixelSize, options);
                    }
                }

                if (y == tectonics.Height - 1)
                    continue;

                var down = new GridPoint(x, y + 1);
                if (tectonics.Raster.GetPlate(down) == currentPlate)
                    continue;

                var yPos = (y + 1) * pixelSize;
                DrawLine(image, x * pixelSize, yPos, (x + 1) * pixelSize, yPos, options);
            }
        }
    }

    private static void DrawLine(Image<Rgba32> image, double x1, double y1, double x2, double y2, TectonicPlateRenderOptions options)
    {
        image.Mutate(ctx => ctx.DrawLine(
            options.PlateBoundaryColor,
            options.PlateBoundaryWidth,
            new PointF((float)(x1 * options.Scale), (float)(y1 * options.Scale)),
            new PointF((float)(x2 * options.Scale), (float)(y2 * options.Scale))));
    }

    private static Color GetCrustColor(CrustKind kind, CrustRenderOptions options) => kind switch
    {
        CrustKind.Continental => options.ContinentalColor,
        CrustKind.Oceanic => options.OceanicColor,
        CrustKind.Shelf => options.ShelfColor,
        CrustKind.Arc => options.ArcColor,
        CrustKind.Rift => options.RiftColor,
        CrustKind.Terrane => options.TerraneColor,
        _ => options.UnknownColor
    };

    private static CrustKind GetDominantCrust(CrustFieldMap crustFields, int x, int y, int radius)
    {
        if (radius <= 0)
            return crustFields.GetCrust(x, y);

        Span<int> counts = stackalloc int[(int)CrustKind.Terrane + 1];
        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(crustFields.Height - 1, y + radius); yy++)
        {
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(crustFields.Width - 1, x + radius); xx++)
                counts[(int)crustFields.GetCrust(xx, yy)]++;
        }

        var best = crustFields.GetCrust(x, y);
        var bestCount = -1;
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] <= bestCount)
                continue;

            best = (CrustKind)index;
            bestCount = counts[index];
        }

        return best;
    }

    private static CoastalZoneKind GetDominantCoastalZone(CrustFieldMap crustFields, int x, int y, int radius)
    {
        if (radius <= 0)
            return crustFields.GetCoastalZone(x, y);

        Span<int> counts = stackalloc int[(int)CoastalZoneKind.ShallowSea + 1];
        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(crustFields.Height - 1, y + radius); yy++)
        {
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(crustFields.Width - 1, x + radius); xx++)
                counts[(int)crustFields.GetCoastalZone(xx, yy)]++;
        }

        var best = crustFields.GetCoastalZone(x, y);
        var bestCount = -1;
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] <= bestCount)
                continue;

            best = (CoastalZoneKind)index;
            bestCount = counts[index];
        }

        return best;
    }

    private static Rgba32 ApplyCoastalZoneTint(Color color, CoastalZoneKind zone, CrustRenderOptions options)
    {
        var baseColor = color.ToPixel<Rgba32>();
        var tint = zone switch
        {
            CoastalZoneKind.Shelf => options.ShelfTintColor,
            CoastalZoneKind.Slope => options.SlopeTintColor,
            CoastalZoneKind.PassiveMargin => options.PassiveMarginTintColor,
            CoastalZoneKind.ActiveMargin => options.ActiveMarginTintColor,
            CoastalZoneKind.ShallowSea => options.ShallowSeaTintColor,
            _ => (Color?)null
        };

        return tint is null ? baseColor : Blend(baseColor, tint.Value.ToPixel<Rgba32>(), options.CoastalZoneTintStrength);
    }

    private static Color GetFeatureFieldColor(TectonicFeatureMap features, int x, int y, TectonicFeatureRenderOptions options)
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
            color = Blend(color, options.SubsidenceColor.ToPixel<Rgba32>(), subsidence * options.FieldIntensity);
            color = Blend(color, options.HeatFlowColor.ToPixel<Rgba32>(), heatFlow * options.FieldIntensity);
            color = Blend(color, options.UpliftColor.ToPixel<Rgba32>(), uplift * options.FieldIntensity);
            color = Blend(color, options.VolcanismColor.ToPixel<Rgba32>(), volcanism * options.FieldIntensity);
            color = Blend(color, options.SeismicityColor.ToPixel<Rgba32>(), seismicity * options.FieldIntensity * 0.8);
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

        color = Blend(color, options.SubsidenceColor.ToPixel<Rgba32>(), normalizedSubsidence * options.FieldIntensity);
        color = Blend(color, options.HeatFlowColor.ToPixel<Rgba32>(), normalizedHeatFlow * options.FieldIntensity);
        color = Blend(color, options.UpliftColor.ToPixel<Rgba32>(), normalizedUplift * options.FieldIntensity);
        color = Blend(color, options.VolcanismColor.ToPixel<Rgba32>(), normalizedVolcanism * options.FieldIntensity);
        color = Blend(color, options.SeismicityColor.ToPixel<Rgba32>(), normalizedSeismicity * options.SeismicityFieldIntensity);
        return color;
    }

    private static double NormalizeFeatureField(double value, double threshold, double saturation, double gamma)
    {
        if (value <= threshold)
            return 0;

        var range = Math.Max(0.0001, saturation - threshold);
        var normalized = Math.Clamp((value - threshold) / range, 0, 1);
        return Math.Pow(normalized, gamma);
    }

    private static Rgba32 Blend(Rgba32 from, Rgba32 to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1 - amount;
        return new Rgba32(
            (byte)Math.Round(from.R * inverse + to.R * amount),
            (byte)Math.Round(from.G * inverse + to.G * amount),
            (byte)Math.Round(from.B * inverse + to.B * amount),
            255);
    }

    private static void DrawFeature(Image<Rgba32> image, TectonicFeature feature, int mapWidth, double pixelSize, TectonicFeatureRenderOptions options)
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
                    ToPixelCenter(previous, pixelSize, options.Scale),
                    ToPixelCenter(current, pixelSize, options.Scale));
            }
        });

        if (feature.SourceSegmentId.HasValue && options.Mode == TectonicFeatureRenderMode.Diagnostic)
        {
            var pointStep = Math.Max(1, feature.Points.Count / options.MaxBoundaryDerivedPointMarkers);
            for (var index = 0; index < feature.Points.Count; index += pointStep)
                DrawPointMarker(image, feature.Points[index], pixelSize, radius, color, options.Scale);
        }
    }

    private static bool ShouldDrawFeature(TectonicFeature feature, TectonicFeatureRenderOptions options)
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

    private static bool ShouldDrawFeatureAsLine(TectonicFeature feature, int mapWidth, TectonicFeatureRenderOptions options)
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

    private static void DrawSparseFeatureMarkers(Image<Rgba32> image, TectonicFeature feature, double pixelSize, int radius, Color color, TectonicFeatureRenderOptions options)
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

    private static bool IsSummaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Rift or
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate or
        TectonicFeatureKind.BackArcBasin;

    private static bool IsSummaryLineamentKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Rift or
        TectonicFeatureKind.Microplate or
        TectonicFeatureKind.BackArcBasin;

    private static bool IsHistoricalSummaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Rift or
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate or
        TectonicFeatureKind.BackArcBasin;

    private static bool IsImportantBoundaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Rift or
        TectonicFeatureKind.BackArcBasin;

    private static double ConnectedStepRatio(IReadOnlyList<GridPoint> points, int mapWidth, int maxStep)
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

    private static bool CanConnectFeaturePoints(GridPoint previous, GridPoint current, int mapWidth, int maxStep)
    {
        var dx = Math.Abs(previous.X - current.X);
        var wrappedDx = Math.Min(dx, Math.Max(0, mapWidth - dx));
        var dy = Math.Abs(previous.Y - current.Y);
        return Math.Max(wrappedDx, dy) <= maxStep && dx <= maxStep;
    }

    private static void DrawIslandMarker(Image<Rgba32> image, TectonicIsland island, double pixelSize, TectonicFeatureRenderOptions options)
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

    private static void DrawPointMarker(Image<Rgba32> image, GridPoint point, double pixelSize, int radius, Color color, float scale)
    {
        var center = ToPixelCenter(point, pixelSize, scale);
        var size = radius * 2 + 1;
        image.Mutate(ctx => ctx.Fill(color, new RectangleF(center.X - radius, center.Y - radius, size, size)));
    }

    private static PointF ToPixelCenter(GridPoint point, double pixelSize, float scale)
    {
        return new PointF(
            (float)((point.X + 0.5) * pixelSize * scale),
            (float)((point.Y + 0.5) * pixelSize * scale));
    }

    private static Color GetFeatureColor(TectonicFeatureKind kind, TectonicFeatureRenderOptions options) => kind switch
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

    private static float GetFeatureWidth(TectonicFeatureKind kind, TectonicFeatureRenderOptions options) => kind switch
    {
        TectonicFeatureKind.Trench => options.MajorFeatureWidth,
        TectonicFeatureKind.Ridge => options.MajorFeatureWidth,
        TectonicFeatureKind.Microplate => options.MajorFeatureWidth,
        TectonicFeatureKind.Hotspot => options.PointFeatureWidth,
        _ => options.FeatureWidth
    };

    private static int GetFeatureDrawOrder(TectonicFeatureKind kind) => kind switch
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

    private static void DrawPlateIds(Image<Rgba32> image, TectonicPlateMap tectonics, double pixelSize, TectonicPlateRenderOptions options)
    {
        foreach (var plate in tectonics.Plates)
        {
            if (plate.PointCount == 0)
                continue;

            var centerX = (plate.Centroid.X + 0.5) * pixelSize * options.Scale;
            var centerY = (plate.Centroid.Y + 0.5) * pixelSize * options.Scale;
            DrawDigits(image, plate.Id.Value.ToString(), (int)Math.Round(centerX), (int)Math.Round(centerY), options);
        }
    }

    private static void DrawDigits(Image<Rgba32> image, string text, int centerX, int centerY, TectonicPlateRenderOptions options)
    {
        var scale = Math.Max(1, options.PlateIdDigitScale);
        var digitWidth = 3 * scale;
        var digitHeight = 5 * scale;
        var spacing = scale;
        var totalWidth = text.Length * digitWidth + Math.Max(0, text.Length - 1) * spacing;
        var startX = centerX - totalWidth / 2;
        var startY = centerY - digitHeight / 2;

        image.Mutate(ctx =>
        {
            ctx.Fill(options.PlateIdBackgroundColor, new RectangleF(startX - scale, startY - scale, totalWidth + scale * 2, digitHeight + scale * 2));

            for (var index = 0; index < text.Length; index++)
                DrawDigit(ctx, text[index], startX + index * (digitWidth + spacing), startY, scale, options.PlateIdColor);
        });
    }

    private static void DrawDigit(IImageProcessingContext ctx, char digit, int x, int y, int scale, Color color)
    {
        var pattern = digit switch
        {
            '0' => new[] { "111", "101", "101", "101", "111" },
            '1' => new[] { "010", "110", "010", "010", "111" },
            '2' => new[] { "111", "001", "111", "100", "111" },
            '3' => new[] { "111", "001", "111", "001", "111" },
            '4' => new[] { "101", "101", "111", "001", "001" },
            '5' => new[] { "111", "100", "111", "001", "111" },
            '6' => new[] { "111", "100", "111", "101", "111" },
            '7' => new[] { "111", "001", "010", "010", "010" },
            '8' => new[] { "111", "101", "111", "101", "111" },
            '9' => new[] { "111", "101", "111", "001", "111" },
            _ => new[] { "000", "000", "000", "000", "000" }
        };

        for (var row = 0; row < pattern.Length; row++)
        {
            for (var column = 0; column < pattern[row].Length; column++)
            {
                if (pattern[row][column] != '1')
                    continue;

                ctx.Fill(color, new RectangleF(x + column * scale, y + row * scale, scale, scale));
            }
        }
    }

    private static IEnumerable<GridPoint> EnumeratePoints(int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                yield return new GridPoint(x, y);
        }
    }
}

public sealed class MapRenderOptions
{
    public float Scale { get; init; } = 1;
    public float BorderWidth { get; init; } = 2;
    public Color LandColor { get; init; } = Color.White;
    public Color WaterColor { get; init; } = Color.Blue;
    public Color RegionBorderColor { get; init; } = Color.Black;
}

public class TectonicPlateRenderOptions
{
    public float Scale { get; init; } = 1;
    public float PlateBoundaryWidth { get; init; } = 1;
    public int PlateIdDigitScale { get; init; } = 3;
    public Color LandColor { get; init; } = Color.White;
    public Color WaterColor { get; init; } = Color.DeepSkyBlue;
    public Color PlateBoundaryColor { get; init; } = Color.Red;
    public Color PlateIdColor { get; init; } = Color.Black;
    public Color PlateIdBackgroundColor { get; init; } = Color.FromRgba(255, 255, 255, 180);
}

public sealed class CrustRenderOptions : TectonicPlateRenderOptions
{
    public CrustRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgb(190, 30, 42);
        PlateBoundaryWidth = 0.75f;
    }

    public bool DrawPlateBoundaries { get; init; } = true;
    public int CrustSmoothingRadius { get; init; } = 1;
    public Color ContinentalColor { get; init; } = Color.FromRgb(213, 190, 142);
    public Color OceanicColor { get; init; } = Color.FromRgb(25, 93, 154);
    public Color ShelfColor { get; init; } = Color.FromRgb(83, 171, 185);
    public Color ArcColor { get; init; } = Color.FromRgb(225, 113, 74);
    public Color RiftColor { get; init; } = Color.FromRgb(202, 79, 132);
    public Color TerraneColor { get; init; } = Color.FromRgb(159, 139, 198);
    public Color UnknownColor { get; init; } = Color.DarkGray;
    public Color ShelfTintColor { get; init; } = Color.FromRgb(162, 224, 218);
    public Color SlopeTintColor { get; init; } = Color.FromRgb(49, 115, 158);
    public Color PassiveMarginTintColor { get; init; } = Color.FromRgb(128, 190, 151);
    public Color ActiveMarginTintColor { get; init; } = Color.FromRgb(232, 82, 77);
    public Color ShallowSeaTintColor { get; init; } = Color.FromRgb(99, 213, 224);
    public double CoastalZoneTintStrength { get; init; } = 0;
}

public sealed class TectonicFeatureRenderOptions : TectonicPlateRenderOptions
{
    public TectonicFeatureRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgba(255, 80, 70, 150);
        PlateBoundaryWidth = 0.75f;
    }

    public TectonicFeatureRenderMode Mode { get; init; } = TectonicFeatureRenderMode.Summary;
    public bool DrawPlateBoundaries { get; init; } = true;
    public int MaxConnectedFeatureStep { get; init; } = 6;
    public int MaxBoundaryDerivedPointMarkers { get; init; } = 1600;
    public int MaxBoundaryDerivedSummaryMarkers { get; init; } = 90;
    public int MaxHistoricalSummaryMarkers { get; init; } = 260;
    public int MaxHotspotSummaryMarkers { get; init; } = 3;
    public int MinBoundaryDerivedLinePoints { get; init; } = 24;
    public int MinHistoricalLinePoints { get; init; } = 8;
    public int MinOrogenLinePoints { get; init; } = 28;
    public double MinConnectedStepRatio { get; init; } = 0.75;
    public bool DrawFeatureFields { get; init; } = true;
    public double MinimumVisibleFieldStrength { get; init; } = 0.08;
    public double UpliftFieldThreshold { get; init; } = 0.42;
    public double SubsidenceFieldThreshold { get; init; } = 0.32;
    public double VolcanismFieldThreshold { get; init; } = 0.28;
    public double HeatFlowFieldThreshold { get; init; } = 0.34;
    public double SeismicityFieldThreshold { get; init; } = 0.68;
    public double FieldSaturation { get; init; } = 1.0;
    public double FieldGamma { get; init; } = 1.35;
    public double FieldIntensity { get; init; } = 0.34;
    public double SeismicityFieldIntensity { get; init; } = 0.12;
    public float FeatureWidth { get; init; } = 1.25f;
    public float MajorFeatureWidth { get; init; } = 1.75f;
    public float OrogenFeatureWidth { get; init; } = 1.1f;
    public float PointFeatureWidth { get; init; } = 3;
    public bool DrawUpliftFieldInSummary { get; init; } = false;
    public bool DrawSeismicityFieldInSummary { get; init; } = false;
    public Color BackgroundColor { get; init; } = Color.FromRgb(23, 36, 47);
    public Color UpliftColor { get; init; } = Color.FromRgb(226, 164, 72);
    public Color SubsidenceColor { get; init; } = Color.FromRgb(53, 113, 181);
    public Color VolcanismColor { get; init; } = Color.FromRgb(235, 69, 67);
    public Color SeismicityColor { get; init; } = Color.FromRgb(204, 176, 91);
    public Color HeatFlowColor { get; init; } = Color.FromRgb(213, 102, 183);
    public Color RidgeColor { get; init; } = Color.FromRgb(88, 214, 226);
    public Color TrenchColor { get; init; } = Color.FromRgb(35, 24, 30);
    public Color ArcColor { get; init; } = Color.FromRgb(255, 140, 74);
    public Color RiftColor { get; init; } = Color.FromRgb(241, 77, 143);
    public Color SutureColor { get; init; } = Color.FromRgb(126, 113, 88);
    public Color OrogenColor { get; init; } = Color.FromRgb(219, 158, 74);
    public Color CratonColor { get; init; } = Color.FromRgb(132, 176, 118);
    public Color PassiveMarginColor { get; init; } = Color.FromRgb(142, 206, 171);
    public Color HotspotColor { get; init; } = Color.FromRgb(255, 243, 119);
    public Color SedimentaryBasinColor { get; init; } = Color.FromRgb(77, 125, 151);
    public Color MicroplateColor { get; init; } = Color.FromRgb(255, 255, 255);
    public Color BackArcBasinColor { get; init; } = Color.FromRgb(83, 152, 213);
    public Color IslandColor { get; init; } = Color.White;
    public Color UnknownFeatureColor { get; init; } = Color.LightGray;
}

public enum TectonicFeatureRenderMode
{
    Summary,
    Diagnostic
}
