using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer
{
    internal class ImageService
    {
        public static List<Coordinate> ParseMapContinents(Image<Rgba32> image)
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
                            terrainPixels.Add(new NetTopologySuite.Geometries.Coordinate(x, y));
                        }
                    }
                }
            });
            return terrainPixels;
        }

        public static void DrawMap(List<Polygon> regions, List<LineString> bounds, int width, int height, string outFile)
        {
            using var image = new Image<Rgba32>(width, height);

            float scaleX = 1;
            float scaleY = 1;
            float offsetX = 0;
            float offsetY = 0;

            var brushes = new Brush[]
            {
            Brushes.Solid(Color.Red),
            Brushes.Solid(Color.Green),
            Brushes.Solid(Color.Blue),
            };

            var random = new Random();

            foreach (var region in regions)
            {
                var color = Color.FromRgb(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256));

                var path = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY))
                    .AddLines(region.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)))
                    .Build();

                image.Mutate(ctx => ctx
                .SetGraphicsOptions(new GraphicsOptions { Antialias = true })
                .Fill(Color.White, path));
            }

            foreach (var region in regions)
            {
                var path = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY))
                    .AddLines(region.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)))
                    .Build();

                image.Mutate(ctx => ctx
                .SetGraphicsOptions(new GraphicsOptions { Antialias = true })
                .Draw(Color.Black, 1, path));
            }

            image.SaveAsPng(outFile);
        }
    }
}
