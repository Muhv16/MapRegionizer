namespace MapRegionizer.Core.Domain;

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

    public ReadOnlySpan<byte> CrustSpan => _crust;
    public ReadOnlySpan<byte> CoastalZoneSpan => _coastalZones;
    public ReadOnlySpan<double> OceanicAgeSpan => _oceanicAge;
    public ReadOnlySpan<double> ContinentalAgeSpan => _continentalAge;
    public ReadOnlySpan<double> LastRiftingAgeSpan => _lastRiftingAge;
    public ReadOnlySpan<double> LastOrogenyAgeSpan => _lastOrogenyAge;
    public ReadOnlySpan<double> LastVolcanismAgeSpan => _lastVolcanismAge;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public enum CrustKind
{
    Continental,
    Oceanic,
    Shelf,
    Arc,
    Rift,
    Terrane
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
