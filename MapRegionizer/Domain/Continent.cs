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
        public List<LineString> ContinentBoundaries { get; }
        public Continent(Polygon polygon, List<LineString> continentBoundaries)
        {
            ContinentPolygon = polygon;
            ContinentBoundaries = continentBoundaries;
        }
    }
}
