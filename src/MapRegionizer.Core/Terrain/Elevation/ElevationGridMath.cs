using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal static class ElevationGridMath
{
    internal static double[] ComputeDistance(MapMask mask, bool sourceIsLand)
    {
        var width = mask.Width;
        var height = mask.Height;
        var length = width * height;

        var distances = new double[length];
        Array.Fill(distances, double.PositiveInfinity);

        var queue = new PriorityQueue<GridPoint, double>();

        for (var y = 0; y < height; y++)
        {
            var row = y * width;

            for (var x = 0; x < width; x++)
            {
                var point = new GridPoint(x, y);
                if (mask.IsLand(point) != sourceIsLand)
                    continue;

                var index = row + x;
                distances[index] = 0.0;
                queue.Enqueue(point, 0.0);
            }
        }

        if (queue.Count == 0)
        {
            Array.Fill(distances, Math.Max(width, height));
            return distances;
        }

        while (queue.TryDequeue(out var current, out var queuedDistance))
        {
            var currentIndex = current.Y * width + current.X;

            if (queuedDistance > distances[currentIndex])
                continue;

            for (var dy = -1; dy <= 1; dy++)
            {
                var ny = current.Y + dy;
                if (ny < 0 || ny >= height)
                    continue;

                var nrow = ny * width;

                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var nx = current.X + dx;
                    if (nx < 0)
                        nx = width - 1;
                    else if (nx >= width)
                        nx = 0;

                    var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                    var nextDistance = queuedDistance + cost;
                    var neighborIndex = nrow + nx;

                    if (nextDistance >= distances[neighborIndex])
                        continue;

                    distances[neighborIndex] = nextDistance;
                    queue.Enqueue(new GridPoint(nx, ny), nextDistance);
                }
            }
        }

        return distances;
    }

    internal static double[] BuildLandEnclosureField(MapMask mask)
    {
        var values = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
                values[y * mask.Width + x] = mask.IsLand(new GridPoint(x, y)) ? 1.0 : 0.0;
        }

        return SmoothField(SmoothField(values, mask.Width, mask.Height, 8), mask.Width, mask.Height, 8);
    }

    internal static IEnumerable<GridPoint> PointsInRadius(int width, int height, GridPoint center, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = center.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                yield return new GridPoint(WrapX(center.X + dx, width), y);
            }
        }
    }

    internal static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height)
    {
        yield return new GridPoint(WrapX(point.X - 1, width), point.Y);
        yield return new GridPoint(WrapX(point.X + 1, width), point.Y);
        if (point.Y > 0) yield return new GridPoint(point.X, point.Y - 1);
        if (point.Y < height - 1) yield return new GridPoint(point.X, point.Y + 1);
    }

    internal static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                yield return new GridPoint(WrapX(point.X + dx, width), y);
            }
        }
    }

    internal static IEnumerable<(GridPoint Point, double Cost)> Neighbors8WithCost(GridPoint point, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                yield return (new GridPoint(WrapX(point.X + dx, width), y), cost);
            }
        }
    }

    internal static double Distance(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, Math.Max(0, width - dx));
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    internal static double WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    internal static int WrapX(int x, int width) => (x % width + width) % width;

}
