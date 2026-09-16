using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Uniform-grid index for the geometry that is queried continuously while a
/// manual map is being edited.  It deliberately stores only lightweight
/// vertex and edge data so pointer movement does not need to scan the draft or
/// allocate NetTopologySuite geometries.
/// </summary>
public sealed class ManualMapDraftSpatialIndex
{
    private readonly double _cellSize;
    private readonly Dictionary<int, MapPoint> _vertexPositions = [];
    private readonly Dictionary<SpatialCell, List<int>> _verticesByCell = [];
    private readonly Dictionary<SpatialCell, List<IndexedEdge>> _edgesByCell = [];

    public ManualMapDraftSpatialIndex(ManualMapDraft draft, double cellSize)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Spatial index cell size must be finite and greater than zero.");

        _cellSize = cellSize;
        Rebuild(draft);
    }

    public void Rebuild(ManualMapDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _vertexPositions.Clear();
        _verticesByCell.Clear();
        _edgesByCell.Clear();

        foreach (var vertex in draft.Vertices)
            AddVertex(vertex);

        var indexedEdges = new HashSet<EdgeKey>();
        foreach (var face in draft.Regions)
        {
            var vertexIds = face.VertexIds.ToArray();
            if (vertexIds.Length > 1 && vertexIds[0] == vertexIds[^1])
                vertexIds = vertexIds[..^1];
            if (vertexIds.Length < 2)
                continue;

            for (var index = 0; index < vertexIds.Length; index++)
            {
                var startId = vertexIds[index];
                var endId = vertexIds[(index + 1) % vertexIds.Length];
                if (startId == endId)
                    continue;

                var key = new EdgeKey(Math.Min(startId, endId), Math.Max(startId, endId));
                if (indexedEdges.Contains(key)
                    || !_vertexPositions.TryGetValue(startId, out var start)
                    || !_vertexPositions.TryGetValue(endId, out var end)
                    || !IsFinite(start)
                    || !IsFinite(end))
                    continue;

                indexedEdges.Add(key);
                AddEdge(new IndexedEdge(key, start, end));
            }
        }
    }

    /// <summary>Adds a newly created free vertex without rebuilding existing edges.</summary>
    public void AddVertex(ManualMapVertex vertex)
    {
        if (!IsFinite(vertex.Position) || _vertexPositions.ContainsKey(vertex.Id))
            return;

        _vertexPositions.Add(vertex.Id, vertex.Position);
        AddToCell(
            _verticesByCell,
            new SpatialCell(GetCellCoordinate(vertex.Position.X), GetCellCoordinate(vertex.Position.Y)),
            vertex.Id);
    }

    public ManualMapVertexSnap? FindNearestVertex(MapPoint point, double tolerance)
    {
        ValidateQuery(tolerance);
        var toleranceSquared = tolerance * tolerance;
        var range = GetCellRange(point, tolerance);
        var nearest = default(ManualMapVertexSnap?);
        var nearestDistanceSquared = double.PositiveInfinity;

        for (var x = range.MinX; x <= range.MaxX; x++)
        {
            for (var y = range.MinY; y <= range.MaxY; y++)
            {
                if (!_verticesByCell.TryGetValue(new SpatialCell(x, y), out var vertexIds))
                    continue;

                foreach (var vertexId in vertexIds)
                {
                    var position = _vertexPositions[vertexId];
                    var distanceSquared = DistanceSquared(point, position);
                    if (distanceSquared > toleranceSquared
                        || !IsBetter(distanceSquared, vertexId, nearestDistanceSquared, nearest?.VertexId))
                        continue;

                    nearestDistanceSquared = distanceSquared;
                    nearest = new ManualMapVertexSnap(vertexId, position, Math.Sqrt(distanceSquared));
                }
            }
        }

        return nearest;
    }

    public ManualMapEdgeSnap? FindNearestEdge(MapPoint point, double tolerance)
    {
        ValidateQuery(tolerance);
        var toleranceSquared = tolerance * tolerance;
        var range = GetCellRange(point, tolerance);
        var visited = new HashSet<EdgeKey>();
        var nearest = default(ManualMapEdgeSnap?);
        var nearestDistanceSquared = double.PositiveInfinity;

        for (var x = range.MinX; x <= range.MaxX; x++)
        {
            for (var y = range.MinY; y <= range.MaxY; y++)
            {
                if (!_edgesByCell.TryGetValue(new SpatialCell(x, y), out var edges))
                    continue;

                foreach (var edge in edges)
                {
                    if (!visited.Add(edge.Key))
                        continue;

                    var projected = Project(point, edge.Start, edge.End);
                    var distanceSquared = DistanceSquared(point, projected);
                    if (distanceSquared > toleranceSquared
                        || distanceSquared <= RegionGeometryPrecision.LengthTolerance * RegionGeometryPrecision.LengthTolerance
                        || !IsBetter(distanceSquared, edge.Key, nearestDistanceSquared, nearest))
                        continue;

                    nearestDistanceSquared = distanceSquared;
                    nearest = new ManualMapEdgeSnap(
                        edge.Key.StartVertexId,
                        edge.Key.EndVertexId,
                        projected,
                        Math.Sqrt(distanceSquared));
                }
            }
        }

        return nearest;
    }

    private void AddEdge(IndexedEdge edge)
    {
        var minX = GetCellCoordinate(Math.Min(edge.Start.X, edge.End.X));
        var maxX = GetCellCoordinate(Math.Max(edge.Start.X, edge.End.X));
        var minY = GetCellCoordinate(Math.Min(edge.Start.Y, edge.End.Y));
        var maxY = GetCellCoordinate(Math.Max(edge.Start.Y, edge.End.Y));

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
                AddToCell(_edgesByCell, new SpatialCell(x, y), edge);
        }
    }

    private CellRange GetCellRange(MapPoint point, double tolerance)
    {
        return new CellRange(
            GetCellCoordinate(point.X - tolerance),
            GetCellCoordinate(point.X + tolerance),
            GetCellCoordinate(point.Y - tolerance),
            GetCellCoordinate(point.Y + tolerance));
    }

    private int GetCellCoordinate(double coordinate) => checked((int)Math.Floor(coordinate / _cellSize));

    private static void AddToCell<T>(Dictionary<SpatialCell, List<T>> cells, SpatialCell cell, T value)
    {
        if (!cells.TryGetValue(cell, out var values))
        {
            values = [];
            cells.Add(cell, values);
        }

        values.Add(value);
    }

    private static bool IsBetter(
        double distanceSquared,
        int candidateId,
        double nearestDistanceSquared,
        int? nearestId)
    {
        return distanceSquared < nearestDistanceSquared
            || distanceSquared.Equals(nearestDistanceSquared) && (!nearestId.HasValue || candidateId < nearestId.Value);
    }

    private static bool IsBetter(
        double distanceSquared,
        EdgeKey candidate,
        double nearestDistanceSquared,
        ManualMapEdgeSnap? nearest)
    {
        if (distanceSquared < nearestDistanceSquared)
            return true;
        if (!distanceSquared.Equals(nearestDistanceSquared) || !nearest.HasValue)
            return false;
        return candidate.StartVertexId < nearest.Value.StartVertexId
            || candidate.StartVertexId == nearest.Value.StartVertexId
            && candidate.EndVertexId < nearest.Value.EndVertexId;
    }

    private static void ValidateQuery(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
    }

    private static bool IsFinite(MapPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static double DistanceSquared(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static MapPoint Project(MapPoint point, MapPoint start, MapPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= RegionGeometryPrecision.LengthTolerance * RegionGeometryPrecision.LengthTolerance)
            return start;

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return new MapPoint(start.X + dx * t, start.Y + dy * t);
    }

    private readonly record struct SpatialCell(int X, int Y);
    private readonly record struct CellRange(int MinX, int MaxX, int MinY, int MaxY);
    private readonly record struct EdgeKey(int StartVertexId, int EndVertexId);
    private readonly record struct IndexedEdge(EdgeKey Key, MapPoint Start, MapPoint End);
}

public readonly record struct ManualMapVertexSnap(int VertexId, MapPoint Position, double Distance);

public readonly record struct ManualMapEdgeSnap(
    int StartVertexId,
    int EndVertexId,
    MapPoint Position,
    double Distance);
