using NetTopologySuite.Geometries;

namespace MapRegionizer.BoundaryServices
{
    internal class PolygonUpdater
    {
        private readonly GeometryFactory _factory;
        public PolygonUpdater(GeometryFactory factory)
        {
            _factory = factory;
        }

        public List<Polygon> UpdatePolygons(List<Polygon> originalPolygons, Dictionary<LineString, LineString> borderReplacements)
        {
            var updatedPolygons = new List<Polygon>();

            foreach (var originalPolygon in originalPolygons)
            {
                var exteriorRing = UpdateRing((LinearRing)originalPolygon.ExteriorRing, borderReplacements);
                var interiorRings = originalPolygon.InteriorRings
                    .Select(r => UpdateRing((LinearRing)r, borderReplacements))
                    .ToArray();

                var updatedPolygon = _factory.CreatePolygon(exteriorRing, interiorRings);
                updatedPolygons.Add(updatedPolygon);
            }

            return updatedPolygons;
        }

        private LinearRing UpdateRing(LinearRing ring, Dictionary<LineString, LineString> replacements)
        {
            var coordinates = ring.Coordinates;
            var newCoordinates = new List<Coordinate>();

            for (int i = 0; i < coordinates.Length - 1; i++)
            {
                var segment = _factory.CreateLineString(new[] { coordinates[i], coordinates[i + 1] });

                if (replacements.TryGetValue(segment, out var replacedSegment))
                {
                    newCoordinates.AddRange(replacedSegment.Coordinates.Take(replacedSegment.NumPoints - 1));
                }
                else if (replacements.TryGetValue((LineString)segment.Reverse(), out var replacedSegmentToReverse))
                {
                    LineString reverseReplacedSegment = (LineString)replacedSegmentToReverse.Reverse();
                    newCoordinates.AddRange(reverseReplacedSegment.Coordinates.Take(reverseReplacedSegment.NumPoints - 1));
                }
                else
                {
                    newCoordinates.Add(coordinates[i]);
                }
            }
            newCoordinates.Add(newCoordinates[0].Copy());

            return _factory.CreateLinearRing(newCoordinates.ToArray());
        }
    }
}
