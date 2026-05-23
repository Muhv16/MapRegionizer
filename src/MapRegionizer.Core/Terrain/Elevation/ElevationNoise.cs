namespace MapRegionizer.Core.Terrain;

internal sealed class ElevationNoise
{
    private readonly int _seed;

    public ElevationNoise(int seed)
    {
        _seed = seed;
    }

    internal double FractalNoise(int x, int y, double scale, int octaves)
    {
        var amplitude = 1.0;
        var frequency = 1.0 / Math.Max(1.0, scale);
        var sum = 0.0;
        var amplitudeSum = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(x * frequency, y * frequency, octave) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.5;
            frequency *= 2.0;
        }

        return amplitudeSum <= 0 ? 0 : sum / amplitudeSum;
    }

    internal double ValueNoise(double x, double y, int octave)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);
        var a = HashSigned(x0, y0, octave);
        var b = HashSigned(x0 + 1, y0, octave);
        var c = HashSigned(x0, y0 + 1, octave);
        var d = HashSigned(x0 + 1, y0 + 1, octave);
        return Math.Clamp((Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty) + 1.0) * 0.5, 0, 1);
    }

    internal double HashSigned(int x, int y, int octave)
    {
        unchecked
        {
            var hash = _seed;
            hash = hash * 397 ^ x;
            hash = hash * 397 ^ (y * 668265263);
            hash = hash * 397 ^ (octave * 1442695041);
            hash ^= hash >> 13;
            hash *= 1274126177;
            hash ^= hash >> 16;
            return ((hash & 0x7fffffff) / (double)int.MaxValue) * 2.0 - 1.0;
        }
    }

    internal static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);

    internal static double Lerp(double a, double b, double t) => a + (b - a) * t;

    internal static double SmoothNoise(int x, int y, int seed, double scale)
    {
        var sampleX = x / Math.Max(1.0, scale);
        var sampleY = y / Math.Max(1.0, scale);
        var x0 = (int)Math.Floor(sampleX);
        var y0 = (int)Math.Floor(sampleY);
        var tx = SmoothStep(sampleX - x0);
        var ty = SmoothStep(sampleY - y0);
        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);
        return Math.Clamp((Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty) + 1.0) * 0.5, 0, 1);
    }

    internal static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            value = (value << 13) ^ value;
            return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
        }
    }

    internal static double HashUnit(int x, int y, int seed) => Math.Clamp((Hash01(x, y, seed) + 1.0) * 0.5, 0, 1);

}

