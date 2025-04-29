using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer;

public class PolygonNeighborFinder
{
    private readonly STRtree<Polygon> _spatialIndex;

    public PolygonNeighborFinder(List<Polygon> polygons)
    {
        _spatialIndex = new STRtree<Polygon>();

        // Заполнение индекса
        foreach (var polygon in polygons)
        {
            _spatialIndex.Insert(polygon.EnvelopeInternal, polygon);
        }
        _spatialIndex.Build();
    }

    public List<Polygon> FindNeighbors(Geometry targetPolygon)
    {
        var neighbors = new List<Polygon>();

        // Поиск кандидатов через индекс
        var candidates = _spatialIndex.Query(targetPolygon.EnvelopeInternal);

        foreach (var candidate in candidates)
        {
            // Игнорируем сам полигон
            if (candidate == targetPolygon)
                continue;

            // Проверяем, что полигоны имеют общую границу
            if (targetPolygon.Touches(candidate))
            {
                neighbors.Add(candidate);
            }
        }

        return neighbors;
    }
}