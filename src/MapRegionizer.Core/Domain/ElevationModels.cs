namespace MapRegionizer.Core.Domain;

public sealed class ElevationMap
{
    private readonly double[] _elevationMeters;
    private readonly double[] _bedElevationMeters;
    private readonly double[] _waterSurfaceMeters;
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
        double[] basinInfluence,
        double[]? bedElevationMeters = null,
        double[]? waterSurfaceMeters = null,
        WaterSurfaceMap? waterSurfaces = null)
    {
        var expectedLength = width * height;
        ValidateLength(elevationMeters, expectedLength, nameof(elevationMeters));
        bedElevationMeters ??= elevationMeters.ToArray();
        waterSurfaceMeters ??= Enumerable.Repeat(double.NaN, expectedLength).ToArray();
        ValidateLength(bedElevationMeters, expectedLength, nameof(bedElevationMeters));
        ValidateLength(waterSurfaceMeters, expectedLength, nameof(waterSurfaceMeters));
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
        _bedElevationMeters = bedElevationMeters;
        _waterSurfaceMeters = waterSurfaceMeters;
        _baseElevationMeters = baseElevationMeters;
        _tectonicElevationMeters = tectonicElevationMeters;
        _roughness = roughness;
        _erosionMask = erosionMask;
        _terrainClasses = terrainClasses;
        _mountainPassPotential = mountainPassPotential;
        _ridgeContinuity = ridgeContinuity;
        _foothillInfluence = foothillInfluence;
        _basinInfluence = basinInfluence;
        WaterSurfaces = waterSurfaces;
    }

    public WaterSurfaceMap? WaterSurfaces { get; }

    public double GetElevation(int x, int y) => _elevationMeters[y * Width + x];

    public double GetElevation(GridPoint point) => GetElevation(point.X, point.Y);

    public double GetBedElevation(int x, int y) => _bedElevationMeters[y * Width + x];

    public double GetBedElevation(GridPoint point) => GetBedElevation(point.X, point.Y);

    public double GetWaterSurface(int x, int y) => _waterSurfaceMeters[y * Width + x];

    public double GetWaterSurface(GridPoint point) => GetWaterSurface(point.X, point.Y);

    public bool HasWaterSurface(int x, int y) => !double.IsNaN(GetWaterSurface(x, y));

    public bool HasWaterSurface(GridPoint point) => HasWaterSurface(point.X, point.Y);

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
        if (HasWaterSurface(x, y))
            return GetBedElevation(x, y) <= -3000 ? ElevationZoneKind.DeepOcean : ElevationZoneKind.ShelfSea;

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

    public ReadOnlySpan<double> ElevationMetersSpan => _elevationMeters;
    public ReadOnlySpan<double> BedElevationMetersSpan => _bedElevationMeters;
    public ReadOnlySpan<double> WaterSurfaceMetersSpan => _waterSurfaceMeters;
    public ReadOnlySpan<double> BaseElevationMetersSpan => _baseElevationMeters;
    public ReadOnlySpan<double> TectonicElevationMetersSpan => _tectonicElevationMeters;
    public ReadOnlySpan<double> RoughnessSpan => _roughness;
    public ReadOnlySpan<double> ErosionMaskSpan => _erosionMask;
    public ReadOnlySpan<byte> TerrainClassSpan => _terrainClasses;
    public ReadOnlySpan<double> MountainPassPotentialSpan => _mountainPassPotential;
    public ReadOnlySpan<double> RidgeContinuitySpan => _ridgeContinuity;
    public ReadOnlySpan<double> FoothillInfluenceSpan => _foothillInfluence;
    public ReadOnlySpan<double> BasinInfluenceSpan => _basinInfluence;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}
