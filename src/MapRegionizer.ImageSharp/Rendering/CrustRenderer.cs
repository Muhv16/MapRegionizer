using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class CrustRenderer
{
    internal static void RenderCrustToFile(GeneratedMap map, string filePath, CrustRenderOptions? options = null)
    {
        using var image = RenderCrust(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderCrust(GeneratedMap map, CrustRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var tectonics = map.TectonicPlates ?? throw new InvalidOperationException("The map does not contain tectonic plate data.");
        var crustFields = tectonics.CrustFields ?? throw new InvalidOperationException("The map does not contain crust field data.");

        options ??= new CrustRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, crustFields.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, crustFields.Width - 1);
                    var crust = GetDominantCrust(crustFields, sourceX, sourceY, options.CrustSmoothingRadius);
                    var color = GetCrustColor(crust, options);
                    if (options.CoastalZoneTintStrength > 0)
                    {
                        var coastalZone = GetDominantCoastalZone(crustFields, sourceX, sourceY, options.CrustSmoothingRadius);
                        color = ApplyCoastalZoneTint(color, coastalZone, options);
                    }

                    row[x] = color.ToPixel<Rgba32>();
                }
            }
        });

        if (options.DrawPlateBoundaries)
            TectonicPlateRenderer.DrawPlateBoundaries(image, tectonics, map.Bounds.PixelSize, options);

        return image;
    }

    internal static Color GetCrustColor(CrustKind kind, CrustRenderOptions options) => kind switch
    {
        CrustKind.Continental => options.ContinentalColor,
        CrustKind.Oceanic => options.OceanicColor,
        CrustKind.Shelf => options.ShelfColor,
        CrustKind.Arc => options.ArcColor,
        CrustKind.Rift => options.RiftColor,
        CrustKind.Terrane => options.TerraneColor,
        _ => options.UnknownColor
    };

    internal static CrustKind GetDominantCrust(CrustFieldMap crustFields, int x, int y, int radius)
    {
        if (radius <= 0)
            return crustFields.GetCrust(x, y);

        Span<int> counts = stackalloc int[(int)CrustKind.Terrane + 1];
        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(crustFields.Height - 1, y + radius); yy++)
        {
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(crustFields.Width - 1, x + radius); xx++)
                counts[(int)crustFields.GetCrust(xx, yy)]++;
        }

        var best = crustFields.GetCrust(x, y);
        var bestCount = -1;
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] <= bestCount)
                continue;

            best = (CrustKind)index;
            bestCount = counts[index];
        }

        return best;
    }

    internal static CoastalZoneKind GetDominantCoastalZone(CrustFieldMap crustFields, int x, int y, int radius)
    {
        if (radius <= 0)
            return crustFields.GetCoastalZone(x, y);

        Span<int> counts = stackalloc int[(int)CoastalZoneKind.ShallowSea + 1];
        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(crustFields.Height - 1, y + radius); yy++)
        {
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(crustFields.Width - 1, x + radius); xx++)
                counts[(int)crustFields.GetCoastalZone(xx, yy)]++;
        }

        var best = crustFields.GetCoastalZone(x, y);
        var bestCount = -1;
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] <= bestCount)
                continue;

            best = (CoastalZoneKind)index;
            bestCount = counts[index];
        }

        return best;
    }

    internal static Rgba32 ApplyCoastalZoneTint(Color color, CoastalZoneKind zone, CrustRenderOptions options)
    {
        var baseColor = color.ToPixel<Rgba32>();
        var tint = zone switch
        {
            CoastalZoneKind.Shelf => options.ShelfTintColor,
            CoastalZoneKind.Slope => options.SlopeTintColor,
            CoastalZoneKind.PassiveMargin => options.PassiveMarginTintColor,
            CoastalZoneKind.ActiveMargin => options.ActiveMarginTintColor,
            CoastalZoneKind.ShallowSea => options.ShallowSeaTintColor,
            _ => (Color?)null
        };

        return tint is null ? baseColor : ColorBlending.Blend(baseColor, tint.Value.ToPixel<Rgba32>(), options.CoastalZoneTintStrength);
    }
}
