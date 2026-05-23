using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal static class ElevationSignalMath
{
    internal static double[] BuildTerrainSignal(TectonicFeatureMap features, Func<int, int, double> readValue, int passes, double threshold, double gamma)
    {
        var values = new double[features.Width * features.Height];
        for (var y = 0; y < features.Height; y++)
        {
            for (var x = 0; x < features.Width; x++)
                values[y * features.Width + x] = Math.Clamp(readValue(x, y), 0, 2);
        }

        return ShapeSignal(SmoothField(values, features.Width, features.Height, passes), threshold, gamma);
    }

    internal static double[] BuildOrogenProvinceSignal(OrogenProvinceMap provinces, Func<int, int, double> readValue, int passes, double threshold, double gamma)
    {
        var values = new double[provinces.Width * provinces.Height];
        for (var y = 0; y < provinces.Height; y++)
        {
            for (var x = 0; x < provinces.Width; x++)
                values[y * provinces.Width + x] = Math.Clamp(readValue(x, y), 0, 1.5);
        }

        return ShapeSignal(SmoothField(values, provinces.Width, provinces.Height, passes), threshold, gamma);
    }

    internal static double[] BuildRiftProvinceSignal(RiftProvinceMap provinces, Func<int, int, double> readValue, int passes, double threshold, double gamma)
    {
        var values = new double[provinces.Width * provinces.Height];
        for (var y = 0; y < provinces.Height; y++)
        {
            for (var x = 0; x < provinces.Width; x++)
                values[y * provinces.Width + x] = Math.Clamp(readValue(x, y), 0, 1.8);
        }

        return ShapeSignal(SmoothField(values, provinces.Width, provinces.Height, passes), threshold, gamma);
    }

    internal static double[] DiffuseTectonicLineSignal(
        double[] values,
        int width,
        int height,
        int mediumPasses,
        int broadPasses,
        double threshold,
        double gamma,
        double localWeight)
    {
        var medium = SmoothField(values, width, height, mediumPasses);
        var broad = SmoothField(medium, width, height, broadPasses);
        var blended = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
            blended[i] = medium[i] * localWeight + broad[i] * (1.0 - localWeight);

        return ShapeSignal(blended, threshold, gamma);
    }

    internal static double[] ShapeSignal(double[] values, double threshold, double gamma)
    {
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var normalized = Math.Clamp((values[i] - threshold) / Math.Max(0.0001, 1.0 - threshold), 0, 1);
            result[i] = Math.Pow(normalized, gamma);
        }

        return result;
    }

    internal static double[] SmoothField(double[] values, int width, int height, int passes)
    {
        var current = values.ToArray();
        var next = new double[current.Length];

        for (var pass = 0; pass < passes; pass++)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;

                for (var x = 0; x < width; x++)
                {
                    var index = row + x;
                    var sum = current[index] * 4.0;
                    var weight = 4.0;

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= height)
                            continue;

                        var nrow = ny * width;

                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            var nx = x + dx;
                            if (nx < 0)
                                nx = width - 1;
                            else if (nx >= width)
                                nx = 0;

                            sum += current[nrow + nx];
                            weight += 1.0;
                        }
                    }

                    next[index] = sum / weight;
                }
            }

            (current, next) = (next, current);
        }

        return current;
    }

}
