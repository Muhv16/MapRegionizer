using NetTopologySuite.Geometries;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer
{
    using NetTopologySuite.Geometries;
    using NetTopologySuite.Operation.Union;
    using NetTopologySuite.Simplify;

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

        public void BuildMapFromCoords(List<Coordinate> terrainCoords)
        {
            var continents = FindContinents(terrainCoords);
            foreach (var continent in continents.Where(c => c.Length > 3))
            {
                var rawPoly = BuildPolygonFromPixels(continent);
                if (rawPoly == null || rawPoly.ExteriorRing.NumPoints <= 3)
                    continue;


                var simp = DouglasPeuckerSimplifier.Simplify(rawPoly, _options.SimplifyTolerance);

                // вариант замены для топологически безопасного упрощения
                // var simp = TopologyPreservingSimplifier.Simplify(rawPoly, _options.SimplifyTolerance);

                if (simp is Polygon p && p.ExteriorRing.NumPoints > 3)
                    MapPolygons.Add(p);
            }
        }

        private List<Coordinate[]> FindContinents(List<Coordinate> terrainCoords)
        {
            var unvisited = new HashSet<Coordinate>(terrainCoords);
            var result = new List<Coordinate[]>();

            var dirs = new[]
            {
            new Coordinate( 1,  0),
            new Coordinate(-1,  0),
            new Coordinate( 0,  1),
            new Coordinate( 0, -1)
        };

            while (unvisited.Count > 0)
            {
                var start = unvisited.First();
                var queue = new Queue<Coordinate>();
                var continent = new List<Coordinate>();
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

        private Polygon BuildPolygonFromPixels(Coordinate[] pixels)
        {
            // 1) Для каждого пикселя создаём unit‑квадрат [x,y]–[x+1,y+1]
            var quads = pixels.Select(p =>
            {
                var x = p.X * _options.PixelSize;
                var y = p.Y * _options.PixelSize;
                var sz = _options.PixelSize;

                var ring = new LinearRing(new[]
                {
                new Coordinate(x, y),
                new Coordinate(x + sz, y),
                new Coordinate(x + sz, y + sz),
                new Coordinate(x, y + sz),
                new Coordinate(x, y)
            });
                return _factory.CreatePolygon(ring);
            }).ToList<Geometry>();

            // 2) Геометрически объединяем все квадраты единым Union‑ом
            var unioned = CascadedPolygonUnion.Union(quads);

            // 3) Если получилось несколько полигонов, берём самый большой по площади
            Polygon? best = null;
            if (unioned is MultiPolygon mpoly)
            {
                best = mpoly.Geometries
                           .OfType<Polygon>()
                           .OrderByDescending(p => p.Area)
                           .First();
            }
            else if (unioned is Polygon poly)
            {
                best = poly;
            }

            return best!;
        }
    }

}
