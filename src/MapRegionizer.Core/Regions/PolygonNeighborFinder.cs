using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace MapRegionizer.Core.Regions;

internal sealed class PolygonNeighborFinder
{
    private readonly STRtree<Polygon> _spatialIndex;

    public PolygonNeighborFinder(IEnumerable<Polygon> polygons)
    {
        _spatialIndex = new STRtree<Polygon>();

        foreach (var polygon in polygons)
            _spatialIndex.Insert(polygon.EnvelopeInternal, polygon);

        _spatialIndex.Build();
    }

    public IReadOnlyList<Polygon> FindNeighbors(Geometry targetPolygon)
    {
        return _spatialIndex.Query(targetPolygon.EnvelopeInternal)
            .Where(candidate => !ReferenceEquals(candidate, targetPolygon) && targetPolygon.Touches(candidate))
            .ToList();
    }
}
