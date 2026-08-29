using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.Terrain;

internal static class HydrologyGridMath
{
    public static readonly (int Dx, int Dy)[] Directions =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1)
    ];

    public static int ChebyshevDistance(GridPoint a, GridPoint b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // Width-only overloads preserve the pre-spatial helper contract; pipeline
    // code always supplies its explicit topology below.
    public static double Distance(GridPoint a, GridPoint b, int width) =>
        Distance(a, b, new CylindricalXTopology(width, Math.Max(a.Y, b.Y) + 1));

    public static double Distance(GridPoint a, GridPoint b, IGridTopology topology)
    {
        var dx = GridTopologyMath.WrappedDeltaX(topology, a.X - b.X);
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static int DownstreamIndex(int index, int direction, int width, int height) =>
        DownstreamIndex(index, direction, new CylindricalXTopology(width, height));

    public static int DownstreamIndex(int index, int direction, IGridTopology topology)
    {
        if (direction < 0 || direction >= Directions.Length)
            return -1;

        var width = topology is CylindricalXTopology cylindrical ? cylindrical.Width :
            topology is OpenRectangularTopology open ? open.Width : 0;
        if (width <= 0)
            return -1;
        var x = index % width;
        var y = index / width;
        var move = Directions[direction];
        if (!topology.TryResolve(new GridPoint(x, y), move.Dx, move.Dy, out var next))
            return -1;
        return next.Y * width + next.X;
    }

    public static GridPoint? Move(GridPoint point, int direction, int width, int height) =>
        Move(point, direction, new CylindricalXTopology(width, height));

    public static GridPoint? Move(GridPoint point, int direction, IGridTopology topology)
    {
        if (direction < 0 || direction >= Directions.Length)
            return null;
        var move = Directions[direction];
        return topology.TryResolve(point, move.Dx, move.Dy, out var next) ? next : null;
    }

    public static int DirectionIndex(GridPoint from, GridPoint to, int width) =>
        DirectionIndex(from, to, new CylindricalXTopology(width, Math.Max(from.Y, to.Y) + 1));

    public static int DirectionIndex(GridPoint from, GridPoint to, IGridTopology topology)
    {
        var dx = GridTopologyMath.WrappedDeltaX(topology, to.X - from.X);
        var dy = to.Y - from.Y;
        for (var i = 0; i < Directions.Length; i++)
        {
            if (Directions[i].Dx == Math.Sign(dx) && Directions[i].Dy == Math.Sign(dy))
                return i;
        }

        return -1;
    }

    public static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height) =>
        Neighbors8(point, new CylindricalXTopology(width, height));

    public static IEnumerable<GridPoint> Neighbors8(GridPoint point, IGridTopology topology)
    {
        for (var i = 0; i < Directions.Length; i++)
        {
            var moved = Move(point, i, topology);
            if (moved.HasValue)
                yield return moved.Value;
        }
    }

    public static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            value = (value << 13) ^ value;
            return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
        }
    }

    public static double HashUnit(int x, int y, int seed) => Math.Clamp((Hash01(x, y, seed) + 1.0) * 0.5, 0, 1);

    public static IReadOnlyList<GridPoint> FindShoreline(int width, int height, IReadOnlyList<GridPoint> waterCells, Func<GridPoint, bool> isSameWater)
        => FindShoreline(width, height, waterCells, isSameWater, new CylindricalXTopology(width, height));

    public static IReadOnlyList<GridPoint> FindShoreline(int width, int height, IReadOnlyList<GridPoint> waterCells, Func<GridPoint, bool> isSameWater, IGridTopology topology)
    {
        var shoreline = new HashSet<GridPoint>();
        foreach (var cell in waterCells)
        {
            foreach (var neighbor in topology.GetNeighbors8(cell))
            {
                if (!isSameWater(neighbor))
                    shoreline.Add(neighbor);
            }
        }

        return shoreline.ToList();
    }

    public static List<int> ReconstructPath(int terminal, int[] previous)
    {
        var path = new List<int>();
        var current = terminal;
        while (current >= 0)
        {
            path.Add(current);
            current = previous[current];
        }

        path.Reverse();
        return path;
    }
}
