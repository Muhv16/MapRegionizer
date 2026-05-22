using MapRegionizer.Core.Domain;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverTopologyGraph
{
    private readonly byte[] _cells;
    private readonly int[] _downstream;

    public int Width { get; }
    public int Height { get; }

    private RiverTopologyGraph(int width, int height, byte[] cells, int[] downstream)
    {
        Width = width;
        Height = height;
        _cells = cells;
        _downstream = downstream;
    }

    public ReadOnlySpan<byte> CellsSpan => _cells;
    public ReadOnlySpan<int> DownstreamSpan => _downstream;

    public bool Contains(int index) => index >= 0 && index < _cells.Length && _cells[index] != 0;

    public int GetDownstream(int index) =>
        index >= 0 && index < _downstream.Length && Contains(index) ? _downstream[index] : -1;

    public GridPoint ToPoint(int index) => new(index % Width, index / Width);

    public byte[] ToRiverCells() => _cells.ToArray();

    public static RiverTopologyGraph Build(int width, int height, int[] flowDirections, byte[] riverCells, int[] lakeIds)
    {
        var cells = riverCells.ToArray();
        var downstream = Enumerable.Repeat(-1, cells.Length).ToArray();
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] == 0 || lakeIds[index] > 0)
                continue;

            var next = DownstreamIndex(index, flowDirections[index], width, height);
            if (next >= 0 && next < cells.Length && cells[next] != 0 && lakeIds[next] <= 0)
                downstream[index] = next;
        }

        return new RiverTopologyGraph(width, height, cells, downstream);
    }

    public void SetDownstream(int from, int to)
    {
        if (!Contains(from) || to < 0 || to >= _downstream.Length || !Contains(to))
            return;

        _downstream[from] = to;
    }

    public void RemoveCell(int index)
    {
        if (!Contains(index))
            return;

        _cells[index] = 0;
        _downstream[index] = -1;
        for (var i = 0; i < _downstream.Length; i++)
        {
            if (_downstream[i] == index)
                _downstream[i] = -1;
        }
    }

    public List<int>[] BuildUpstreamLists()
    {
        var upstream = Enumerable.Range(0, _downstream.Length).Select(_ => new List<int>()).ToArray();
        for (var index = 0; index < _downstream.Length; index++)
        {
            var next = GetDownstream(index);
            if (next >= 0)
                upstream[next].Add(index);
        }

        return upstream;
    }

    public int[] BuildLongestUpstreamDepths(List<int>[] upstream)
    {
        var depth = new int[_downstream.Length];
        var remaining = new int[_downstream.Length];
        var queue = new Queue<int>();
        for (var index = 0; index < _downstream.Length; index++)
        {
            if (!Contains(index))
                continue;

            remaining[index] = upstream[index].Count(i => Contains(i));
            depth[index] = 1;
            if (remaining[index] == 0)
                queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var next = GetDownstream(index);
            if (next < 0)
                continue;

            depth[next] = Math.Max(depth[next], depth[index] + 1);
            remaining[next]--;
            if (remaining[next] == 0)
                queue.Enqueue(next);
        }

        return depth;
    }

    public bool WouldCreateCycle(int from, int target)
    {
        var current = target;
        var guard = 0;
        while (current >= 0 && current < _downstream.Length && guard++ < _downstream.Length)
        {
            if (current == from)
                return true;

            current = GetDownstream(current);
        }

        return false;
    }

    public IEnumerable<RiverTopologyEdge> Edges()
    {
        for (var index = 0; index < _downstream.Length; index++)
        {
            var next = GetDownstream(index);
            if (next >= 0)
                yield return new RiverTopologyEdge(index, next);
        }
    }
}

internal readonly record struct RiverTopologyEdge(int From, int To);
