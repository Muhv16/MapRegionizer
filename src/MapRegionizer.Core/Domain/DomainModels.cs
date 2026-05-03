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

public sealed record TectonicPlateMap(
    int Width,
    int Height,
    IReadOnlyList<TectonicPlate> Plates,
    IReadOnlyList<PlateBoundary> Boundaries,
    IReadOnlyDictionary<GridPoint, TectonicPlateId> PlateByPoint,
    IReadOnlyDictionary<GridPoint, CrustKind> CrustByPoint);

public sealed record TectonicPlate(
    TectonicPlateId Id,
    TectonicPlateKind Kind,
    IReadOnlySet<GridPoint> Points,
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
