using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MapRegionizer.BoundaryServices;

internal class BorderCurver
{
    private readonly GeometryFactory _factory;
    private readonly MapOptions _options;

    public BorderCurver(GeometryFactory factory, MapOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public Dictionary<LineString, LineString> Distortion(List<LineString> borders)
    {
        var borderReplacements = new Dictionary<LineString, LineString>();
        for (int i = 0; i < borders.Count; i++)
        {
            var border = borders[i];
            var newLine = CurveLine(border);
            borderReplacements[border] = newLine;
        }

        return borderReplacements;
    }

    private LineString CurveLine(LineString line)
    {
        if (line.Length < _options.MinLineLenghtToCurve)
            return line;
        int points = (int)Math.Round(line.Length * _options.DistortionDetail, MidpointRounding.ToEven);
        if (points <= 0)
            return line;
        var newPoints = GetEquidistantPoints(line, points);
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
            coords.Insert(i + 1, newPoints[i]);
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
            for (int i = 0; i < pointsCount; i++)
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

    
}
