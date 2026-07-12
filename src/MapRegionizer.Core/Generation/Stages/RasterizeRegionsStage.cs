using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class RasterizeRegionsStage : IMapGenerationStage
{
    public string Id => MapStageIds.RasterizeRegions;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Mask, MapDataKeys.Regions };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.RegionRaster };

    public void Execute(MapGenerationContext context)
    {
        var width = context.Mask.Width;
        var height = context.Mask.Height;
        var regionIds = new int[width * height];
        var regions = context.Regions.Where(r => !r.Shape.IsEmpty).ToArray();

        foreach (var point in context.Mask.LandPoints)
        {
            var sample = CreateSamplePoint(context.GeometryFactory, point, context.Options.PixelSize);
            var region = FindCoveringRegion(regions, sample) ?? FindNearestRegion(regions, sample);
            if (region is not null)
                regionIds[point.Y * width + point.X] = region.Id.Value;
        }

        context.RegionRaster = new RegionRaster(width, height, regionIds);
    }

    private static Point CreateSamplePoint(GeometryFactory geometryFactory, GridPoint point, double pixelSize)
    {
        return geometryFactory.CreatePoint(new Coordinate(
            (point.X + 0.5) * pixelSize,
            (point.Y + 0.5) * pixelSize));
    }

    private static MapRegion? FindCoveringRegion(IReadOnlyList<MapRegion> regions, Point sample)
    {
        foreach (var region in regions)
        {
            if (region.Shape.EnvelopeInternal.Contains(sample.Coordinate) && region.Shape.Covers(sample))
                return region;
        }

        return null;
    }

    private static MapRegion? FindNearestRegion(IReadOnlyList<MapRegion> regions, Point sample)
    {
        MapRegion? nearest = null;
        var nearestDistance = double.PositiveInfinity;

        foreach (var region in regions)
        {
            var envelope = region.Shape.EnvelopeInternal;
            if (!envelope.Contains(sample.Coordinate) && DistanceToEnvelope(envelope, sample.Coordinate) > nearestDistance)
                continue;

            var distance = region.Shape.Distance(sample);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = region;
        }

        return nearest;
    }

    private static double DistanceToEnvelope(Envelope envelope, Coordinate coordinate)
    {
        var dx = coordinate.X < envelope.MinX
            ? envelope.MinX - coordinate.X
            : coordinate.X > envelope.MaxX
                ? coordinate.X - envelope.MaxX
                : 0;
        var dy = coordinate.Y < envelope.MinY
            ? envelope.MinY - coordinate.Y
            : coordinate.Y > envelope.MaxY
                ? coordinate.Y - envelope.MaxY
                : 0;

        return Math.Sqrt(dx * dx + dy * dy);
    }
}
