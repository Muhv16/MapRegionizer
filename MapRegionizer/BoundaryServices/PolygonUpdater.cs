using NetTopologySuite.Geometries;
using System.Globalization;

namespace MapRegionizer.BoundaryServices
{
    internal class PolygonUpdater
    {
        private readonly GeometryFactory _factory;
        private const int KeyDecimalPlaces = 6;

        public PolygonUpdater(GeometryFactory factory)
        {
            _factory = factory;
        }

        public List<Polygon> UpdatePolygons(List<Polygon> originalPolygons, Dictionary<string, LineString> borderReplacements)
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

        private LinearRing UpdateRing(LinearRing ring, Dictionary<string, LineString> replacements)
        {
            var coordinates = ring.Coordinates;
            var newCoordinates = new List<Coordinate>();

            for (int i = 0; i < coordinates.Length - 1; i++)
            {
                var a = coordinates[i];
                var b = coordinates[i + 1];
                var key = MakeKey(a, b);

                if (replacements.TryGetValue(key, out var replacedSegment))
                {
                    // Добавляем все точки заменённого отрезка, кроме последней (чтобы не дублировать при следующем отрезке)
                    var ptsToAdd = replacedSegment.Coordinates.Take(replacedSegment.NumPoints - 1);
                    foreach (var c in ptsToAdd)
                        newCoordinates.Add(c.Copy());
                }
                else
                {
                    // Если замены нет — просто добавляем начальную точку сегмента
                    newCoordinates.Add(a.Copy());
                }
            }

            // Закрываем кольцо
            if (newCoordinates.Count == 0)
            {
                // fallback: возьмём оригинальные координаты
                return _factory.CreateLinearRing(coordinates);
            }

            // Добавляем закрывающуюся точку (копию первой)
            newCoordinates.Add(newCoordinates[0].Copy());

            return _factory.CreateLinearRing(newCoordinates.ToArray());
        }

        private string MakeKey(Coordinate a, Coordinate b)
        {
            string fmt = "F" + KeyDecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var s1 = a.X.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) + ":" + a.Y.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            var s2 = b.X.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) + ":" + b.Y.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            return s1 + "|" + s2;
        }
    }
}