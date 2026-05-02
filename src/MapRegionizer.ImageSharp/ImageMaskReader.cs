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

    public static MapMask Read(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);

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

        return new MapMask(image.Width, image.Height, landPoints);
    }
}
