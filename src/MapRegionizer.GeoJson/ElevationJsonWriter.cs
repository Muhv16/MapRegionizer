using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class ElevationJsonWriter
{
    public static string Write(ElevationMap elevation, ElevationJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        options ??= new ElevationJsonExportOptions();
        return JsonSerializer.Serialize(ToDto(elevation, options), CreateSerializerOptions(options));
    }

    public static string Write(GeneratedMap map, ElevationJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Elevation is null)
            throw new InvalidOperationException("The map does not contain elevation data.");

        return Write(map.Elevation, options);
    }

    public static void WriteToFile(ElevationMap elevation, string filePath, ElevationJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(elevation, options));

    public static void WriteToFile(GeneratedMap map, string filePath, ElevationJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(map, options));

    private static JsonSerializerOptions CreateSerializerOptions(ElevationJsonExportOptions options)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = options.WriteIndented ?? options.Mode == ElevationJsonExportMode.Diagnostic,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private static ElevationMapDto ToDto(ElevationMap elevation, ElevationJsonExportOptions options)
    {
        var diagnostic = options.Mode == ElevationJsonExportMode.Diagnostic;
        return new ElevationMapDto(
            elevation.Width,
            elevation.Height,
            EncodeRows(elevation, elevation.GetElevation, diagnostic ? 1 : 10),
            EncodeZoneRows(elevation),
            diagnostic ? EncodeRows(elevation, elevation.GetBaseElevation, 1) : null,
            diagnostic ? EncodeRows(elevation, elevation.GetTectonicElevation, 1) : null,
            diagnostic ? EncodeUnitRows(elevation, elevation.GetRoughness, 1000) : null,
            diagnostic ? EncodeUnitRows(elevation, elevation.GetErosionMask, 1000) : null);
    }

    private static IReadOnlyList<string> EncodeRows(ElevationMap elevation, Func<int, int, double> readValue, int binSize)
    {
        var rows = new string[elevation.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < elevation.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = HeightCode(readValue(0, y), binSize);

            for (var x = 1; x < elevation.Width; x++)
            {
                var value = HeightCode(readValue(x, y), binSize);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, elevation.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeUnitRows(ElevationMap elevation, Func<int, int, double> readValue, int multiplier)
    {
        var rows = new string[elevation.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < elevation.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = UnitCode(readValue(0, y), multiplier);

            for (var x = 1; x < elevation.Width; x++)
            {
                var value = UnitCode(readValue(x, y), multiplier);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, elevation.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeZoneRows(ElevationMap elevation)
    {
        var rows = new string[elevation.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < elevation.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = ZoneCode(elevation.GetZone(0, y));

            for (var x = 1; x < elevation.Width; x++)
            {
                var value = ZoneCode(elevation.GetZone(x, y));
                if (value == current)
                    continue;

                AppendRun(sb, current.ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current.ToString(), elevation.Width - runStart);
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

    private static string HeightCode(double value, int binSize)
    {
        var bin = (int)Math.Round(value / Math.Max(1, binSize));
        return bin.ToString(CultureInfo.InvariantCulture);
    }

    private static string UnitCode(double value, int multiplier)
    {
        var bin = (int)Math.Round(Math.Clamp(value, 0, 1) * multiplier);
        return bin.ToString(CultureInfo.InvariantCulture);
    }

    private static char ZoneCode(ElevationZoneKind zone) => zone switch
    {
        ElevationZoneKind.DeepOcean => 'D',
        ElevationZoneKind.ShelfSea => 'S',
        ElevationZoneKind.CoastalLowland => 'C',
        ElevationZoneKind.Lowland => 'L',
        ElevationZoneKind.Highland => 'H',
        ElevationZoneKind.Mountain => 'M',
        ElevationZoneKind.IceCapCandidate => 'I',
        _ => '?'
    };

    private sealed record ElevationMapDto(
        int Width,
        int Height,
        IReadOnlyList<string> ElevationRows,
        IReadOnlyList<string> ZoneRows,
        IReadOnlyList<string>? BaseElevationRows,
        IReadOnlyList<string>? TectonicElevationRows,
        IReadOnlyList<string>? RoughnessRows,
        IReadOnlyList<string>? ErosionMaskRows);
}

public sealed class ElevationJsonExportOptions
{
    public ElevationJsonExportMode Mode { get; init; } = ElevationJsonExportMode.Summary;
    public bool? WriteIndented { get; init; }
}

public enum ElevationJsonExportMode
{
    Summary,
    Diagnostic
}
