#pragma warning disable CS0618

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

public sealed record GeneratedMap
{
    public GeneratedMap(
        MapBounds Bounds,
        IReadOnlyList<Landmass> Landmasses,
        IReadOnlyList<WaterBody> WaterBodies,
        IReadOnlyList<MapRegion> Regions,
        WorldContextMode WorldContextMode,
        TectonicPlateMap? TectonicPlates = null,
        ElevationMap? Elevation = null,
        WaterBodyTopology? WaterBodyTopology = null,
        WaterSurfaceMap? WaterSurfaces = null,
        HydrologyMap? Hydrology = null,
        ClimateMap? Climate = null,
        RegionRaster? RegionRaster = null,
        MapSpatialReference? SpatialReference = null,
        RequestedDomain? RequestedDomain = null,
        WorkingDomain? WorkingDomain = null)
        : this(
            Bounds,
            Landmasses,
            WaterBodies,
            Regions,
            WorldContextMode,
            TectonicPlates,
            Elevation,
            WaterBodyTopology,
            WaterSurfaces,
            Hydrology,
            Climate,
            RegionRaster,
            SpatialReference,
            RequestedDomain,
            WorkingDomain,
            isLegacyCompatibilityRequest: false,
            initializeCanonical: true)
    {
    }

    internal GeneratedMap(
        MapBounds Bounds,
        IReadOnlyList<Landmass> Landmasses,
        IReadOnlyList<WaterBody> WaterBodies,
        IReadOnlyList<MapRegion> Regions,
        WorldContextMode WorldContextMode,
        TectonicPlateMap? TectonicPlates,
        ElevationMap? Elevation,
        WaterBodyTopology? WaterBodyTopology,
        WaterSurfaceMap? WaterSurfaces,
        HydrologyMap? Hydrology,
        ClimateMap? Climate,
        RegionRaster? RegionRaster,
        MapSpatialReference? SpatialReference,
        RequestedDomain? RequestedDomain,
        WorkingDomain? WorkingDomain,
        bool isLegacyCompatibilityRequest)
        : this(
            Bounds,
            Landmasses,
            WaterBodies,
            Regions,
            WorldContextMode,
            TectonicPlates,
            Elevation,
            WaterBodyTopology,
            WaterSurfaces,
            Hydrology,
            Climate,
            RegionRaster,
            SpatialReference,
            RequestedDomain,
            WorkingDomain,
            isLegacyCompatibilityRequest,
            initializeCanonical: true)
    {
    }

    private GeneratedMap(
        MapBounds Bounds,
        IReadOnlyList<Landmass> Landmasses,
        IReadOnlyList<WaterBody> WaterBodies,
        IReadOnlyList<MapRegion> Regions,
        WorldContextMode WorldContextMode,
        TectonicPlateMap? TectonicPlates,
        ElevationMap? Elevation,
        WaterBodyTopology? WaterBodyTopology,
        WaterSurfaceMap? WaterSurfaces,
        HydrologyMap? Hydrology,
        ClimateMap? Climate,
        RegionRaster? RegionRaster,
        MapSpatialReference? SpatialReference,
        RequestedDomain? RequestedDomain,
        WorkingDomain? WorkingDomain,
        bool isLegacyCompatibilityRequest,
        bool initializeCanonical)
    {
        this.Bounds = Bounds;
        this.Landmasses = Landmasses;
        this.WaterBodies = WaterBodies;
        this.Regions = Regions;
        this.TectonicPlates = TectonicPlates;
        this.Elevation = Elevation;
        this.WaterBodyTopology = WaterBodyTopology;
        this.WaterSurfaces = WaterSurfaces;
        this.Hydrology = Hydrology;
        this.Climate = Climate;
        this.RegionRaster = RegionRaster;
        this.SpatialReference = SpatialReference;
        this.RequestedDomain = RequestedDomain;
        this.WorkingDomain = WorkingDomain;
        if (!Enum.IsDefined(WorldContextMode))
            throw new ArgumentOutOfRangeException(nameof(WorldContextMode));
        this.WorldContextMode = WorldContextMode;
        this.IsLegacyCompatibilityRequest = isLegacyCompatibilityRequest;
    }

    /// <summary>Compatibility constructor for callers passing the old mode enum.</summary>
    [Obsolete("Use the WorldContextMode constructor. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public GeneratedMap(
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
        RegionalGenerationMode GenerationMode = RegionalGenerationMode.Legacy)
        : this(
            Bounds,
            Landmasses,
            WaterBodies,
            Regions,
            GenerationMode.ToWorldContextMode(),
            TectonicPlates,
            Elevation,
            WaterBodyTopology,
            WaterSurfaces,
            Hydrology,
            Climate,
            RegionRaster,
            SpatialReference,
            RequestedDomain,
            WorkingDomain,
            GenerationMode == RegionalGenerationMode.Legacy,
            initializeCanonical: true)
    {
    }

    public MapBounds Bounds { get; init; }
    public IReadOnlyList<Landmass> Landmasses { get; init; }
    public IReadOnlyList<WaterBody> WaterBodies { get; init; }
    public IReadOnlyList<MapRegion> Regions { get; init; }
    public TectonicPlateMap? TectonicPlates { get; init; }
    public ElevationMap? Elevation { get; init; }
    public WaterBodyTopology? WaterBodyTopology { get; init; }
    public WaterSurfaceMap? WaterSurfaces { get; init; }
    public HydrologyMap? Hydrology { get; init; }
    public ClimateMap? Climate { get; init; }
    public RegionRaster? RegionRaster { get; init; }
    public MapSpatialReference? SpatialReference { get; init; }
    public RequestedDomain? RequestedDomain { get; init; }
    public WorkingDomain? WorkingDomain { get; init; }
    public WorldContextMode WorldContextMode { get; init; }
    internal bool IsLegacyCompatibilityRequest { get; init; }

    /// <summary>
    /// Obsolete compatibility projection of the canonical state. Legacy is
    /// represented by the separate compatibility marker and never enters the
    /// canonical world-context model.
    /// </summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public RegionalGenerationMode GenerationMode => IsLegacyCompatibilityRequest
        ? RegionalGenerationMode.Legacy
        : WorldContextMode.ToRegionalGenerationMode();
    public WorldContextMode ContextMode => WorldContextMode;

    /// <summary>Compatibility deconstruction retaining the former mode value.</summary>
    [Obsolete("Use the named properties and WorldContextMode.")]
    public void Deconstruct(
        out MapBounds bounds,
        out IReadOnlyList<Landmass> landmasses,
        out IReadOnlyList<WaterBody> waterBodies,
        out IReadOnlyList<MapRegion> regions,
        out TectonicPlateMap? tectonicPlates,
        out ElevationMap? elevation,
        out WaterBodyTopology? waterBodyTopology,
        out WaterSurfaceMap? waterSurfaces,
        out HydrologyMap? hydrology,
        out ClimateMap? climate,
        out RegionRaster? regionRaster,
        out MapSpatialReference? spatialReference,
        out RequestedDomain? requestedDomain,
        out WorkingDomain? workingDomain,
        out RegionalGenerationMode generationMode)
    {
        bounds = Bounds;
        landmasses = Landmasses;
        waterBodies = WaterBodies;
        regions = Regions;
        tectonicPlates = TectonicPlates;
        elevation = Elevation;
        waterBodyTopology = WaterBodyTopology;
        waterSurfaces = WaterSurfaces;
        hydrology = Hydrology;
        climate = Climate;
        regionRaster = RegionRaster;
        spatialReference = SpatialReference;
        requestedDomain = RequestedDomain;
        workingDomain = WorkingDomain;
        generationMode = GenerationMode;
    }
}

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
