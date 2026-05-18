using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class RiverJsonWriter
{
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

        return new RiverMapDto(
            hydrology.Width,
            hydrology.Height,
            new RiverSummaryDto(
                rivers.Count,
                hydrology.Rivers.Count(r => IsMajorRiver(r, options, DynamicShortRiverLimit(hydrology))),
                basins.Count(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                rivers.Count(r => r.TargetKind == DrainageTargetKind.Ocean),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea),
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
        int DeltaCount,
        double MeanLengthCells,
        double MaxLengthCells,
        int LakeOutletCount,
        int EndorheicLakeCount);

    private static RiverQualityDto BuildQuality(HydrologyMap hydrology, RiverJsonExportOptions options)
    {
        var shortLimit = DynamicShortRiverLimit(hydrology);
        var outletLakeIds = hydrology.LakeOutlets.Where(o => o.HasOutlet).Select(o => o.LakeId.Value).ToHashSet();
        var confluenceCount = hydrology.Rivers
            .Count(r => hydrology.Rivers.Any(other => other.Id != r.Id && other.Cells.Any(c => c == r.Mouth)));
        var majorCount = hydrology.Rivers.Count(r => IsMajorRiver(r, options, shortLimit));
        var straightRuns = hydrology.Rivers.SelectMany(r => DetectStraightRuns(r.Cells, 6)).ToList();
        var expectedEndorheic = hydrology.Rivers.Count(r =>
            r.TargetKind == DrainageTargetKind.EndorheicDryBasin ||
            r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea &&
            (!r.TargetId.HasValue || !outletLakeIds.Contains(r.TargetId.Value)));
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
            expectedEndorheic);
    }

    private static bool IsMajorRiver(RiverSegment river, RiverJsonExportOptions options, int shortLimit) =>
        river.Discharge >= options.MajorRiverDischargeThreshold &&
        river.Cells.Count > shortLimit &&
        !(river.MeanSlope > 16.0 && river.Discharge < options.MajorRiverDischargeThreshold * 1.35);

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
        int EndorheicRiverCountExpected);

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
}

public sealed class RiverJsonExportOptions
{
    public bool WriteIndented { get; init; } = true;
    public bool IncludeCellPaths { get; init; }
    public bool IncludeDiagnosticRasters { get; init; }
    public double MajorRiverDischargeThreshold { get; init; } = 220.0;
}
