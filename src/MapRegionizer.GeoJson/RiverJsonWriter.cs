using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class RiverJsonWriter
{
    private const double GeometryEpsilon = 0.000001;
    private const double TouchTolerance = 0.15;

    public static string Write(HydrologyMap hydrology, RiverJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        options ??= new RiverJsonExportOptions();
        return JsonSerializer.Serialize(ToDto(hydrology, options), CreateSerializerOptions(options));
    }

    public static string Write(GeneratedMap map, RiverJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Hydrology is null)
            throw new InvalidOperationException("The map does not contain hydrology data.");

        return Write(map.Hydrology, options);
    }

    public static void WriteToFile(HydrologyMap hydrology, string filePath, RiverJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(hydrology, options));

    public static void WriteToFile(GeneratedMap map, string filePath, RiverJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(map, options));

    private static JsonSerializerOptions CreateSerializerOptions(RiverJsonExportOptions options) =>
        new()
        {
            WriteIndented = options.WriteIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

    private static RiverMapDto ToDto(HydrologyMap hydrology, RiverJsonExportOptions options)
    {
        var rivers = hydrology.Rivers
            .OrderBy(r => r.Id)
            .Select(r => ToRiverDto(r, options))
            .ToList();
        var mouths = hydrology.Mouths
            .OrderBy(m => m.RiverId)
            .Select(m => new RiverMouthDto(
                m.RiverId,
                ToPoint(m.Cell),
                m.TargetKind,
                m.TargetId,
                m.Kind,
                Math.Round(m.Discharge, 3)))
            .ToList();
        var outlets = hydrology.LakeOutlets
            .OrderBy(o => o.LakeId.Value)
            .Select(o => new LakeOutletDto(
                o.LakeId.Value,
                o.HasOutlet,
                o.OutletCell.HasValue ? ToPoint(o.OutletCell.Value) : null,
                o.DownstreamCell.HasValue ? ToPoint(o.DownstreamCell.Value) : null,
                Math.Round(o.SpillElevationMeters, 2),
                Math.Round(o.BreachCost, 3),
                Math.Round(o.OutletScore, 6)))
            .ToList();
        var basins = hydrology.Basins
            .OrderBy(b => b.Id)
            .Select(b => new DrainageBasinDto(
                b.Id,
                b.TargetKind,
                b.TargetId,
                ToPoint(b.TerminalCell),
                b.CellCount,
                Math.Round(b.TotalRunoff, 3)))
            .ToList();

        var quality = BuildQuality(hydrology, options);
        var outletLakeIds = hydrology.LakeOutlets.Where(o => o.HasOutlet).Select(o => o.LakeId.Value).ToHashSet();

        return new RiverMapDto(
            hydrology.Width,
            hydrology.Height,
            new RiverSummaryDto(
                rivers.Count,
                hydrology.Rivers.Count(r => r.IsMajor || IsMajorRiver(r, options, DynamicShortRiverLimit(hydrology))),
                basins.Count(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                rivers.Count(r => r.TargetKind == DrainageTargetKind.Ocean),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea),
                rivers.Count(r => r.Kind == RiverKind.Endorheic),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
                                  (!r.TargetId.HasValue || !outletLakeIds.Contains(r.TargetId.Value))),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
                                  r.TargetId.HasValue && outletLakeIds.Contains(r.TargetId.Value)),
                rivers.Count(r => r.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                mouths.Count(m => m.Kind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta),
                Math.Round(rivers.Count == 0 ? 0 : rivers.Average(r => r.LengthCells), 2),
                Math.Round(rivers.Count == 0 ? 0 : rivers.Max(r => r.LengthCells), 2),
                outlets.Count(o => o.HasOutlet),
                outlets.Count(o => !o.HasOutlet)),
            quality,
            rivers,
            mouths,
            outlets,
            basins,
            options.IncludeDiagnosticRasters ? EncodeRows(hydrology, hydrology.GetHydroSurface, 1) : null,
            options.IncludeDiagnosticRasters ? EncodeRows(hydrology, hydrology.GetFlowAccumulation, 1) : null,
            options.IncludeDiagnosticRasters ? EncodeIntRows(hydrology, hydrology.GetDrainageBasinId) : null);
    }

    private static RiverDto ToRiverDto(RiverSegment river, RiverJsonExportOptions options)
    {
        return new RiverDto(
            river.Id,
            river.Kind,
            river.MouthKind,
            river.LandComponentId,
            river.TargetKind,
            river.TargetId,
            ToPoint(river.Source),
            ToPoint(river.Mouth),
            ToPoint(river.DrainageTerminal),
            Math.Round(river.Discharge, 3),
            Math.Round(river.LengthCells, 2),
            Math.Round(river.MeanSlope, 5),
            river.Order,
            river.IsMajor,
            Math.Round(river.VisibleRank, 4),
            river.ParentRiverId,
            river.TributaryIds is { Count: > 0 } ? river.TributaryIds : null,
            river.Cells.Count,
            options.IncludeCellPaths ? river.Cells.Select(ToPoint).ToList() : null,
            river.Polyline.Select(ToPoint).ToList());
    }

    private static IReadOnlyList<string> EncodeRows(HydrologyMap hydrology, Func<int, int, double> readValue, int binSize)
    {
        var rows = new string[hydrology.Height];
        var sb = new StringBuilder();
        for (var y = 0; y < hydrology.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = Code(readValue(0, y), binSize);
            for (var x = 1; x < hydrology.Width; x++)
            {
                var value = Code(readValue(x, y), binSize);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, hydrology.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeIntRows(HydrologyMap hydrology, Func<int, int, int> readValue)
    {
        var rows = new string[hydrology.Height];
        var sb = new StringBuilder();
        for (var y = 0; y < hydrology.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = readValue(0, y).ToString(CultureInfo.InvariantCulture);
            for (var x = 1; x < hydrology.Width; x++)
            {
                var value = readValue(x, y).ToString(CultureInfo.InvariantCulture);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, hydrology.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static void AppendRun(StringBuilder sb, string value, int length)
    {
        if (sb.Length > 0)
            sb.Append(',');

        sb.Append(value).Append('x').Append(length);
    }

    private static string Code(double value, int binSize)
    {
        var bin = (int)Math.Round(value / Math.Max(1, binSize));
        return bin.ToString(CultureInfo.InvariantCulture);
    }

    private static PointDto ToPoint(GridPoint point) => new(point.X, point.Y);

    private static MapPointDto ToPoint(MapPoint point) => new(Math.Round(point.X, 3), Math.Round(point.Y, 3));

    private sealed record RiverMapDto(
        int Width,
        int Height,
        RiverSummaryDto Summary,
        RiverQualityDto Quality,
        IReadOnlyList<RiverDto> Rivers,
        IReadOnlyList<RiverMouthDto> Mouths,
        IReadOnlyList<LakeOutletDto> LakeOutlets,
        IReadOnlyList<DrainageBasinDto> Basins,
        IReadOnlyList<string>? HydroSurfaceRows,
        IReadOnlyList<string>? FlowAccumulationRows,
        IReadOnlyList<string>? DrainageBasinRows);

    private sealed record RiverSummaryDto(
        int RiverCount,
        int MajorRiverCount,
        int EndorheicBasinCount,
        int OceanRiverCount,
        int LakeRiverCount,
        int EndorheicRiverCount,
        int LakeInflowRiverCount,
        int ClosedLakeRiverCount,
        int OpenLakeRiverCount,
        int DryBasinRiverCount,
        int DeltaCount,
        double MeanLengthCells,
        double MaxLengthCells,
        int LakeOutletCount,
        int EndorheicLakeCount);

    private static RiverQualityDto BuildQuality(HydrologyMap hydrology, RiverJsonExportOptions options)
    {
        var shortLimit = DynamicShortRiverLimit(hydrology);
        var confluenceCount = hydrology.Rivers
            .Count(r => hydrology.Rivers.Any(other => other.Id != r.Id && other.Cells.Any(c => c == r.Mouth)));
        var majorCount = hydrology.Rivers.Count(r => r.IsMajor || IsMajorRiver(r, options, shortLimit));
        var straightRuns = hydrology.Rivers.SelectMany(r => DetectStraightRuns(r.Cells, 6)).ToList();
        var zigZagRuns = hydrology.Rivers.Select(r => MaxAlternatingZigZagRun(r.Cells)).ToList();
        var curvature = hydrology.Rivers.Select(r => MeanCurvature(r.Cells)).ToList();
        var sharpTurns = hydrology.Rivers.Sum(r => CountTurns(r.Cells, minimumDelta: 3));
        var backtrackTurns = hydrology.Rivers.Sum(r => CountTurns(r.Cells, minimumDelta: 4));
        var expectedEndorheic = hydrology.Rivers.Count(r => r.Kind == RiverKind.Endorheic);
        var crossingRiverEdges = CountCrossingRiverEdges(hydrology);
        var polylineCrossings = CountPolylineCrossings(hydrology.Rivers, hydrology.Width);
        var detached = hydrology.Rivers.Count(r =>
            r.TargetKind != DrainageTargetKind.Ocean &&
            r.TargetKind != DrainageTargetKind.Lake &&
            r.TargetKind != DrainageTargetKind.InlandSea &&
            r.TargetKind != DrainageTargetKind.EndorheicDryBasin);

        return new RiverQualityDto(
            straightRuns.Count,
            straightRuns.Count == 0 ? 0 : straightRuns.Max(),
            hydrology.Rivers.Count(r => r.Cells.Count <= shortLimit),
            detached,
            confluenceCount,
            Math.Round(majorCount == 0 ? 0.0 : confluenceCount / (double)majorCount, 3),
            expectedEndorheic,
            zigZagRuns.Count == 0 ? 0 : zigZagRuns.Max(),
            Math.Round(curvature.Count == 0 ? 0.0 : curvature.Average(), 4),
            sharpTurns,
            backtrackTurns,
            crossingRiverEdges,
            polylineCrossings);
    }

    private static bool IsMajorRiver(RiverSegment river, RiverJsonExportOptions options, int shortLimit) =>
        river.Discharge >= options.MajorRiverDischargeThreshold &&
        river.Cells.Count > shortLimit &&
        !(river.MeanSlope > 16.0 && river.Discharge < options.MajorRiverDischargeThreshold * 1.35);

    private static int CountCrossingRiverEdges(HydrologyMap hydrology)
    {
        var edges = BuildCanonicalRiverEdges(hydrology.Rivers, hydrology.Width);
        var count = 0;
        for (var y = 0; y < hydrology.Height - 1; y++)
        {
            for (var x = 0; x < hydrology.Width; x++)
            {
                var eastX = WrapX(x + 1, hydrology.Width);
                var a = y * hydrology.Width + x;
                var b = y * hydrology.Width + eastX;
                var c = (y + 1) * hydrology.Width + x;
                var d = (y + 1) * hydrology.Width + eastX;
                if (edges.Contains(EdgeKey(a, d)) && edges.Contains(EdgeKey(b, c)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static HashSet<long> BuildCanonicalRiverEdges(IReadOnlyList<RiverSegment> rivers, int width)
    {
        var edges = new HashSet<long>();
        foreach (var river in rivers)
        {
            for (var i = 0; i < river.Cells.Count - 1; i++)
            {
                var first = river.Cells[i].Y * width + WrapX(river.Cells[i].X, width);
                var second = river.Cells[i + 1].Y * width + WrapX(river.Cells[i + 1].X, width);
                edges.Add(EdgeKey(first, second));
            }
        }

        return edges;
    }

    private static long EdgeKey(int first, int second)
    {
        if (first > second)
            (first, second) = (second, first);

        return ((long)first << 32) | (uint)second;
    }

    private static int CountPolylineCrossings(IReadOnlyList<RiverSegment> rivers, int width)
    {
        var segments = rivers.SelectMany(r => BuildPolylineSegments(r, width)).ToList();
        var count = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var first = segments[i];
            for (var j = i + 1; j < segments.Count; j++)
            {
                var second = segments[j];
                if (first.River.Id == second.River.Id)
                    continue;
                if (!BoundingBoxesOverlap(first, second) || !TryFindContact(first, second, out var point))
                    continue;
                if (IsAllowedConfluenceTouch(point, first.River, second.River, width))
                    continue;
                if (CrossingAngleDegrees(first, second) < 35.0 && !IsParentChild(first.River, second.River))
                    continue;

                count++;
            }
        }

        return count;
    }

    private static IEnumerable<RiverPolylineSegment> BuildPolylineSegments(RiverSegment river, int width)
    {
        for (var i = 0; i < river.Polyline.Count - 1; i++)
        {
            var a = river.Polyline[i];
            var b = river.Polyline[i + 1];
            if (Math.Abs(b.X - a.X) > width / 2.0)
                continue;
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (length > 0.001)
                yield return new RiverPolylineSegment(river, a, b, length);
        }
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

    private static double Distance(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToPoint(GridPoint cell, MapPoint point, int width)
    {
        var dx = WrappedDeltaX((cell.X + 0.5) - point.X, width);
        var dy = cell.Y + 0.5 - point.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int DownstreamIndex(int index, int direction, int width, int height)
    {
        if (direction < 0 || direction >= 8 || width <= 0)
            return -1;

        var x = index % width;
        var y = index / width;
        var (dx, dy) = DirectionOffset(direction);
        var nextY = y + dy;
        if (nextY < 0 || nextY >= height)
            return -1;

        return nextY * width + WrapX(x + dx, width);
    }

    private static (int Dx, int Dy) DirectionOffset(int direction) => direction switch
    {
        0 => (1, 0),
        1 => (1, 1),
        2 => (0, 1),
        3 => (-1, 1),
        4 => (-1, 0),
        5 => (-1, -1),
        6 => (0, -1),
        7 => (1, -1),
        _ => (0, 0)
    };

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private static double WrappedDeltaX(double dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static int DynamicShortRiverLimit(HydrologyMap hydrology) =>
        Math.Clamp((int)Math.Round(Math.Sqrt(hydrology.Width * hydrology.Height) / 34.0), 5, 8);

    private static IEnumerable<int> DetectStraightRuns(IReadOnlyList<GridPoint> cells, int minRun)
    {
        if (cells.Count < minRun + 1)
            yield break;

        var direction = Direction(cells[0], cells[1], int.MaxValue / 4);
        var length = 1;
        for (var i = 1; i < cells.Count - 1; i++)
        {
            var nextDirection = Direction(cells[i], cells[i + 1], int.MaxValue / 4);
            if (nextDirection == direction)
            {
                length++;
                continue;
            }

            if (direction >= 0 && length >= minRun)
                yield return length;
            direction = nextDirection;
            length = 1;
        }

        if (direction >= 0 && length >= minRun)
            yield return length;
    }

    private static int MaxAlternatingZigZagRun(IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count < 5)
            return 0;

        var best = 0;
        var run = 0;
        var previousA = -1;
        var previousB = -1;
        for (var i = 0; i < cells.Count - 1; i++)
        {
            var direction = Direction(cells[i], cells[i + 1], int.MaxValue / 4);
            if (i >= 2 && direction == previousA && previousA != previousB)
                run++;
            else
                run = 0;

            best = Math.Max(best, run + 2);
            previousA = previousB;
            previousB = direction;
        }

        return best < 4 ? 0 : best;
    }

    private static double MeanCurvature(IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count < 3)
            return 0.0;

        var total = 0.0;
        var count = 0;
        for (var i = 1; i < cells.Count - 1; i++)
        {
            var a = Direction(cells[i - 1], cells[i], int.MaxValue / 4);
            var b = Direction(cells[i], cells[i + 1], int.MaxValue / 4);
            if (a < 0 || b < 0)
                continue;

            var delta = Math.Abs(a - b);
            total += Math.Min(delta, 8 - delta) / 4.0;
            count++;
        }

        return count == 0 ? 0.0 : total / count;
    }

    private static int CountTurns(IReadOnlyList<GridPoint> cells, int minimumDelta)
    {
        var count = 0;
        for (var i = 1; i < cells.Count - 1; i++)
        {
            var a = Direction(cells[i - 1], cells[i], int.MaxValue / 4);
            var b = Direction(cells[i], cells[i + 1], int.MaxValue / 4);
            if (a < 0 || b < 0)
                continue;

            var delta = Math.Abs(a - b);
            if (Math.Min(delta, 8 - delta) >= minimumDelta)
                count++;
        }

        return count;
    }

    private static int Direction(GridPoint from, GridPoint to, int width)
    {
        var dx = to.X - from.X;
        if (Math.Abs(dx) > width / 2.0)
            dx = dx > 0 ? dx - width : dx + width;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0)
            return -1;
        return (Math.Sign(dx), Math.Sign(dy)) switch
        {
            (1, 0) => 0,
            (1, 1) => 1,
            (0, 1) => 2,
            (-1, 1) => 3,
            (-1, 0) => 4,
            (-1, -1) => 5,
            (0, -1) => 6,
            (1, -1) => 7,
            _ => -1
        };
    }

    private sealed record RiverQualityDto(
        int StraightRunCount,
        int MaxStraightRunCells,
        int ShortRiverCount,
        int DetachedRiverCount,
        int ConfluenceCount,
        double MeanTributariesPerMajorRiver,
        int EndorheicRiverCountExpected,
        int MaxAlternatingZigZagRunCells,
        double MeanCurvature,
        int SharpTurnCount,
        int BacktrackLikeTurnCount,
        int CrossingRiverEdgeCount,
        int PolylineCrossingCount);

    private sealed record RiverDto(
        int Id,
        RiverKind Kind,
        RiverMouthKind? MouthKind,
        int? LandComponentId,
        DrainageTargetKind TargetKind,
        int? TargetId,
        PointDto Source,
        PointDto Mouth,
        PointDto DrainageTerminal,
        double Discharge,
        double LengthCells,
        double MeanSlope,
        int Order,
        bool IsMajor,
        double VisibleRank,
        int? ParentRiverId,
        IReadOnlyList<int>? TributaryIds,
        int CellCount,
        IReadOnlyList<PointDto>? Cells,
        IReadOnlyList<MapPointDto> Polyline);

    private sealed record RiverMouthDto(
        int RiverId,
        PointDto Cell,
        DrainageTargetKind TargetKind,
        int? TargetId,
        RiverMouthKind Kind,
        double Discharge);

    private sealed record LakeOutletDto(
        int LakeId,
        bool HasOutlet,
        PointDto? OutletCell,
        PointDto? DownstreamCell,
        double SpillElevationMeters,
        double BreachCost,
        double OutletScore);

    private sealed record DrainageBasinDto(
        int Id,
        DrainageTargetKind TargetKind,
        int? TargetId,
        PointDto TerminalCell,
        int CellCount,
        double TotalRunoff);

    private sealed record PointDto(int X, int Y);

    private sealed record MapPointDto(double X, double Y);

    private sealed record RiverPolylineSegment(RiverSegment River, MapPoint A, MapPoint B, double Length);
}

public sealed class RiverJsonExportOptions
{
    public bool WriteIndented { get; init; } = true;
    public bool IncludeCellPaths { get; init; } = true;
    public bool IncludeDiagnosticRasters { get; init; }
    public double MajorRiverDischargeThreshold { get; init; } = 220.0;
}
