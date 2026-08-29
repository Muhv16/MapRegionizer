using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Spatial;

public interface IGridMapping
{
    GeoCoordinate GridToGeographic(GridCoordinate point);
    GridCoordinate GeographicToGrid(GeoCoordinate point);
}

public interface IGridTopology
{
    /// <summary>
    /// Resolves a displacement according to world edge semantics. A false
    /// result means that the requested direction is an open boundary; callers
    /// must not implement wrapping or pole handling themselves.
    /// </summary>
    bool TryResolve(GridPoint origin, int dx, int dy, out GridPoint result);
    IEnumerable<GridPoint> GetNeighbors4(GridPoint point);
    IEnumerable<GridPoint> GetNeighbors8(GridPoint point);
}

public interface IGridMetric
{
    double Distance(GridCoordinate a, GridCoordinate b);
}

public interface ISurfaceMetric
{
    double Distance(GeoCoordinate a, GeoCoordinate b);
}

public sealed class OpenRectangularTopology : IGridTopology
{
    public OpenRectangularTopology(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public bool TryResolve(GridPoint origin, int dx, int dy, out GridPoint result)
    {
        var x = origin.X + dx;
        var y = origin.Y + dy;
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            result = default;
            return false;
        }

        result = new GridPoint(x, y);
        return true;
    }

    public IEnumerable<GridPoint> GetNeighbors4(GridPoint point)
    {
        foreach (var (dx, dy) in TopologyDirections.Cardinal)
        {
            if (TryResolve(point, dx, dy, out var neighbor))
                yield return neighbor;
        }
    }

    public IEnumerable<GridPoint> GetNeighbors8(GridPoint point)
    {
        foreach (var (dx, dy) in TopologyDirections.Eight)
        {
            if (TryResolve(point, dx, dy, out var neighbor))
                yield return neighbor;
        }
    }
}

public sealed class CylindricalXTopology : IGridTopology
{
    public CylindricalXTopology(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public bool TryResolve(GridPoint origin, int dx, int dy, out GridPoint result)
    {
        var y = origin.Y + dy;
        if ((uint)y >= (uint)Height)
        {
            result = default;
            return false;
        }

        result = new GridPoint(NormalizeX(origin.X + dx, Width), y);
        return true;
    }

    public IEnumerable<GridPoint> GetNeighbors4(GridPoint point)
    {
        foreach (var (dx, dy) in TopologyDirections.Cardinal)
        {
            if (TryResolve(point, dx, dy, out var neighbor))
                yield return neighbor;
        }
    }

    public IEnumerable<GridPoint> GetNeighbors8(GridPoint point)
    {
        foreach (var (dx, dy) in TopologyDirections.Eight)
        {
            if (TryResolve(point, dx, dy, out var neighbor))
                yield return neighbor;
        }
    }

    public static int NormalizeX(int x, int width) => (x % width + width) % width;

    public static double WrappedDeltaX(double dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;
        return dx > 0 ? dx - width : dx + width;
    }

    public double WrappedDeltaX(double dx)
    {
        return WrappedDeltaX(dx, Width);
    }
}

public sealed class EquirectangularGridMapping : IGridMapping
{
    private readonly MapSpatialReference _reference;

    public EquirectangularGridMapping(MapSpatialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        _reference = reference;
    }

    public GeoCoordinate GridToGeographic(GridCoordinate point)
    {
        var u = point.X / _reference.GridWidth;
        var v = point.Y / _reference.GridHeight;
        return new GeoCoordinate(
            _reference.Longitude.StartLongitudeDegrees + u * _reference.Longitude.SpanDegrees,
            _reference.NorthLatitude - v * _reference.Coverage.LatitudeSpan);
    }

    public GridCoordinate GeographicToGrid(GeoCoordinate point)
    {
        var longitudeDelta = LongitudeInterval.NormalizePositive(point.LongitudeDegrees - _reference.Longitude.StartLongitudeDegrees);
        if (Math.Abs(point.LongitudeDegrees - _reference.Longitude.EndLongitudeDegrees) <= 1e-10 ||
            Math.Abs(longitudeDelta - _reference.Longitude.SpanDegrees) <= 1e-10)
            longitudeDelta = _reference.Longitude.SpanDegrees;

        return new GridCoordinate(
            longitudeDelta / _reference.Longitude.SpanDegrees * _reference.GridWidth,
            (_reference.NorthLatitude - point.LatitudeDegrees) / _reference.Coverage.LatitudeSpan * _reference.GridHeight);
    }
}

/// <summary>Mapping for projected Mercator grids; also useful to output geographic coordinates.</summary>
public sealed class WebMercatorGridMapping : IGridMapping
{
    public const double WebMercatorLatitudeLimit = 85.0511287798066;
    public const double ProjectedCellAspectTolerance = 1e-6;

    private readonly MapSpatialReference _reference;
    private readonly double _northMercator;
    private readonly double _southMercator;

    public WebMercatorGridMapping(MapSpatialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        _reference = reference;
        _northMercator = MercatorY(reference.NorthLatitude);
        _southMercator = MercatorY(reference.SouthLatitude);

        if (reference.PreserveProjectedCellAspectRatio)
            ValidateProjectedCellAspectRatio(reference);
    }

    public GeoCoordinate GridToGeographic(GridCoordinate point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point), "Grid coordinates must be finite.");

        var u = point.X / _reference.GridWidth;
        var v = point.Y / _reference.GridHeight;
        var longitude = _reference.Longitude.StartLongitudeDegrees + u * _reference.Longitude.SpanDegrees;
        var mercatorY = _northMercator + v * (_southMercator - _northMercator);
        return new GeoCoordinate(longitude, InverseMercatorY(mercatorY));
    }

    public GridCoordinate GeographicToGrid(GeoCoordinate point)
    {
        var longitudeDelta = LongitudeInterval.NormalizePositive(point.LongitudeDegrees - _reference.Longitude.StartLongitudeDegrees);
        if (Math.Abs(point.LongitudeDegrees - _reference.Longitude.EndLongitudeDegrees) <= 1e-10 ||
            Math.Abs(longitudeDelta - _reference.Longitude.SpanDegrees) <= 1e-10)
            longitudeDelta = _reference.Longitude.SpanDegrees;

        var mercator = MercatorY(point.LatitudeDegrees);
        var result = new GridCoordinate(
            longitudeDelta / _reference.Longitude.SpanDegrees * _reference.GridWidth,
            (mercator - _northMercator) / (_southMercator - _northMercator) * _reference.GridHeight);
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y))
            throw new InvalidOperationException("Geographic coordinates could not be mapped to a finite grid coordinate.");
        return result;
    }

    public static double MercatorY(double latitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees < -WebMercatorLatitudeLimit || latitudeDegrees > WebMercatorLatitudeLimit)
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), $"Mercator latitude must be within ±{WebMercatorLatitudeLimit:R} degrees.");

        var radians = latitudeDegrees * Math.PI / 180.0;
        return Math.Log(Math.Tan(Math.PI / 4 + radians / 2));
    }

    public static double InverseMercatorY(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Mercator Y must be finite.");

        var latitude = Math.Atan(Math.Sinh(value)) * 180.0 / Math.PI;
        if (!double.IsFinite(latitude))
            throw new InvalidOperationException("Inverse Mercator produced a non-finite latitude.");
        return latitude;
    }

    public static double ProjectedCellWidth(MapSpatialReference reference)
    {
        ValidateProjectedMetricReference(reference);
        var projectedWidth = WebMercator3857.EarthRadiusMeters * reference.Longitude.SpanDegrees * Math.PI / 180.0;
        return projectedWidth / reference.GridWidth;
    }

    public static double ProjectedCellHeight(MapSpatialReference reference)
    {
        ValidateProjectedMetricReference(reference);
        var projectedHeight = WebMercator3857.EarthRadiusMeters *
                              Math.Abs(MercatorY(reference.NorthLatitude) - MercatorY(reference.SouthLatitude));
        return projectedHeight / reference.GridHeight;
    }

    public static double ProjectedCellAspectRatio(MapSpatialReference reference) =>
        ProjectedCellWidth(reference) / ProjectedCellHeight(reference);

    private static void ValidateProjectedMetricReference(MapSpatialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        if (reference.GridMapping != GridMappingKind.WebMercator)
            throw new ArgumentException("Projected cell metrics require Web Mercator grid mapping.", nameof(reference));
    }

    private static void ValidateProjectedCellAspectRatio(MapSpatialReference reference)
    {
        var projectedCellWidth = ProjectedCellWidth(reference);
        var projectedCellHeight = ProjectedCellHeight(reference);
        var scale = Math.Max(projectedCellWidth, projectedCellHeight);
        if (!double.IsFinite(projectedCellWidth) || !double.IsFinite(projectedCellHeight) ||
            Math.Abs(projectedCellWidth - projectedCellHeight) > scale * ProjectedCellAspectTolerance)
        {
            throw new ArgumentException(
                $"Web Mercator projected cell dimensions must match when {nameof(MapSpatialReference.PreserveProjectedCellAspectRatio)} is enabled. " +
                $"Got {projectedCellWidth:R} m by {projectedCellHeight:R} m.",
                nameof(reference));
        }
    }
}

public sealed class GridSamplingMetric : IGridMetric
{
    public double Distance(GridCoordinate a, GridCoordinate b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class SphericalSurfaceMetric : ISurfaceMetric
{
    private readonly double _radius;

    public SphericalSurfaceMetric(double radius = 1.0)
    {
        if (!double.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        _radius = radius;
    }

    public double Distance(GeoCoordinate a, GeoCoordinate b)
    {
        var lat1 = a.LatitudeDegrees * Math.PI / 180.0;
        var lat2 = b.LatitudeDegrees * Math.PI / 180.0;
        var dLat = lat2 - lat1;
        var dLon = LongitudeInterval.Normalize(b.LongitudeDegrees - a.LongitudeDegrees) * Math.PI / 180.0;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return _radius * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
}

public sealed class PlanarSurfaceMetric : ISurfaceMetric
{
    public double Distance(GeoCoordinate a, GeoCoordinate b)
    {
        // A planar world has no implicit antimeridian seam. Consumers that
        // want a periodic surface use SphericalSurfaceMetric instead.
        var dx = a.LongitudeDegrees - b.LongitudeDegrees;
        var dy = a.LatitudeDegrees - b.LatitudeDegrees;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class MapSpatialContext
{
    private MapSpatialContext(
        MapSpatialReference spatialReference,
        IGridMapping gridMapping,
        IGridTopology gridTopology,
        IGridMetric gridMetric,
        ISurfaceMetric surfaceMetric)
    {
        SpatialReference = spatialReference;
        GridMapping = gridMapping;
        GridTopology = gridTopology;
        GridMetric = gridMetric;
        SurfaceMetric = surfaceMetric;
    }

    public MapSpatialReference SpatialReference { get; }
    public MapSpatialReference Reference => SpatialReference;
    public IGridMapping GridMapping { get; }
    public IGridTopology GridTopology { get; }
    public IGridMetric GridMetric { get; }
    public ISurfaceMetric SurfaceMetric { get; }

    public GeoCoordinate GridToGeographic(GridCoordinate point) => GridMapping.GridToGeographic(point);
    public GridCoordinate GeographicToGrid(GeoCoordinate point) => GridMapping.GeographicToGrid(point);

    public static MapSpatialContext Create(MapSpatialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();

        IGridMapping mapping = reference.GridMapping switch
        {
            GridMappingKind.Equirectangular => new EquirectangularGridMapping(reference),
            GridMappingKind.WebMercator => new WebMercatorGridMapping(reference),
            _ => throw new InvalidOperationException($"Unknown grid mapping: {reference.GridMapping}.")
        };
        IGridTopology topology = reference.Topology switch
        {
            GridTopologyKind.OpenRectangular => new OpenRectangularTopology(reference.GridWidth, reference.GridHeight),
            GridTopologyKind.CylindricalX => new CylindricalXTopology(reference.GridWidth, reference.GridHeight),
            _ => throw new InvalidOperationException($"Unknown grid topology: {reference.Topology}.")
        };
        var surfaceMetric = reference.WorldModel.Kind == WorldModelKind.Spherical
            ? new SphericalSurfaceMetric(reference.WorldModel.PlanetRadius ?? 1.0) as ISurfaceMetric
            : new PlanarSurfaceMetric();
        return new MapSpatialContext(reference, mapping, topology, new GridSamplingMetric(), surfaceMetric);
    }

    public static MapSpatialContext Create(int width, int height, MapSpatialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return Create(options.CreateReference(width, height));
    }
}

public static class GridTopologyMath
{
    public static IEnumerable<GridPoint> GetNeighbors4(IGridTopology topology, GridPoint point) => topology.GetNeighbors4(point);
    public static IEnumerable<GridPoint> GetNeighbors8(IGridTopology topology, GridPoint point) => topology.GetNeighbors8(point);

    public static int NormalizeX(IGridTopology topology, int x)
    {
        return topology is CylindricalXTopology cylindrical
            ? CylindricalXTopology.NormalizeX(x, cylindrical.Width)
            : x;
    }

    public static double WrappedDeltaX(IGridTopology topology, double dx)
    {
        return topology is CylindricalXTopology cylindrical ? cylindrical.WrappedDeltaX(dx) : dx;
    }

    public static bool TryResolveX(IGridTopology topology, int x, out GridPoint result) =>
        topology.TryResolve(new GridPoint(0, 0), x, 0, out result);

    /// <summary>
    /// Returns whether any cardinal direction is an open world boundary at
    /// the cell. This is intentionally derived from <see cref="IGridTopology.TryResolve"/>
    /// so a future pole topology can change edge semantics without changing
    /// generators that classify boundary-connected components.
    /// </summary>
    public static bool IsOpenBoundary(IGridTopology topology, GridPoint point)
    {
        ArgumentNullException.ThrowIfNull(topology);
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            if (!topology.TryResolve(point, dx, dy, out _))
                return true;
        }

        return false;
    }
}

internal static class TopologyDirections
{
    // Preserve the traversal order used by the legacy raster helpers. This is
    // observable in deterministic priority-queue tie breaks.
    public static readonly (int Dx, int Dy)[] Cardinal = [(-1, 0), (1, 0), (0, -1), (0, 1)];
    public static readonly (int Dx, int Dy)[] Eight =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1)
    ];
}
