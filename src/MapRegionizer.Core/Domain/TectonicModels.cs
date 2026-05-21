namespace MapRegionizer.Core.Domain;

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

    public ReadOnlySpan<short> PlatesSpan => _plates;
    public ReadOnlySpan<byte> CrustSpan => _crust;
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
    TectonicFeatureMap? Features = null,
    OrogenProvinceMap? OrogenProvinces = null,
    RiftProvinceMap? RiftProvinces = null);

public sealed record TectonicPlate(
    TectonicPlateId Id,
    TectonicPlateKind Kind,
    int PointCount,
    GridPoint Centroid,
    GridVector Motion,
    double Activity,
    double Density,
    double Thickness,
    double? MeanOceanicAge);

public sealed record PlateBoundary(
    TectonicPlateId PlateA,
    TectonicPlateId PlateB,
    IReadOnlyList<GridPoint> Points,
    PlateBoundaryKind Kind,
    BoundaryMode BoundaryMode,
    double Convergence,
    double Divergence,
    double Shear,
    double Activity,
    double? MeanOceanicAge,
    double? SubductingOceanicAge,
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
    BoundaryMode BoundaryMode,
    double Convergence,
    double Divergence,
    double Shear,
    double Activity,
    double? MeanOceanicAge,
    double? SubductingOceanicAge,
    TectonicPlateId? SubductingPlate);

public readonly record struct TectonicPlateId(int Value);

public readonly record struct GridVector(double X, double Y);

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

public enum BoundaryMode
{
    PureTransform,
    Transpression,
    Transtension,
    ObliqueSubduction,
    MidOceanRidge,
    ContinentalRift,
    OceanOceanSubduction,
    OceanContinentSubduction,
    ContinentContinentCollision,
    PassiveMargin,
    DiffuseIntraplateBoundary,
    AccretionaryBoundary,
    BackArcSpreading,
    MixedSegmentBoundary
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
