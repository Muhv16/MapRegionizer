using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer;

internal class BoundaryDistortioner
{
    private readonly GeometryFactory _factory;
    private readonly MapOptions _options;

    public List<LineString>? RegionalBoundaries { get; private set; }

    public BoundaryDistortioner(GeometryFactory factory, MapOptions options)
    {
        _factory = factory;
        _options = options;
    }

    private const int distortionDetail = 4;
    public List<Polygon> Distortion(List<Polygon> polygons)
    {
        var borders = FindSharedBorders(polygons);
        var borderReplacements = new Dictionary<LineString, LineString>();
        for (int i = 0; i < borders.Count; i++)
        {
            var border = borders[i];
            if (border.Length < 5)
                continue;
            var newPoints = GetEquidistantPoints(border, distortionDetail);
            var coords = border.Coordinates.ToList();

            for (int j = 0; j < newPoints.Count; j++)
            {
                newPoints[j].X += 1;
                newPoints[j].Y -= 1;
                coords.Insert(j + 1, newPoints[j]);
            }
            var newLine = _factory.CreateLineString(coords.ToArray());
            borderReplacements[border] = newLine;
        }

        RegionalBoundaries = new List<LineString>(borderReplacements.Values);
        var updatedPolygons = UpdatePolygons(polygons, borderReplacements);
        return updatedPolygons;
    }

    private List<Polygon> UpdatePolygons(List<Polygon> originalPolygons, Dictionary<LineString, LineString> borderReplacements)
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

    private static List<Coordinate> GetEquidistantPoints(LineString line, int numberOfIntermediatePoints)
    {
        if (numberOfIntermediatePoints <= 0)
            throw new ArgumentException("Количество точек должно быть больше 0");

        var points = new List<Coordinate>();
        double totalLength = line.Length;
        double segmentLength = totalLength / (numberOfIntermediatePoints + 1);

        var locIndex = new LengthLocationMap(line);

        for (int i = 1; i <= numberOfIntermediatePoints; i++)
        {
            double distance = segmentLength * i;
            var location = locIndex.GetLocation(distance);
            points.Add(location.GetCoordinate(line));
        }

        return points;
    }

    private List<LineString> FindSharedBorders(List<Polygon> polygons)
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
