using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class ColorBlending
{
    internal static Rgba32 Blend(Rgba32 from, Rgba32 to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1 - amount;
        return new Rgba32(
            (byte)Math.Round(from.R * inverse + to.R * amount),
            (byte)Math.Round(from.G * inverse + to.G * amount),
            (byte)Math.Round(from.B * inverse + to.B * amount),
            255);
    }

    internal static Rgba32 LerpColor(Color from, Color to, double amount)
    {
        return Blend(from.ToPixel<Rgba32>(), to.ToPixel<Rgba32>(), amount);
    }

    internal static Rgba32 ApplyShade(Rgba32 color, double shade, double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        shade = 1.0 + (shade - 1.0) * strength;
        if (shade < 1.0)
            return Blend(color, new Rgba32(0, 0, 0, 255), 1.0 - shade);

        return Blend(color, new Rgba32(255, 255, 255, 255), Math.Clamp(shade - 1.0, 0, 1));
    }

    internal static Rgba32 GetDivergingHeightColor(double value, double low, double high)
    {
        if (value < 0)
            return LerpColor(Color.FromRgb(105, 199, 198), Color.FromRgb(11, 44, 89), Math.Clamp(value / Math.Min(-1.0, low), 0, 1));

        return LerpColor(Color.FromRgb(213, 200, 143), Color.FromRgb(126, 121, 116), Math.Clamp(value / Math.Max(1.0, high), 0, 1));
    }

    internal static Rgba32 GetDivergingContributionColor(double value, double low, double high)
    {
        if (value < 0)
            return LerpColor(Color.FromRgb(42, 89, 156), Color.FromRgb(23, 37, 58), Math.Clamp(value / Math.Min(-1.0, low), 0, 1));

        return LerpColor(Color.FromRgb(44, 68, 55), Color.FromRgb(224, 180, 99), Math.Clamp(value / Math.Max(1.0, high), 0, 1));
    }

    internal static Rgba32 GetUnitColor(double value, Color low, Color high)
    {
        return LerpColor(low, high, Math.Clamp(value, 0, 1));
    }

    internal static Rgba32 BoostSaturation(Rgba32 color, double saturationBoost, double valueBoost)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var luma = r * 0.2126 + g * 0.7152 + b * 0.0722;
        r = Math.Clamp(luma + (r - luma) * saturationBoost + valueBoost, 0, 1);
        g = Math.Clamp(luma + (g - luma) * saturationBoost + valueBoost, 0, 1);
        b = Math.Clamp(luma + (b - luma) * saturationBoost + valueBoost, 0, 1);
        return new Rgba32((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255), 255);
    }

    internal static double HashNoise(int x, int y, int seed)
    {
        unchecked
        {
            var hash = x * 374761393 + y * 668265263 + seed * 1442695041;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (double)int.MaxValue;
        }
    }
}
