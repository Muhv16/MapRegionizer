using NetTopologySuite.Geometries;

namespace MapRegionizer.Domain
{
    public class Continent
    {
        public Polygon ContinentPolygon { get; }
        public List<Polygon> Regions { get; }
        public Continent(Polygon polygon, List<Polygon> regions)
        {
            ContinentPolygon = polygon;
            Regions = regions;
        }
    }
}
