using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

public static class ImageMaskReader
{
    public static MapMask Read(string filePath)
    {
        using var image = Image.Load<Rgba32>(filePath);
        return Read(image);
    }

    /// <summary>
    /// Reads an image mask and assigns it a world-grid origin while preserving
    /// the image's local cell coordinates. A <see cref="MapMask"/> stores
    /// points relative to its window; this adapter is the single boundary where
    /// a file-local image becomes a world-aligned request window.
    /// </summary>
    public static MapMask ReadAt(string filePath, int originX, int originY)
    {
        using var image = Image.Load<Rgba32>(filePath);
        return ReadAt(image, originX, originY);
    }

    public static MapMask Read(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new MapMask(image.Width, image.Height, ReadLandPoints(image));
    }

    /// <summary>Reads an in-memory image into a world-aligned window.</summary>
    public static MapMask ReadAt(Image<Rgba32> image, int originX, int originY)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new MapMask(
            new GridWindow(originX, originY, image.Width, image.Height),
            ReadLandPoints(image));
    }

    private static IReadOnlySet<GridPoint> ReadLandPoints(Image<Rgba32> image)
    {
        var landPoints = new HashSet<GridPoint>();
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.R >= 250 && pixel.G >= 250 && pixel.B >= 250 && pixel.A > 0)
                        landPoints.Add(new GridPoint(x, y));
                }
            }
        });

        return landPoints;
    }
}
