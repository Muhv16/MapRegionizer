using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace MapRegionizer.Core.Shapes;

internal sealed class LandShapeExtractor
{
    private readonly PixelComponentFinder _componentFinder = new();
    private readonly PixelPolygonBuilder _polygonBuilder;

    public LandShapeExtractor(GeometryFactory geometryFactory)
    {
        _polygonBuilder = new PixelPolygonBuilder(geometryFactory);
    }

    public IEnumerable<Landmass> Extract(MapMask mask, MapGenerationOptions options)
    {
        var components = _componentFinder.FindConnectedComponents(mask.LandPoints).Where(c => c.Count > 3);
        var id = 1;

        foreach (var component in components)
        {
            var polygon = _polygonBuilder.Build(component, options.PixelSize);
            if (polygon is null || polygon.ExteriorRing.NumPoints <= 3)
                continue;

            var simplified = options.ShapeExtraction.SimplifyTolerance > 0
                ? DouglasPeuckerSimplifier.Simplify(polygon, options.ShapeExtraction.SimplifyTolerance)
                : polygon;

            if (simplified is Polygon result && result.ExteriorRing.NumPoints > 3)
                yield return new Landmass(new LandmassId(id++), result);
        }
    }
}

internal sealed class PixelComponentFinder
{
    public IReadOnlyList<IReadOnlyList<GridPoint>> FindConnectedComponents(IReadOnlySet<GridPoint> points)
    {
        var unvisited = new HashSet<GridPoint>(points);
        var result = new List<IReadOnlyList<GridPoint>>();
        var directions = new[] { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1), new GridPoint(0, -1) };

        while (unvisited.Count > 0)
        {
            var start = unvisited.OrderBy(point => point.Y).ThenBy(point => point.X).First();
            var queue = new Queue<GridPoint>();
            var component = new List<GridPoint>();
            queue.Enqueue(start);
            unvisited.Remove(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var direction in directions)
                {
                    var neighbor = new GridPoint(current.X + direction.X, current.Y + direction.Y);
                    if (!unvisited.Remove(neighbor))
                        continue;

                    queue.Enqueue(neighbor);
                }
            }

            result.Add(component);
        }

        return result;
    }
}

internal sealed class PixelPolygonBuilder
{
    private readonly GeometryFactory _geometryFactory;

    public PixelPolygonBuilder(GeometryFactory geometryFactory)
    {
        _geometryFactory = geometryFactory;
    }

    public Polygon? Build(IReadOnlyCollection<GridPoint> pixels, double pixelSize)
    {
        var rectangles = BuildRectangles(pixels, pixelSize);
        var unioned = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(rectangles);
        if (unioned is null || unioned.IsEmpty)
            return null;

        var polygons = ExtractPolygons(unioned).ToList();
        if (polygons.Count == 0)
            return null;

        var outer = polygons.OrderByDescending(p => p.Area).First();
        var holes = Enumerable.Range(0, outer.NumInteriorRings)
            .Select(i => _geometryFactory.CreateLinearRing(outer.GetInteriorRingN(i).Coordinates))
            .ToArray();

        return _geometryFactory.CreatePolygon(_geometryFactory.CreateLinearRing(outer.ExteriorRing.Coordinates), holes);
    }

    private List<Geometry> BuildRectangles(IReadOnlyCollection<GridPoint> pixels, double pixelSize)
    {
        var rows = pixels.GroupBy(p => p.Y).ToDictionary(g => g.Key, g => g.Select(p => p.X).Distinct().Order().ToList());
        var rectangles = new List<Geometry>();
        var active = new Dictionary<Segment, ActiveRectangle>();
        var previousY = int.MinValue;

        foreach (var y in rows.Keys.Order())
        {
            if (previousY != int.MinValue && y != previousY + 1)
            {
                rectangles.AddRange(active.Values.Select(CreateRectangle));
                active.Clear();
            }

            var currentSegments = BuildSegments(rows[y]).ToHashSet();

            foreach (var closed in active.Keys.Where(s => !currentSegments.Contains(s)).ToList())
            {
                rectangles.Add(CreateRectangle(active[closed]));
                active.Remove(closed);
            }

            foreach (var segment in currentSegments)
            {
                if (active.TryGetValue(segment, out var rectangle))
                    rectangle.Y1Exclusive = y + 1;
                else
                    active[segment] = new ActiveRectangle(segment.X0, segment.X1, y, y + 1);
            }

            previousY = y;
        }

        rectangles.AddRange(active.Values.Select(CreateRectangle));
        return rectangles;

        Geometry CreateRectangle(ActiveRectangle rectangle)
        {
            var x0 = rectangle.X0 * pixelSize;
            var x1 = (rectangle.X1 + 1) * pixelSize;
            var y0 = rectangle.Y0 * pixelSize;
            var y1 = rectangle.Y1Exclusive * pixelSize;

            return _geometryFactory.CreatePolygon(_geometryFactory.CreateLinearRing(new[]
            {
                new Coordinate(x0, y0),
                new Coordinate(x1, y0),
                new Coordinate(x1, y1),
                new Coordinate(x0, y1),
                new Coordinate(x0, y0)
            }));
        }
    }

    private static IEnumerable<Segment> BuildSegments(IReadOnlyList<int> xs)
    {
        var index = 0;
        while (index < xs.Count)
        {
            var x0 = xs[index];
            var x1 = x0;
            index++;

            while (index < xs.Count && xs[index] == x1 + 1)
                x1 = xs[index++];

            yield return new Segment(x0, x1);
        }
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

    private readonly record struct Segment(int X0, int X1);

    private sealed class ActiveRectangle
    {
        public ActiveRectangle(int x0, int x1, int y0, int y1Exclusive)
        {
            X0 = x0;
            X1 = x1;
            Y0 = y0;
            Y1Exclusive = y1Exclusive;
        }

        public int X0 { get; }
        public int X1 { get; }
        public int Y0 { get; }
        public int Y1Exclusive { get; set; }
    }
}
