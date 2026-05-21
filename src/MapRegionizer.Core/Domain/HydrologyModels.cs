namespace MapRegionizer.Core.Domain;

public sealed class HydrologyMap
{
    private readonly double[] _hydroSurfaceMeters;
    private readonly int[] _flowDirections;
    private readonly double[] _flowAccumulation;
    private readonly int[] _drainageBasinIds;
    private readonly byte[] _riverCells;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<RiverSegment> Rivers { get; }
    public IReadOnlyList<RiverMouth> Mouths { get; }
    public IReadOnlyList<LakeOutlet> LakeOutlets { get; }
    public IReadOnlyList<DrainageBasin> Basins { get; }

    public HydrologyMap(
        int width,
        int height,
        double[] hydroSurfaceMeters,
        int[] flowDirections,
        double[] flowAccumulation,
        int[] drainageBasinIds,
        byte[] riverCells,
        IReadOnlyList<RiverSegment> rivers,
        IReadOnlyList<RiverMouth> mouths,
        IReadOnlyList<LakeOutlet> lakeOutlets,
        IReadOnlyList<DrainageBasin> basins)
    {
        var expectedLength = width * height;
        ValidateLength(hydroSurfaceMeters, expectedLength, nameof(hydroSurfaceMeters));
        ValidateLength(flowDirections, expectedLength, nameof(flowDirections));
        ValidateLength(flowAccumulation, expectedLength, nameof(flowAccumulation));
        ValidateLength(drainageBasinIds, expectedLength, nameof(drainageBasinIds));
        ValidateLength(riverCells, expectedLength, nameof(riverCells));

        Width = width;
        Height = height;
        _hydroSurfaceMeters = hydroSurfaceMeters;
        _flowDirections = flowDirections;
        _flowAccumulation = flowAccumulation;
        _drainageBasinIds = drainageBasinIds;
        _riverCells = riverCells;
        Rivers = rivers;
        Mouths = mouths;
        LakeOutlets = lakeOutlets;
        Basins = basins;
    }

    public double GetHydroSurface(int x, int y) => _hydroSurfaceMeters[y * Width + x];

    public double GetHydroSurface(GridPoint point) => GetHydroSurface(point.X, point.Y);

    public int GetFlowDirection(int x, int y) => _flowDirections[y * Width + x];

    public int GetFlowDirection(GridPoint point) => GetFlowDirection(point.X, point.Y);

    public double GetFlowAccumulation(int x, int y) => _flowAccumulation[y * Width + x];

    public double GetFlowAccumulation(GridPoint point) => GetFlowAccumulation(point.X, point.Y);

    public int GetDrainageBasinId(int x, int y) => _drainageBasinIds[y * Width + x];

    public int GetDrainageBasinId(GridPoint point) => GetDrainageBasinId(point.X, point.Y);

    public bool IsRiverCell(int x, int y) => _riverCells[y * Width + x] != 0;

    public bool IsRiverCell(GridPoint point) => IsRiverCell(point.X, point.Y);

    public ReadOnlySpan<double> HydroSurfaceMetersSpan => _hydroSurfaceMeters;
    public ReadOnlySpan<int> FlowDirectionsSpan => _flowDirections;
    public ReadOnlySpan<double> FlowAccumulationSpan => _flowAccumulation;
    public ReadOnlySpan<int> DrainageBasinIdsSpan => _drainageBasinIds;
    public ReadOnlySpan<byte> RiverCellsSpan => _riverCells;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public sealed record RiverSegment(
    int Id,
    IReadOnlyList<GridPoint> Cells,
    IReadOnlyList<MapPoint> Polyline,
    GridPoint Source,
    GridPoint Mouth,
    GridPoint DrainageTerminal,
    int? LandComponentId,
    DrainageTargetKind TargetKind,
    int? TargetId,
    double Discharge,
    double LengthCells,
    double MeanSlope,
    RiverKind Kind,
    RiverMouthKind? MouthKind = null,
    int Order = 1,
    bool IsMajor = false,
    double VisibleRank = 0.0,
    int? ParentRiverId = null,
    IReadOnlyList<int>? TributaryIds = null);

public sealed record RiverMouth(
    int RiverId,
    GridPoint Cell,
    DrainageTargetKind TargetKind,
    int? TargetId,
    RiverMouthKind Kind,
    double Discharge);

public sealed record LakeOutlet(
    WaterBodyId LakeId,
    bool HasOutlet,
    GridPoint? OutletCell,
    GridPoint? DownstreamCell,
    double SpillElevationMeters,
    double BreachCost,
    double OutletScore);

public sealed record DrainageBasin(
    int Id,
    DrainageTargetKind TargetKind,
    int? TargetId,
    GridPoint TerminalCell,
    int CellCount,
    double TotalRunoff);

public enum RiverKind
{
    Mountain,
    Plain,
    Rift,
    Deltaic,
    Endorheic
}

public enum RiverMouthKind
{
    SimpleMouth,
    Estuary,
    Delta,
    MarshDelta,
    InlandDelta
}

public enum DrainageTargetKind
{
    Ocean,
    Lake,
    InlandSea,
    EndorheicDryBasin
}
