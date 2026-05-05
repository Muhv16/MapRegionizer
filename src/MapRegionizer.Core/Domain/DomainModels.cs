using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Domain;

public sealed record MapMask(int Width, int Height, IReadOnlySet<GridPoint> LandPoints)
{
    public bool IsLand(GridPoint point) => LandPoints.Contains(point);
}

public readonly record struct GridPoint(int X, int Y);

public sealed record GeneratedMap(
    MapBounds Bounds,
    IReadOnlyList<Landmass> Landmasses,
    IReadOnlyList<WaterBody> WaterBodies,
    IReadOnlyList<MapRegion> Regions,
    TectonicPlateMap? TectonicPlates = null);

public sealed record MapBounds(double Width, double Height, double PixelSize);

public sealed record Landmass(LandmassId Id, Polygon Shape);

public sealed record WaterBody(WaterBodyId Id, Polygon Shape);

public sealed record MapRegion(RegionId Id, LandmassId LandmassId, Polygon Shape);

public readonly record struct LandmassId(int Value);

public readonly record struct WaterBodyId(int Value);

public readonly record struct RegionId(int Value);

public sealed class TectonicPlateRaster
{
    private readonly short[] _plates;
    private readonly byte[] _crust;

    public int Width { get; }
    public int Height { get; }

    public TectonicPlateRaster(int width, int height, short[] plates, byte[] crust)
    {
        if (plates.Length != width * height)
            throw new ArgumentException($"Plates array length must be {width * height}", nameof(plates));
        if (crust.Length != width * height)
            throw new ArgumentException($"Crust array length must be {width * height}", nameof(crust));

        Width = width;
        Height = height;
        _plates = plates;
        _crust = crust;
    }

    public TectonicPlateId GetPlate(int x, int y)
    {
        var index = y * Width + x;
        return new TectonicPlateId(_plates[index]);
    }

    public TectonicPlateId GetPlate(GridPoint point) => GetPlate(point.X, point.Y);

    public CrustKind GetCrust(int x, int y)
    {
        var index = y * Width + x;
        return (CrustKind)_crust[index];
    }

    public CrustKind GetCrust(GridPoint point) => GetCrust(point.X, point.Y);

    internal ReadOnlySpan<short> PlatesSpan => _plates;
    internal ReadOnlySpan<byte> CrustSpan => _crust;
}

public sealed record TectonicPlateMap(
    int Width,
    int Height,
    IReadOnlyList<TectonicPlate> Plates,
    IReadOnlyList<PlateBoundary> Boundaries,
    TectonicPlateRaster Raster);

public sealed record TectonicPlate(
    TectonicPlateId Id,
    TectonicPlateKind Kind,
    int PointCount,
    GridPoint Centroid,
    GridVector Motion,
    double Activity,
    double Density,
    double Thickness);

public sealed record PlateBoundary(
    TectonicPlateId PlateA,
    TectonicPlateId PlateB,
    IReadOnlyList<GridPoint> Points,
    PlateBoundaryKind Kind,
    double Convergence,
    double Divergence,
    double Shear,
    TectonicPlateId? SubductingPlate);

public readonly record struct TectonicPlateId(int Value);

public readonly record struct GridVector(double X, double Y);

public enum CrustKind
{
    Continental,
    Oceanic
}

public enum TectonicPlateKind
{
    Continental,
    Oceanic,
    Mixed
}

public enum PlateBoundaryKind
{
    Convergent,
    Divergent,
    Transform,
    Passive
}
