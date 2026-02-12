using NetTopologySuite.Geometries;

namespace MapRegionizer.BoundaryServices
{
    internal class BorderFinder
    {
        private readonly GeometryFactory _factory;
        public BorderFinder(GeometryFactory factory)
        {
            _factory = factory;
        }

        public List<LineString> FindSharedBorders(List<Polygon> polygons)
        {
            var edgeCounts = new Dictionary<string, int>();

            foreach (var polygon in polygons)
            {
                var rings = new List<LineString> { polygon.ExteriorRing };
                rings.AddRange(polygon.InteriorRings);

                foreach (var ring in rings)
                {
                    var coordinates = ring.Coordinates;

                    for (int i = 0; i < coordinates.Length - 1; i++)
                    {
                        var edge = new[] { coordinates[i], coordinates[i + 1] };
                        var normalizedEdgeKey = NormalizeEdge(edge);
                        if (edgeCounts.ContainsKey(normalizedEdgeKey))
                            edgeCounts[normalizedEdgeKey]++;
                        else
                            edgeCounts[normalizedEdgeKey] = 1;
                    }
                }
            }
            var sharedEdges = new List<LineString>();

            foreach (var pair in edgeCounts)
            {
                if (pair.Value == 2)
                {
                    var edgeCoordinates = ParseEdgeKey(pair.Key);
                    sharedEdges.Add(_factory.CreateLineString(edgeCoordinates));
                }
            }

            return sharedEdges;
        }
        private static string NormalizeEdge(Coordinate[] edge)
        {
            var ordered = edge.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
            return $"{ordered[0].X},{ordered[0].Y};{ordered[1].X},{ordered[1].Y}";
        }

        private static Coordinate[] ParseEdgeKey(string key)
        {
            var parts = key.Split(';');
            var coord1 = parts[0].Split(',');
            var coord2 = parts[1].Split(',');

            return new[]
            {
                new Coordinate(double.Parse(coord1[0]), double.Parse(coord1[1])),
                new Coordinate(double.Parse(coord2[0]), double.Parse(coord2[1]))
            };
        }
    }
}
