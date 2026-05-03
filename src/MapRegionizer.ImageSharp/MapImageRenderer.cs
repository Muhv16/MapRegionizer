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
        foreach (var point in EnumeratePoints(tectonics.Width, tectonics.Height))
        {
            var currentPlate = tectonics.PlateByPoint[point];
            var right = new GridPoint(point.X + 1 == tectonics.Width ? 0 : point.X + 1, point.Y);

            if (tectonics.PlateByPoint[right] != currentPlate)
            {
                if (point.X == tectonics.Width - 1)
                {
                    DrawLine(image, 0, point.Y * pixelSize, 0, (point.Y + 1) * pixelSize, options);
                    DrawLine(image, tectonics.Width * pixelSize, point.Y * pixelSize, tectonics.Width * pixelSize, (point.Y + 1) * pixelSize, options);
                }
                else
                {
                    var x = (point.X + 1) * pixelSize;
                    DrawLine(image, x, point.Y * pixelSize, x, (point.Y + 1) * pixelSize, options);
                }
            }

            if (point.Y == tectonics.Height - 1)
                continue;

            var down = new GridPoint(point.X, point.Y + 1);
            if (tectonics.PlateByPoint[down] == currentPlate)
                continue;

            var y = (point.Y + 1) * pixelSize;
            DrawLine(image, point.X * pixelSize, y, (point.X + 1) * pixelSize, y, options);
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

    private static void DrawPlateIds(Image<Rgba32> image, TectonicPlateMap tectonics, double pixelSize, TectonicPlateRenderOptions options)
    {
        foreach (var plate in tectonics.Plates)
        {
            if (plate.Points.Count == 0)
                continue;

            var centerX = plate.Points.Average(p => p.X + 0.5) * pixelSize * options.Scale;
            var centerY = plate.Points.Average(p => p.Y + 0.5) * pixelSize * options.Scale;
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

public sealed class TectonicPlateRenderOptions
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
