using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace MapRegionizer.Core.Regions;

internal sealed class VoronoiRegionGenerator
{
    private readonly GeometryFactory _geometryFactory;
    private readonly Random _random;
    private readonly RegionMerger _regionMerger = new();

    public VoronoiRegionGenerator(GeometryFactory geometryFactory, Random random)
    {
        _geometryFactory = geometryFactory;
        _random = random;
    }

    public IReadOnlyList<Polygon> Generate(Polygon landmass, RegionGenerationOptions options)
    {
        if (landmass.Area < options.TargetArea)
            return new[] { landmass };

        var diagram = FormVoronoiDiagram(landmass, options);
        var regions = ClipRegions(landmass, diagram);
        _regionMerger.MergeSmallRegions(regions, options);
        return regions;
    }

    private GeometryCollection FormVoronoiDiagram(Polygon shape, RegionGenerationOptions options)
    {
        var pointCount = Math.Max(1, (int)(shape.Area * options.PointsMultiplier / options.TargetArea));
        var points = new Coordinate[pointCount];

        for (var i = 0; i < points.Length; i++)
        {
            do
            {
                var x = shape.EnvelopeInternal.MinX + _random.NextDouble() * shape.EnvelopeInternal.Width;
                var y = shape.EnvelopeInternal.MinY + _random.NextDouble() * shape.EnvelopeInternal.Height;
                points[i] = new Coordinate(x, y);
            }
            while (!shape.Contains(_geometryFactory.CreatePoint(points[i])));
        }

        var builder = new VoronoiDiagramBuilder();
        builder.SetSites(points);
        builder.ClipEnvelope = shape.EnvelopeInternal;
        return builder.GetDiagram(_geometryFactory);
    }

    private static List<Polygon> ClipRegions(Polygon shape, GeometryCollection diagram)
    {
        var clippedRegions = new List<Polygon>();

        foreach (var geometry in diagram.Geometries)
        {
            if (geometry is not Polygon voronoiPolygon)
                continue;

            var clipped = voronoiPolygon.Intersection(shape);
            if (clipped.IsEmpty)
                continue;

            switch (clipped)
            {
                case Polygon polygon:
                    clippedRegions.Add(polygon);
                    break;
                case MultiPolygon multiPolygon:
                    clippedRegions.AddRange(multiPolygon.Geometries.OfType<Polygon>());
                    break;
                case GeometryCollection collection:
                    clippedRegions.AddRange(collection.Geometries.OfType<Polygon>());
                    break;
            }
        }

        return clippedRegions;
    }

}

internal sealed class RegionMerger
{
    public void MergeSmallRegions(List<Polygon> regions, RegionGenerationOptions options)
    {
        bool mergedAny;
        do
        {
            mergedAny = false;

            for (var i = 0; i < regions.Count; i++)
            {
                if (regions[i].Area >= options.TargetArea * options.MinAreaRatio)
                    continue;

                var current = regions[i];
                var neighborFinder = new PolygonNeighborFinder(regions);
                var neighbors = neighborFinder
                    .FindNeighbors(current)
                    .OrderByDescending(n => current.Boundary.Intersection(n.Boundary).Length)
                    .ToList();

                var neighbor = neighbors.FirstOrDefault(n => n.Area + current.Area < options.TargetArea * options.MaxAreaRatio)
                    ?? neighbors.FirstOrDefault();

                if (neighbor is null)
                    continue;

                var neighborIndex = regions.FindIndex(r => ReferenceEquals(r, neighbor));
                if (neighborIndex < 0)
                    continue;

                var merged = current.Union(neighbor);
                if (merged is not Polygon mergedPolygon)
                    continue;

                regions[neighborIndex] = mergedPolygon;
                regions.RemoveAt(i);
                i--;
                mergedAny = true;
            }
        }
        while (mergedAny);
    }
}
