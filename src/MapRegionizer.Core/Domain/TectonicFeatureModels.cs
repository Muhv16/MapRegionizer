namespace MapRegionizer.Core.Domain;

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
