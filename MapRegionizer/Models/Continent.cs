using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
