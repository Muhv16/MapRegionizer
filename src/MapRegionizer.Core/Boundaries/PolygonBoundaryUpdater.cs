using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Boundaries;

internal sealed class PolygonBoundaryUpdater
{
    private readonly GeometryFactory _geometryFactory;

    public PolygonBoundaryUpdater(GeometryFactory geometryFactory)
    {
        _geometryFactory = geometryFactory;
    }

    public IReadOnlyList<Polygon> UpdatePolygons(IEnumerable<Polygon> polygons, IReadOnlyDictionary<string, LineString> replacements)
    {
        return polygons.Select(polygon =>
        {
            var exterior = UpdateRing((LinearRing)polygon.ExteriorRing, replacements);
            var interiors = polygon.InteriorRings.Select(r => UpdateRing((LinearRing)r, replacements)).ToArray();
            return _geometryFactory.CreatePolygon(exterior, interiors);
        }).ToList();
    }

    private LinearRing UpdateRing(LinearRing ring, IReadOnlyDictionary<string, LineString> replacements)
    {
        var coordinates = ring.Coordinates;
        var updated = new List<Coordinate>();

        for (var i = 0; i < coordinates.Length - 1; i++)
        {
            var a = coordinates[i];
            var b = coordinates[i + 1];

            if (replacements.TryGetValue(BoundaryKey.MakeDirected(a, b), out var replacement))
                updated.AddRange(replacement.Coordinates.Take(replacement.NumPoints - 1).Select(c => c.Copy()));
            else
                updated.Add(a.Copy());
        }

        if (updated.Count == 0)
            return _geometryFactory.CreateLinearRing(coordinates);

        updated.Add(updated[0].Copy());
        return _geometryFactory.CreateLinearRing(updated.ToArray());
    }
}
