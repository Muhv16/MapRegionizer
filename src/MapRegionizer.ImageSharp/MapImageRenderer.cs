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

    public static void RenderElevationToFile(GeneratedMap map, string filePath, ElevationRenderOptions? options = null)
    {
        using var image = RenderElevation(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderElevation(GeneratedMap map, ElevationRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var elevation = map.Elevation ?? throw new InvalidOperationException("The map does not contain elevation data.");

        options ??= new ElevationRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, elevation.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, elevation.Width - 1);
                    var shade = options.DrawHillshade && options.Mode == ElevationRenderMode.FinalElevation
                        ? ComputeHillshade(elevation, sourceX, sourceY, options)
                        : 1.0;
                    row[x] = GetElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, shade, options);
                }
            }
        });

        if (options.DrawPlateBoundaries && map.TectonicPlates is not null)
            DrawPlateBoundaries(image, map.TectonicPlates, map.Bounds.PixelSize, options);

        return image;
    }

    public static void RenderElevationRiversToFile(GeneratedMap map, string filePath, RiverRenderOptions? options = null)
    {
        using var image = RenderElevationRivers(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderElevationRivers(GeneratedMap map, RiverRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Hydrology is null)
            throw new InvalidOperationException("The map does not contain hydrology data.");

        options ??= new RiverRenderOptions();
        var image = RenderElevation(map, new ElevationRenderOptions
        {
            Scale = options.Scale,
            Mode = ElevationRenderMode.FinalElevation,
            DrawHillshade = true
        });

        DrawRivers(image, map.Hydrology, map.Bounds.PixelSize, options);
        return image;
    }

    public static void RenderClimateToFile(GeneratedMap map, string filePath, ClimateRenderOptions? options = null)
    {
        using var image = RenderClimate(map, options);
        image.SaveAsPng(filePath);
    }

    public static Image<Rgba32> RenderClimate(GeneratedMap map, ClimateRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var climate = map.Climate ?? throw new InvalidOperationException("The map does not contain climate data.");

        options ??= new ClimateRenderOptions();
        var elevation = map.Elevation;
        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);
        var elevationOptions = options.Elevation;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, climate.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, climate.Width - 1);
                    var shade = options.DrawHillshade && elevation is not null && options.Mode == ClimateRenderMode.Biomes
                        ? ComputeHillshade(elevation, Math.Min(sourceX, elevation.Width - 1), Math.Min(sourceY, elevation.Height - 1), elevationOptions)
                        : 1.0;
                    row[x] = GetClimateColor(map, climate, sourceX, sourceY, shade, options);
                }
            }
        });

        if (options.Mode == ClimateRenderMode.Biomes && map.Hydrology is not null)
        {
            if (options.DrawRiverValleyAccents)
                DrawRiverValleyAccents(image, map.Hydrology, map.Bounds.PixelSize, options);

            if (!options.DrawRivers)
                return image;

            DrawRivers(image, map.Hydrology, map.Bounds.PixelSize, new RiverRenderOptions
            {
                Scale = options.Scale,
                Opacity = options.PresentationRiverOpacity
            });
        }

        return image;
    }

    public static void RenderElevationDebugToFiles(
        GeneratedMap map,
        string outputDirectory,
        string prefix = "elevation",
        ElevationRenderOptions? options = null,
        RiverRenderOptions? riverOptions = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        Directory.CreateDirectory(outputDirectory);
        options ??= new ElevationRenderOptions();
        riverOptions ??= new RiverRenderOptions { Scale = options.Scale };

        var modes = new[]
        {
            (ElevationRenderMode.FinalElevation, "final"),
            (ElevationRenderMode.BaseElevation, "base"),
            (ElevationRenderMode.TectonicContribution, "tectonic"),
            (ElevationRenderMode.Roughness, "roughness"),
            (ElevationRenderMode.ErosionMask, "erosion"),
            (ElevationRenderMode.TerrainZones, "terrain-zones"),
            (ElevationRenderMode.MountainInfluence, "mountain"),
            (ElevationRenderMode.BasinInfluence, "basin")
        };

        foreach (var (mode, suffix) in modes)
            RenderElevationToFile(map, System.IO.Path.Combine(outputDirectory, $"{prefix}-{suffix}.png"), new ElevationRenderOptions
            {
                Scale = options.Scale,
                Mode = mode,
                DrawHillshade = options.DrawHillshade,
                DrawPlateBoundaries = options.DrawPlateBoundaries,
                PlateBoundaryWidth = options.PlateBoundaryWidth
            });

        if (map.Hydrology is not null)
            RenderElevationRiversToFile(map, System.IO.Path.Combine(outputDirectory, $"{prefix}-rivers.png"), riverOptions);
    }

    private static void DrawRivers(Image<Rgba32> image, HydrologyMap hydrology, double pixelSize, RiverRenderOptions options)
    {
        var widthScale = BuildRiverWidthScale(hydrology.Rivers, options);
        foreach (var river in hydrology.Rivers.OrderBy(r => r.Discharge))
        {
            if (river.Polyline.Count < 2)
                continue;

            var color = ApplyRiverOpacity(GetRiverColor(river, options), options);
            var width = GetRiverWidth(river, options, widthScale);
            image.Mutate(ctx =>
            {
                ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = true });
                foreach (var run in BuildRiverRenderRuns(river.Polyline, hydrology.Width, pixelSize, options.Scale))
                    ctx.DrawLine(color, width, run);
            });

            if (options.DrawDebugMarkers && river.MouthKind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta)
                DrawMouthMarker(image, river, pixelSize, options);
        }

        if (options.DrawDebugMarkers)
        {
            foreach (var outlet in hydrology.LakeOutlets.Where(o => o.HasOutlet && o.OutletCell.HasValue))
                DrawPointMarker(image, outlet.OutletCell!.Value, pixelSize, Math.Max(1, (int)Math.Round(options.OutletMarkerRadius)), options.OutletColor, options.Scale);
        }
    }

    private static void DrawMouthMarker(Image<Rgba32> image, RiverSegment river, double pixelSize, RiverRenderOptions options)
    {
        var radius = Math.Clamp((int)Math.Round(options.MaxRiverWidth + 1.0), 2, 6);
        var color = river.MouthKind switch
        {
            RiverMouthKind.MarshDelta => options.MarshDeltaColor,
            RiverMouthKind.InlandDelta => options.InlandDeltaColor,
            _ => options.DeltaColor
        };
        color = ApplyRiverOpacity(color, options);
        DrawPointMarker(image, river.Mouth, pixelSize, radius, color, options.Scale);
    }

    private static Color GetRiverColor(RiverSegment river, RiverRenderOptions options) => river.Kind switch
    {
        RiverKind.Mountain => options.MountainRiverColor,
        RiverKind.Rift => options.RiftRiverColor,
        RiverKind.Deltaic => options.DeltaRiverColor,
        RiverKind.Endorheic => options.EndorheicRiverColor,
        _ => options.PlainRiverColor
    };

    private static Color ApplyRiverOpacity(Color color, RiverRenderOptions options)
    {
        if (options.Opacity >= 0.999)
            return color;

        var rgba = color.ToPixel<Rgba32>();
        var alpha = (byte)Math.Clamp((int)Math.Round(rgba.A * Math.Clamp(options.Opacity, 0.0, 1.0)), 0, 255);
        return Color.FromRgba(rgba.R, rgba.G, rgba.B, alpha);
    }

    private static RiverWidthScale BuildRiverWidthScale(IReadOnlyList<RiverSegment> rivers, RiverRenderOptions options)
    {
        if (rivers.Count == 0)
            return new RiverWidthScale(0, 1);

        var sorted = rivers.Select(r => r.Discharge).Order().ToList();
        var low = PercentileSorted(sorted, options.WidthLowPercentile);
        var high = PercentileSorted(sorted, options.WidthHighPercentile);
        if (high <= low + 0.0001)
            high = sorted[^1] + 1.0;

        return new RiverWidthScale(low, high);
    }

    private static float GetRiverWidth(RiverSegment river, RiverRenderOptions options, RiverWidthScale scale)
    {
        var normalized = Math.Clamp((river.Discharge - scale.Low) / Math.Max(0.0001, scale.High - scale.Low), 0, 1);
        normalized = Math.Pow(normalized, options.WidthGamma);
        var rank = river.VisibleRank > 0 ? river.VisibleRank : normalized;
        var orderFactor = river.Order switch
        {
            <= 1 => 0.58,
            2 => 0.74,
            _ => 1.0
        };
        var majorFactor = river.IsMajor ? 1.0 : 0.72;
        var width = options.MinRiverWidth + (options.MaxRiverWidth - options.MinRiverWidth) * Math.Max(normalized, rank * 0.82);
        return (float)Math.Clamp(width * orderFactor * majorFactor, options.MinRiverWidth * 0.55, options.MaxRiverWidth);
    }

    private static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static PointF ToPixelPoint(MapPoint point, double pixelSize, float scale)
    {
        return new PointF((float)(point.X * pixelSize * scale), (float)(point.Y * pixelSize * scale));
    }

    private static IReadOnlyList<PointF[]> BuildRiverRenderRuns(IReadOnlyList<MapPoint> polyline, int mapWidth, double pixelSize, float scale)
    {
        var runs = new List<PointF[]>();
        var current = new List<MapPoint>();
        foreach (var point in polyline)
        {
            if (current.Count > 0 && Math.Abs(current[^1].X - point.X) > mapWidth / 2.0)
            {
                AddSmoothedRun(current, runs, pixelSize, scale);
                current.Clear();
            }

            current.Add(point);
        }

        AddSmoothedRun(current, runs, pixelSize, scale);
        return runs;
    }

    private static void AddSmoothedRun(IReadOnlyList<MapPoint> run, List<PointF[]> runs, double pixelSize, float scale)
    {
        if (run.Count < 2)
            return;

        if (run.Count == 2)
        {
            runs.Add(run.Select(p => ToPixelPoint(p, pixelSize, scale)).ToArray());
            return;
        }

        var points = new List<PointF> { ToPixelPoint(run[0], pixelSize, scale) };
        for (var index = 0; index < run.Count - 1; index++)
        {
            var p0 = run[Math.Max(0, index - 1)];
            var p1 = run[index];
            var p2 = run[index + 1];
            var p3 = run[Math.Min(run.Count - 1, index + 2)];
            var distance = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
            var samples = Math.Clamp((int)Math.Ceiling(distance * pixelSize * scale * 0.55), 3, 8);
            for (var sample = 1; sample <= samples; sample++)
            {
                var t = sample / (double)samples;
                points.Add(ToPixelPoint(CatmullRom(p0, p1, p2, p3, t), pixelSize, scale));
            }
        }

        runs.Add(points.ToArray());
    }

    private static MapPoint CatmullRom(MapPoint p0, MapPoint p1, MapPoint p2, MapPoint p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return new MapPoint(
            0.5 * (2.0 * p1.X + (-p0.X + p2.X) * t + (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * t2 + (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * t3),
            0.5 * (2.0 * p1.Y + (-p0.Y + p2.Y) * t + (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * t2 + (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * t3));
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

    private static Rgba32 GetElevationColor(
        ElevationMap elevation,
        WaterSurfaceMap? waterSurfaces,
        WaterBodyTopology? waterBodyTopology,
        int x,
        int y,
        double shade,
        ElevationRenderOptions options)
    {
        return options.Mode switch
        {
            ElevationRenderMode.BaseElevation => GetDivergingHeightColor(elevation.GetBaseElevation(x, y), -4600, 2600),
            ElevationRenderMode.TectonicContribution => GetDivergingContributionColor(elevation.GetTectonicElevation(x, y), -900, 2800),
            ElevationRenderMode.Roughness => GetUnitColor(elevation.GetRoughness(x, y), Color.FromRgb(36, 73, 82), Color.FromRgb(226, 206, 142)),
            ElevationRenderMode.ErosionMask => GetUnitColor(elevation.GetErosionMask(x, y), Color.FromRgb(40, 46, 73), Color.FromRgb(128, 219, 196)),
            ElevationRenderMode.TerrainZones => GetTerrainClassColor(elevation.GetTerrainClass(x, y), 1.0, options),
            ElevationRenderMode.MountainInfluence => GetMountainInfluenceColor(elevation, x, y),
            ElevationRenderMode.BasinInfluence => GetUnitColor(elevation.GetBasinInfluence(x, y), Color.FromRgb(58, 93, 84), Color.FromRgb(207, 184, 108)),
            _ => GetFinalElevationColor(elevation, waterSurfaces, waterBodyTopology, x, y, shade, options)
        };
    }

    private static Rgba32 GetFinalElevationColor(
        ElevationMap elevationMap,
        WaterSurfaceMap? waterSurfaces,
        WaterBodyTopology? waterBodyTopology,
        int x,
        int y,
        double shade,
        ElevationRenderOptions options)
    {
        var elevation = elevationMap.GetElevation(x, y);
        Rgba32 color;
        if (elevationMap.HasWaterSurface(x, y))
        {
            var surface = elevationMap.GetWaterSurface(x, y);
            var bed = elevationMap.GetBedElevation(x, y);
            var depthMeters = surface <= 0 ? Math.Max(-bed, surface - bed) : Math.Max(0.0, surface - bed);
            var depth = Math.Clamp(depthMeters / Math.Max(1.0, Math.Abs(options.DeepOceanDepthMeters)), 0, 1);
            var depthColor = depth < 0.35
                ? LerpColor(options.ShallowWaterColor, options.ShelfWaterColor, depth / 0.35)
                : LerpColor(options.ShelfWaterColor, options.DeepWaterColor, (depth - 0.35) / 0.65);
            var terrainColor = GetTerrainClassColor(elevationMap.GetTerrainClass(x, y), 1.0, options);
            color = Blend(depthColor, terrainColor, 0.18);
            var bodyId = waterBodyTopology?.GetWaterBodyId(x, y);
            var body = bodyId.HasValue ? waterSurfaces?.GetBodySurface(bodyId.Value) : null;
            if (body?.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea && body.MaxDepthMeters > 0)
            {
                var lakeDepth = Math.Clamp(depthMeters / Math.Max(1.0, body.MaxDepthMeters), 0, 1);
                var lakeShade = Math.Pow(lakeDepth, 0.78);
                var deepLakeColor = body.LakeOrigin switch
                {
                    LakeOriginKind.Tectonic => options.TectonicLakeDepthColor,
                    LakeOriginKind.VolcanicKarst => options.VolcanicLakeDepthColor,
                    _ => options.LakeDepthColor
                };
                color = Blend(color, deepLakeColor.ToPixel<Rgba32>(), lakeShade * options.LakeDepthTintStrength);
            }
        }
        else
        {
            var terrainClass = elevationMap.GetTerrainClass(x, y);
            var terrainColor = GetTerrainClassColor(terrainClass, 1.0, options);
            var elevationColor = GetContinuousLandColor(elevation, options);
            var terrainWeight = terrainClass switch
            {
                TerrainClassKind.Mountain => 0.78,
                TerrainClassKind.Highland => 0.62,
                TerrainClassKind.DesertPlateauCandidate => 0.58,
                TerrainClassKind.DryBasin => 0.55,
                TerrainClassKind.Beach => 0.48,
                TerrainClassKind.DeltaCandidate => 0.48,
                TerrainClassKind.SedimentaryBasin => 0.42,
                TerrainClassKind.AlluvialPlain => 0.40,
                TerrainClassKind.CoastalPlain => 0.38,
                TerrainClassKind.InteriorLowland => 0.34,
                _ => 0.45
            };
            color = Blend(elevationColor, terrainColor, terrainWeight);
            if (terrainClass == TerrainClassKind.Highland)
            {
                var highlandColor = elevation switch
                {
                    < 820 => LerpColor(options.InteriorLowlandColor, options.HighlandColor, Math.Clamp((elevation - 500.0) / 320.0, 0, 1)),
                    < 1250 => LerpColor(options.HighlandColor, options.UplandColor, Math.Clamp((elevation - 820.0) / 430.0, 0, 1)),
                    _ => LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1250.0) / 520.0, 0, 1))
                };
                color = Blend(color, highlandColor, 0.58);
            }
            else if (terrainClass == TerrainClassKind.Mountain)
            {
                var mountainColor = elevation switch
                {
                    < 2100 => LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1300.0) / 800.0, 0, 1)),
                    _ => LerpColor(options.MountainColor, options.SnowColor, Math.Clamp((elevation - 2100.0) / Math.Max(1.0, options.SnowElevationMeters - 2100.0), 0, 1))
                };
                color = Blend(color, mountainColor, 0.72);
            }
        }

        var shadeStrength = elevation < 0 ? options.OceanHillshadeStrength : options.HillshadeStrength;
        return ApplyShade(color, shade, shadeStrength);
    }

    private static Rgba32 GetContinuousLandColor(double elevation, ElevationRenderOptions options)
    {
        return elevation switch
        {
            < 90 => LerpColor(options.BeachColor, options.CoastalPlainColor, Math.Clamp(elevation / 90.0, 0, 1)),
            < 420 => LerpColor(options.CoastalPlainColor, options.InteriorLowlandColor, Math.Clamp((elevation - 90.0) / 330.0, 0, 1)),
            < 900 => LerpColor(options.InteriorLowlandColor, options.HighlandColor, Math.Clamp((elevation - 420.0) / 480.0, 0, 1)),
            < 1450 => LerpColor(options.HighlandColor, options.UplandColor, Math.Clamp((elevation - 900.0) / 550.0, 0, 1)),
            < 2200 => LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1450.0) / 750.0, 0, 1)),
            _ => LerpColor(options.MountainColor, options.SnowColor, Math.Clamp((elevation - 2200.0) / Math.Max(1.0, options.SnowElevationMeters - 2200.0), 0, 1))
        };
    }

    private static Rgba32 GetTerrainClassColor(TerrainClassKind terrainClass, double shade, ElevationRenderOptions options)
    {
        var color = terrainClass switch
        {
            TerrainClassKind.Ocean => options.DeepWaterColor.ToPixel<Rgba32>(),
            TerrainClassKind.ShelfSea => options.ShallowWaterColor.ToPixel<Rgba32>(),
            TerrainClassKind.DeepChannel => options.DeepChannelColor.ToPixel<Rgba32>(),
            TerrainClassKind.ShallowBank => options.ShallowBankColor.ToPixel<Rgba32>(),
            TerrainClassKind.AbyssalBasin => options.AbyssalBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.SubmarineRidge => options.SubmarineRidgeColor.ToPixel<Rgba32>(),
            TerrainClassKind.Trench => options.TrenchColor.ToPixel<Rgba32>(),
            TerrainClassKind.StraitDepth => options.StraitDepthColor.ToPixel<Rgba32>(),
            TerrainClassKind.InlandSeaDepth => options.InlandSeaDepthColor.ToPixel<Rgba32>(),
            TerrainClassKind.Beach => options.BeachColor.ToPixel<Rgba32>(),
            TerrainClassKind.CoastalPlain => options.CoastalPlainColor.ToPixel<Rgba32>(),
            TerrainClassKind.AlluvialPlain => options.AlluvialPlainColor.ToPixel<Rgba32>(),
            TerrainClassKind.InteriorLowland => options.InteriorLowlandColor.ToPixel<Rgba32>(),
            TerrainClassKind.SedimentaryBasin => options.SedimentaryBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.DryBasin => options.DryBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.DeltaCandidate => options.DeltaCandidateColor.ToPixel<Rgba32>(),
            TerrainClassKind.DesertPlateauCandidate => options.DesertPlateauCandidateColor.ToPixel<Rgba32>(),
            TerrainClassKind.Highland => options.HighlandColor.ToPixel<Rgba32>(),
            TerrainClassKind.Mountain => options.MountainColor.ToPixel<Rgba32>(),
            _ => options.LowlandColor.ToPixel<Rgba32>()
        };

        return ApplyShade(color, shade, terrainClass is TerrainClassKind.Ocean or TerrainClassKind.ShelfSea ? options.OceanHillshadeStrength : options.HillshadeStrength);
    }

    private static Rgba32 GetMountainInfluenceColor(ElevationMap elevation, int x, int y)
    {
        var ridge = elevation.GetRidgeContinuity(x, y);
        var foothill = elevation.GetFoothillInfluence(x, y);
        var pass = elevation.GetMountainPassPotential(x, y);
        var color = Blend(Color.FromRgb(43, 74, 70).ToPixel<Rgba32>(), Color.FromRgb(116, 104, 91).ToPixel<Rgba32>(), foothill);
        color = Blend(color, Color.FromRgb(226, 219, 197).ToPixel<Rgba32>(), ridge);
        return Blend(color, Color.FromRgb(92, 168, 196).ToPixel<Rgba32>(), pass * 0.85);
    }

    private static Rgba32 GetClimateColor(GeneratedMap map, ClimateMap climate, int x, int y, double shade, ClimateRenderOptions options)
    {
        return options.Mode switch
        {
            ClimateRenderMode.DebugBiomes => GetBiomeColor(climate.GetBiome(x, y), options),
            ClimateRenderMode.Temperature => GetTemperatureColor(climate.GetMeanAnnualTemperature(x, y), options),
            ClimateRenderMode.Moisture => GetUnitColor(climate.GetMoisture(x, y), options.DryColor, options.WetColor),
            ClimateRenderMode.BiomeMoisture => GetUnitColor(climate.GetBiomeMoisture(x, y), options.DryColor, options.WetColor),
            ClimateRenderMode.Precipitation => GetUnitColor(climate.GetPrecipitation(x, y), options.DryColor, options.RainColor),
            ClimateRenderMode.Seasonality => GetUnitColor(Math.Clamp(climate.GetSeasonality(x, y) / 36.0, 0, 1), options.LowSeasonalityColor, options.HighSeasonalityColor),
            ClimateRenderMode.Habitability => GetUnitColor(climate.GetHabitability(x, y), options.LowHabitabilityColor, options.HighHabitabilityColor),
            ClimateRenderMode.Agriculture => GetUnitColor(climate.GetAgriculturalPotential(x, y), options.LowAgricultureColor, options.HighAgricultureColor),
            ClimateRenderMode.Ice => GetUnitColor(climate.GetIceScore(x, y), options.NoIceColor, options.IceColor),
            _ => GetBiomeReliefColor(map, climate, x, y, shade, options)
        };
    }

    private static Rgba32 GetBiomeReliefColor(GeneratedMap map, ClimateMap climate, int x, int y, double shade, ClimateRenderOptions options)
    {
        var biome = climate.GetBiome(x, y);
        var elevation = map.Elevation;
        if (elevation is null)
            return GetBiomeColor(biome, options);

        var sourceX = Math.Min(x, elevation.Width - 1);
        var sourceY = Math.Min(y, elevation.Height - 1);
        var elevationOptions = options.Elevation;
        if (elevation.HasWaterSurface(sourceX, sourceY) || biome == BiomeKind.Ocean)
            return GetFinalElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, shade, elevationOptions);

        var biomeColor = GetBoundaryAwareBiomeColor(climate, x, y, options);
        biomeColor = ApplyBiomeTexture(biomeColor, biome, x, y, climate, options);
        var terrainColor = GetFinalElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, 1.0, elevationOptions);
        var terrainClass = elevation.GetTerrainClass(sourceX, sourceY);
        var reliefBlend = terrainClass switch
        {
            TerrainClassKind.Mountain => options.MountainReliefBlend,
            TerrainClassKind.Highland => options.HighlandReliefBlend,
            TerrainClassKind.DesertPlateauCandidate => options.PlateauReliefBlend,
            TerrainClassKind.DryBasin => options.BasinReliefBlend,
            _ => options.LandReliefBlend
        };

        var color = Blend(biomeColor, terrainColor, reliefBlend);
        var ridge = elevation.GetRidgeContinuity(sourceX, sourceY);
        var foothill = elevation.GetFoothillInfluence(sourceX, sourceY);
        var mountainOverlay = climate.GetMountainOverlay(x, y);
        if (ridge > 0.18 || terrainClass == TerrainClassKind.Mountain || mountainOverlay > 0.34)
            color = Blend(color, GetBiomeMountainTint(biome, options), Math.Clamp(0.16 + Math.Max(ridge, mountainOverlay) * 0.36, 0, options.MountainTintStrength));
        else if (foothill > 0.15)
            color = Blend(color, terrainColor, Math.Clamp(foothill * 0.18, 0, 0.2));

        if (IsDesertBiome(biome))
            color = ApplyDesertPresentationModifiers(color, biome, x, y, options);

        var river = Math.Pow(climate.GetRiverValleyInfluence(x, y), options.RiverValleyOverlayGamma);
        if (river > options.RiverValleyOverlayThreshold)
            color = Blend(color, options.RiverValleyOverlayColor.ToPixel<Rgba32>(), Math.Clamp((river - options.RiverValleyOverlayThreshold) / (1.0 - options.RiverValleyOverlayThreshold) * options.RiverValleyOverlayStrength, 0, options.RiverValleyOverlayStrength));

        var wetland = climate.GetWetlandInfluence(x, y);
        if (wetland > 0.12)
            color = Blend(color, options.WetlandOverlayColor.ToPixel<Rgba32>(), Math.Clamp((wetland - 0.12) / 0.88 * options.WetlandOverlayStrength, 0, options.WetlandOverlayStrength));

        var snow = Math.Max(climate.GetSnowOverlay(x, y), climate.GetIceScore(x, y) * 0.72);
        if (snow > options.SnowOverlayThreshold)
            color = Blend(color, options.IceColor.ToPixel<Rgba32>(), Math.Clamp((snow - options.SnowOverlayThreshold) / (1.0 - options.SnowOverlayThreshold) * options.IceOverlayStrength, 0, options.IceOverlayStrength));

        return ApplyShade(color, shade, options.BiomeHillshadeStrength);
    }

    private static Rgba32 GetBoundaryAwareBiomeColor(ClimateMap climate, int x, int y, ClimateRenderOptions options)
    {
        var center = climate.GetBiome(x, y);
        var color = GetBiomeColor(center, options);
        var neighborColor = new Rgba32(0, 0, 0, 255);
        var neighborCount = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            var yy = y + dy;
            if (yy < 0 || yy >= climate.Height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var xx = WrapX(x + dx, climate.Width);
                var neighbor = climate.GetBiome(xx, yy);
                if (neighbor == center)
                    continue;

                neighborColor = Blend(neighborColor, GetBiomeColor(neighbor, options), 1.0 / (neighborCount + 1));
                neighborCount++;
            }
        }

        if (neighborCount == 0)
            return BoostSaturation(color, options.BiomeCenterSaturationBoost, options.BiomeCenterValueBoost);

        var boundaryAmount = Math.Clamp((neighborCount - 1.0) / 7.0 * options.BiomeBoundaryBlend, 0, options.BiomeBoundaryBlend);
        return Blend(color, neighborColor, boundaryAmount);
    }

    private static Rgba32 ApplyBiomeTexture(Rgba32 color, BiomeKind biome, int x, int y, ClimateMap climate, ClimateRenderOptions options)
    {
        if (options.TextureStrength <= 0 || biome == BiomeKind.Ocean)
            return color;

        var fine = HashNoise(x, y, 17);
        var broad = HashNoise(x / 7, y / 7, 53);
        var value = biome switch
        {
            BiomeKind.TropicalRainforest or BiomeKind.TemperateRainforest or BiomeKind.BorealForest => fine * 0.65 + broad * 0.35 - 0.55,
            BiomeKind.Savanna => broad * 0.75 + fine * 0.25 - 0.42,
            BiomeKind.OpenWoodland => broad * 0.68 + fine * 0.32 - 0.48,
            BiomeKind.HotDesert or BiomeKind.SemiDesert => broad * 0.78 + fine * 0.22 - 0.34,
            BiomeKind.RockyDesert => broad * 0.46 + fine * 0.54 - 0.42,
            BiomeKind.Tundra or BiomeKind.PolarDesert => fine * 0.35 + broad * 0.65 - 0.50,
            BiomeKind.Marsh or BiomeKind.Floodplain or BiomeKind.Mangrove => fine - 0.42,
            _ => fine * 0.45 + broad * 0.55 - 0.50
        };

        var strength = Math.Clamp(Math.Abs(value) * options.TextureStrength, 0, options.TextureStrength);
        var target = value >= 0
            ? options.TextureLightColor.ToPixel<Rgba32>()
            : options.TextureDarkColor.ToPixel<Rgba32>();
        if (biome is BiomeKind.Marsh or BiomeKind.Floodplain or BiomeKind.Mangrove)
            target = value >= 0 ? options.WetlandTextureLightColor.ToPixel<Rgba32>() : options.WetlandTextureDarkColor.ToPixel<Rgba32>();

        return Blend(color, target, strength);
    }

    private static Rgba32 ApplyDesertPresentationModifiers(Rgba32 color, BiomeKind biome, int x, int y, ClimateRenderOptions options)
    {
        var broad = HashNoise(x / 9, y / 9, 211);
        var streak = HashNoise((x + y) / 11, (x - y) / 11, 307);
        var rocky = HashNoise(x / 3, y / 3, 401);
        var warmAmount = Math.Clamp((broad - 0.28) * options.DesertWarmthStrength, 0, options.DesertWarmthStrength);
        var result = Blend(color, options.DesertSunColor.ToPixel<Rgba32>(), warmAmount);

        if (biome is BiomeKind.RockyDesert or BiomeKind.ColdDesert)
            result = Blend(result, options.DesertRockColor.ToPixel<Rgba32>(), Math.Clamp((rocky - 0.35) * options.DesertRockStrength, 0, options.DesertRockStrength));
        else
            result = Blend(result, options.DesertDuneColor.ToPixel<Rgba32>(), Math.Clamp((streak - 0.38) * options.DesertDuneStrength, 0, options.DesertDuneStrength));

        return result;
    }

    private static bool IsDesertBiome(BiomeKind biome) => biome is
        BiomeKind.HotDesert or
        BiomeKind.SemiDesert or
        BiomeKind.RockyDesert or
        BiomeKind.ColdDesert or
        BiomeKind.SaltFlat;

    private static Rgba32 BoostSaturation(Rgba32 color, double saturationBoost, double valueBoost)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var luma = r * 0.2126 + g * 0.7152 + b * 0.0722;
        r = Math.Clamp(luma + (r - luma) * saturationBoost + valueBoost, 0, 1);
        g = Math.Clamp(luma + (g - luma) * saturationBoost + valueBoost, 0, 1);
        b = Math.Clamp(luma + (b - luma) * saturationBoost + valueBoost, 0, 1);
        return new Rgba32((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255), 255);
    }

    private static void DrawRiverValleyAccents(Image<Rgba32> image, HydrologyMap hydrology, double pixelSize, ClimateRenderOptions options)
    {
        var widthScale = BuildRiverWidthScale(hydrology.Rivers, new RiverRenderOptions
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
            var width = (float)(options.RiverValleyAccentMinWidth + (options.RiverValleyAccentMaxWidth - options.RiverValleyAccentMinWidth) * normalized);
            var color = Color.FromPixel(Blend(options.RiverValleyAccentColor.ToPixel<Rgba32>(), options.RiverValleyMajorAccentColor.ToPixel<Rgba32>(), normalized));
            image.Mutate(ctx =>
            {
                ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = true });
                foreach (var run in BuildRiverRenderRuns(river.Polyline, hydrology.Width, pixelSize, options.Scale))
                    ctx.DrawLine(color, width, run);
            });
        }
    }

    private static double HashNoise(int x, int y, int seed)
    {
        unchecked
        {
            var hash = x * 374761393 + y * 668265263 + seed * 1442695041;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (double)int.MaxValue;
        }
    }

    private static Rgba32 GetBiomeMountainTint(BiomeKind biome, ClimateRenderOptions options)
    {
        return biome switch
        {
            BiomeKind.HotDesert or BiomeKind.ColdDesert => options.DryMountainColor.ToPixel<Rgba32>(),
            BiomeKind.Tundra or BiomeKind.AlpineTundra or BiomeKind.PolarDesert => options.ColdMountainColor.ToPixel<Rgba32>(),
            BiomeKind.IceSheet => options.IceColor.ToPixel<Rgba32>(),
            _ => options.MountainColor.ToPixel<Rgba32>()
        };
    }

    private static Rgba32 GetTemperatureColor(double temperature, ClimateRenderOptions options)
    {
        if (temperature < -10)
            return LerpColor(options.ExtremeColdColor, options.ColdColor, Math.Clamp((temperature + 35.0) / 25.0, 0, 1));
        if (temperature < 8)
            return LerpColor(options.ColdColor, options.CoolColor, Math.Clamp((temperature + 10.0) / 18.0, 0, 1));
        if (temperature < 22)
            return LerpColor(options.CoolColor, options.WarmColor, Math.Clamp((temperature - 8.0) / 14.0, 0, 1));

        return LerpColor(options.WarmColor, options.HotColor, Math.Clamp((temperature - 22.0) / 18.0, 0, 1));
    }

    private static Rgba32 GetBiomeColor(BiomeKind biome, ClimateRenderOptions options)
    {
        return biome switch
        {
            BiomeKind.Ocean => options.OceanColor.ToPixel<Rgba32>(),
            BiomeKind.TropicalRainforest => options.TropicalRainforestColor.ToPixel<Rgba32>(),
            BiomeKind.MonsoonForest => options.MonsoonForestColor.ToPixel<Rgba32>(),
            BiomeKind.DryTropicalForest => options.DryTropicalForestColor.ToPixel<Rgba32>(),
            BiomeKind.TropicalSeasonalForest => options.TropicalSeasonalForestColor.ToPixel<Rgba32>(),
            BiomeKind.Savanna => options.SavannaColor.ToPixel<Rgba32>(),
            BiomeKind.OpenWoodland => options.OpenWoodlandColor.ToPixel<Rgba32>(),
            BiomeKind.HotDesert => options.HotDesertColor.ToPixel<Rgba32>(),
            BiomeKind.SemiDesert => options.SemiDesertColor.ToPixel<Rgba32>(),
            BiomeKind.RockyDesert => options.RockyDesertColor.ToPixel<Rgba32>(),
            BiomeKind.SaltFlat => options.SaltFlatColor.ToPixel<Rgba32>(),
            BiomeKind.ColdDesert => options.ColdDesertColor.ToPixel<Rgba32>(),
            BiomeKind.Steppe => options.SteppeColor.ToPixel<Rgba32>(),
            BiomeKind.XericShrubland => options.XericShrublandColor.ToPixel<Rgba32>(),
            BiomeKind.MediterraneanShrubland => options.MediterraneanShrublandColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateGrassland => options.TemperateGrasslandColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateForest => options.TemperateForestColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateRainforest => options.TemperateRainforestColor.ToPixel<Rgba32>(),
            BiomeKind.BorealForest => options.BorealForestColor.ToPixel<Rgba32>(),
            BiomeKind.Tundra => options.TundraColor.ToPixel<Rgba32>(),
            BiomeKind.PolarDesert => options.PolarDesertColor.ToPixel<Rgba32>(),
            BiomeKind.IceSheet => options.IceColor.ToPixel<Rgba32>(),
            BiomeKind.AlpineTundra => options.AlpineTundraColor.ToPixel<Rgba32>(),
            BiomeKind.Wetland => options.WetlandColor.ToPixel<Rgba32>(),
            BiomeKind.Floodplain => options.FloodplainColor.ToPixel<Rgba32>(),
            BiomeKind.Marsh => options.MarshColor.ToPixel<Rgba32>(),
            BiomeKind.Mangrove => options.MangroveColor.ToPixel<Rgba32>(),
            BiomeKind.MontaneForest => options.MontaneForestColor.ToPixel<Rgba32>(),
            BiomeKind.CloudForest => options.CloudForestColor.ToPixel<Rgba32>(),
            BiomeKind.SnowyMountain => options.SnowyMountainColor.ToPixel<Rgba32>(),
            BiomeKind.VolcanicBadlands => options.VolcanicBadlandsColor.ToPixel<Rgba32>(),
            _ => options.UnknownBiomeColor.ToPixel<Rgba32>()
        };
    }

    private static Rgba32 GetDivergingHeightColor(double value, double low, double high)
    {
        if (value < 0)
            return LerpColor(Color.FromRgb(105, 199, 198), Color.FromRgb(11, 44, 89), Math.Clamp(value / Math.Min(-1.0, low), 0, 1));

        return LerpColor(Color.FromRgb(213, 200, 143), Color.FromRgb(126, 121, 116), Math.Clamp(value / Math.Max(1.0, high), 0, 1));
    }

    private static Rgba32 GetDivergingContributionColor(double value, double low, double high)
    {
        if (value < 0)
            return LerpColor(Color.FromRgb(42, 89, 156), Color.FromRgb(23, 37, 58), Math.Clamp(value / Math.Min(-1.0, low), 0, 1));

        return LerpColor(Color.FromRgb(44, 68, 55), Color.FromRgb(224, 180, 99), Math.Clamp(value / Math.Max(1.0, high), 0, 1));
    }

    private static Rgba32 GetUnitColor(double value, Color low, Color high)
    {
        return LerpColor(low, high, Math.Clamp(value, 0, 1));
    }

    private static double ComputeHillshade(ElevationMap elevation, int x, int y, ElevationRenderOptions options)
    {
        var left = elevation.GetElevation(WrapX(x - 1, elevation.Width), y);
        var right = elevation.GetElevation(WrapX(x + 1, elevation.Width), y);
        var up = elevation.GetElevation(x, Math.Max(0, y - 1));
        var down = elevation.GetElevation(x, Math.Min(elevation.Height - 1, y + 1));
        var eastSlope = (left - right) / Math.Max(1.0, options.HillshadeElevationScale);
        var southSlope = (up - down) / Math.Max(1.0, options.HillshadeElevationScale);
        return Math.Clamp(0.9 + eastSlope * 0.18 + southSlope * 0.24, 0.58, 1.22);
    }

    private static Rgba32 LerpColor(Color from, Color to, double amount)
    {
        return Blend(from.ToPixel<Rgba32>(), to.ToPixel<Rgba32>(), amount);
    }

    private static Rgba32 ApplyShade(Rgba32 color, double shade, double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        shade = 1.0 + (shade - 1.0) * strength;
        if (shade < 1.0)
            return Blend(color, new Rgba32(0, 0, 0, 255), 1.0 - shade);

        return Blend(color, new Rgba32(255, 255, 255, 255), Math.Clamp(shade - 1.0, 0, 1));
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
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate;

    private static bool IsSummaryLineamentKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Microplate;

    private static bool IsHistoricalSummaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Ridge or
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Arc or
        TectonicFeatureKind.Hotspot or
        TectonicFeatureKind.Microplate;

    private static bool IsImportantBoundaryFeatureKind(TectonicFeatureKind kind) => kind is
        TectonicFeatureKind.Trench or
        TectonicFeatureKind.Ridge;

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

    private static int WrapX(int x, int width) => (x % width + width) % width;
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

    public bool DrawPlateBoundaries { get; init; }
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

public sealed class ElevationRenderOptions : TectonicPlateRenderOptions
{
    public ElevationRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgba(40, 30, 25, 120);
        PlateBoundaryWidth = 0.65f;
    }

    public bool DrawHillshade { get; init; } = true;
    public bool DrawPlateBoundaries { get; init; }
    public ElevationRenderMode Mode { get; init; } = ElevationRenderMode.FinalElevation;
    public double HillshadeStrength { get; init; } = 0.42;
    public double OceanHillshadeStrength { get; init; } = 0.035;
    public double HillshadeElevationScale { get; init; } = 3200;
    public double DeepOceanDepthMeters { get; init; } = -6500;
    public double SnowElevationMeters { get; init; } = 3200;
    public Color DeepWaterColor { get; init; } = Color.FromRgb(16, 53, 105);
    public Color ShelfWaterColor { get; init; } = Color.FromRgb(47, 133, 169);
    public Color ShallowWaterColor { get; init; } = Color.FromRgb(113, 198, 196);
    public Color LakeDepthColor { get; init; } = Color.FromRgb(27, 98, 136);
    public Color TectonicLakeDepthColor { get; init; } = Color.FromRgb(20, 81, 132);
    public Color VolcanicLakeDepthColor { get; init; } = Color.FromRgb(31, 88, 121);
    public double LakeDepthTintStrength { get; init; } = 0.26;
    public Color DeepChannelColor { get; init; } = Color.FromRgb(24, 78, 128);
    public Color ShallowBankColor { get; init; } = Color.FromRgb(136, 213, 194);
    public Color AbyssalBasinColor { get; init; } = Color.FromRgb(12, 43, 92);
    public Color SubmarineRidgeColor { get; init; } = Color.FromRgb(76, 168, 180);
    public Color TrenchColor { get; init; } = Color.FromRgb(8, 35, 82);
    public Color StraitDepthColor { get; init; } = Color.FromRgb(28, 105, 151);
    public Color InlandSeaDepthColor { get; init; } = Color.FromRgb(73, 166, 181);
    public Color BeachColor { get; init; } = Color.FromRgb(172, 202, 132);
    public Color LowlandColor { get; init; } = Color.FromRgb(96, 156, 88);
    public Color CoastalPlainColor { get; init; } = Color.FromRgb(137, 190, 117);
    public Color AlluvialPlainColor { get; init; } = Color.FromRgb(116, 168, 92);
    public Color InteriorLowlandColor { get; init; } = Color.FromRgb(91, 145, 83);
    public Color SedimentaryBasinColor { get; init; } = Color.FromRgb(151, 178, 116);
    public Color DryBasinColor { get; init; } = Color.FromRgb(174, 157, 105);
    public Color DeltaCandidateColor { get; init; } = Color.FromRgb(119, 198, 139);
    public Color DesertPlateauCandidateColor { get; init; } = Color.FromRgb(168, 151, 94);
    public Color HighlandColor { get; init; } = Color.FromRgb(116, 145, 80);
    public Color UplandColor { get; init; } = Color.FromRgb(160, 144, 86);
    public Color MountainColor { get; init; } = Color.FromRgb(126, 118, 111);
    public Color SnowColor { get; init; } = Color.FromRgb(238, 241, 235);
}

public sealed class RiverRenderOptions
{
    public float Scale { get; init; } = 1;
    public bool DrawDebugMarkers { get; init; }
    public double Opacity { get; init; } = 1.0;
    public double MinRiverWidth { get; init; } = 0.35;
    public double MaxRiverWidth { get; init; } = 3.2;
    public double WidthLowPercentile { get; init; } = 0.10;
    public double WidthHighPercentile { get; init; } = 0.98;
    public double WidthGamma { get; init; } = 1.7;
    public double OutletMarkerRadius { get; init; } = 1.6;
    public Color PlainRiverColor { get; init; } = Color.FromRgba(35, 113, 188, 180);
    public Color MountainRiverColor { get; init; } = Color.FromRgba(90, 183, 220, 180);
    public Color RiftRiverColor { get; init; } = Color.FromRgba(43, 104, 174, 180);
    public Color DeltaRiverColor { get; init; } = Color.FromRgba(42, 151, 177, 180);
    public Color EndorheicRiverColor { get; init; } = Color.FromRgba(58, 137, 163, 180);
    public Color DeltaColor { get; init; } = Color.FromRgba(61, 180, 166, 180);
    public Color MarshDeltaColor { get; init; } = Color.FromRgba(72, 170, 126, 180);
    public Color InlandDeltaColor { get; init; } = Color.FromRgba(85, 150, 142, 180);
    public Color OutletColor { get; init; } = Color.FromRgba(230, 247, 255, 160);
}

public sealed class ClimateRenderOptions
{
    public float Scale { get; init; } = 1;
    public ClimateRenderMode Mode { get; init; } = ClimateRenderMode.Biomes;
    public bool DrawHillshade { get; init; } = true;
    public bool DrawRivers { get; init; } = true;
    public bool DrawRiverValleyAccents { get; init; } = true;
    public double LandReliefBlend { get; init; } = 0.24;
    public double HighlandReliefBlend { get; init; } = 0.34;
    public double MountainReliefBlend { get; init; } = 0.52;
    public double PlateauReliefBlend { get; init; } = 0.42;
    public double BasinReliefBlend { get; init; } = 0.32;
    public double MountainTintStrength { get; init; } = 0.42;
    public double IceOverlayStrength { get; init; } = 0.84;
    public double SnowOverlayThreshold { get; init; } = 0.08;
    public double RiverValleyOverlayStrength { get; init; } = 0.22;
    public double RiverValleyOverlayThreshold { get; init; } = 0.48;
    public double RiverValleyOverlayGamma { get; init; } = 1.85;
    public double WetlandOverlayStrength { get; init; } = 0.42;
    public double BiomeHillshadeStrength { get; init; } = 0.48;
    public double BiomeBoundaryBlend { get; init; } = 0.16;
    public double BiomeCenterSaturationBoost { get; init; } = 1.16;
    public double BiomeCenterValueBoost { get; init; } = 0.015;
    public double TextureStrength { get; init; } = 0.12;
    public double DesertWarmthStrength { get; init; } = 0.20;
    public double DesertDuneStrength { get; init; } = 0.18;
    public double DesertRockStrength { get; init; } = 0.22;
    public double MinRiverWidth { get; init; } = 0.38;
    public double MaxRiverWidth { get; init; } = 2.25;
    public double RiverValleyAccentMinWidth { get; init; } = 0.45;
    public double RiverValleyAccentMaxWidth { get; init; } = 1.65;
    public double RiverValleyAccentGamma { get; init; } = 1.35;
    public double RiverWidthLowPercentile { get; init; } = 0.06;
    public double RiverWidthHighPercentile { get; init; } = 0.98;
    public double RiverWidthGamma { get; init; } = 1.55;
    public double PresentationRiverOpacity { get; init; } = 0.82;
    public ElevationRenderOptions Elevation { get; init; } = new()
    {
        DrawHillshade = true,
        HillshadeStrength = 0.50,
        OceanHillshadeStrength = 0.06
    };
    public Color OceanColor { get; init; } = Color.FromRgb(42, 113, 162);
    public Color TropicalRainforestColor { get; init; } = Color.FromRgb(20, 117, 63);
    public Color MonsoonForestColor { get; init; } = Color.FromRgb(58, 151, 77);
    public Color DryTropicalForestColor { get; init; } = Color.FromRgb(113, 166, 68);
    public Color TropicalSeasonalForestColor { get; init; } = Color.FromRgb(96, 163, 82);
    public Color SavannaColor { get; init; } = Color.FromRgb(190, 186, 54);
    public Color OpenWoodlandColor { get; init; } = Color.FromRgb(132, 168, 67);
    public Color HotDesertColor { get; init; } = Color.FromRgb(229, 161, 70);
    public Color SemiDesertColor { get; init; } = Color.FromRgb(224, 196, 119);
    public Color RockyDesertColor { get; init; } = Color.FromRgb(150, 126, 105);
    public Color SaltFlatColor { get; init; } = Color.FromRgb(218, 214, 188);
    public Color ColdDesertColor { get; init; } = Color.FromRgb(155, 158, 145);
    public Color SteppeColor { get; init; } = Color.FromRgb(197, 170, 66);
    public Color XericShrublandColor { get; init; } = Color.FromRgb(163, 146, 105);
    public Color MediterraneanShrublandColor { get; init; } = Color.FromRgb(101, 132, 61);
    public Color TemperateGrasslandColor { get; init; } = Color.FromRgb(139, 198, 84);
    public Color TemperateForestColor { get; init; } = Color.FromRgb(70, 143, 83);
    public Color TemperateRainforestColor { get; init; } = Color.FromRgb(40, 128, 106);
    public Color BorealForestColor { get; init; } = Color.FromRgb(61, 111, 86);
    public Color TundraColor { get; init; } = Color.FromRgb(151, 161, 133);
    public Color PolarDesertColor { get; init; } = Color.FromRgb(197, 201, 190);
    public Color IceColor { get; init; } = Color.FromRgb(235, 242, 243);
    public Color AlpineTundraColor { get; init; } = Color.FromRgb(144, 137, 126);
    public Color WetlandColor { get; init; } = Color.FromRgb(72, 143, 128);
    public Color FloodplainColor { get; init; } = Color.FromRgb(94, 163, 112);
    public Color MarshColor { get; init; } = Color.FromRgb(54, 137, 121);
    public Color MangroveColor { get; init; } = Color.FromRgb(25, 111, 87);
    public Color MontaneForestColor { get; init; } = Color.FromRgb(67, 126, 84);
    public Color CloudForestColor { get; init; } = Color.FromRgb(42, 130, 104);
    public Color SnowyMountainColor { get; init; } = Color.FromRgb(202, 210, 205);
    public Color VolcanicBadlandsColor { get; init; } = Color.FromRgb(121, 102, 91);
    public Color MountainColor { get; init; } = Color.FromRgb(128, 124, 112);
    public Color DryMountainColor { get; init; } = Color.FromRgb(158, 139, 106);
    public Color ColdMountainColor { get; init; } = Color.FromRgb(142, 145, 139);
    public Color RiverValleyOverlayColor { get; init; } = Color.FromRgb(62, 184, 104);
    public Color WetlandOverlayColor { get; init; } = Color.FromRgb(43, 139, 132);
    public Color RiverValleyAccentColor { get; init; } = Color.FromRgb(75, 214, 119);
    public Color RiverValleyMajorAccentColor { get; init; } = Color.FromRgb(67, 188, 169);
    public Color PresentationRiverColor { get; init; } = Color.FromRgb(36, 119, 205);
    public Color PresentationMountainRiverColor { get; init; } = Color.FromRgb(82, 184, 226);
    public Color PresentationDeltaRiverColor { get; init; } = Color.FromRgb(31, 158, 179);
    public Color DesertSunColor { get; init; } = Color.FromRgb(246, 188, 76);
    public Color DesertDuneColor { get; init; } = Color.FromRgb(255, 222, 130);
    public Color DesertRockColor { get; init; } = Color.FromRgb(111, 95, 88);
    public Color TextureLightColor { get; init; } = Color.FromRgb(255, 246, 198);
    public Color TextureDarkColor { get; init; } = Color.FromRgb(34, 50, 38);
    public Color WetlandTextureLightColor { get; init; } = Color.FromRgb(110, 192, 166);
    public Color WetlandTextureDarkColor { get; init; } = Color.FromRgb(22, 76, 91);
    public Color UnknownBiomeColor { get; init; } = Color.DarkGray;
    public Color ExtremeColdColor { get; init; } = Color.FromRgb(51, 85, 155);
    public Color ColdColor { get; init; } = Color.FromRgb(106, 169, 203);
    public Color CoolColor { get; init; } = Color.FromRgb(121, 177, 128);
    public Color WarmColor { get; init; } = Color.FromRgb(218, 180, 91);
    public Color HotColor { get; init; } = Color.FromRgb(191, 76, 58);
    public Color DryColor { get; init; } = Color.FromRgb(210, 184, 112);
    public Color WetColor { get; init; } = Color.FromRgb(42, 124, 112);
    public Color RainColor { get; init; } = Color.FromRgb(47, 116, 187);
    public Color LowSeasonalityColor { get; init; } = Color.FromRgb(82, 158, 153);
    public Color HighSeasonalityColor { get; init; } = Color.FromRgb(176, 91, 75);
    public Color LowHabitabilityColor { get; init; } = Color.FromRgb(70, 75, 78);
    public Color HighHabitabilityColor { get; init; } = Color.FromRgb(107, 184, 111);
    public Color LowAgricultureColor { get; init; } = Color.FromRgb(92, 83, 70);
    public Color HighAgricultureColor { get; init; } = Color.FromRgb(194, 187, 83);
    public Color NoIceColor { get; init; } = Color.FromRgb(58, 111, 134);
}

public readonly record struct RiverWidthScale(double Low, double High);

public enum ClimateRenderMode
{
    Biomes,
    DebugBiomes,
    Temperature,
    Moisture,
    BiomeMoisture,
    Precipitation,
    Seasonality,
    Habitability,
    Agriculture,
    Ice
}

public enum ElevationRenderMode
{
    FinalElevation,
    BaseElevation,
    TectonicContribution,
    Roughness,
    ErosionMask,
    TerrainZones,
    MountainInfluence,
    BasinInfluence
}

public sealed class TectonicFeatureRenderOptions : TectonicPlateRenderOptions
{
    public TectonicFeatureRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgba(255, 80, 70, 150);
        PlateBoundaryWidth = 0.75f;
    }

    public TectonicFeatureRenderMode Mode { get; init; } = TectonicFeatureRenderMode.Summary;
    public bool DrawPlateBoundaries { get; init; }
    public int MaxConnectedFeatureStep { get; init; } = 6;
    public int MaxBoundaryDerivedPointMarkers { get; init; } = 1600;
    public int MaxBoundaryDerivedSummaryMarkers { get; init; } = 90;
    public int MaxHistoricalSummaryMarkers { get; init; } = 260;
    public int MaxHotspotSummaryMarkers { get; init; } = 1;
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
