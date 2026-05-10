using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class ClimateJsonWriter
{
    public static string Write(ClimateMap climate, ClimateJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(climate);
        options ??= new ClimateJsonExportOptions();
        return JsonSerializer.Serialize(ToDto(climate, options), CreateSerializerOptions(options));
    }

    public static string Write(GeneratedMap map, ClimateJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Climate is null)
            throw new InvalidOperationException("The map does not contain climate data.");

        return Write(map.Climate, options);
    }

    public static void WriteToFile(ClimateMap climate, string filePath, ClimateJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(climate, options));

    public static void WriteToFile(GeneratedMap map, string filePath, ClimateJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(map, options));

    private static JsonSerializerOptions CreateSerializerOptions(ClimateJsonExportOptions options) =>
        new()
        {
            WriteIndented = options.WriteIndented ?? options.Mode == ClimateJsonExportMode.Diagnostic,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

    private static ClimateMapDto ToDto(ClimateMap climate, ClimateJsonExportOptions options)
    {
        var diagnostic = options.Mode == ClimateJsonExportMode.Diagnostic;
        return new ClimateMapDto(
            climate.Width,
            climate.Height,
            BuildSummary(climate),
            EncodeClimateClassRows(climate),
            EncodeBiomeRows(climate),
            EncodeRows(climate, climate.GetMeanAnnualTemperature, 1),
            EncodeUnitRows(climate, climate.GetMoisture, 1000),
            EncodeUnitRows(climate, climate.GetBiomeMoisture, 1000),
            EncodeUnitRows(climate, climate.GetHabitability, 1000),
            EncodeUnitRows(climate, climate.GetAgriculturalPotential, 1000),
            diagnostic ? EncodeRows(climate, climate.GetSummerTemperature, 1) : null,
            diagnostic ? EncodeRows(climate, climate.GetWinterTemperature, 1) : null,
            diagnostic ? EncodeRows(climate, climate.GetSeasonality, 1) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetLatitudeNorm, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetAtmosphericMoisture, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetPrecipitation, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetRainShadow, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetMonsoonInfluence, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetRiverValleyInfluence, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetWetlandInfluence, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetSnowOverlay, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetMountainOverlay, 1000) : null,
            diagnostic ? EncodeUnitRows(climate, climate.GetIceScore, 1000) : null);
    }

    private static ClimateSummaryDto BuildSummary(ClimateMap climate)
    {
        var biomeCounts = Enum.GetValues<BiomeKind>().ToDictionary(k => k, _ => 0);
        var classCounts = Enum.GetValues<ClimateClassKind>().ToDictionary(k => k, _ => 0);
        var minTemp = double.PositiveInfinity;
        var maxTemp = double.NegativeInfinity;
        var maxIce = 0.0;

        for (var y = 0; y < climate.Height; y++)
        {
            for (var x = 0; x < climate.Width; x++)
            {
                var biome = climate.GetBiome(x, y);
                var climateClass = climate.GetClimateClass(x, y);
                biomeCounts[biome]++;
                classCounts[climateClass]++;
                var temp = climate.GetMeanAnnualTemperature(x, y);
                minTemp = Math.Min(minTemp, temp);
                maxTemp = Math.Max(maxTemp, temp);
                maxIce = Math.Max(maxIce, climate.GetIceScore(x, y));
            }
        }

        return new ClimateSummaryDto(
            Math.Round(minTemp, 2),
            Math.Round(maxTemp, 2),
            Math.Round(maxIce, 3),
            biomeCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key.ToString()).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            classCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key.ToString()).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
    }

    private static IReadOnlyList<string> EncodeRows(ClimateMap climate, Func<int, int, double> readValue, int binSize)
    {
        var rows = new string[climate.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < climate.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = Code(readValue(0, y), binSize);

            for (var x = 1; x < climate.Width; x++)
            {
                var value = Code(readValue(x, y), binSize);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, climate.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeUnitRows(ClimateMap climate, Func<int, int, double> readValue, int multiplier)
    {
        var rows = new string[climate.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < climate.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = UnitCode(readValue(0, y), multiplier);

            for (var x = 1; x < climate.Width; x++)
            {
                var value = UnitCode(readValue(x, y), multiplier);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, climate.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeClimateClassRows(ClimateMap climate)
    {
        var rows = new string[climate.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < climate.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = ClimateClassCode(climate.GetClimateClass(0, y));

            for (var x = 1; x < climate.Width; x++)
            {
                var value = ClimateClassCode(climate.GetClimateClass(x, y));
                if (value == current)
                    continue;

                AppendRun(sb, current.ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current.ToString(), climate.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeBiomeRows(ClimateMap climate)
    {
        var rows = new string[climate.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < climate.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = BiomeCode(climate.GetBiome(0, y));

            for (var x = 1; x < climate.Width; x++)
            {
                var value = BiomeCode(climate.GetBiome(x, y));
                if (value == current)
                    continue;

                AppendRun(sb, current.ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current.ToString(), climate.Width - runStart);
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

    private static string UnitCode(double value, int multiplier)
    {
        var bin = (int)Math.Round(Math.Clamp(value, 0, 1) * multiplier);
        return bin.ToString(CultureInfo.InvariantCulture);
    }

    private static char ClimateClassCode(ClimateClassKind climateClass) => climateClass switch
    {
        ClimateClassKind.Ocean => 'O',
        ClimateClassKind.TropicalWet => 'W',
        ClimateClassKind.TropicalSeasonal => 'T',
        ClimateClassKind.HotArid => 'A',
        ClimateClassKind.SemiArid => 'S',
        ClimateClassKind.WarmTemperate => 'M',
        ClimateClassKind.TemperateWet => 'E',
        ClimateClassKind.Continental => 'C',
        ClimateClassKind.Boreal => 'B',
        ClimateClassKind.Tundra => 'U',
        ClimateClassKind.PolarDesert => 'P',
        ClimateClassKind.IceCap => 'I',
        ClimateClassKind.Alpine => 'L',
        _ => '?'
    };

    private static char BiomeCode(BiomeKind biome) => biome switch
    {
        BiomeKind.Ocean => 'O',
        BiomeKind.TropicalRainforest => 'R',
        BiomeKind.MonsoonForest => 'M',
        BiomeKind.DryTropicalForest => 'Y',
        BiomeKind.TropicalSeasonalForest => 'F',
        BiomeKind.Savanna => 'S',
        BiomeKind.OpenWoodland => 'V',
        BiomeKind.HotDesert => 'D',
        BiomeKind.SemiDesert => 'Q',
        BiomeKind.RockyDesert => 'K',
        BiomeKind.SaltFlat => 'Z',
        BiomeKind.ColdDesert => 'C',
        BiomeKind.Steppe => 'P',
        BiomeKind.XericShrubland => 'X',
        BiomeKind.MediterraneanShrubland => 'H',
        BiomeKind.TemperateGrassland => 'G',
        BiomeKind.TemperateForest => 'T',
        BiomeKind.TemperateRainforest => 'W',
        BiomeKind.BorealForest => 'B',
        BiomeKind.Tundra => 'U',
        BiomeKind.PolarDesert => 'A',
        BiomeKind.IceSheet => 'I',
        BiomeKind.AlpineTundra => 'L',
        BiomeKind.Wetland => 'N',
        BiomeKind.Floodplain => 'J',
        BiomeKind.Marsh => 'e',
        BiomeKind.Mangrove => 'm',
        BiomeKind.MontaneForest => 'o',
        BiomeKind.CloudForest => 'c',
        BiomeKind.SnowyMountain => 's',
        BiomeKind.VolcanicBadlands => 'v',
        _ => '?'
    };

    private sealed record ClimateMapDto(
        int Width,
        int Height,
        ClimateSummaryDto Summary,
        IReadOnlyList<string> ClimateClassRows,
        IReadOnlyList<string> BiomeRows,
        IReadOnlyList<string> MeanAnnualTemperatureRows,
        IReadOnlyList<string> MoistureRows,
        IReadOnlyList<string> BiomeMoistureRows,
        IReadOnlyList<string> HabitabilityRows,
        IReadOnlyList<string> AgriculturalPotentialRows,
        IReadOnlyList<string>? SummerTemperatureRows,
        IReadOnlyList<string>? WinterTemperatureRows,
        IReadOnlyList<string>? SeasonalityRows,
        IReadOnlyList<string>? LatitudeNormRows,
        IReadOnlyList<string>? AtmosphericMoistureRows,
        IReadOnlyList<string>? PrecipitationRows,
        IReadOnlyList<string>? RainShadowRows,
        IReadOnlyList<string>? MonsoonInfluenceRows,
        IReadOnlyList<string>? RiverValleyInfluenceRows,
        IReadOnlyList<string>? WetlandInfluenceRows,
        IReadOnlyList<string>? SnowOverlayRows,
        IReadOnlyList<string>? MountainOverlayRows,
        IReadOnlyList<string>? IceScoreRows);

    private sealed record ClimateSummaryDto(
        double MinMeanAnnualTemperature,
        double MaxMeanAnnualTemperature,
        double MaxIceScore,
        IReadOnlyDictionary<string, int> BiomeCounts,
        IReadOnlyDictionary<string, int> ClimateClassCounts);
}

public sealed class ClimateJsonExportOptions
{
    public ClimateJsonExportMode Mode { get; init; } = ClimateJsonExportMode.Summary;
    public bool? WriteIndented { get; init; }
}

public enum ClimateJsonExportMode
{
    Summary,
    Diagnostic
}
