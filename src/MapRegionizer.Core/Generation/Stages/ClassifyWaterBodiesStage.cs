using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class ClassifyWaterBodiesStage : IMapGenerationStage
{
    public string Id => MapStageIds.ClassifyWaterBodies;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.Landmasses,
        MapDataKeys.WaterBodies,
        MapDataKeys.SpatialContext
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.WaterBodyTopology };

    public void Execute(MapGenerationContext context)
    {
        context.WaterBodyTopology = BuildTopology(
            context.Mask,
            context.WaterBodies,
            context.Options,
            context.GeometryFactory,
            context.SpatialContext.SpatialReference.LegacyCompatibility is (LegacyCompatibilityProfile.Flat or LegacyCompatibilityProfile.Regional)
                ? new OpenRectangularTopology(context.Mask.Width, context.Mask.Height)
                : context.SpatialContext.GridTopology);
    }

    private static WaterBodyTopology BuildTopology(
        MapMask mask,
        IReadOnlyList<WaterBody> waterBodies,
        MapGenerationOptions options,
        GeometryFactory geometryFactory,
        IGridTopology gridTopology)
    {
        var width = mask.Width;
        var height = mask.Height;
        var length = width * height;
        var waterBodyIds = new int[length];
        var waterBodyKinds = new byte[length];
        var classifications = new List<WaterBodyClassification>();
        var components = FindWaterComponents(mask, gridTopology);
        var edgeConnectedComponents = components
            .Where(component => TouchesMapEdge(component, gridTopology))
            .ToList();
        var oceanDistances = ComputeOceanDistances(mask, edgeConnectedComponents, gridTopology);
        var usedIds = new HashSet<int>();

        foreach (var component in components)
        {
            var id = FindWaterBodyId(component, waterBodies, usedIds, options.EffectiveSpatial.UnitsPerCell, geometryFactory);
            usedIds.Add(id.Value);

            var touchesEdge = TouchesMapEdge(component, gridTopology);
            var areaRatio = component.Count / (double)Math.Max(1, length);
            var nearOcean = !touchesEdge && IsNearOcean(component, oceanDistances, width, options.WaterBodies.OceanSeaNearOceanMaxDistanceCells);
            var kind = ClassifyKind(touchesEdge, areaRatio, nearOcean, options.WaterBodies);
            classifications.Add(new WaterBodyClassification(id, kind, component.Count, touchesEdge, areaRatio));

            foreach (var point in component)
            {
                var index = point.Y * width + point.X;
                waterBodyIds[index] = id.Value;
                waterBodyKinds[index] = (byte)kind;
            }
        }

        return new WaterBodyTopology(width, height, waterBodyIds, waterBodyKinds, classifications);
    }

    private static WaterBodyKind ClassifyKind(bool touchesEdge, double areaRatio, bool nearOcean, WaterBodyClassificationOptions options)
    {
        if (touchesEdge)
            return WaterBodyKind.Ocean;

        if (areaRatio >= options.OceanSeaMinAreaRatio)
            return WaterBodyKind.OceanSea;

        if (areaRatio >= options.InlandSeaMinAreaRatio)
            return nearOcean ? WaterBodyKind.OceanSea : WaterBodyKind.InlandSea;

        return WaterBodyKind.InlandLake;
    }

    private static bool TouchesMapEdge(IReadOnlyList<GridPoint> component, IGridTopology gridTopology) =>
        component.Any(point => GridTopologyMath.IsOpenBoundary(gridTopology, point));

    private static int[] ComputeOceanDistances(
        MapMask mask,
        IReadOnlyList<IReadOnlyList<GridPoint>> oceanComponents,
        IGridTopology gridTopology)
    {
        var distances = Enumerable.Repeat(-1, mask.Width * mask.Height).ToArray();
        var queue = new Queue<GridPoint>();

        foreach (var component in oceanComponents)
        {
            foreach (var point in component)
            {
                var index = point.Y * mask.Width + point.X;
                if (distances[index] >= 0)
                    continue;

                distances[index] = 0;
                queue.Enqueue(point);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distances[current.Y * mask.Width + current.X];

            foreach (var neighbor in gridTopology.GetNeighbors4(current))
            {
                var index = neighbor.Y * mask.Width + neighbor.X;
                if (distances[index] >= 0)
                    continue;

                distances[index] = currentDistance + 1;
                queue.Enqueue(neighbor);
            }
        }

        return distances;
    }

    private static bool IsNearOcean(IReadOnlyList<GridPoint> component, int[] oceanDistances, int width, int maxDistanceCells)
    {
        foreach (var point in component)
        {
            var distance = oceanDistances[point.Y * width + point.X];
            if (distance >= 0 && distance <= maxDistanceCells)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<IReadOnlyList<GridPoint>> FindWaterComponents(MapMask mask, IGridTopology gridTopology)
    {
        var unvisited = new HashSet<GridPoint>();
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (!mask.IsLand(point))
                    unvisited.Add(point);
            }
        }

        var components = new List<IReadOnlyList<GridPoint>>();
        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            var queue = new Queue<GridPoint>();
            var component = new List<GridPoint>();
            queue.Enqueue(start);
            unvisited.Remove(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var neighbor in gridTopology.GetNeighbors4(current))
                {
                    if (!unvisited.Remove(neighbor))
                        continue;

                    queue.Enqueue(neighbor);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static WaterBodyId FindWaterBodyId(
        IReadOnlyList<GridPoint> component,
        IReadOnlyList<WaterBody> waterBodies,
        HashSet<int> usedIds,
        double pixelSize,
        GeometryFactory geometryFactory)
    {
        if (waterBodies.Count == 0)
            return new WaterBodyId(usedIds.Count + 1);

        var scores = waterBodies
            .Where(w => !usedIds.Contains(w.Id.Value))
            .ToDictionary(w => w.Id.Value, _ => 0);
        var step = Math.Max(1, component.Count / 256);

        for (var i = 0; i < component.Count; i += step)
        {
            var point = component[i];
            var sample = geometryFactory.CreatePoint(new Coordinate((point.X + 0.5) * pixelSize, (point.Y + 0.5) * pixelSize));
            foreach (var waterBody in waterBodies)
            {
                if (usedIds.Contains(waterBody.Id.Value))
                    continue;

                if (waterBody.Shape.Covers(sample))
                    scores[waterBody.Id.Value]++;
            }
        }

        var best = scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .FirstOrDefault();

        if (best.Value > 0)
            return new WaterBodyId(best.Key);

        var center = ComponentCenter(component, pixelSize);
        var fallback = waterBodies
            .Where(w => !usedIds.Contains(w.Id.Value))
            .OrderBy(w => w.Shape.Distance(center))
            .FirstOrDefault();

        return fallback?.Id ?? new WaterBodyId(usedIds.Count + 1);
    }

    private static Point ComponentCenter(IReadOnlyList<GridPoint> component, double pixelSize)
    {
        var sumX = 0.0;
        var sumY = 0.0;
        foreach (var point in component)
        {
            sumX += point.X + 0.5;
            sumY += point.Y + 0.5;
        }

        return new GeometryFactory().CreatePoint(new Coordinate(sumX / component.Count * pixelSize, sumY / component.Count * pixelSize));
    }

}
