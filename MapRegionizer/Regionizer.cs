using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Triangulate;

namespace MapRegionizer
{
    internal class Regionizer
    {
        private readonly GeometryFactory _factory;
        private readonly MapOptions _options;
        public List<Polygon>? DefaultVoronoiRegions { get; private set; }

        public Regionizer(GeometryFactory factory, MapOptions options)
        {
            _factory = factory;
            _options = options;
        }

        public List<Polygon> Regionize(Polygon mapPolygon)
        {
            if (mapPolygon.Area < _options.TargetSize) return new List<Polygon>() { mapPolygon };
            DefaultVoronoiRegions = ClipRegions(mapPolygon, FormVoronoiDiargam(mapPolygon.Coordinates, mapPolygon));
            var editedRegions = new List<Polygon>(DefaultVoronoiRegions);
            MergeRegions(editedRegions);
            return editedRegions;
        }

        private void MergeRegions(List<Polygon> regions)
        {
            bool mergedAny;
            do
            {
                mergedAny = false;
               
                for (int i = 0; i < regions.Count; i++)
                {
                    var polygonNeghborFinder = new PolygonNeighborFinder(regions);
                    if (regions[i].Area < _options.TargetSize * _options.MaxDownward)
                    {
                        var polyI = regions[i];

                        var neghborsSTR = polygonNeghborFinder
                            .FindNeighbors(polyI)
                            .OrderByDescending(n => polyI.Boundary.Intersection(n.Boundary).Length);

                        var neighbor = neghborsSTR.FirstOrDefault(n => n.Area + polyI.Area < _options.TargetSize * _options.MaxUpward) ?? neghborsSTR.FirstOrDefault();
                        if (neighbor != null)
                        {
                            int defaultRegion = Enumerable.Range(0, regions.Count)
                                .Single(k => regions[k] == neighbor);
                            neighbor = (Polygon)polyI.Union(neighbor);
                            regions[defaultRegion] = neighbor;
                            regions.RemoveAt(i);
                            i--;
                            mergedAny = true;
                        }
                    }
                }
            } while (mergedAny);
        }

        private GeometryCollection FormVoronoiDiargam(Coordinate[] terrainPixels, Polygon shapePolygon)
        {
            var random = new Random();
            var voronoiPoints = new Coordinate[(int)(shapePolygon.Area * _options.PointsMultiplier / _options.TargetSize)].ToArray();

            for (int i = 0; i < voronoiPoints.Length; i++)
            {
                do
                {
                    var x = shapePolygon.EnvelopeInternal.MinX +
                           random.NextDouble() * shapePolygon.EnvelopeInternal.Width;
                    var y = shapePolygon.EnvelopeInternal.MinY +
                           random.NextDouble() * shapePolygon.EnvelopeInternal.Height;
                    voronoiPoints[i] = new Coordinate(x, y);
                }
                while (!shapePolygon.Contains(_factory.CreatePoint(voronoiPoints[i])));
            }

            var voronoiBuilder = new VoronoiDiagramBuilder();
            voronoiBuilder.SetSites(voronoiPoints);
            voronoiBuilder.ClipEnvelope = shapePolygon.EnvelopeInternal;
            var voronoiDiagram = voronoiBuilder.GetDiagram(_factory);

            return voronoiDiagram;
        }

        private List<Polygon> ClipRegions(Polygon shapePolygon, GeometryCollection voronoiDiagram)
        {
            var clippedRegions = new List<Polygon>();
            foreach (var geom in voronoiDiagram.Geometries)
            {
                if (geom is Polygon voronoiPolygon)
                {
                    var clipped = voronoiPolygon.Intersection(shapePolygon);
                    if (!clipped.IsEmpty)
                    {
                        if (clipped is MultiPolygon multiClipped)
                            clippedRegions.AddRange(multiClipped.Cast<Polygon>());
                        else clippedRegions.Add((Polygon)clipped);
                    }
                }
            }
            return clippedRegions;
        }

    }
}
