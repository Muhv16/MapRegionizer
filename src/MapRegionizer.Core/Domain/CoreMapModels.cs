using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Domain;

public sealed record MapMask
{
    public MapMask(int Width, int Height, IReadOnlySet<GridPoint> LandPoints)
        : this(GridWindow.FromSize(Width, Height), LandPoints)
    {
    }

    /// <summary>
    /// Creates a mask for a world-aligned window.  Land points are local cell
    /// coordinates in the window (the same convention as the legacy mask).
    /// </summary>
    public MapMask(GridWindow Window, IReadOnlySet<GridPoint> LandPoints)
    {
        ArgumentNullException.ThrowIfNull(LandPoints);

        this.Window = Window;
        Width = Window.Width;
        Height = Window.Height;
        this.LandPoints = LandPoints;
        if (LandPoints.Any(point => point.X < 0 || point.X >= Width || point.Y < 0 || point.Y >= Height))
            throw new ArgumentException("Land points must use local coordinates inside the mask window.", nameof(LandPoints));
    }

    public int Width { get; init; }
    public int Height { get; init; }
    public GridWindow Window { get; }
    public int OriginX => Window.X;
    public int OriginY => Window.Y;
    public IReadOnlySet<GridPoint> LandPoints { get; init; }

    public bool IsLand(GridPoint point) => LandPoints.Contains(point);

    public void Deconstruct(out int width, out int height, out IReadOnlySet<GridPoint> landPoints)
    {
        width = Width;
        height = Height;
        landPoints = LandPoints;
    }

    public MapMask Crop(GridWindow requestedWindow)
    {
        if (!Window.Contains(requestedWindow))
            throw new ArgumentException("Requested window must be contained by the mask window.", nameof(requestedWindow));

        var offsetX = requestedWindow.X - Window.X;
        var offsetY = requestedWindow.Y - Window.Y;
        var points = LandPoints
            .Where(point => point.X >= offsetX && point.X < offsetX + requestedWindow.Width &&
                            point.Y >= offsetY && point.Y < offsetY + requestedWindow.Height)
            .Select(point => new GridPoint(point.X - offsetX, point.Y - offsetY))
            .ToHashSet();
        return new MapMask(requestedWindow, points);
    }
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
    MapSpatialReference? SpatialReference = null,
    RequestedDomain? RequestedDomain = null,
    WorkingDomain? WorkingDomain = null,
    RegionalGenerationMode GenerationMode = RegionalGenerationMode.Legacy);

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
