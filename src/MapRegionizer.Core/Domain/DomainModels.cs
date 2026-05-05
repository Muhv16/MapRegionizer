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
    TectonicPlateRaster Raster,
    TectonicHistory? History = null,
    CrustFieldMap? CrustFields = null,
    PlateDomainMap? PlateDomains = null,
    TectonicBoundaryMap? BoundaryMap = null,
    TectonicFeatureMap? Features = null);

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
    TectonicPlateId? SubductingPlate,
    IReadOnlyList<PlateBoundarySegment>? Segments = null,
    IReadOnlyList<int>? SegmentIds = null);

public sealed record TectonicHistory(
    int Width,
    int Height,
    IReadOnlyList<TectonicLineament> Lineaments,
    IReadOnlyList<TectonicEvent> Events,
    IReadOnlyList<GridPoint> CratonCenters,
    IReadOnlyList<GridPoint> Hotspots);

public sealed record TectonicLineament(
    int Id,
    TectonicFeatureKind Kind,
    IReadOnlyList<GridPoint> Points,
    double Age,
    double Intensity);

public sealed record TectonicEvent(
    int Id,
    TectonicEventKind Kind,
    GridPoint Center,
    double Age,
    double Radius,
    double Intensity);

public sealed class CrustFieldMap
{
    private readonly byte[] _crust;
    private readonly byte[] _coastalZones;
    private readonly double[] _oceanicAge;
    private readonly double[] _continentalAge;
    private readonly double[] _lastRiftingAge;
    private readonly double[] _lastOrogenyAge;
    private readonly double[] _lastVolcanismAge;

    public int Width { get; }
    public int Height { get; }

    public CrustFieldMap(
        int width,
        int height,
        byte[] crust,
        byte[] coastalZones,
        double[] oceanicAge,
        double[] continentalAge,
        double[] lastRiftingAge,
        double[] lastOrogenyAge,
        double[] lastVolcanismAge)
    {
        var expectedLength = width * height;
        ValidateLength(crust, expectedLength, nameof(crust));
        ValidateLength(coastalZones, expectedLength, nameof(coastalZones));
        ValidateLength(oceanicAge, expectedLength, nameof(oceanicAge));
        ValidateLength(continentalAge, expectedLength, nameof(continentalAge));
        ValidateLength(lastRiftingAge, expectedLength, nameof(lastRiftingAge));
        ValidateLength(lastOrogenyAge, expectedLength, nameof(lastOrogenyAge));
        ValidateLength(lastVolcanismAge, expectedLength, nameof(lastVolcanismAge));

        Width = width;
        Height = height;
        _crust = crust;
        _coastalZones = coastalZones;
        _oceanicAge = oceanicAge;
        _continentalAge = continentalAge;
        _lastRiftingAge = lastRiftingAge;
        _lastOrogenyAge = lastOrogenyAge;
        _lastVolcanismAge = lastVolcanismAge;
    }

    public CrustKind GetCrust(int x, int y) => (CrustKind)_crust[y * Width + x];

    public CrustKind GetCrust(GridPoint point) => GetCrust(point.X, point.Y);

    public CoastalZoneKind GetCoastalZone(int x, int y) => (CoastalZoneKind)_coastalZones[y * Width + x];

    public CoastalZoneKind GetCoastalZone(GridPoint point) => GetCoastalZone(point.X, point.Y);

    public double GetOceanicAge(int x, int y) => _oceanicAge[y * Width + x];

    public double GetOceanicAge(GridPoint point) => GetOceanicAge(point.X, point.Y);

    public double GetContinentalAge(int x, int y) => _continentalAge[y * Width + x];

    public double GetContinentalAge(GridPoint point) => GetContinentalAge(point.X, point.Y);

    public double GetLastRiftingAge(int x, int y) => _lastRiftingAge[y * Width + x];

    public double GetLastRiftingAge(GridPoint point) => GetLastRiftingAge(point.X, point.Y);

    public double GetLastOrogenyAge(int x, int y) => _lastOrogenyAge[y * Width + x];

    public double GetLastOrogenyAge(GridPoint point) => GetLastOrogenyAge(point.X, point.Y);

    public double GetLastVolcanismAge(int x, int y) => _lastVolcanismAge[y * Width + x];

    public double GetLastVolcanismAge(GridPoint point) => GetLastVolcanismAge(point.X, point.Y);

    internal ReadOnlySpan<byte> CrustSpan => _crust;
    internal ReadOnlySpan<byte> CoastalZoneSpan => _coastalZones;
    internal ReadOnlySpan<double> OceanicAgeSpan => _oceanicAge;
    internal ReadOnlySpan<double> ContinentalAgeSpan => _continentalAge;
    internal ReadOnlySpan<double> LastRiftingAgeSpan => _lastRiftingAge;
    internal ReadOnlySpan<double> LastOrogenyAgeSpan => _lastOrogenyAge;
    internal ReadOnlySpan<double> LastVolcanismAgeSpan => _lastVolcanismAge;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public sealed class PlateDomainMap
{
    private readonly short[] _plates;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<PlateDomain> Domains { get; }

    public PlateDomainMap(int width, int height, short[] plates, IReadOnlyList<PlateDomain> domains)
    {
        if (plates.Length != width * height)
            throw new ArgumentException($"Plates array length must be {width * height}", nameof(plates));

        Width = width;
        Height = height;
        _plates = plates;
        Domains = domains;
    }

    public TectonicPlateId GetPlate(int x, int y) => new(_plates[y * Width + x]);

    public TectonicPlateId GetPlate(GridPoint point) => GetPlate(point.X, point.Y);

    internal ReadOnlySpan<short> PlatesSpan => _plates;
}

public sealed record PlateDomain(
    TectonicPlateId Id,
    TectonicPlateKind Kind,
    int PointCount,
    GridPoint Centroid,
    GridVector Motion,
    double Activity,
    double Density,
    double Thickness,
    bool IsMicroplate);

public sealed record TectonicBoundaryMap(
    int Width,
    int Height,
    IReadOnlyList<PlateBoundarySegment> Segments);

public sealed record PlateBoundarySegment(
    int Id,
    TectonicPlateId PlateA,
    TectonicPlateId PlateB,
    IReadOnlyList<GridPoint> Points,
    BoundarySegmentKind Kind,
    double Convergence,
    double Divergence,
    double Shear,
    TectonicPlateId? SubductingPlate);

public sealed class TectonicFeatureMap
{
    private readonly double[] _uplift;
    private readonly double[] _subsidence;
    private readonly double[] _volcanism;
    private readonly double[] _seismicity;
    private readonly double[] _heatFlow;
    private readonly double[] _sedimentSupply;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<TectonicFeature> Features { get; }
    public IReadOnlyList<TectonicIsland> Islands { get; }

    public TectonicFeatureMap(
        int width,
        int height,
        IReadOnlyList<TectonicFeature> features,
        IReadOnlyList<TectonicIsland> islands,
        double[] uplift,
        double[] subsidence,
        double[] volcanism,
        double[] seismicity,
        double[] heatFlow,
        double[] sedimentSupply)
    {
        var expectedLength = width * height;
        CrustFieldMapValidateLength(uplift, expectedLength, nameof(uplift));
        CrustFieldMapValidateLength(subsidence, expectedLength, nameof(subsidence));
        CrustFieldMapValidateLength(volcanism, expectedLength, nameof(volcanism));
        CrustFieldMapValidateLength(seismicity, expectedLength, nameof(seismicity));
        CrustFieldMapValidateLength(heatFlow, expectedLength, nameof(heatFlow));
        CrustFieldMapValidateLength(sedimentSupply, expectedLength, nameof(sedimentSupply));

        Width = width;
        Height = height;
        Features = features;
        Islands = islands;
        _uplift = uplift;
        _subsidence = subsidence;
        _volcanism = volcanism;
        _seismicity = seismicity;
        _heatFlow = heatFlow;
        _sedimentSupply = sedimentSupply;
    }

    public double GetUplift(int x, int y) => _uplift[y * Width + x];

    public double GetSubsidence(int x, int y) => _subsidence[y * Width + x];

    public double GetVolcanism(int x, int y) => _volcanism[y * Width + x];

    public double GetSeismicity(int x, int y) => _seismicity[y * Width + x];

    public double GetHeatFlow(int x, int y) => _heatFlow[y * Width + x];

    public double GetSedimentSupply(int x, int y) => _sedimentSupply[y * Width + x];

    private static void CrustFieldMapValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public sealed record TectonicFeature(
    int Id,
    TectonicFeatureKind Kind,
    IReadOnlyList<GridPoint> Points,
    double Age,
    double Intensity,
    int? SourceSegmentId = null);

public sealed record TectonicIsland(
    GridPoint Center,
    IslandKind Kind,
    double Area,
    TectonicPlateId PlateId);

public readonly record struct TectonicPlateId(int Value);

public readonly record struct GridVector(double X, double Y);

public enum CrustKind
{
    Continental,
    Oceanic,
    Shelf,
    Arc,
    Rift,
    Terrane
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

public enum BoundarySegmentKind
{
    Subduction,
    Collision,
    ContinentalRift,
    MidOceanRidge,
    Transform,
    BackArcBasin,
    PassiveMargin
}

public enum CoastalZoneKind
{
    None,
    Shelf,
    Slope,
    PassiveMargin,
    ActiveMargin,
    ShallowSea
}

public enum IslandKind
{
    VolcanicArc,
    Hotspot,
    Microcontinent,
    UpliftedRidge,
    ShelfArchipelago
}

public enum TectonicFeatureKind
{
    Ridge,
    Trench,
    Arc,
    Rift,
    Suture,
    Orogen,
    Craton,
    PassiveMargin,
    Hotspot,
    SedimentaryBasin,
    Microplate,
    BackArcBasin
}

public enum TectonicEventKind
{
    OceanOpening,
    OceanClosing,
    ContinentalRifting,
    Orogeny,
    Volcanism,
    CratonStabilization,
    TerraneAccretion
}
