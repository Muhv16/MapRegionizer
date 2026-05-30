using MapRegionizer.Core.Domain;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;

namespace MapRegionizer.Core.Terrain;

internal static class RiverPathGeometryValidator
{
    private const double GeometryEpsilon = 0.000001;
    private const double TouchTolerance = 0.000001;

    internal static bool IsSimpleCellPath(IReadOnlyList<GridPoint> cells, int width) =>
        !HasDuplicateCells(cells) && !TryFindCellSelfIntersection(cells, width, out _);

    internal static bool HasDuplicateCells(IReadOnlyList<GridPoint> cells)
    {
        var seen = new HashSet<GridPoint>();
        foreach (var cell in cells)
        {
            if (!seen.Add(cell))
                return true;
        }

        return false;
    }

    internal static bool TryFindDuplicateCellLoop(IReadOnlyList<GridPoint> cells, out CellLoop loop)
    {
        var firstSeen = new Dictionary<GridPoint, int>();
        for (var i = 0; i < cells.Count; i++)
        {
            if (firstSeen.TryGetValue(cells[i], out var first))
            {
                loop = new CellLoop(cells[i], first, i);
                return true;
            }

            firstSeen[cells[i]] = i;
        }

        loop = default;
        return false;
    }

    internal static bool TryFindCellSelfIntersection(IReadOnlyList<GridPoint> cells, int width, out SegmentPair crossing)
    {
        for (var i = 0; i < cells.Count - 1; i++)
        {
            var first = CellSegment(cells[i], cells[i + 1], width);
            if (!first.HasValue)
                continue;

            for (var j = i + 2; j < cells.Count - 1; j++)
            {
                var second = CellSegment(cells[j], cells[j + 1], width);
                if (!second.HasValue)
                    continue;
                if (SegmentsShareOnlyAllowedEndpoint(first.Value.A, first.Value.B, second.Value.A, second.Value.B, i, j))
                    continue;
                if (!SegmentsIntersect(first.Value.A, first.Value.B, second.Value.A, second.Value.B, TouchTolerance))
                    continue;

                crossing = new SegmentPair(i, j);
                return true;
            }
        }

        crossing = default;
        return false;
    }

    internal static bool IsSimplePolyline(IReadOnlyList<MapPoint> polyline, int width) =>
        !TryFindPolylineSelfIntersection(polyline, width, out _);

    internal static bool TryFindPolylineSelfIntersection(IReadOnlyList<MapPoint> polyline, int width, out SegmentPair crossing)
    {
        for (var i = 0; i < polyline.Count - 1; i++)
        {
            if (IsWrapBreak(polyline[i], polyline[i + 1], width))
                continue;

            for (var j = i + 2; j < polyline.Count - 1; j++)
            {
                if (IsWrapBreak(polyline[j], polyline[j + 1], width))
                    continue;
                if (SegmentsShareOnlyAllowedEndpoint(polyline[i], polyline[i + 1], polyline[j], polyline[j + 1], i, j))
                    continue;
                if (!SegmentsIntersect(polyline[i], polyline[i + 1], polyline[j], polyline[j + 1], TouchTolerance))
                    continue;

                crossing = new SegmentPair(i, j);
                return true;
            }
        }

        crossing = default;
        return false;
    }

    private static (MapPoint A, MapPoint B)? CellSegment(GridPoint a, GridPoint b, int width)
    {
        if (Math.Abs(WrappedDeltaX(b.X - a.X, width)) > 1)
            return null;

        return (new MapPoint(a.X + 0.5, a.Y + 0.5), new MapPoint(b.X + 0.5, b.Y + 0.5));
    }

    private static bool IsWrapBreak(MapPoint a, MapPoint b, int width) =>
        Math.Abs(b.X - a.X) > width / 2.0;

    private static bool SegmentsShareOnlyAllowedEndpoint(MapPoint a, MapPoint b, MapPoint c, MapPoint d, int firstIndex, int secondIndex)
    {
        if (Math.Abs(firstIndex - secondIndex) <= 1)
            return true;

        return false;
    }

    private static bool SegmentsIntersect(MapPoint a, MapPoint b, MapPoint c, MapPoint d, double tolerance)
    {
        if (!BoundingBoxesOverlap(a, b, c, d, tolerance))
            return false;

        var denominator = (a.X - b.X) * (c.Y - d.Y) - (a.Y - b.Y) * (c.X - d.X);
        if (Math.Abs(denominator) > GeometryEpsilon)
        {
            var px = ((a.X * b.Y - a.Y * b.X) * (c.X - d.X) - (a.X - b.X) * (c.X * d.Y - c.Y * d.X)) / denominator;
            var py = ((a.X * b.Y - a.Y * b.X) * (c.Y - d.Y) - (a.Y - b.Y) * (c.X * d.Y - c.Y * d.X)) / denominator;
            return SegmentParameter(a, b, px, py) >= -GeometryEpsilon &&
                   SegmentParameter(a, b, px, py) <= 1.0 + GeometryEpsilon &&
                   SegmentParameter(c, d, px, py) >= -GeometryEpsilon &&
                   SegmentParameter(c, d, px, py) <= 1.0 + GeometryEpsilon;
        }

        return DistanceToSegment(a, c, d) <= tolerance ||
               DistanceToSegment(b, c, d) <= tolerance ||
               DistanceToSegment(c, a, b) <= tolerance ||
               DistanceToSegment(d, a, b) <= tolerance;
    }

    private static bool BoundingBoxesOverlap(MapPoint a, MapPoint b, MapPoint c, MapPoint d, double tolerance) =>
        Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + tolerance &&
        Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + tolerance;

    private static double SegmentParameter(MapPoint a, MapPoint b, double x, double y) =>
        Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)
            ? Math.Abs(b.X - a.X) <= GeometryEpsilon ? 0.0 : (x - a.X) / (b.X - a.X)
            : Math.Abs(b.Y - a.Y) <= GeometryEpsilon ? 0.0 : (y - a.Y) / (b.Y - a.Y);

    private static double DistanceToSegment(MapPoint point, MapPoint start, MapPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= GeometryEpsilon)
            return Distance(point, start);

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        return Distance(point, new MapPoint(start.X + dx * t, start.Y + dy * t));
    }

    private static double Distance(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    internal readonly record struct CellLoop(GridPoint Cell, int FirstIndex, int LastIndex);

    internal readonly record struct SegmentPair(int FirstIndex, int SecondIndex);
}
