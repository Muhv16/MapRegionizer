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
        return _options.CoordinateSystem switch
        {
            OutputCoordinateSystem.GridMapUnits => canonicalPoint,
            OutputCoordinateSystem.GeographicLongitudeLatitude => TransformToGeographic(canonicalPoint),
            OutputCoordinateSystem.WebMercator => throw new NotSupportedException(
                "Web Mercator output is reserved for the projection milestone; use GeographicLongitudeLatitude in Milestone 1."),
            _ => throw new InvalidOperationException($"Unknown output coordinate system: {_options.CoordinateSystem}.")
        };
    }

    /// <summary>Transforms an NTS geometry by cloning every coordinate.</summary>
    public Geometry Transform(Geometry canonicalGeometry)
    {
        ArgumentNullException.ThrowIfNull(canonicalGeometry);
        return canonicalGeometry switch
        {
            Point point => canonicalGeometry.Factory.CreatePoint(TransformCoordinate(point.Coordinate)),
            LineString line => canonicalGeometry.Factory.CreateLineString(TransformCoordinates(line.Coordinates)),
            Polygon polygon => TransformPolygon(polygon),
            MultiPoint multiPoint => canonicalGeometry.Factory.CreateMultiPoint(
                multiPoint.Geometries.Cast<Point>().Select(point => canonicalGeometry.Factory.CreatePoint(TransformCoordinate(point.Coordinate))).ToArray()),
            MultiLineString multiLine => canonicalGeometry.Factory.CreateMultiLineString(
                multiLine.Geometries.Cast<LineString>().Select(line => (LineString)Transform(line)).ToArray()),
            MultiPolygon multiPolygon => canonicalGeometry.Factory.CreateMultiPolygon(
                multiPolygon.Geometries.Cast<Polygon>().Select(polygon => (Polygon)Transform(polygon)).ToArray()),
            GeometryCollection collection => canonicalGeometry.Factory.CreateGeometryCollection(
                collection.Geometries.Select(Transform).ToArray()),
            _ => throw new NotSupportedException($"Unsupported geometry type: {canonicalGeometry.GeometryType}.")
        };
    }

    public Geometry TransformGeometry(Geometry canonicalGeometry) => Transform(canonicalGeometry);

    private MapPoint TransformToGeographic(MapPoint canonicalPoint)
    {
        var unitsPerCell = _spatialContext.SpatialReference.UnitsPerCell;
        var gridPoint = new GridCoordinate(canonicalPoint.X / unitsPerCell, canonicalPoint.Y / unitsPerCell);
        var geographic = _spatialContext.GridToGeographic(gridPoint);
        return new MapPoint(geographic.LongitudeDegrees, geographic.LatitudeDegrees);
    }

    private Coordinate TransformCoordinate(Coordinate canonicalCoordinate)
    {
        var point = Transform(new MapPoint(canonicalCoordinate.X, canonicalCoordinate.Y));
        return new Coordinate(point.X, point.Y);
    }

    private Coordinate[] TransformCoordinates(IEnumerable<Coordinate> coordinates) =>
        coordinates.Select(TransformCoordinate).ToArray();

    private Polygon TransformPolygon(Polygon polygon)
    {
        var factory = polygon.Factory;
        var shell = factory.CreateLinearRing(TransformCoordinates(polygon.ExteriorRing.Coordinates));
        var holes = polygon.InteriorRings
            .Select(ring => factory.CreateLinearRing(TransformCoordinates(ring.Coordinates)))
            .ToArray();
        return factory.CreatePolygon(shell, holes);
    }
}

/// <summary>Compatibility-friendly name for consumers that call this an output transformer.</summary>
public sealed class OutputCoordinateTransformer : ICoordinateTransformer
{
    private readonly MapCoordinateTransformer _inner;

    public OutputCoordinateTransformer(MapSpatialContext spatialContext, MapOutputOptions? options = null) =>
        _inner = new MapCoordinateTransformer(spatialContext, options);

    public MapPoint Transform(MapPoint canonicalPoint) => _inner.Transform(canonicalPoint);
    public Geometry Transform(Geometry canonicalGeometry) => _inner.Transform(canonicalGeometry);
    public Geometry TransformGeometry(Geometry canonicalGeometry) => _inner.Transform(canonicalGeometry);
}
