using NetTopologySuite.Geometries;
using System.Globalization;

namespace MapRegionizer.Core.Boundaries;

internal static class BoundaryKey
{
    private const int KeyDecimalPlaces = 6;

    public static string MakeDirected(Coordinate a, Coordinate b)
    {
        var format = "F" + KeyDecimalPlaces.ToString(CultureInfo.InvariantCulture);
        return $"{a.X.ToString(format, CultureInfo.InvariantCulture)}:{a.Y.ToString(format, CultureInfo.InvariantCulture)}|{b.X.ToString(format, CultureInfo.InvariantCulture)}:{b.Y.ToString(format, CultureInfo.InvariantCulture)}";
    }

    public static string MakeUndirected(Coordinate a, Coordinate b)
    {
        var ordered = new[] { a, b }.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
        return $"{ordered[0].X.ToString(CultureInfo.InvariantCulture)},{ordered[0].Y.ToString(CultureInfo.InvariantCulture)};{ordered[1].X.ToString(CultureInfo.InvariantCulture)},{ordered[1].Y.ToString(CultureInfo.InvariantCulture)}";
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
