using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace MapRegionizer.ImageSharp;

internal static class RenderingGeometry
{
    internal static PointF ToPixelPoint(MapPoint point, double pixelSize, float scale)
    {
        return new PointF((float)(point.X * pixelSize * scale), (float)(point.Y * pixelSize * scale));
    }

    internal static PointF ToPixelCenter(GridPoint point, double pixelSize, float scale)
    {
        return new PointF(
            (float)((point.X + 0.5) * pixelSize * scale),
            (float)((point.Y + 0.5) * pixelSize * scale));
    }

    internal static void FillPolygon(Image<Rgba32> image, NtsPolygon polygon, Color color, float scale)
    {
        var path = BuildPath(polygon, scale);
        image.Mutate(ctx => ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = false }).Fill(color, path));
    }

    internal static void DrawPolygonBoundary(Image<Rgba32> image, NtsPolygon polygon, Color color, float width, float scale)
    {
        var path = BuildPath(polygon, scale);
        image.Mutate(ctx => ctx.SetGraphicsOptions(new GraphicsOptions { Antialias = false }).Draw(color, width, path));
    }

    internal static IPath BuildPath(NtsPolygon polygon, float scale)
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

    internal static void DrawLine(Image<Rgba32> image, double x1, double y1, double x2, double y2, TectonicPlateRenderOptions options)
    {
        image.Mutate(ctx => ctx.DrawLine(
            options.PlateBoundaryColor,
            options.PlateBoundaryWidth,
            new PointF((float)(x1 * options.Scale), (float)(y1 * options.Scale)),
            new PointF((float)(x2 * options.Scale), (float)(y2 * options.Scale))));
    }

    internal static IEnumerable<GridPoint> EnumeratePoints(int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                yield return new GridPoint(x, y);
        }
    }

    internal static int WrapX(int x, int width) => (x % width + width) % width;
}
