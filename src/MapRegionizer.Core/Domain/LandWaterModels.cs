using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Domain;

public sealed record Landmass(LandmassId Id, Polygon Shape);

public sealed record WaterBody(WaterBodyId Id, Polygon Shape);

public sealed record WaterBodyClassification(
    WaterBodyId Id,
    WaterBodyKind Kind,
    int CellCount,
    bool TouchesMapEdge,
    double AreaRatio);

public enum WaterBodyKind
{
    Ocean,
    OceanSea,
    InlandLake,
    InlandSea
}

public sealed class WaterBodyTopology
{
    private readonly int[] _waterBodyIds;
    private readonly byte[] _waterBodyKinds;
    private readonly Dictionary<int, WaterBodyClassification> _byId;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<WaterBodyClassification> Bodies { get; }

    public WaterBodyTopology(
        int width,
        int height,
        int[] waterBodyIds,
        byte[] waterBodyKinds,
        IReadOnlyList<WaterBodyClassification> bodies)
    {
        var expectedLength = width * height;
        if (waterBodyIds.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", nameof(waterBodyIds));
        if (waterBodyKinds.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", nameof(waterBodyKinds));

        Width = width;
        Height = height;
        _waterBodyIds = waterBodyIds;
        _waterBodyKinds = waterBodyKinds;
        Bodies = bodies;
        _byId = bodies.ToDictionary(b => b.Id.Value);
    }

    public WaterBodyId? GetWaterBodyId(int x, int y)
    {
        var value = _waterBodyIds[y * Width + x];
        return value <= 0 ? null : new WaterBodyId(value);
    }

    public WaterBodyId? GetWaterBodyId(GridPoint point) => GetWaterBodyId(point.X, point.Y);

    public WaterBodyKind? GetKind(int x, int y)
    {
        var id = _waterBodyIds[y * Width + x];
        return id <= 0 ? null : (WaterBodyKind)_waterBodyKinds[y * Width + x];
    }

    public WaterBodyKind? GetKind(GridPoint point) => GetKind(point.X, point.Y);

    public WaterBodyClassification? GetClassification(WaterBodyId id) =>
        _byId.TryGetValue(id.Value, out var classification) ? classification : null;

    public bool IsOceanicWater(int x, int y)
    {
        var kind = GetKind(x, y);
        return kind is WaterBodyKind.Ocean or WaterBodyKind.OceanSea;
    }

    public bool IsOceanicWater(GridPoint point) => IsOceanicWater(point.X, point.Y);

    public bool IsInlandWater(int x, int y)
    {
        var kind = GetKind(x, y);
        return kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea;
    }

    public bool IsInlandWater(GridPoint point) => IsInlandWater(point.X, point.Y);

    public ReadOnlySpan<int> WaterBodyIdsSpan => _waterBodyIds;
    public ReadOnlySpan<byte> WaterBodyKindsSpan => _waterBodyKinds;
}

public sealed record WaterBodySurface(
    WaterBodyId Id,
    WaterBodyKind Kind,
    double SurfaceElevationMeters,
    double SpillElevationMeters,
    double MarginMeters,
    double MaxDepthMeters,
    int ShorelineCellCount,
    int CellCount = 0,
    GridPoint? Centroid = null,
    LakeLocationKind? LakeLocation = null,
    LakeOriginKind? LakeOrigin = null,
    LakeProfileKind? LakeProfile = null,
    double MeanShorelineElevationMeters = 0,
    double ShorelineReliefMeters = 0,
    double TectonicInfluence = 0,
    double VolcanicInfluence = 0);

public enum LakeLocationKind
{
    Mountain,
    Plain,
    Plateau
}

public enum LakeOriginKind
{
    Tectonic,
    Glacial,
    Erosional,
    VolcanicKarst
}

public enum LakeProfileKind
{
    MountainBowl,
    PlainGaussian,
    TectonicTrough,
    VolcanicCone
}

public sealed class WaterSurfaceMap
{
    private readonly double[] _waterSurfaceMeters;
    private readonly Dictionary<int, WaterBodySurface> _byId;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<WaterBodySurface> Bodies { get; }

    public WaterSurfaceMap(int width, int height, double[] waterSurfaceMeters, IReadOnlyList<WaterBodySurface> bodies)
    {
        var expectedLength = width * height;
        if (waterSurfaceMeters.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", nameof(waterSurfaceMeters));

        Width = width;
        Height = height;
        _waterSurfaceMeters = waterSurfaceMeters;
        Bodies = bodies;
        _byId = bodies.ToDictionary(b => b.Id.Value);
    }

    public double GetWaterSurface(int x, int y) => _waterSurfaceMeters[y * Width + x];

    public double GetWaterSurface(GridPoint point) => GetWaterSurface(point.X, point.Y);

    public WaterBodySurface? GetBodySurface(WaterBodyId id) =>
        _byId.TryGetValue(id.Value, out var surface) ? surface : null;

    public ReadOnlySpan<double> WaterSurfaceMetersSpan => _waterSurfaceMeters;
}
