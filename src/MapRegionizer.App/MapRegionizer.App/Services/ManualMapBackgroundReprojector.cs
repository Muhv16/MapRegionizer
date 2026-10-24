using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Spatial;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MapRegionizer.App.Services;

/// <summary>Inverse-maps a source background raster into a target map grid.</summary>
public static class ManualMapBackgroundReprojector
{
    private const int MaximumSourceDimension = 4096;
    private const int MaximumTargetDimension = 2048;

    public static byte[] ReprojectToPng(
        string sourceImagePath,
        BackgroundImagePlacement sourcePlacement,
        MapSpatialReference sourceReference,
        MapSpatialReference targetReference,
        double sourceLongitudeOffsetDegrees = 0,
        ParametricGeoreferenceTransform? georeferenceTransform = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);
        ArgumentNullException.ThrowIfNull(sourcePlacement);
        ArgumentNullException.ThrowIfNull(sourceReference);
        ArgumentNullException.ThrowIfNull(targetReference);
        sourcePlacement.Validate();
        sourceReference.Validate();
        targetReference.Validate();
        if (!double.IsFinite(sourceLongitudeOffsetDegrees))
            throw new ArgumentOutOfRangeException(nameof(sourceLongitudeOffsetDegrees));

        using var source = Image.Load<Rgba32>(sourceImagePath);
        if (Math.Max(source.Width, source.Height) > MaximumSourceDimension)
        {
            var scale = (double)MaximumSourceDimension / Math.Max(source.Width, source.Height);
            source.Mutate(context => context.Resize(
                Math.Max(1, (int)Math.Round(source.Width * scale)),
                Math.Max(1, (int)Math.Round(source.Height * scale))));
        }

        var targetScale = (double)MaximumTargetDimension / Math.Max(targetReference.GridWidth, targetReference.GridHeight);
        var outputWidth = Math.Max(1, (int)Math.Round(targetReference.GridWidth * Math.Min(1, targetScale)));
        var outputHeight = Math.Max(1, (int)Math.Round(targetReference.GridHeight * Math.Min(1, targetScale)));
        var output = new Image<Rgba32>(outputWidth, outputHeight);
        try
        {
            var sourceContext = MapSpatialContext.Create(sourceReference);
            var targetContext = MapSpatialContext.Create(targetReference);
            output.ProcessPixelRows(destination =>
            {
                for (var y = 0; y < outputHeight; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = destination.GetRowSpan(y);
                    var targetGridY = (y + .5) / outputHeight * targetReference.GridHeight;
                    for (var x = 0; x < outputWidth; x++)
                    {
                        if ((x & 127) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        var targetGridX = (x + .5) / outputWidth * targetReference.GridWidth;
                        GeoCoordinate targetGeographic;
                        MapPoint sourceMapPoint;
                        try
                        {
                            targetGeographic = targetContext.GridToGeographic(new GridCoordinate(targetGridX, targetGridY));
                            sourceMapPoint = georeferenceTransform?.ToSourceMap(targetGeographic)
                                ?? MapToSource(sourceContext, sourceReference, targetGeographic, sourceLongitudeOffsetDegrees);
                        }
                        catch (ArgumentException)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                            continue;
                        }

                        var uv = sourcePlacement.ToImageUv(sourceMapPoint);
                        if (!double.IsFinite(uv.U) || !double.IsFinite(uv.V) || uv.U < 0 || uv.U > 1 || uv.V < 0 || uv.V > 1)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0);
                            continue;
                        }
                        row[x] = SampleBilinear(source, uv.U * (source.Width - 1), uv.V * (source.Height - 1));
                    }
                }
            });

            using var stream = new MemoryStream();
            output.SaveAsPng(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return stream.ToArray();
        }
        finally
        {
            output.Dispose();
        }
    }

    private static MapPoint MapToSource(
        MapSpatialContext sourceContext,
        MapSpatialReference sourceReference,
        GeoCoordinate targetGeographic,
        double sourceLongitudeOffsetDegrees)
    {
        var sourceGeographic = targetGeographic with
        {
            LongitudeDegrees = LongitudeInterval.Normalize(targetGeographic.LongitudeDegrees - sourceLongitudeOffsetDegrees)
        };
        var grid = sourceContext.GeographicToGrid(sourceGeographic);
        if (!double.IsFinite(grid.X) || !double.IsFinite(grid.Y) ||
            grid.X < 0 || grid.X > sourceReference.GridWidth || grid.Y < 0 || grid.Y > sourceReference.GridHeight)
            return new MapPoint(double.NaN, double.NaN);
        return new MapPoint(grid.X * sourceReference.UnitsPerCell, grid.Y * sourceReference.UnitsPerCell);
    }

    private static Rgba32 SampleBilinear(Image<Rgba32> image, double x, double y)
    {
        var x0 = Math.Clamp((int)Math.Floor(x), 0, image.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, image.Height - 1);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var fx = Math.Clamp(x - x0, 0, 1);
        var fy = Math.Clamp(y - y0, 0, 1);
        var upperLeft = image[x0, y0];
        var upperRight = image[x1, y0];
        var lowerLeft = image[x0, y1];
        var lowerRight = image[x1, y1];
        return new Rgba32(
            Blend(upperLeft.R, upperRight.R, lowerLeft.R, lowerRight.R, fx, fy),
            Blend(upperLeft.G, upperRight.G, lowerLeft.G, lowerRight.G, fx, fy),
            Blend(upperLeft.B, upperRight.B, lowerLeft.B, lowerRight.B, fx, fy),
            Blend(upperLeft.A, upperRight.A, lowerLeft.A, lowerRight.A, fx, fy));
    }

    private static byte Blend(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, double x, double y)
    {
        var top = topLeft + (topRight - topLeft) * x;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * x;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * y), 0, 255);
    }
}
