namespace MapRegionizer.Core.Domain;

public sealed record GeneratedLakeBody(
    WaterBodyId Id,
    IReadOnlyList<GridPoint> Cells,
    GridPoint Centroid,
    bool IsCluster,
    double LocalReliefMeters,
    double MaxDepthMeters);

public sealed class GeneratedLakeMap
{
    private readonly int[] _lakeIds;
    private readonly Dictionary<int, GeneratedLakeBody> _byId;

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<GeneratedLakeBody> Bodies { get; }

    public GeneratedLakeMap(int width, int height, IReadOnlyList<GeneratedLakeBody> bodies)
    {
        Width = width;
        Height = height;
        Bodies = bodies;
        _lakeIds = new int[width * height];
        _byId = bodies.ToDictionary(b => b.Id.Value);

        foreach (var body in bodies)
        {
            foreach (var cell in body.Cells)
            {
                if (cell.X < 0 || cell.X >= width || cell.Y < 0 || cell.Y >= height)
                    throw new ArgumentOutOfRangeException(nameof(bodies), "Generated lake cell is outside the map.");

                _lakeIds[cell.Y * width + cell.X] = body.Id.Value;
            }
        }
    }

    public static GeneratedLakeMap Empty(int width, int height) => new(width, height, []);

    public WaterBodyId? GetLakeId(int x, int y)
    {
        var value = _lakeIds[y * Width + x];
        return value <= 0 ? null : new WaterBodyId(value);
    }

    public WaterBodyId? GetLakeId(GridPoint point) => GetLakeId(point.X, point.Y);

    public GeneratedLakeBody? GetLake(WaterBodyId id) =>
        _byId.TryGetValue(id.Value, out var lake) ? lake : null;

    public bool Contains(int x, int y) => _lakeIds[y * Width + x] > 0;

    public bool Contains(GridPoint point) => Contains(point.X, point.Y);
}
