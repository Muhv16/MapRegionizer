using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;


namespace MapRegionizer
{
    internal class ImageService
    {
        public static void DrawMap(List<Polygon> regions, List<LineString> bounds, int width, int height, string outFile)
        {
            float scaleX = 1;
            float scaleY = 1;
            float offsetX = 0;
            float offsetY = 0;

            using var image = new Image<Rgba32>((int)(width * scaleX), (int)(height * scaleY));

            foreach (var region in regions)
            {

                var path = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY))
                    .AddLines(region.ExteriorRing.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)))
                    .Build();
                var holesPaths = region.InteriorRings.Select(ir =>
                    new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY))
                    .AddLines(ir.Coordinates.Select(c =>
                    new PointF((float)c.X + offsetX, (float)c.Y + offsetY)))
                    .Build());

                image.Mutate(ctx => ctx
                .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
                .Fill(Color.White, path));
                foreach(var hole in holesPaths)
                {
                    image.Mutate(ctx => ctx
                    .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
                    .Fill(Color.Blue, hole));
                }
            }

            foreach (var bound in bounds)
            {
                var path = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY))
                    .AddLines(bound.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)))
                    .Build();

                image.Mutate(ctx => ctx
                .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
                .Draw(Color.Black, 2, path));
            }

            image.SaveAsPng(outFile);
        }
    }
}
