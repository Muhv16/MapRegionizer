namespace MapRegionizer.Core.Domain;

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

    public ReadOnlySpan<short> PlatesSpan => _plates;
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

    public ReadOnlySpan<double> InfluenceSpan => _influence;
    public ReadOnlySpan<double> StrengthSpan => _strength;
    public ReadOnlySpan<double> AxisSpan => _axis;

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

    public ReadOnlySpan<double> RiftInfluenceSpan => _riftInfluence;
    public ReadOnlySpan<double> RiftAxisSpan => _riftAxis;
    public ReadOnlySpan<double> GrabenMaskSpan => _grabenMask;
    public ReadOnlySpan<double> ShoulderUpliftMaskSpan => _shoulderUpliftMask;
    public ReadOnlySpan<double> HeatFlowMaskSpan => _heatFlowMask;
    public ReadOnlySpan<double> BreakupMaskSpan => _breakupMask;

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

public enum RiftProvinceKind
{
    ContinentalRift,
    BackArcExtension,
    DiffuseExtension
}
