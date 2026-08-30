using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Climate;

/// <summary>
/// Values supplied by the surrounding world at a regional boundary.  The
/// interface is intentionally analytical-friendly and does not expose raster
/// implementation details, so a coarse world climate can be plugged in
/// without coupling it to a particular coverage kind.
/// </summary>
public interface IClimateBoundaryContext
{
    double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees);
    double GetExternalElevation(int worldX, int worldY) => 0.0;
    bool GetExternalWaterInfluence(int worldX, int worldY) => false;
}

/// <summary>Explicit isolated policy: a local raster starts each upwind row dry.</summary>
public sealed class IsolatedClimateBoundaryContext : IClimateBoundaryContext
{
    public static IsolatedClimateBoundaryContext Instance { get; } = new();

    private IsolatedClimateBoundaryContext()
    {
    }

    public double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees) => 0.0;
}

/// <summary>
/// Deterministic analytical world boundary.  It provides non-zero incoming
/// moisture for automatic world-context requests while remaining independent of
/// raster dimensions and iteration order.
/// </summary>
public sealed class AnalyticalClimateBoundaryContext : IClimateBoundaryContext
{
    public AnalyticalClimateBoundaryContext(int worldSeed)
    {
        WorldSeed = worldSeed;
    }

    public int WorldSeed { get; }

    public double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees)
    {
        var variation = StableHash.Unit(WorldSeed, worldX, worldY, 0xC11A);
        var latitudeFactor = 1.0 - Math.Clamp(Math.Abs(latitudeDegrees) / 90.0, 0.0, 1.0) * 0.32;
        return Math.Clamp((0.34 + variation * 0.46) * latitudeFactor, 0.0, 1.0);
    }

    public double GetExternalElevation(int worldX, int worldY) =>
        (StableHash.Unit(WorldSeed, worldX, worldY, 0xE1E) - 0.5) * 1200.0;

    public bool GetExternalWaterInfluence(int worldX, int worldY) =>
        StableHash.Unit(WorldSeed, worldX / 8, worldY / 8, 0xA7E) > 0.58;
}

/// <summary>World-level climate identity and its boundary provider.</summary>
public sealed record ClimateWorldContext
{
    public ClimateWorldContext(int worldSeed, IClimateBoundaryContext? boundary = null)
    {
        WorldSeed = worldSeed;
        Boundary = boundary ?? new AnalyticalClimateBoundaryContext(worldSeed);
    }

    public int WorldSeed { get; }
    public IClimateBoundaryContext Boundary { get; }

    public static ClimateWorldContext Create(int worldSeed, WorldContextMode mode, IClimateBoundaryContext? boundary = null)
    {
        if (boundary is not null)
            return new ClimateWorldContext(worldSeed, boundary);

        return mode switch
        {
            WorldContextMode.Isolated => new ClimateWorldContext(worldSeed, IsolatedClimateBoundaryContext.Instance),
            WorldContextMode.Automatic => new ClimateWorldContext(worldSeed, new AnalyticalClimateBoundaryContext(worldSeed)),
            WorldContextMode.Custom => throw new ArgumentException(
                "Custom world context requires an explicit climate boundary context.",
                nameof(boundary)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown world context mode.")
        };
    }

    [Obsolete("Use WorldContextMode.")]
    public static ClimateWorldContext Create(int worldSeed, RegionalGenerationMode mode, IClimateBoundaryContext? boundary = null)
    {
        if (boundary is not null)
            return Create(worldSeed, mode.ToWorldContextMode(), boundary);

        // Preserve the old adapter's permissive Custom behavior. The
        // canonical overload requires an explicit custom boundary, while the
        // obsolete API historically fell back to the analytical provider.
        return mode switch
        {
            RegionalGenerationMode.Legacy or RegionalGenerationMode.Isolated =>
                Create(worldSeed, WorldContextMode.Isolated),
            RegionalGenerationMode.Automatic or RegionalGenerationMode.Custom =>
                Create(worldSeed, WorldContextMode.Automatic),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown generation mode.")
        };
    }
}

internal static class StableHash
{
    public static uint Hash(int seed, int x, int y, int salt)
    {
        unchecked
        {
            uint value = 2166136261u;
            value = (value ^ (uint)seed) * 16777619u;
            value = (value ^ (uint)x) * 16777619u;
            value = (value ^ (uint)y) * 16777619u;
            value = (value ^ (uint)salt) * 16777619u;
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }
    }

    public static double Unit(int seed, int x, int y, int salt) => Hash(seed, x, y, salt) / (double)uint.MaxValue;

    public static int Int32(int seed, int x, int y, int salt) => unchecked((int)Hash(seed, x, y, salt));
}
