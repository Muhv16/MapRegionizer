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
            DrawMap(regions, bounds, [], width, height, outFile);
        }
        public static void DrawMap(List<Polygon> regions, List<LineString> bounds, List<Polygon> seas, int width, int height, string outFile)
        {
            float scaleX = 1;
            float scaleY = 1;
            float offsetX = 0;
            float offsetY = 0;

            using var image = new Image<Rgba32>((int)(width * scaleX), (int)(height * scaleY));

            // Отрисовка регионов (континентов)
            foreach (var region in regions)
            {
                var builder = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY));

                // Внешнее кольцо
                builder.AddLines(region.ExteriorRing.Coordinates.Select(c =>
                    new PointF((float)c.X + offsetX, (float)c.Y + offsetY)));
                builder.CloseFigure();

                // Внутренние кольца (дырки)
                foreach (var interiorRing in region.InteriorRings)
                {
                    builder.AddLines(interiorRing.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)));
                    builder.CloseFigure();
                }

                var path = builder.Build();

                image.Mutate(ctx => ctx
                    .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
                    .Fill(Color.White, path));
            }

            // Отрисовка морей
            foreach (var sea in seas)
            {
                var builder = new SixLabors.ImageSharp.Drawing.PathBuilder()
                    .SetTransform(Matrix3x2.CreateScale(scaleX, scaleY));

                builder.AddLines(sea.ExteriorRing.Coordinates.Select(c =>
                    new PointF((float)c.X + offsetX, (float)c.Y + offsetY)));
                builder.CloseFigure();

                foreach (var interiorRing in sea.InteriorRings)
                {
                    builder.AddLines(interiorRing.Coordinates.Select(c =>
                        new PointF((float)c.X + offsetX, (float)c.Y + offsetY)));
                    builder.CloseFigure();
                }

                var seaPath = builder.Build();

                image.Mutate(ctx => ctx
                    .SetGraphicsOptions(new GraphicsOptions { Antialias = false })
                    .Fill(Color.Blue, seaPath));
            }

            // Отрисовка границ (линий) — без изменений
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
