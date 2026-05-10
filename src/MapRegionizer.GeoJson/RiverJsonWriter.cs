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

        return new RiverMapDto(
            hydrology.Width,
            hydrology.Height,
            new RiverSummaryDto(
                rivers.Count,
                rivers.Count(r => r.Discharge >= options.MajorRiverDischargeThreshold),
                basins.Count(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                rivers.Count(r => r.TargetKind == DrainageTargetKind.Ocean),
                rivers.Count(r => r.TargetKind is DrainageTargetKind.Lake or DrainageTargetKind.InlandSea),
                rivers.Count(r => r.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                mouths.Count(m => m.Kind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta),
                Math.Round(rivers.Count == 0 ? 0 : rivers.Average(r => r.LengthCells), 2),
                Math.Round(rivers.Count == 0 ? 0 : rivers.Max(r => r.LengthCells), 2),
                outlets.Count(o => o.HasOutlet),
                outlets.Count(o => !o.HasOutlet)),
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
