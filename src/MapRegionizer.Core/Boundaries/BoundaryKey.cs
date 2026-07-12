using NetTopologySuite.Geometries;
using System.Globalization;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Boundaries;

internal static class BoundaryKey
{
    public static string MakeDirected(Coordinate a, Coordinate b)
    {
        return $"{RegionGeometryPrecision.GetCoordinateKey(a)}|{RegionGeometryPrecision.GetCoordinateKey(b)}";
    }

    public static string MakeUndirected(Coordinate a, Coordinate b)
    {
        var ordered = new[] { a, b }.OrderBy(coordinate => coordinate.X).ThenBy(coordinate => coordinate.Y).ToArray();
        return $"{ordered[0].X.ToString("R", CultureInfo.InvariantCulture)},{ordered[0].Y.ToString("R", CultureInfo.InvariantCulture)};{ordered[1].X.ToString("R", CultureInfo.InvariantCulture)},{ordered[1].Y.ToString("R", CultureInfo.InvariantCulture)}";
    }

    public static Coordinate[] ParseUndirected(string key)
    {
        var parts = key.Split(';');
        var first = parts[0].Split(',');
        var second = parts[1].Split(',');

        return new[]
        {
            new Coordinate(double.Parse(first[0], CultureInfo.InvariantCulture), double.Parse(first[1], CultureInfo.InvariantCulture)),
            new Coordinate(double.Parse(second[0], CultureInfo.InvariantCulture), double.Parse(second[1], CultureInfo.InvariantCulture))
        };
    }
}
