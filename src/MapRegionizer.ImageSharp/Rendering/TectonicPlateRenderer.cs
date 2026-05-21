using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Numerics;

namespace MapRegionizer.ImageSharp;

internal static class TectonicPlateRenderer
{
    internal static void RenderTectonicPlatesToFile(GeneratedMap map, string filePath, TectonicPlateRenderOptions? options = null)
    {
        using var image = RenderTectonicPlates(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderTectonicPlates(GeneratedMap map, TectonicPlateRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.TectonicPlates is null)
            throw new InvalidOperationException("The map does not contain tectonic plate data.");

        options ??= new TectonicPlateRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);

        foreach (var water in map.WaterBodies)
            RenderingGeometry.FillPolygon(image, water.Shape, options.WaterColor, options.Scale);

        foreach (var landmass in map.Landmasses)
            RenderingGeometry.FillPolygon(image, landmass.Shape, options.LandColor, options.Scale);

        DrawPlateBoundaries(image, map.TectonicPlates, map.Bounds.PixelSize, options);
        DrawPlateIds(image, map.TectonicPlates, map.Bounds.PixelSize, options);

        return image;
    }

    internal static void DrawPlateBoundaries(Image<Rgba32> image, TectonicPlateMap tectonics, double pixelSize, TectonicPlateRenderOptions options)
    {
        for (var y = 0; y < tectonics.Height; y++)
        {
            for (var x = 0; x < tectonics.Width; x++)
            {
                var point = new GridPoint(x, y);
                var currentPlate = tectonics.Raster.GetPlate(x, y);
                var right = new GridPoint(x + 1 == tectonics.Width ? 0 : x + 1, y);

                if (tectonics.Raster.GetPlate(right) != currentPlate)
                {
                    if (x == tectonics.Width - 1)
                    {
                        RenderingGeometry.DrawLine(image, 0, y * pixelSize, 0, (y + 1) * pixelSize, options);
                        RenderingGeometry.DrawLine(image, tectonics.Width * pixelSize, y * pixelSize, tectonics.Width * pixelSize, (y + 1) * pixelSize, options);
                    }
                    else
                    {
                        var xPos = (x + 1) * pixelSize;
                        RenderingGeometry.DrawLine(image, xPos, y * pixelSize, xPos, (y + 1) * pixelSize, options);
                    }
                }

                if (y == tectonics.Height - 1)
                    continue;

                var down = new GridPoint(x, y + 1);
                if (tectonics.Raster.GetPlate(down) == currentPlate)
                    continue;

                var yPos = (y + 1) * pixelSize;
                RenderingGeometry.DrawLine(image, x * pixelSize, yPos, (x + 1) * pixelSize, yPos, options);
            }
        }
    }

    internal static void DrawPlateIds(Image<Rgba32> image, TectonicPlateMap tectonics, double pixelSize, TectonicPlateRenderOptions options)
    {
        foreach (var plate in tectonics.Plates)
        {
            if (plate.PointCount == 0)
                continue;

            var centerX = (plate.Centroid.X + 0.5) * pixelSize * options.Scale;
            var centerY = (plate.Centroid.Y + 0.5) * pixelSize * options.Scale;
            DrawDigits(image, plate.Id.Value.ToString(), (int)Math.Round(centerX), (int)Math.Round(centerY), options);
        }
    }

    internal static void DrawDigits(Image<Rgba32> image, string text, int centerX, int centerY, TectonicPlateRenderOptions options)
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

    internal static void DrawDigit(IImageProcessingContext ctx, char digit, int x, int y, int scale, Color color)
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
}
