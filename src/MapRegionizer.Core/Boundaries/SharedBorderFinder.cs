using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Boundaries;

internal sealed class SharedBorderFinder
{
    private readonly GeometryFactory _geometryFactory;

    public SharedBorderFinder(GeometryFactory geometryFactory)
    {
        _geometryFactory = geometryFactory;
    }

    public IReadOnlyList<LineString> FindSharedBorders(IEnumerable<Polygon> polygons)
    {
        var edgeCounts = new Dictionary<string, int>();

        foreach (var polygon in polygons)
        {
            var rings = new List<LineString> { polygon.ExteriorRing };
            rings.AddRange(polygon.InteriorRings);

            foreach (var ring in rings)
            {
                var coordinates = ring.Coordinates;
                for (var i = 0; i < coordinates.Length - 1; i++)
                {
                    var key = BoundaryKey.MakeUndirected(coordinates[i], coordinates[i + 1]);
                    edgeCounts[key] = edgeCounts.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }
        }

        return edgeCounts
            .Where(pair => pair.Value == 2)
            .Select(pair => _geometryFactory.CreateLineString(BoundaryKey.ParseUndirected(pair.Key)))
            .ToList();
    }
}
