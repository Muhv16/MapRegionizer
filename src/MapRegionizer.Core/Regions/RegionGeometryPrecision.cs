using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Regions;

/// <summary>
/// Defines the topology-comparison precision shared by landmass and region geometry.
/// </summary>
public static class RegionGeometryPrecision
{
    public const int DecimalPlaces = 6;
    public const double Scale = 1_000_000d;
    public const double LengthTolerance = 1d / Scale;
    public static string GetCoordinateKey(Coordinate coordinate) => $"{coordinate.X:F6},{coordinate.Y:F6}";

    public static string GetUndirectedSegmentKey(Coordinate first, Coordinate second)
    {
        var firstKey = GetCoordinateKey(first);
        var secondKey = GetCoordinateKey(second);
        return string.CompareOrdinal(firstKey, secondKey) <= 0 ? $"{firstKey}|{secondKey}" : $"{secondKey}|{firstKey}";
    }

    public static bool IsEquivalent(Coordinate first, Coordinate second) =>
        Math.Abs(first.X - second.X) <= LengthTolerance
        && Math.Abs(first.Y - second.Y) <= LengthTolerance;

    public static double GetAreaTolerance(Geometry geometry) => Math.Max(1, geometry.Length) * LengthTolerance;
}
