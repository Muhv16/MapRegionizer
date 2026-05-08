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
    ElevationMap? Elevation = null);

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
    double? MeanOceanicAge,
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
    BoundaryMode BoundaryMode,
    double Convergence,
    double Divergence,
    double Shear,
    double Activity,
    double? MeanOceanicAge,
    double? SubductingOceanicAge,
    TectonicPlateId? SubductingPlate);

public sealed class OrogenProvinceMap
{
    private readonly double[] _influence;
    private readonly double[] _strength;
    private readonly double[] _axis;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<OrogenProvince> Provinces { get; }

    public OrogenProvinceMap(
        int width,
        int height,
        IReadOnlyList<OrogenProvince> provinces,
        double[] influence,
        double[] strength,
        double[] axis)
    {
        var expectedLength = width * height;
        CrustFieldMapValidateLength(influence, expectedLength, nameof(influence));
        CrustFieldMapValidateLength(strength, expectedLength, nameof(strength));
        CrustFieldMapValidateLength(axis, expectedLength, nameof(axis));

        Width = width;
        Height = height;
        Provinces = provinces;
        _influence = influence;
        _strength = strength;
        _axis = axis;
    }

    public double GetInfluence(int x, int y) => _influence[y * Width + x];

    public double GetStrength(int x, int y) => _strength[y * Width + x];

    public double GetAxis(int x, int y) => _axis[y * Width + x];

    internal ReadOnlySpan<double> InfluenceSpan => _influence;
    internal ReadOnlySpan<double> StrengthSpan => _strength;
    internal ReadOnlySpan<double> AxisSpan => _axis;

    private static void CrustFieldMapValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public sealed record OrogenProvince(
    int Id,
    IReadOnlyList<GridPoint> AxisPoints,
    double Age,
    double Activity,
    double MeanScore,
    double BaseWidth,
    int? SourceLineamentId = null,
    int? SourceBoundarySegmentId = null);

public sealed class RiftProvinceMap
{
    private readonly double[] _riftInfluence;
    private readonly double[] _riftAxis;
    private readonly double[] _grabenMask;
    private readonly double[] _shoulderUpliftMask;
    private readonly double[] _heatFlowMask;
    private readonly double[] _breakupMask;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<RiftProvince> Provinces { get; }

    public RiftProvinceMap(
        int width,
        int height,
        IReadOnlyList<RiftProvince> provinces,
        double[] riftInfluence,
        double[] riftAxis,
        double[] grabenMask,
        double[] shoulderUpliftMask,
        double[] heatFlowMask,
        double[] breakupMask)
    {
        var expectedLength = width * height;
        ValidateLength(riftInfluence, expectedLength, nameof(riftInfluence));
        ValidateLength(riftAxis, expectedLength, nameof(riftAxis));
        ValidateLength(grabenMask, expectedLength, nameof(grabenMask));
        ValidateLength(shoulderUpliftMask, expectedLength, nameof(shoulderUpliftMask));
        ValidateLength(heatFlowMask, expectedLength, nameof(heatFlowMask));
        ValidateLength(breakupMask, expectedLength, nameof(breakupMask));

        Width = width;
        Height = height;
        Provinces = provinces;
        _riftInfluence = riftInfluence;
        _riftAxis = riftAxis;
        _grabenMask = grabenMask;
        _shoulderUpliftMask = shoulderUpliftMask;
        _heatFlowMask = heatFlowMask;
        _breakupMask = breakupMask;
    }

    public double GetRiftInfluence(int x, int y) => _riftInfluence[y * Width + x];

    public double GetRiftAxis(int x, int y) => _riftAxis[y * Width + x];

    public double GetGrabenMask(int x, int y) => _grabenMask[y * Width + x];

    public double GetShoulderUpliftMask(int x, int y) => _shoulderUpliftMask[y * Width + x];

    public double GetHeatFlowMask(int x, int y) => _heatFlowMask[y * Width + x];

    public double GetBreakupMask(int x, int y) => _breakupMask[y * Width + x];

    internal ReadOnlySpan<double> RiftInfluenceSpan => _riftInfluence;
    internal ReadOnlySpan<double> RiftAxisSpan => _riftAxis;
    internal ReadOnlySpan<double> GrabenMaskSpan => _grabenMask;
    internal ReadOnlySpan<double> ShoulderUpliftMaskSpan => _shoulderUpliftMask;
    internal ReadOnlySpan<double> HeatFlowMaskSpan => _heatFlowMask;
    internal ReadOnlySpan<double> BreakupMaskSpan => _breakupMask;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public sealed record RiftProvince(
    int Id,
    RiftProvinceKind Kind,
    IReadOnlyList<GridPoint> AxisPoints,
    IReadOnlyList<RiftProvinceSegment> Segments,
    double Age,
    double Activity,
    double MeanScore,
    double BaseWidth,
    int? SourceLineamentId = null,
    int? SourceBoundarySegmentId = null);

public sealed record RiftProvinceSegment(
    GridPoint Center,
    GridVector Direction,
    double Length,
    double Width,
    double Strength,
    bool IsFailedArm);

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

public sealed class ElevationMap
{
    private readonly double[] _elevationMeters;
    private readonly double[] _baseElevationMeters;
    private readonly double[] _tectonicElevationMeters;
    private readonly double[] _roughness;
    private readonly double[] _erosionMask;
    private readonly byte[] _terrainClasses;
    private readonly double[] _mountainPassPotential;
    private readonly double[] _ridgeContinuity;
    private readonly double[] _foothillInfluence;
    private readonly double[] _basinInfluence;

    public int Width { get; }
    public int Height { get; }

    public ElevationMap(
        int width,
        int height,
        double[] elevationMeters,
        double[] baseElevationMeters,
        double[] tectonicElevationMeters,
        double[] roughness,
        double[] erosionMask,
        byte[] terrainClasses,
        double[] mountainPassPotential,
        double[] ridgeContinuity,
        double[] foothillInfluence,
        double[] basinInfluence)
    {
        var expectedLength = width * height;
        ValidateLength(elevationMeters, expectedLength, nameof(elevationMeters));
        ValidateLength(baseElevationMeters, expectedLength, nameof(baseElevationMeters));
        ValidateLength(tectonicElevationMeters, expectedLength, nameof(tectonicElevationMeters));
        ValidateLength(roughness, expectedLength, nameof(roughness));
        ValidateLength(erosionMask, expectedLength, nameof(erosionMask));
        ValidateLength(terrainClasses, expectedLength, nameof(terrainClasses));
        ValidateLength(mountainPassPotential, expectedLength, nameof(mountainPassPotential));
        ValidateLength(ridgeContinuity, expectedLength, nameof(ridgeContinuity));
        ValidateLength(foothillInfluence, expectedLength, nameof(foothillInfluence));
        ValidateLength(basinInfluence, expectedLength, nameof(basinInfluence));

        Width = width;
        Height = height;
        _elevationMeters = elevationMeters;
        _baseElevationMeters = baseElevationMeters;
        _tectonicElevationMeters = tectonicElevationMeters;
        _roughness = roughness;
        _erosionMask = erosionMask;
        _terrainClasses = terrainClasses;
        _mountainPassPotential = mountainPassPotential;
        _ridgeContinuity = ridgeContinuity;
        _foothillInfluence = foothillInfluence;
        _basinInfluence = basinInfluence;
    }

    public double GetElevation(int x, int y) => _elevationMeters[y * Width + x];

    public double GetElevation(GridPoint point) => GetElevation(point.X, point.Y);

    public double GetBaseElevation(int x, int y) => _baseElevationMeters[y * Width + x];

    public double GetBaseElevation(GridPoint point) => GetBaseElevation(point.X, point.Y);

    public double GetTectonicElevation(int x, int y) => _tectonicElevationMeters[y * Width + x];

    public double GetTectonicElevation(GridPoint point) => GetTectonicElevation(point.X, point.Y);

    public double GetRoughness(int x, int y) => _roughness[y * Width + x];

    public double GetRoughness(GridPoint point) => GetRoughness(point.X, point.Y);

    public double GetErosionMask(int x, int y) => _erosionMask[y * Width + x];

    public double GetErosionMask(GridPoint point) => GetErosionMask(point.X, point.Y);

    public TerrainClassKind GetTerrainClass(int x, int y) => (TerrainClassKind)_terrainClasses[y * Width + x];

    public TerrainClassKind GetTerrainClass(GridPoint point) => GetTerrainClass(point.X, point.Y);

    public double GetMountainPassPotential(int x, int y) => _mountainPassPotential[y * Width + x];

    public double GetMountainPassPotential(GridPoint point) => GetMountainPassPotential(point.X, point.Y);

    public double GetRidgeContinuity(int x, int y) => _ridgeContinuity[y * Width + x];

    public double GetRidgeContinuity(GridPoint point) => GetRidgeContinuity(point.X, point.Y);

    public double GetFoothillInfluence(int x, int y) => _foothillInfluence[y * Width + x];

    public double GetFoothillInfluence(GridPoint point) => GetFoothillInfluence(point.X, point.Y);

    public double GetBasinInfluence(int x, int y) => _basinInfluence[y * Width + x];

    public double GetBasinInfluence(GridPoint point) => GetBasinInfluence(point.X, point.Y);

    public ElevationZoneKind GetZone(int x, int y)
    {
        var elevation = GetElevation(x, y);
        return elevation switch
        {
            <= -3000 => ElevationZoneKind.DeepOcean,
            < 0 => ElevationZoneKind.ShelfSea,
            < 180 => ElevationZoneKind.CoastalLowland,
            < 900 => ElevationZoneKind.Lowland,
            < 2200 => ElevationZoneKind.Highland,
            < 5200 => ElevationZoneKind.Mountain,
            _ => ElevationZoneKind.IceCapCandidate
        };
    }

    public ElevationZoneKind GetZone(GridPoint point) => GetZone(point.X, point.Y);

    internal ReadOnlySpan<double> ElevationMetersSpan => _elevationMeters;
    internal ReadOnlySpan<double> BaseElevationMetersSpan => _baseElevationMeters;
    internal ReadOnlySpan<double> TectonicElevationMetersSpan => _tectonicElevationMeters;
    internal ReadOnlySpan<double> RoughnessSpan => _roughness;
    internal ReadOnlySpan<double> ErosionMaskSpan => _erosionMask;
    internal ReadOnlySpan<byte> TerrainClassSpan => _terrainClasses;
    internal ReadOnlySpan<double> MountainPassPotentialSpan => _mountainPassPotential;
    internal ReadOnlySpan<double> RidgeContinuitySpan => _ridgeContinuity;
    internal ReadOnlySpan<double> FoothillInfluenceSpan => _foothillInfluence;
    internal ReadOnlySpan<double> BasinInfluenceSpan => _basinInfluence;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

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

public enum RiftProvinceKind
{
    ContinentalRift,
    BackArcExtension,
    DiffuseExtension
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

public enum ElevationZoneKind
{
    DeepOcean,
    ShelfSea,
    CoastalLowland,
    Lowland,
    Highland,
    Mountain,
    IceCapCandidate
}

public enum TerrainClassKind
{
    Ocean,
    ShelfSea,
    DeepChannel,
    ShallowBank,
    AbyssalBasin,
    SubmarineRidge,
    Trench,
    StraitDepth,
    InlandSeaDepth,
    Beach,
    CoastalPlain,
    AlluvialPlain,
    InteriorLowland,
    SedimentaryBasin,
    DryBasin,
    DeltaCandidate,
    DesertPlateauCandidate,
    Highland,
    Mountain
}
