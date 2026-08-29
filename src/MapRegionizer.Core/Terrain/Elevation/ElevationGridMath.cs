using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal static class ElevationGridMath
{
    internal static double[] ComputeDistance(MapMask mask, bool sourceIsLand, IGridTopology? topology = null)
    {
        topology ??= new CylindricalXTopology(mask.Width, mask.Height);
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

            foreach (var neighbor in topology.GetNeighbors8(current))
            {
                var dx = GridTopologyMath.WrappedDeltaX(topology, neighbor.X - current.X);
                var dy = neighbor.Y - current.Y;
                var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                var nextDistance = queuedDistance + cost;
                var neighborIndex = neighbor.Y * width + neighbor.X;

                if (nextDistance >= distances[neighborIndex])
                    continue;

                distances[neighborIndex] = nextDistance;
                queue.Enqueue(neighbor, nextDistance);
            }
        }

        return distances;
    }

    internal static double[] BuildLandEnclosureField(MapMask mask, IGridTopology? topology = null)
    {
        var values = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
                values[y * mask.Width + x] = mask.IsLand(new GridPoint(x, y)) ? 1.0 : 0.0;
        }

        return SmoothField(SmoothField(values, mask.Width, mask.Height, 8, topology), mask.Width, mask.Height, 8, topology);
    }

    internal static IEnumerable<GridPoint> PointsInRadius(int width, int height, GridPoint center, int radius, IGridTopology? topology = null)
    {
        topology ??= new CylindricalXTopology(width, height);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                if (topology.TryResolve(center, dx, dy, out var point))
                    yield return point;
            }
        }
    }

    // Width/height overloads are legacy adapters. Spatially-aware generation
    // passes the session topology explicitly.
    internal static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height, IGridTopology? topology = null) =>
        (topology ?? new CylindricalXTopology(width, height)).GetNeighbors4(point);

    internal static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height, IGridTopology? topology = null) =>
        (topology ?? new CylindricalXTopology(width, height)).GetNeighbors8(point);

    internal static IEnumerable<(GridPoint Point, double Cost)> Neighbors8WithCost(GridPoint point, int width, int height, IGridTopology? topology = null)
    {
        topology ??= new CylindricalXTopology(width, height);
        foreach (var neighbor in topology.GetNeighbors8(point))
        {
            var dx = GridTopologyMath.WrappedDeltaX(topology, neighbor.X - point.X);
            var dy = neighbor.Y - point.Y;
            var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
            yield return (neighbor, cost);
        }
    }

    internal static double Distance(GridPoint a, GridPoint b, int width, IGridTopology? topology = null)
    {
        var dx = GridTopologyMath.WrappedDeltaX(topology ?? new CylindricalXTopology(width, Math.Max(a.Y, b.Y) + 1), a.X - b.X);
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

}
