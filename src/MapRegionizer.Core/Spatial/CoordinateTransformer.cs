using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Spatial;

/// <summary>
/// Transforms canonical GridMapUnits geometry at the output boundary. It never
/// mutates the source geometry or any generation data.
/// </summary>
public interface ICoordinateTransformer
{
    MapPoint Transform(MapPoint canonicalPoint);
    Geometry Transform(Geometry canonicalGeometry);
}

public sealed class MapCoordinateTransformer : ICoordinateTransformer
{
    private const double LongitudeEpsilon = 1e-10;
    private const double GeometryEpsilon = 1e-9;

    private readonly MapSpatialContext _spatialContext;
    private readonly MapOutputOptions _options;

    public MapCoordinateTransformer(MapSpatialContext spatialContext, MapOutputOptions? options = null)
    {
        _spatialContext = spatialContext ?? throw new ArgumentNullException(nameof(spatialContext));
        _options = options ?? new MapOutputOptions();
        _options.Validate();
    }

    public MapCoordinateTransformer(MapSpatialReference spatialReference, MapOutputOptions? options = null)
        : this(MapSpatialContext.Create(spatialReference), options)
    {
    }

    public MapSpatialReference Reference => _spatialContext.SpatialReference;
    public MapOutputOptions Options => _options;

    public MapPoint Transform(MapPoint canonicalPoint)
    {
        if (_options.CoordinateSystem == OutputCoordinateSystem.GridMapUnits)
            return canonicalPoint;

        var geographic = ToGeographic(canonicalPoint);
        var transformed = _options.CoordinateSystem switch
        {
            OutputCoordinateSystem.GeographicLongitudeLatitude =>
                new MapPoint(geographic.LongitudeDegrees, geographic.LatitudeDegrees),
            OutputCoordinateSystem.WebMercator or OutputCoordinateSystem.WebMercator3857 =>
                WebMercator3857.Forward(geographic, _options.LatitudeOverflowPolicy),
            _ => throw new InvalidOperationException($"Unknown output coordinate system: {_options.CoordinateSystem}.")
        };
        // A point has no segment that can be seam-cut later. Auto/Split
        // therefore normalize point-like output immediately; path output
        // remains unwrapped until SplitLineString/SplitPolygon runs.
        return ShouldSplitAntimeridian
            ? NormalizePointToWorldCopy(transformed)
            : transformed;
    }

    /// <summary>
    /// Transforms an NTS geometry by cloning every coordinate. Web Mercator
    /// paths are adaptively densified before projection and, by default, cut at
    /// the antimeridian on the output copy.
    /// </summary>
    public Geometry Transform(Geometry canonicalGeometry)
    {
        ArgumentNullException.ThrowIfNull(canonicalGeometry);
        return canonicalGeometry switch
        {
            Point point when point.IsEmpty => canonicalGeometry.Factory.CreatePoint(),
            Point point => canonicalGeometry.Factory.CreatePoint(TransformCoordinate(point.Coordinate)),
            LineString line => TransformLineString(line),
            Polygon polygon => TransformPolygon(polygon),
            MultiPoint multiPoint => TransformMultiPoint(multiPoint),
            MultiLineString multiLine => TransformMultiLineString(multiLine),
            MultiPolygon multiPolygon => TransformMultiPolygon(multiPolygon),
            GeometryCollection collection => canonicalGeometry.Factory.CreateGeometryCollection(
                collection.Geometries.Select(Transform).ToArray()),
            _ => throw new NotSupportedException($"Unsupported geometry type: {canonicalGeometry.GeometryType}.")
        };
    }

    /// <summary>Compatibility alias retained for callers using the old name.</summary>
    public Geometry TransformGeometry(Geometry canonicalGeometry) => Transform(canonicalGeometry);

    private Geometry TransformLineString(LineString line)
    {
        if (line.IsEmpty)
            return line.Factory.CreateLineString();

        if (_options.CoordinateSystem == OutputCoordinateSystem.GridMapUnits)
            return line.Factory.CreateLineString(CloneCoordinates(line.Coordinates));

        var coordinates = TransformPath(line.Coordinates);
        var transformed = line.Factory.CreateLineString(coordinates);
        return ShouldSplitAntimeridian
            ? SplitLineString(transformed)
            : transformed;
    }

    private Geometry TransformMultiLineString(MultiLineString multiLine)
    {
        var lines = new List<LineString>();
        foreach (var line in multiLine.Geometries.Cast<LineString>())
        {
            var transformed = TransformLineString(line);
            AddLineStrings(transformed, lines);
        }

        return multiLine.Factory.CreateMultiLineString(lines.ToArray());
    }

    private Geometry TransformMultiPoint(MultiPoint multiPoint)
    {
        var points = multiPoint.Geometries.Cast<Point>()
            .Select(point => point.IsEmpty
                ? multiPoint.Factory.CreatePoint()
                : multiPoint.Factory.CreatePoint(TransformCoordinate(point.Coordinate)))
            .ToArray();
        return multiPoint.Factory.CreateMultiPoint(points);
    }

    private Geometry TransformPolygon(Polygon polygon)
    {
        if (polygon.IsEmpty)
            return polygon.Factory.CreatePolygon();

        if (_options.CoordinateSystem == OutputCoordinateSystem.GridMapUnits)
        {
            var factory = polygon.Factory;
            var shell = factory.CreateLinearRing(CloneCoordinates(polygon.ExteriorRing.Coordinates));
            var holes = polygon.InteriorRings
                .Select(ring => factory.CreateLinearRing(CloneCoordinates(ring.Coordinates)))
                .ToArray();
            return factory.CreatePolygon(shell, holes);
        }

        var outputFactory = polygon.Factory;
        var outputShell = outputFactory.CreateLinearRing(TransformPath(polygon.ExteriorRing.Coordinates, closed: true));
        var outputHoles = polygon.InteriorRings
            .Select(ring => outputFactory.CreateLinearRing(TransformPath(ring.Coordinates, closed: true)))
            .ToArray();
        var transformed = outputFactory.CreatePolygon(outputShell, outputHoles);
        return ShouldSplitAntimeridian
            ? SplitPolygon(transformed)
            : transformed;
    }

    private Geometry TransformMultiPolygon(MultiPolygon multiPolygon)
    {
        var polygons = new List<Polygon>();
        foreach (var polygon in multiPolygon.Geometries.Cast<Polygon>())
            AddPolygons(TransformPolygon(polygon), polygons);

        return multiPolygon.Factory.CreateMultiPolygon(polygons.ToArray());
    }

    private Coordinate TransformCoordinate(Coordinate coordinate)
    {
        var point = Transform(new MapPoint(coordinate.X, coordinate.Y));
        return new Coordinate(point.X, point.Y);
    }

    private Coordinate[] TransformPath(IReadOnlyList<Coordinate> canonicalCoordinates, bool closed = false)
    {
        if (canonicalCoordinates.Count == 0)
            return [];

        if (_options.CoordinateSystem == OutputCoordinateSystem.GridMapUnits)
            return CloneCoordinates(canonicalCoordinates);

        var path = BuildGeographicPath(canonicalCoordinates, closed);
        if (_options.CoordinateSystem == OutputCoordinateSystem.GeographicLongitudeLatitude)
            return path.Select(sample => new Coordinate(sample.Geographic.Longitude, sample.Geographic.Latitude)).ToArray();

        var projected = new List<Coordinate>(path.Count);
        for (var i = 0; i < path.Count - 1; i++)
        {
            var start = path[i];
            var end = path[i + 1];
            if (i == 0)
                projected.Add(Project(start.Geographic));
            AppendProjectedSegment(start, end, 0, projected);
        }

        if (path.Count == 1)
            projected.Add(Project(path[0].Geographic));
        else if (closed && projected.Count > 1)
            projected[^1] = projected[0];

        return projected.ToArray();
    }

    private List<PathSample> BuildGeographicPath(IReadOnlyList<Coordinate> canonicalCoordinates, bool closed)
    {
        var path = new List<PathSample>(canonicalCoordinates.Count);
        var firstCanonical = new MapPoint(canonicalCoordinates[0].X, canonicalCoordinates[0].Y);
        var first = ToGeographic(firstCanonical);
        var firstSample = new PathSample(
            firstCanonical,
            new GeoSample(first.LongitudeDegrees, first.LatitudeDegrees));
        path.Add(firstSample);

        for (var i = 1; i < canonicalCoordinates.Count; i++)
        {
            var rawCanonical = new MapPoint(canonicalCoordinates[i].X, canonicalCoordinates[i].Y);
            var raw = ToGeographic(rawCanonical);
            var previous = path[^1].Geographic;
            var longitude = UnwrapLongitude(raw.LongitudeDegrees, previous.Longitude);
            var canonical = AlignCanonicalToPath(path[^1].Canonical, rawCanonical, previous.Longitude, longitude);
            path.Add(new PathSample(canonical, new GeoSample(longitude, raw.LatitudeDegrees)));
        }

        if (closed && path.Count > 1)
            path[^1] = path[0];

        return path;
    }

    private void AppendProjectedSegment(PathSample start, PathSample end, int depth, List<Coordinate> output)
    {
        var startProjected = Project(start.Geographic);
        var endProjected = Project(end.Geographic);
        var length = Distance(startProjected, endProjected);
        if (!_options.EnableAdaptiveDensification || length <= _options.MinDensificationSegmentLength)
        {
            output.Add(endProjected);
            return;
        }

        // Subdivide in the canonical generation space. Averaging geographic
        // latitudes is incorrect for a Web Mercator generation grid because
        // its canonical Y is already linear in projected Mercator Y.
        var midpointCanonical = InterpolateCanonical(start.Canonical, end.Canonical);
        var midpointGeographic = ToGeographic(midpointCanonical);
        var midpoint = new PathSample(
            midpointCanonical,
            new GeoSample(
                UnwrapLongitude(midpointGeographic.LongitudeDegrees, start.Geographic.Longitude),
                midpointGeographic.LatitudeDegrees));
        var midpointProjected = Project(midpoint.Geographic);
        var firstQuarterCanonical = InterpolateCanonical(start.Canonical, midpoint.Canonical);
        var firstQuarterGeographic = ToGeographic(firstQuarterCanonical);
        var firstQuarter = new PathSample(
            firstQuarterCanonical,
            new GeoSample(
                UnwrapLongitude(firstQuarterGeographic.LongitudeDegrees, start.Geographic.Longitude),
                firstQuarterGeographic.LatitudeDegrees));
        var firstQuarterProjected = Project(firstQuarter.Geographic);

        var lastQuarterCanonical = InterpolateCanonical(midpoint.Canonical, end.Canonical);
        var lastQuarterGeographic = ToGeographic(lastQuarterCanonical);
        var lastQuarter = new PathSample(
            lastQuarterCanonical,
            new GeoSample(
                UnwrapLongitude(lastQuarterGeographic.LongitudeDegrees, midpoint.Geographic.Longitude),
                lastQuarterGeographic.LatitudeDegrees));
        var lastQuarterProjected = Project(lastQuarter.Geographic);

        var midpointError = Distance(
            midpointProjected,
            InterpolateProjected(startProjected, endProjected, .5));
        var firstQuarterError = Distance(
            firstQuarterProjected,
            InterpolateProjected(startProjected, endProjected, .25));
        var lastQuarterError = Distance(
            lastQuarterProjected,
            InterpolateProjected(startProjected, endProjected, .75));
        var error = Math.Max(midpointError, Math.Max(firstQuarterError, lastQuarterError));
        if (!double.IsFinite(error))
            throw new InvalidOperationException("Projected geometry densification produced a non-finite deviation.");

        if (error <= _options.ProjectionErrorTolerance)
        {
            output.Add(endProjected);
            return;
        }

        if (depth >= _options.MaxDensificationDepth)
            throw new InvalidOperationException(
                $"Projected geometry exceeded the configured error tolerance of {_options.ProjectionErrorTolerance:R} " +
                $"after reaching maximum densification depth {_options.MaxDensificationDepth}.");

        AppendProjectedSegment(start, midpoint, depth + 1, output);
        AppendProjectedSegment(midpoint, end, depth + 1, output);
    }

    private MapPoint InterpolateCanonical(MapPoint start, MapPoint end)
    {
        // BuildGeographicPath aligns every cylindrical endpoint to the
        // unwrapped branch in GridMapUnits before this method is called. Keep
        // the interpolation in that map-unit space; re-normalizing recursive
        // midpoints would lose the intended direction at a full-world edge.
        return new MapPoint(
            start.X + (end.X - start.X) * .5,
            start.Y + (end.Y - start.Y) * .5);
    }

    private MapPoint AlignCanonicalToPath(
        MapPoint previousCanonical,
        MapPoint rawCanonical,
        double previousLongitude,
        double unwrappedLongitude)
    {
        if (_spatialContext.GridTopology is not CylindricalXTopology ||
            Reference.Longitude.SpanDegrees <= LongitudeEpsilon)
            return rawCanonical;

        var widthInMapUnits = Reference.WidthInMapUnits;
        var desiredX = previousCanonical.X +
                       (unwrappedLongitude - previousLongitude) /
                       Reference.Longitude.SpanDegrees * widthInMapUnits;
        var worldTurns = Math.Round((desiredX - rawCanonical.X) / widthInMapUnits);
        return new MapPoint(
            rawCanonical.X + worldTurns * widthInMapUnits,
            rawCanonical.Y);
    }

    private static Coordinate InterpolateProjected(Coordinate start, Coordinate end, double parameter) =>
        new(
            start.X + (end.X - start.X) * parameter,
            start.Y + (end.Y - start.Y) * parameter);

    private Coordinate Project(GeoSample sample)
    {
        var projected = WebMercator3857.Forward(
            new GeoCoordinate(sample.Longitude, sample.Latitude),
            _options.LatitudeOverflowPolicy);
        EnsureFinite(projected);
        return new Coordinate(projected.X, projected.Y);
    }

    private GeoCoordinate ToGeographic(MapPoint canonicalPoint)
    {
        if (!double.IsFinite(canonicalPoint.X) || !double.IsFinite(canonicalPoint.Y))
            throw new ArgumentOutOfRangeException(nameof(canonicalPoint), "Canonical coordinates must be finite.");

        var unitsPerCell = Reference.UnitsPerCell;
        var gridPoint = new GridCoordinate(canonicalPoint.X / unitsPerCell, canonicalPoint.Y / unitsPerCell);
        var geographic = _spatialContext.GridToGeographic(gridPoint);
        if (!double.IsFinite(geographic.LongitudeDegrees) || !double.IsFinite(geographic.LatitudeDegrees))
            throw new InvalidOperationException("Spatial mapping produced a non-finite geographic coordinate.");
        return geographic;
    }

    private double UnwrapLongitude(double longitude, double previous)
    {
        var delta = longitude - previous;
        // An open regional interval wider than 180 degrees is directed data,
        // not a cylindrical seam. Preserve its long edge so the explicit
        // Split policy can cut it into world copies instead of silently
        // replacing it with the opposite short arc. Cylindrical generation
        // keeps the historical shortest seam behavior.
        var preserveDirectedRegionalEdge =
            Reference.Topology != GridTopologyKind.CylindricalX &&
            Reference.Longitude.SpanDegrees > 180.0 + LongitudeEpsilon &&
            Reference.Longitude.Contains(previous) &&
            Reference.Longitude.Contains(longitude) &&
            Math.Abs(delta) > 180.0 + LongitudeEpsilon &&
            Math.Abs(delta) < 360.0 - LongitudeEpsilon;
        if (preserveDirectedRegionalEdge)
            return longitude;

        // Preserve an explicitly directed full-world edge (for example
        // -180 -> 180), while making ordinary seam crossings take the short
        // path. Regional intervals wider than 180 degrees remain directed by
        // the grid mapping and are split only at the output boundary.
        if (Math.Abs(delta) > 180.0 + LongitudeEpsilon && Math.Abs(Math.Abs(delta) - 360.0) > LongitudeEpsilon)
        {
            while (delta > 180.0)
                delta -= 360.0;
            while (delta < -180.0)
                delta += 360.0;
            longitude = previous + delta;
        }

        return longitude;
    }

    private bool ShouldSplitAntimeridian => _options.CoordinateSystem switch
    {
        OutputCoordinateSystem.WebMercator or OutputCoordinateSystem.WebMercator3857 =>
            _options.AntimeridianPolicy is AntimeridianOutputPolicy.Auto or AntimeridianOutputPolicy.Split,
        OutputCoordinateSystem.GeographicLongitudeLatitude => _options.AntimeridianPolicy == AntimeridianOutputPolicy.Split,
        _ => false
    };

    private Geometry SplitLineString(LineString line)
    {
        var world = GetWorldAxis();
        var pieces = new List<LineString>();
        var current = new List<Coordinate>();
        var coordinates = line.Coordinates;
        if (coordinates.Length == 0)
            return line.Factory.CreateLineString(CloneCoordinates(coordinates));

        var strip = WorldIndex(coordinates[0].X, world.Min, world.Width);
        current.Add(NormalizeCoordinate(coordinates[0], strip, world.Width));
        for (var i = 1; i < coordinates.Length; i++)
        {
            var start = coordinates[i - 1];
            var end = coordinates[i];
            var endStrip = WorldIndex(end.X, world.Min, world.Width);
            while (endStrip != strip)
            {
                var direction = endStrip > strip ? 1 : -1;
                var boundary = direction > 0
                    ? world.Min + (strip + 1) * world.Width
                    : world.Min + strip * world.Width;
                var denominator = end.X - start.X;
                var t = Math.Abs(denominator) <= GeometryEpsilon ? 0.5 : (boundary - start.X) / denominator;
                t = Math.Clamp(t, 0.0, 1.0);
                var crossing = new Coordinate(boundary, start.Y + (end.Y - start.Y) * t);
                current.Add(NormalizeCoordinate(crossing, strip, world.Width));
                AddLinePart(line.Factory, current, pieces);
                strip += direction;
                current = [NormalizeCoordinate(crossing, strip, world.Width)];
            }

            current.Add(NormalizeCoordinate(end, strip, world.Width));
        }

        AddLinePart(line.Factory, current, pieces);
        if (pieces.Count == 1)
            return pieces[0];
        return line.Factory.CreateMultiLineString(pieces.ToArray());
    }

    private Geometry SplitPolygon(Polygon polygon)
    {
        var world = GetWorldAxis();
        var envelope = polygon.EnvelopeInternal;
        var firstStrip = WorldIndex(envelope.MinX, world.Min, world.Width);
        var lastStrip = WorldIndex(envelope.MaxX, world.Min, world.Width);
        var pieces = new List<Polygon>();
        var clipMinY = _options.CoordinateSystem == OutputCoordinateSystem.GeographicLongitudeLatitude ? -90.0 : -WebMercator3857.HalfWorldMeters;
        var clipMaxY = _options.CoordinateSystem == OutputCoordinateSystem.GeographicLongitudeLatitude ? 90.0 : WebMercator3857.HalfWorldMeters;

        for (var strip = firstStrip; strip <= lastStrip; strip++)
        {
            var minX = world.Min + strip * world.Width;
            var maxX = minX + world.Width;
            var clippingEnvelope = new Envelope(minX, maxX, clipMinY, clipMaxY);
            var clipped = polygon.Intersection(polygon.Factory.ToGeometry(clippingEnvelope));
            foreach (var clippedPolygon in EnumeratePolygons(clipped))
            {
                if (clippedPolygon.IsEmpty || clippedPolygon.Area <= GeometryEpsilon)
                    continue;

                var normalized = TranslatePolygon(clippedPolygon, -strip * world.Width);
                EnsureFinite(normalized);
                pieces.Add(normalized);
            }
        }

        if (pieces.Count == 0)
            return polygon.Factory.CreatePolygon();
        if (pieces.Count == 1)
            return pieces[0];
        return polygon.Factory.CreateMultiPolygon(pieces.ToArray());
    }

    private Polygon TranslatePolygon(Polygon polygon, double offsetX)
    {
        var factory = polygon.Factory;
        var shell = factory.CreateLinearRing(TranslateCoordinates(polygon.ExteriorRing.Coordinates, offsetX));
        var holes = polygon.InteriorRings
            .Select(ring => factory.CreateLinearRing(TranslateCoordinates(ring.Coordinates, offsetX)))
            .ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private static Coordinate[] TranslateCoordinates(IEnumerable<Coordinate> coordinates, double offsetX) =>
        coordinates.Select(coordinate => new Coordinate(coordinate.X + offsetX, coordinate.Y)).ToArray();

    private static Coordinate[] CloneCoordinates(IEnumerable<Coordinate> coordinates) =>
        coordinates.Select(coordinate => new Coordinate(coordinate.X, coordinate.Y)).ToArray();

    private static void AddLineStrings(Geometry geometry, ICollection<LineString> destination)
    {
        switch (geometry)
        {
            case LineString line when line.NumPoints >= 2:
                destination.Add(line);
                break;
            case GeometryCollection collection:
                foreach (var child in collection.Geometries)
                    AddLineStrings(child, destination);
                break;
        }
    }

    private static void AddPolygons(Geometry geometry, ICollection<Polygon> destination)
    {
        switch (geometry)
        {
            case Polygon polygon when !polygon.IsEmpty:
                destination.Add(polygon);
                break;
            case GeometryCollection collection:
                foreach (var child in collection.Geometries)
                    AddPolygons(child, destination);
                break;
        }
    }

    private static IEnumerable<Polygon> EnumeratePolygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        if (geometry is not GeometryCollection collection)
            yield break;

        foreach (var child in collection.Geometries)
        {
            foreach (var childPolygon in EnumeratePolygons(child))
                yield return childPolygon;
        }
    }

    private (double Min, double Width) GetWorldAxis() => _options.CoordinateSystem switch
    {
        OutputCoordinateSystem.GeographicLongitudeLatitude => (-180.0, 360.0),
        OutputCoordinateSystem.WebMercator or OutputCoordinateSystem.WebMercator3857 =>
            (-WebMercator3857.HalfWorldMeters, WebMercator3857.WorldWidthMeters),
        _ => throw new InvalidOperationException("Antimeridian splitting requires a geographic or Web Mercator output.")
    };

    private MapPoint NormalizePointToWorldCopy(MapPoint point)
    {
        var world = GetWorldAxis();
        var strip = WorldIndex(point.X, world.Min, world.Width);
        return new MapPoint(point.X - strip * world.Width, point.Y);
    }

    private static int WorldIndex(double value, double min, double width)
    {
        var index = (int)Math.Floor((value - min) / width);
        var boundary = min + index * width;
        if (index > 0 && Math.Abs(value - boundary) <= GeometryEpsilon)
            index--;
        return index;
    }

    private static Coordinate NormalizeCoordinate(Coordinate coordinate, int strip, double width) =>
        new(coordinate.X - strip * width, coordinate.Y);

    private static void AddLinePart(GeometryFactory factory, List<Coordinate> coordinates, ICollection<LineString> destination)
    {
        if (coordinates.Count < 2)
            return;

        var copy = CloneCoordinates(coordinates);
        if (Distance(copy[0], copy[^1]) <= GeometryEpsilon)
            return;
        destination.Add(factory.CreateLineString(copy));
    }

    private static double Distance(Coordinate first, Coordinate second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void EnsureFinite(MapPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new InvalidOperationException("Output geometry contains a non-finite coordinate.");
    }

    private static void EnsureFinite(Geometry geometry)
    {
        foreach (var coordinate in geometry.Coordinates)
        {
            if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y))
                throw new InvalidOperationException("Output geometry contains a non-finite coordinate.");
        }
    }

    private readonly record struct GeoSample(double Longitude, double Latitude);
    private readonly record struct PathSample(MapPoint Canonical, GeoSample Geographic);
}

/// <summary>Compatibility-friendly name for consumers that call this an output transformer.</summary>
public sealed class OutputCoordinateTransformer : ICoordinateTransformer
{
    private readonly MapCoordinateTransformer _inner;

    public OutputCoordinateTransformer(MapSpatialContext spatialContext, MapOutputOptions? options = null) =>
        _inner = new MapCoordinateTransformer(spatialContext, options);

    public OutputCoordinateTransformer(MapSpatialReference spatialReference, MapOutputOptions? options = null) =>
        _inner = new MapCoordinateTransformer(spatialReference, options);

    public MapSpatialReference Reference => _inner.Reference;
    public MapOutputOptions Options => _inner.Options;

    public MapPoint Transform(MapPoint canonicalPoint) => _inner.Transform(canonicalPoint);
    public Geometry Transform(Geometry canonicalGeometry) => _inner.Transform(canonicalGeometry);
    public Geometry TransformGeometry(Geometry canonicalGeometry) => _inner.Transform(canonicalGeometry);
}
