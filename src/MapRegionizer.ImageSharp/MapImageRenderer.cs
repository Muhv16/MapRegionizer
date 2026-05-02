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
}

public sealed class MapRenderOptions
{
    public float Scale { get; init; } = 1;
    public float BorderWidth { get; init; } = 2;
    public Color LandColor { get; init; } = Color.White;
    public Color WaterColor { get; init; } = Color.Blue;
    public Color RegionBorderColor { get; init; } = Color.Black;
}
