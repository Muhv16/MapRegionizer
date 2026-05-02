using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;

namespace MapRegionizer.Core.Shapes;

internal sealed class WaterShapeExtractor
{
    private readonly GeometryFactory _geometryFactory;

    public WaterShapeExtractor(GeometryFactory geometryFactory)
    {
        _geometryFactory = geometryFactory;
    }

    public IEnumerable<WaterBody> Extract(IReadOnlyList<Landmass> landmasses, int width, int height, MapGenerationOptions options)
    {
        var fullMap = _geometryFactory.CreatePolygon(_geometryFactory.CreateLinearRing(new[]
        {
            new Coordinate(0, 0),
            new Coordinate(width * options.PixelSize, 0),
            new Coordinate(width * options.PixelSize, height * options.PixelSize),
            new Coordinate(0, height * options.PixelSize),
            new Coordinate(0, 0)
        }));

        Geometry land = landmasses.Count == 0
            ? _geometryFactory.CreateGeometryCollection(Array.Empty<Geometry>())
            : CascadedPolygonUnion.Union(landmasses.Select(l => l.Shape).Cast<Geometry>().ToList()) ?? _geometryFactory.CreateGeometryCollection(Array.Empty<Geometry>());

        if (!land.IsValid)
            land = land.Buffer(0);

        var water = fullMap.Difference(land);
        if (water.IsEmpty)
            yield break;

        if (!water.IsValid)
            water = water.Buffer(0);

        if (options.ShapeExtraction.SimplifyTolerance > 0)
            water = DouglasPeuckerSimplifier.Simplify(water, options.ShapeExtraction.SimplifyTolerance);

        var id = 1;
        foreach (var polygon in ExtractPolygons(water))
            yield return new WaterBody(new WaterBodyId(id++), polygon);
    }

    private static IEnumerable<Polygon> ExtractPolygons(Geometry geometry)
    {
        return geometry switch
        {
            Polygon polygon => new[] { polygon },
            MultiPolygon multiPolygon => multiPolygon.Geometries.OfType<Polygon>(),
            GeometryCollection collection => collection.Geometries.OfType<Polygon>(),
            _ => Enumerable.Empty<Polygon>()
        };
    }
}
