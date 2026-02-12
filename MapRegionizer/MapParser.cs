using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer
{
    internal class MapParser
    {
        public List<Coordinate> ParseMapContinents(Image<Rgba32> image)
        {
            var terrainPixels = new List<NetTopologySuite.Geometries.Coordinate>();

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];

                        if (pixel.R >= 250 && pixel.G >= 250 && pixel.B >= 250 && pixel.A > 0)
                        {
                            terrainPixels.Add(new Coordinate(x, y));
                        }
                    }
                }
            });
            return terrainPixels;
        }
    }
}
