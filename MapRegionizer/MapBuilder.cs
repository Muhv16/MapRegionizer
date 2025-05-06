using NetTopologySuite.Geometries;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer
{
    internal class MapBuilder
    {
        private readonly GeometryFactory _factory;
        private readonly MapOptions _options;
        public List<Polygon> MapPolygons { get; private set; }
        public MapBuilder(GeometryFactory factory, MapOptions options)
        {
            _factory = factory;
            _options = options;
            MapPolygons = new List<Polygon>();

        }
        public void BuildMapFromCoords(List<Coordinate> continentsCoords)
        {
            var continents = FindContinents(continentsCoords);
            foreach (var continent in continents.Where(c => c.Length > 3))
            {
                var ring = new LinearRing(GetTracedCountour(ContourDefine(continent)));
                MapPolygons.Add(_factory.CreatePolygon(ring));
            }
        }


        public List<Coordinate[]> FindContinents(List<Coordinate> terrainCoords)
        {
            var unvisited = new HashSet<Coordinate>(terrainCoords);
            var result = new List<Coordinate[]>();

            Coordinate[] dirs = {
            new Coordinate(1, 0),
            new Coordinate(-1, 0),
            new Coordinate(0, 1),
            new Coordinate(0, -1)
            };

            while (unvisited.Count > 0)
            {
                var continent = new List<Coordinate>();
                var queue = new Queue<Coordinate>();
                var start = unvisited.First();
                queue.Enqueue(start);
                unvisited.Remove(start);

                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    continent.Add(cur);
                    foreach (var d in dirs)
                    {
                        var nb = new Coordinate(cur.X + d.X, cur.Y + d.Y);
                        if (unvisited.Contains(nb))
                        {
                            unvisited.Remove(nb);
                            queue.Enqueue(nb);
                        }
                    }
                }
                result.Add(continent.ToArray());
            }
            return result;
        }

        private Coordinate[] ContourDefine(Coordinate[] shapeCoordinates)
        {
            var shapeSet = new HashSet<Coordinate>(shapeCoordinates);

            (int dx, int dy)[] offsets = new[]
            {
                ( 0,  1), ( 1,  0), ( 0, -1), (-1,  0),
            };

            var contourPixels = new List<Coordinate>();

            foreach (var coord in shapeCoordinates)
            {
                foreach (var (dx, dy) in offsets)
                {
                    var neighbor = new Coordinate(coord.X + dx, coord.Y + dy);
                    if (!shapeSet.Contains(neighbor))
                    {
                        contourPixels.Add(coord);
                        break;
                    }
                }
            }
            contourPixels.Add(contourPixels[0]);

            return contourPixels.ToArray();
        }

        private Coordinate[] GetTracedCountour(Coordinate[] contourPixels)
        {
            var sortedResult = new List<Coordinate>();
            double yMin = contourPixels.Select(c => c.Y).Min();
            double xMin = contourPixels.Where(c => c.Y == yMin).Select(c => c.X).Min();

            var currentPixel = new Coordinate(xMin, yMin);
            sortedResult.Add(currentPixel);

            var directions = new List<Coordinate>()
            {
                (1, 0),
                (1, 1),
                (0, 1),
                (-1, 1),
                (-1, 0),
                (-1, -1),
                (0, -1),
                (1, -1)
            };
            int prevDirIndex = 0;
            for (int i = 0; i < contourPixels.Length; i++)
            {
                for (int di = 0; di < directions.Count; di++)
                {
                    int dirIndex = (prevDirIndex + di) % 8;
                    var dir = directions[dirIndex];
                    var nextPoint = new Coordinate(currentPixel.X + dir.X, currentPixel.Y + dir.Y);
                    if (contourPixels.Contains(nextPoint.CoordinateValue) && !sortedResult.Contains(nextPoint.CoordinateValue))
                    {
                        dirIndex = (dirIndex - 2) % 8;
                        currentPixel = nextPoint.CoordinateValue;
                        sortedResult.Add(currentPixel.CoordinateValue);
                        break;
                    }
                }
            }
            sortedResult.Add(sortedResult[0]);
            return sortedResult.ToArray();
        }
    }
}
