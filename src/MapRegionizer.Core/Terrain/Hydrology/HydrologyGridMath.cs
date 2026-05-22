using MapRegionizer.Core.Domain;

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

    public static double Distance(GridPoint a, GridPoint b, int width)
    {
        var dx = WrappedDeltaX(a.X - b.X, width);
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static int DownstreamIndex(int index, int direction, int width, int height)
    {
        if (direction < 0 || direction >= Directions.Length || width <= 0)
            return -1;

        var x = index % width;
        var y = index / width;
        var move = Directions[direction];
        var nextY = y + move.Dy;
        if (nextY < 0 || nextY >= height)
            return -1;
        var nextX = WrapX(x + move.Dx, width);
        return nextY * width + nextX;
    }

    public static GridPoint? Move(GridPoint point, int direction, int width, int height)
    {
        var move = Directions[direction];
        var y = point.Y + move.Dy;
        if (y < 0 || y >= height)
            return null;

        return new GridPoint(WrapX(point.X + move.Dx, width), y);
    }

    public static int DirectionIndex(GridPoint from, GridPoint to, int width)
    {
        var dx = WrappedDeltaX(to.X - from.X, width);
        var dy = to.Y - from.Y;
        for (var i = 0; i < Directions.Length; i++)
        {
            if (Directions[i].Dx == Math.Sign(dx) && Directions[i].Dy == Math.Sign(dy))
                return i;
        }

        return -1;
    }

    public static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height)
    {
        for (var i = 0; i < Directions.Length; i++)
        {
            var moved = Move(point, i, width, height);
            if (moved.HasValue)
                yield return moved.Value;
        }
    }

    public static int WrapX(int x, int width) => (x % width + width) % width;

    public static int WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
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
    {
        var shoreline = new HashSet<GridPoint>();
        foreach (var cell in waterCells)
        {
            foreach (var neighbor in Neighbors8(cell, width, height))
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