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
    IReadOnlyList<MapRegion> Regions);

public sealed record MapBounds(double Width, double Height, double PixelSize);

public sealed record Landmass(LandmassId Id, Polygon Shape);

public sealed record WaterBody(WaterBodyId Id, Polygon Shape);

public sealed record MapRegion(RegionId Id, LandmassId LandmassId, Polygon Shape);

public readonly record struct LandmassId(int Value);

public readonly record struct WaterBodyId(int Value);

public readonly record struct RegionId(int Value);
