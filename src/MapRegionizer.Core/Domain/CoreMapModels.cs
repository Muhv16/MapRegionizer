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
    TectonicPlateMap? TectonicPlates = null,
    ElevationMap? Elevation = null,
    WaterBodyTopology? WaterBodyTopology = null,
    WaterSurfaceMap? WaterSurfaces = null,
    HydrologyMap? Hydrology = null,
    ClimateMap? Climate = null,
    RegionRaster? RegionRaster = null,
    MapSpatialReference? SpatialReference = null);

/// <summary>
/// Map extents in canonical map units. The constructor keeps the historical
/// <c>PixelSize</c> parameter name so named-argument callers remain source
/// compatible; the canonical property is <see cref="UnitsPerCell"/>.
/// </summary>
public sealed record MapBounds
{
    public MapBounds(double Width, double Height, double PixelSize)
    {
        this.Width = Width;
        this.Height = Height;
        UnitsPerCell = PixelSize;
    }

    public double Width { get; init; }
    public double Height { get; init; }
    public double UnitsPerCell { get; init; }

    [Obsolete("Use UnitsPerCell. This alias is retained for compatibility.")]
    public double PixelSize
    {
        get => UnitsPerCell;
        init => UnitsPerCell = value;
    }

    public void Deconstruct(out double width, out double height, out double unitsPerCell)
    {
        width = Width;
        height = Height;
        unitsPerCell = UnitsPerCell;
    }
}

public readonly record struct MapPoint(double X, double Y);

public sealed record MapRegion(RegionId Id, LandmassId LandmassId, Polygon Shape);

public sealed class RegionRaster
{
    private readonly int[] _regionIds;

    public RegionRaster(int width, int height, int[] regionIds)
    {
        var expectedLength = width * height;
        if (regionIds.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", nameof(regionIds));

        Width = width;
        Height = height;
        _regionIds = regionIds;
    }

    public int Width { get; }
    public int Height { get; }

    public int GetRegionId(int x, int y) => _regionIds[y * Width + x];

    public int GetRegionId(GridPoint point) => GetRegionId(point.X, point.Y);

    public ReadOnlySpan<int> RegionIdsSpan => _regionIds;
}

public readonly record struct LandmassId(int Value);

public readonly record struct WaterBodyId(int Value);

public readonly record struct RegionId(int Value);
