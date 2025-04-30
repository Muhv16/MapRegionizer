using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

    public List<Polygon> Distortion(List<Polygon> polygons)
    {
        var borders = FindSharedBorders(polygons);
        var borderReplacements = new Dictionary<LineString, LineString>();
        for (int i = 0; i < borders.Count; i++)
        {
            var border = borders[i];
            var newLine = CurveLine(border);
            borderReplacements[border] = newLine;
        }

        RegionalBoundaries = new List<LineString>(borderReplacements.Values);
        var updatedPolygons = UpdatePolygons(polygons, borderReplacements);
        return updatedPolygons;
    }

    private LineString CurveLine(LineString line)
    {
        if (line.Length < _options.MinLineLenghtToCurve)
            return line;

        var newPoints = GetEquidistantPoints(line, (int)Math.Round((line.Length * _options.DistortionDetail), MidpointRounding.ToEven));
        if (newPoints.Count == 0)
            return line;


        var coords = line.Coordinates.ToList();
        var yOffsets = new double[newPoints.Count];
        var xOffsets = new double[newPoints.Count];

        if (Math.Abs(line.EndPoint.X - line.StartPoint.X) > _options.MinLineLenghtToCurve / 3)
        {
            yOffsets = GenerateCurveOffsets(newPoints.Count);
        }
        if (Math.Abs(line.EndPoint.Y - line.StartPoint.Y) > _options.MinLineLenghtToCurve / 3)
        {
            xOffsets = GenerateCurveOffsets(newPoints.Count);
        }
        
        var random = new Random();
        var offsetDir = random.Next(0, 2) == 0 ? -1 : 1;
        for (int i = 0; i < newPoints.Count; i++)
        {
            newPoints[i].X += xOffsets[i] * offsetDir;
            newPoints[i].Y += yOffsets[i] * offsetDir;
            coords.Insert(i+1, newPoints[i]);
        }
        var newLine = _factory.CreateLineString(coords.ToArray());
        return newLine;
    }

    /// <summary>
    /// Генерация отступов для искривления прямых
    /// </summary>
    /// <param name="pointsCount"></param>
    /// <returns></returns>
    private double[] GenerateCurveOffsets(int pointsCount)
    {
        double noiseStrength = 0.2;

        var result = new double[pointsCount];
        if (pointsCount < 0)
            return result;
        Random random = new Random();
        if (pointsCount <= 3)
        {
            for (int i = 0; i < pointsCount;i++)
            {
                result[i] = random.Next(-1, 2);
            }
        }
        else
        {
            //Параболоподобный набор отступов
            int centerIndex = result.Length / 2;
            bool evenLength = result.Length % 2 == 0;
            Random rand = new Random();

            double stretchFactor = 1.0 / (centerIndex * centerIndex);
            if (evenLength) stretchFactor = 1.0 / ((centerIndex - 0.5) * (centerIndex - 0.5));

            for (int i = 0; i < result.Length; i++)
            {
                double distance = i - centerIndex;
                if (evenLength) distance = i - centerIndex + 0.5;
                double value = _options.MaxOffst * (1 - stretchFactor * distance * distance);

                // Шум для длинных прямых
                if (pointsCount > 9 && Math.Abs(distance) > 1)
                {
                    double noise = (rand.NextDouble() - 0.5) * noiseStrength;
                    value += noise;
                }
                result[i] = Math.Max(value, 0.1);
            }

            // Корректировка для четных массивов
            if (evenLength)
            {
                result[centerIndex - 1] = Math.Max(result[centerIndex - 1], result[centerIndex]);
            }
        }
        return result;

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
