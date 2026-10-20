using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class RenderingColor
{
    internal static Color FromRgb(byte red, byte green, byte blue) =>
        Color.FromPixel(new Rgb24(red, green, blue));

    internal static Color FromRgba(byte red, byte green, byte blue, byte alpha) =>
        Color.FromPixel(new Rgba32(red, green, blue, alpha));
}
