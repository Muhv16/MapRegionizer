using MapRegionizer.Core.Domain;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;

namespace MapRegionizer.Core.Terrain;

internal static class VisibleRiverCrossingRepairer
{
    private const double MinimumCrossingAngleDegrees = 35.0;
    private const double GeometryEpsilon = 0.000001;
    private const double TouchTolerance = 0.15;

    internal static List<RiverSegment> ResolvePolylineCrossings(IReadOnlyList<RiverSegment> rivers, int width)
    {
        var result = rivers.ToList();
        for (var pass = 0; pass < 64; pass++)
        {
            var contact = FindFirstInvalidContact(result, width);
            if (!contact.HasValue)
                break;

            var value = contact.Value;
            var weak = ChooseWeakRiver(value.First.River, value.Second.River);
            var weakSegment = weak.Id == value.First.River.Id ? value.First : value.Second;
            var strong = weak.Id == value.First.River.Id ? value.Second.River : value.First.River;
            var repaired = ConvertCrossingToConfluence(weak, weakSegment, strong, value.Point, width);
            var index = result.FindIndex(r => r.Id == weak.Id);
            if (index < 0)
                break;

            if (repaired is null)
                result.RemoveAt(index);
            else
                result[index] = repaired;
        }

        return result;
    }

    internal static int CountPolylineCrossings(IReadOnlyList<RiverSegment> rivers, int width) =>
        EnumerateInvalidContacts(rivers, width).Count();

    private static RiverContact? FindFirstInvalidContact(IReadOnlyList<RiverSegment> rivers, int width)
    {
        RiverContact? best = null;
        foreach (var contact in EnumerateInvalidContacts(rivers, width))
        {
            if (!best.HasValue)
            {
                best = contact;
                continue;
            }

            var crossingPriority = CrossingPriority(contact);
            var bestPriority = CrossingPriority(best.Value);
            if (crossingPriority > bestPriority ||
                Math.Abs(crossingPriority - bestPriority) <= 0.001 && contact.AngleDegrees > best.Value.AngleDegrees)
            {
                best = contact;
            }
        }

        return best;
    }

    private static double CrossingPriority(RiverContact crossing)
    {
        var parentChildBonus =
            crossing.First.River.ParentRiverId == crossing.Second.River.Id ||
            crossing.Second.River.ParentRiverId == crossing.First.River.Id
                ? 100000.0
                : 0.0;
        var endorheicBonus = crossing.First.River.Kind == RiverKind.Endorheic || crossing.Second.River.Kind == RiverKind.Endorheic
            ? 25000.0
            : 0.0;
        return parentChildBonus + endorheicBonus + Math.Min(crossing.First.River.Discharge, crossing.Second.River.Discharge);
    }

    private static IEnumerable<RiverContact> EnumerateInvalidContacts(IReadOnlyList<RiverSegment> rivers, int width)
    {
        var segments = rivers.SelectMany(r => BuildSegments(r, width)).ToList();
        for (var i = 0; i < segments.Count; i++)
        {
            var first = segments[i];
            for (var j = i + 1; j < segments.Count; j++)
            {
                var second = segments[j];
                if (first.River.Id == second.River.Id)
                    continue;
                if (!BoundingBoxesOverlap(first, second))
                    continue;
                if (!TryFindContact(first, second, out var point))
                    continue;

                var angle = CrossingAngleDegrees(first, second);
                if (IsAllowedConfluenceTouch(point, first.River, second.River, width))
                    continue;
                if (angle < MinimumCrossingAngleDegrees && !IsParentChild(first.River, second.River))
                    continue;

                yield return new RiverContact(first, second, point, angle);
            }
        }
    }

    private static IEnumerable<RiverPolylineSegment> BuildSegments(RiverSegment river, int width)
    {
        for (var i = 0; i < river.Polyline.Count - 1; i++)
        {
            var a = river.Polyline[i];
            var b = river.Polyline[i + 1];
            if (Math.Abs(b.X - a.X) > width / 2.0)
                continue;
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (length <= 0.001)
                continue;

            yield return new RiverPolylineSegment(river, i, a, b, length);
        }
    }

    private static RiverSegment? ConvertCrossingToConfluence(
        RiverSegment weak,
        RiverPolylineSegment weakSegment,
        RiverSegment strong,
        MapPoint crossing,
        int width)
    {
        if (weak.Cells.Count < 2 || strong.Cells.Count == 0)
            return null;

        var mouth = strong.Cells
            .OrderBy(c => DistanceToPoint(c, crossing, width))
            .ThenBy(c => HydrologyGridMath.Distance(c, strong.Mouth, width))
            .First();
        var cutIndex = ChooseWeakCutIndex(weak, crossing, width);
        if (cutIndex < 1)
            return null;

        var cells = weak.Cells.Take(cutIndex + 1).ToList();
        if (cells.Count < 3)
            return null;

        var polyline = BuildConfluencePolyline(weak, weakSegment.SegmentIndex, crossing);
        return weak with
        {
            Cells = cells,
            Polyline = polyline,
            Mouth = mouth,
            DrainageTerminal = strong.DrainageTerminal,
            TargetKind = strong.TargetKind,
            TargetId = strong.TargetId,
            LengthCells = Math.Round((double)cells.Count, 2),
            MouthKind = RiverMouthKind.SimpleMouth
        };
    }

    private static int ChooseWeakCutIndex(RiverSegment weak, MapPoint crossing, int width)
    {
        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < weak.Cells.Count; i++)
        {
            var distance = DistanceToPoint(weak.Cells[i], crossing, width);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static List<MapPoint> BuildConfluencePolyline(RiverSegment river, int crossingSegmentIndex, MapPoint crossing)
    {
        var takeCount = Math.Clamp(crossingSegmentIndex + 1, 1, river.Polyline.Count);
        var polyline = river.Polyline.Take(takeCount).ToList();
        if (polyline.Count == 0 || Distance(polyline[^1], crossing) > 0.001)
            polyline.Add(crossing);
        return polyline;
    }

    private static RiverSegment ChooseWeakRiver(RiverSegment first, RiverSegment second)
    {
        if (first.ParentRiverId == second.Id)
            return first;
        if (second.ParentRiverId == first.Id)
            return second;
        if (Math.Abs(first.Discharge - second.Discharge) > 0.001)
            return first.Discharge < second.Discharge ? first : second;
        if (first.Cells.Count != second.Cells.Count)
            return first.Cells.Count < second.Cells.Count ? first : second;
        return first.Id > second.Id ? first : second;
    }

    private static bool BoundingBoxesOverlap(RiverPolylineSegment first, RiverPolylineSegment second) =>
        Math.Max(Math.Min(first.A.X, first.B.X), Math.Min(second.A.X, second.B.X)) <= Math.Min(Math.Max(first.A.X, first.B.X), Math.Max(second.A.X, second.B.X)) + TouchTolerance &&
        Math.Max(Math.Min(first.A.Y, first.B.Y), Math.Min(second.A.Y, second.B.Y)) <= Math.Min(Math.Max(first.A.Y, first.B.Y), Math.Max(second.A.Y, second.B.Y)) + TouchTolerance;

    private static bool TryFindContact(RiverPolylineSegment first, RiverPolylineSegment second, out MapPoint point)
    {
        var x1 = first.A.X;
        var y1 = first.A.Y;
        var x2 = first.B.X;
        var y2 = first.B.Y;
        var x3 = second.A.X;
        var y3 = second.A.Y;
        var x4 = second.B.X;
        var y4 = second.B.Y;
        var denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denominator) > GeometryEpsilon)
        {
            var px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denominator;
            var py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denominator;
            var firstT = SegmentParameter(first.A, first.B, px, py);
            var secondT = SegmentParameter(second.A, second.B, px, py);
            if (firstT < -GeometryEpsilon || firstT > 1.0 + GeometryEpsilon ||
                secondT < -GeometryEpsilon || secondT > 1.0 + GeometryEpsilon)
            {
                point = default;
                return false;
            }

            point = new MapPoint(px, py);
            return true;
        }

        foreach (var candidate in new[] { first.A, first.B })
        {
            if (DistanceToSegment(candidate, second.A, second.B) <= TouchTolerance)
            {
                point = candidate;
                return true;
            }
        }

        foreach (var candidate in new[] { second.A, second.B })
        {
            if (DistanceToSegment(candidate, first.A, first.B) <= TouchTolerance)
            {
                point = candidate;
                return true;
            }
        }

        point = default;
        return false;
    }

    private static double SegmentParameter(MapPoint a, MapPoint b, double x, double y) =>
        Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)
            ? (x - a.X) / (b.X - a.X)
            : (y - a.Y) / (b.Y - a.Y);

    private static double CrossingAngleDegrees(RiverPolylineSegment first, RiverPolylineSegment second)
    {
        var ax = first.B.X - first.A.X;
        var ay = first.B.Y - first.A.Y;
        var bx = second.B.X - second.A.X;
        var by = second.B.Y - second.A.Y;
        var cosine = Math.Abs(ax * bx + ay * by) / Math.Max(0.000001, first.Length * second.Length);
        return Math.Acos(Math.Clamp(cosine, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    private static bool IsAllowedConfluenceTouch(MapPoint point, RiverSegment first, RiverSegment second, int width)
    {
        var firstIsChild = first.ParentRiverId == second.Id;
        var secondIsChild = second.ParentRiverId == first.Id;
        if (firstIsChild || secondIsChild)
        {
            var child = firstIsChild ? first : second;
            return Distance(child.Polyline[^1], point) <= TouchTolerance;
        }

        return Distance(first.Polyline[^1], point) <= TouchTolerance && second.Cells.Any(c => DistanceToPoint(c, point, width) <= 1.05) ||
               Distance(second.Polyline[^1], point) <= TouchTolerance && first.Cells.Any(c => DistanceToPoint(c, point, width) <= 1.05);
    }

    private static bool IsParentChild(RiverSegment first, RiverSegment second) =>
        first.ParentRiverId == second.Id || second.ParentRiverId == first.Id;

    private static double DistanceToSegment(MapPoint point, MapPoint start, MapPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= GeometryEpsilon)
            return Distance(point, start);

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var projected = new MapPoint(start.X + dx * t, start.Y + dy * t);
        return Distance(point, projected);
    }

    private static double DistanceToPoint(GridPoint cell, MapPoint point, int width)
    {
        var dx = WrappedDeltaX((cell.X + 0.5) - point.X, width);
        var dy = cell.Y + 0.5 - point.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double WrappedDeltaX(double dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private readonly record struct RiverPolylineSegment(RiverSegment River, int SegmentIndex, MapPoint A, MapPoint B, double Length);

    private readonly record struct RiverContact(RiverPolylineSegment First, RiverPolylineSegment Second, MapPoint Point, double AngleDegrees);
}
