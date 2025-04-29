using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Polygonize;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer;

internal static class PolygonExtensions
{
    public static List<Polygon> SplitPolygon(this Polygon polygon, bool isHorizontalSplit, bool equalByBBox = true, double tolerance = 1e-3)
    {
        var geomFactory = polygon.Factory;
        var env = polygon.EnvelopeInternal;

        // 1) определим координату раздела
        double minC, maxC, splitC;

        if (isHorizontalSplit)
        {
            minC = env.MinY;
            maxC = env.MaxY;
        }
        else
        {
            minC = env.MinX;
            maxC = env.MaxX;
        }

        if (equalByBBox)
        {
            splitC = (minC + maxC) / 2.0;
        }
        else
        {
            // бинарный поиск по площади
            double low = minC, high = maxC;
            double totalArea = polygon.Area;

            for (int iter = 0; iter < 50; iter++)
            {
                double mid = (low + high) / 2.0;
                var halves = BuildHalfSpaces(geomFactory, env, isHorizontalSplit, mid);
                double area1 = polygon.Intersection(halves.half1).Area;

                if (area1 / totalArea > 0.5)
                    high = mid;
                else
                    low = mid;
            }
            splitC = (low + high) / 2.0;
        }

        // 2) строим «половинные» прямоугольники
        var (half1, half2) = BuildHalfSpaces(geomFactory, env, isHorizontalSplit, splitC);

        // 3) усечение оригинала
        var part1 = polygon.Intersection(half1);
        var part2 = polygon.Intersection(half2);
        var result = new List<Polygon>();
        if (part1 is MultiPolygon parts1)
            result.AddRange(parts1.Cast<Polygon>());
        else result.Add((Polygon)part1);
        if (part2 is MultiPolygon parts2)
            result.AddRange(parts2.Cast<Polygon>());
        else result.Add((Polygon)part2);


        return result;
    }

    /// <summary>
    /// Строит два полупространства (в виде больших прямоугольников),
    /// разделённых линией на coord по горизонтали или вертикали.
    /// </summary>
    private static (Polygon half1, Polygon half2) BuildHalfSpaces(
        GeometryFactory f,
        Envelope env,
        bool horizontal,
        double coord)
    {
        double minX = env.MinX - 1, maxX = env.MaxX + 1;
        double minY = env.MinY - 1, maxY = env.MaxY + 1;

        if (horizontal)
        {
            // нижняя часть: Y ≤ coord
            var lower = f.CreatePolygon(new[]
            {
        new Coordinate(minX, minY),
        new Coordinate(maxX, minY),
        new Coordinate(maxX, coord),
        new Coordinate(minX, coord),
        new Coordinate(minX, minY)
    });
            // верхняя: Y ≥ coord
            var upper = f.CreatePolygon(new[]
            {
        new Coordinate(minX, coord),
        new Coordinate(maxX, coord),
        new Coordinate(maxX, maxY),
        new Coordinate(minX, maxY),
        new Coordinate(minX, coord)
    });
            return (lower, upper);
        }
        else
        {
            // левая часть: X ≤ coord
            var left = f.CreatePolygon(new[]
            {
        new Coordinate(minX, minY),
        new Coordinate(coord, minY),
        new Coordinate(coord, maxY),
        new Coordinate(minX, maxY),
        new Coordinate(minX, minY)
    });
            // правая часть: X ≥ coord
            var right = f.CreatePolygon(new[]
            {
        new Coordinate(coord, minY),
        new Coordinate(maxX, minY),
        new Coordinate(maxX, maxY),
        new Coordinate(coord, maxY),
        new Coordinate(coord, minY)
    });
            return (left, right);
        }
    }
}
