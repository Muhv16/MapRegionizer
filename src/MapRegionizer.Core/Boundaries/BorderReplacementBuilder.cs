using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace MapRegionizer.Core.Boundaries;

internal sealed class BorderReplacementBuilder
{
    private readonly GeometryFactory _geometryFactory;
    private readonly Random _random;

    public BorderReplacementBuilder(GeometryFactory geometryFactory, Random random)
    {
        _geometryFactory = geometryFactory;
        _random = random;
    }

    public IReadOnlyDictionary<string, LineString> BuildReplacements(IEnumerable<LineString> borders, Polygon landmass, BoundaryDistortionOptions options)
    {
        var replacements = new Dictionary<string, LineString>(StringComparer.Ordinal);

        foreach (var border in borders)
        {
            var curved = CurveLine(border, landmass, options);
            var coordinates = border.Coordinates;
            var indexed = new LengthIndexedLine(curved);

            for (var i = 0; i < coordinates.Length - 1; i++)
            {
                var a = coordinates[i];
                var b = coordinates[i + 1];
                var start = indexed.Project(a);
                var end = indexed.Project(b);

                var segment = ExtractReplacementSegment(indexed, a, b, start, end);
                replacements.TryAdd(BoundaryKey.MakeDirected(a, b), segment);
                replacements.TryAdd(BoundaryKey.MakeDirected(b, a), (LineString)segment.Reverse());
            }
        }

        return replacements;
    }

    private LineString ExtractReplacementSegment(LengthIndexedLine indexed, Coordinate a, Coordinate b, double start, double end)
    {
        if (Math.Abs(start - end) < 1e-9)
            return _geometryFactory.CreateLineString(new[] { a.Copy(), b.Copy() });

        var extracted = indexed.ExtractLine(Math.Min(start, end), Math.Max(start, end));
        return extracted as LineString ?? _geometryFactory.CreateLineString(new[] { a.Copy(), b.Copy() });
    }

    private LineString CurveLine(LineString line, Polygon landmass, BoundaryDistortionOptions options)
    {
        if (line.Length < options.MinLineLengthToCurve)
            return line;

        var pointCount = (int)Math.Round(line.Length * options.Detail, MidpointRounding.ToEven);
        if (pointCount <= 0)
            return line;

        var intermediatePoints = GetEquidistantPoints(line, pointCount);
        if (intermediatePoints.Count == 0)
            return line;

        var coordinates = line.Coordinates.ToList();
        var yOffsets = Math.Abs(line.EndPoint.X - line.StartPoint.X) > options.MinLineLengthToCurve / 3 ? GenerateCurveOffsets(pointCount, options.MaxOffset) : new double[pointCount];
        var xOffsets = Math.Abs(line.EndPoint.Y - line.StartPoint.Y) > options.MinLineLengthToCurve / 3 ? GenerateCurveOffsets(pointCount, options.MaxOffset) : new double[pointCount];
        var direction = _random.Next(0, 2) == 0 ? -1 : 1;

        for (var i = 0; i < intermediatePoints.Count; i++)
        {
            intermediatePoints[i].X += xOffsets[i] * direction;
            intermediatePoints[i].Y += yOffsets[i] * direction;
            coordinates.Insert(i + 1, intermediatePoints[i]);
        }

        var curved = _geometryFactory.CreateLineString(coordinates.ToArray());
        if (landmass.Covers(curved))
            return curved;

        var clipped = curved.Intersection(landmass);
        return clipped switch
        {
            LineString safeLine => safeLine,
            MultiLineString multiLine when multiLine.NumGeometries > 0 && multiLine.GetGeometryN(0) is LineString first => first,
            _ => line
        };
    }

    private double[] GenerateCurveOffsets(int pointCount, double maxOffset)
    {
        var result = new double[pointCount];
        if (pointCount <= 3)
        {
            for (var i = 0; i < pointCount; i++)
                result[i] = _random.Next(-1, 2);
            return result;
        }

        var centerIndex = result.Length / 2;
        var evenLength = result.Length % 2 == 0;
        var stretchFactor = evenLength ? 1.0 / ((centerIndex - 0.5) * (centerIndex - 0.5)) : 1.0 / (centerIndex * centerIndex);

        for (var i = 0; i < result.Length; i++)
        {
            var distance = evenLength ? i - centerIndex + 0.5 : i - centerIndex;
            var value = maxOffset * (1 - stretchFactor * distance * distance);
            if (pointCount > 9 && Math.Abs(distance) > 1)
                value += (_random.NextDouble() - 0.5) * 0.2;

            result[i] = Math.Max(value, 0.1);
        }

        if (evenLength)
            result[centerIndex - 1] = Math.Max(result[centerIndex - 1], result[centerIndex]);

        return result;
    }

    private static List<Coordinate> GetEquidistantPoints(LineString line, int pointCount)
    {
        var points = new List<Coordinate>();
        var segmentLength = line.Length / (pointCount + 1);
        var lengthMap = new LengthLocationMap(line);

        for (var i = 1; i <= pointCount; i++)
            points.Add(lengthMap.GetLocation(segmentLength * i).GetCoordinate(line));

        return points;
    }
}
