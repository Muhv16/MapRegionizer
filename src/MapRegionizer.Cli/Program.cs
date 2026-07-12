using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using MapRegionizer.Runner;

var exitCode = Run(args);
return exitCode;

static int Run(string[] args)
{
    if (args.Length == 0 || IsHelp(args[0]))
    {
        PrintUsage();
        return 0;
    }

    var command = args[0].Trim().ToLowerInvariant();
    if (command != "generate")
    {
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        return 2;
    }

    try
    {
        var options = ParseGenerateOptions(args.Skip(1).ToArray());
        var result = new MapGenerationRunner().Run(options);

        Console.WriteLine("Generation completed.");
        Console.WriteLine($"Output: {result.Summary.OutputDirectory}");
        Console.WriteLine($"Regions: {result.Summary.Map.RegionCount}");
        Console.WriteLine($"Landmasses: {result.Summary.Map.LandmassCount}");
        Console.WriteLine($"Water bodies: {result.Summary.Map.WaterBodyCount}");
        if (result.Summary.Map.TectonicPlateCount is { } plates)
            Console.WriteLine($"Tectonic plates: {plates}");
        if (result.Summary.Map.MinElevationMeters is { } minElevation &&
            result.Summary.Map.MaxElevationMeters is { } maxElevation)
            Console.WriteLine($"Elevation range: {minElevation:F0}..{maxElevation:F0} m");
        if (result.Summary.Map.MinMeanAnnualTemperature is { } minTemperature &&
            result.Summary.Map.MaxMeanAnnualTemperature is { } maxTemperature)
            Console.WriteLine($"Mean annual temperature: {minTemperature:F1}..{maxTemperature:F1} C");
        if (result.Artifacts.RegionsBin is { } regionsBin)
            Console.WriteLine($"Region raster: {regionsBin}");
        Console.WriteLine($"Summary: {result.Artifacts.SummaryJson}");

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Generation failed: {ex.Message}");
        return 1;
    }
}

static MapGenerationRunOptions ParseGenerateOptions(string[] args)
{
    var options = new MapGenerationRunOptions();
    var index = 0;

    while (index < args.Length)
    {
        var name = args[index++];
        if (IsHelp(name))
        {
            PrintGenerateUsage();
            Environment.Exit(0);
        }

        var normalized = NormalizeName(name);
        if (normalized == "debug")
        {
            options.Debug = true;
            continue;
        }

        if (normalized == "rasterize-regions")
        {
            var separatorIndex = name.IndexOf('=');
            if (separatorIndex >= 0)
                options.RasterizeRegions = ParseBool(name[(separatorIndex + 1)..], name);
            else if (index < args.Length && !args[index].StartsWith('-'))
                options.RasterizeRegions = ParseBool(args[index++], name);
            else
                options.RasterizeRegions = true;
            continue;
        }

        var value = ReadValue(args, ref index, name);
        switch (normalized)
        {
            case "config":
                options = LoadConfig(value);
                break;
            case "mask":
            case "input":
            case "m":
                options.MaskPath = value;
                break;
            case "out":
            case "output":
            case "o":
                options.OutputDirectory = value;
                break;
            case "pixel-size":
                options.PixelSize = ParseDouble(value, name);
                break;
            case "simplify-tolerance":
                options.SimplifyTolerance = ParseDouble(value, name);
                break;
            case "target-area":
                options.TargetArea = ParseUInt(value, name);
                break;
            case "points-multiplier":
                options.PointsMultiplier = ParseDouble(value, name);
                break;
            case "min-area-ratio":
                options.MinAreaRatio = ParseDouble(value, name);
                break;
            case "max-area-ratio":
                options.MaxAreaRatio = ParseDouble(value, name);
                break;
            case "boundary-detail":
                options.BoundaryDetail = ParseDouble(value, name);
                break;
            case "max-offset":
                options.MaxOffset = ParseDouble(value, name);
                break;
            case "min-line-length-to-curve":
                options.MinLineLengthToCurve = ParseDouble(value, name);
                break;
            case "seed":
                options.Seed = ParseInt(value, name);
                break;
            case "projection":
                options.ProjectionMode = ParseProjection(value, name);
                break;
            case "plate-count":
                options.PlateCount = ParseInt(value, name);
                break;
            case "hotspot-count":
                options.HotspotCount = ParseInt(value, name);
                break;
            case "generate-small-lakes":
                options.GenerateSmallLakes = ParseBool(value, name);
                break;
            case "small-lake-count-multiplier":
                options.SmallLakeCountMultiplier = ParseDouble(value, name);
                break;
            case "small-lake-scatter-multiplier":
                options.SmallLakeScatterMultiplier = ParseDouble(value, name);
                break;
            case "small-lake-size-multiplier":
                options.SmallLakeSizeMultiplier = ParseDouble(value, name);
                break;
            case "river-density":
                options.RiverDensity = ParseDouble(value, name);
                break;
            case "mountain-river-density":
                options.MountainRiverDensity = ParseDouble(value, name);
                break;
            case "max-mountain-sources-per-cluster":
                options.MaxMountainSourcesPerCluster = ParseInt(value, name);
                break;
            case "min-mountain-source-spacing":
                options.MinMountainSourceSpacing = ParseInt(value, name);
                break;
            case "major-river-count-multiplier":
                options.MajorRiverCountMultiplier = ParseDouble(value, name);
                break;
            case "long-river-count-multiplier":
                options.LongRiverCountMultiplier = ParseDouble(value, name);
                break;
            case "tributary-density":
                options.TributaryDensity = ParseDouble(value, name);
                break;
            case "major-river-tributary-multiplier":
                options.MajorRiverTributaryMultiplier = ParseDouble(value, name);
                break;
            case "lake-outlet-inflow-force-multiplier":
                options.LakeOutletInflowForceMultiplier = ParseDouble(value, name);
                break;
            case "endorheic-basin-chance":
                options.EndorheicBasinChance = ParseDouble(value, name);
                break;
            case "max-endorheic-basins":
                options.MaxEndorheicBasins = ParseInt(value, name);
                break;
            case "delta-frequency":
                options.DeltaFrequency = ParseDouble(value, name);
                break;
            case "meander-strength":
                options.MeanderStrength = ParseDouble(value, name);
                break;
            case "lake-outlet-strictness":
                options.LakeOutletStrictness = ParseDouble(value, name);
                break;
            case "preserve-river-coastline":
                options.PreserveRiverCoastline = ParseBool(value, name);
                break;
            case "allow-river-carving":
                options.AllowRiverCarving = ParseBool(value, name);
                break;
            case "climate-polar-latitude-margin":
                options.ClimatePolarLatitudeMargin = ParseDouble(value, name);
                break;
            case "climate-equator-temperature":
                options.ClimateEquatorTemperatureCelsius = ParseDouble(value, name);
                break;
            case "climate-pole-cooling":
                options.ClimatePoleCoolingCelsius = ParseDouble(value, name);
                break;
            case "climate-lapse-rate":
                options.ClimateLapseRateCelsiusPerMeter = ParseDouble(value, name);
                break;
            case "tectonic-json-mode":
                options.TectonicJsonMode = ParseEnum<TectonicPlateJsonExportMode>(value, name);
                break;
            case "elevation-json-mode":
                options.ElevationJsonMode = ParseEnum<ElevationJsonExportMode>(value, name);
                break;
            case "climate-json-mode":
                options.ClimateJsonMode = ParseEnum<ClimateJsonExportMode>(value, name);
                break;
            case "region-raster":
                options.RasterizeRegions = ParseBool(value, name);
                break;
            case "region-draft":
                options.RegionDraftPath = value;
                break;
            case "write-region-draft":
                options.RegionDraftOutputPath = value;
                break;
            case "region-distortion":
                options.RegionDraftDistortionEnabled = ParseBool(value, name);
                break;
            default:
                throw new ArgumentException($"Unknown option: {name}");
        }
    }

    return options;
}

static MapGenerationRunOptions LoadConfig(string path)
{
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<MapGenerationRunOptions>(json, CreateJsonOptions())
        ?? throw new InvalidOperationException($"Config file is empty: {path}");
}

static JsonSerializerOptions CreateJsonOptions()
{
    return new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

static string ReadValue(string[] args, ref int index, string name)
{
    var separatorIndex = name.IndexOf('=');
    if (separatorIndex >= 0)
        return name[(separatorIndex + 1)..];

    if (index >= args.Length)
        throw new ArgumentException($"Option {name} requires a value.");

    return args[index++];
}

static string NormalizeName(string name)
{
    var separatorIndex = name.IndexOf('=');
    if (separatorIndex >= 0)
        name = name[..separatorIndex];

    return name.TrimStart('-').ToLowerInvariant();
}

static bool IsHelp(string value) => value is "-h" or "--help" or "help";

static int ParseInt(string value, string name)
{
    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        return result;

    throw new ArgumentException($"Option {name} expects an integer value.");
}

static uint ParseUInt(string value, string name)
{
    if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        return result;

    throw new ArgumentException($"Option {name} expects an unsigned integer value.");
}

static double ParseDouble(string value, string name)
{
    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        return result;

    throw new ArgumentException($"Option {name} expects a number with invariant culture decimal separator.");
}

static bool ParseBool(string value, string name)
{
    if (bool.TryParse(value, out var result))
        return result;

    return value.Trim().ToLowerInvariant() switch
    {
        "1" or "yes" or "y" or "on" or "enable" or "enabled" => true,
        "0" or "no" or "n" or "off" or "disable" or "disabled" => false,
        _ => throw new ArgumentException($"Option {name} expects a boolean value.")
    };
}

static MapProjectionMode ParseProjection(string value, string name)
{
    var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal);

    return normalized.ToLowerInvariant() switch
    {
        "equirectangularworld" => MapProjectionMode.EquirectangularWorld,
        "flat" => MapProjectionMode.Flat,
        "regional" => MapProjectionMode.Regional,
        _ => throw new ArgumentException($"Option {name} expects one of: equirectangular-world, flat, regional.")
    };
}

static T ParseEnum<T>(string value, string name)
    where T : struct, Enum
{
    var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal);

    foreach (var enumValue in Enum.GetValues<T>())
    {
        if (enumValue.ToString().Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase))
            return enumValue;
    }

    throw new ArgumentException($"Option {name} expects one of: {string.Join(", ", Enum.GetNames<T>())}.");
}

static void PrintUsage()
{
    Console.WriteLine("MapRegionizer.Cli");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  generate    Generate map artifacts from a mask image.");
    Console.WriteLine();
    Console.WriteLine("Run `dotnet run --project src/MapRegionizer.Cli -- generate --help` for options.");
}

static void PrintGenerateUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/MapRegionizer.Cli -- generate --mask <image> --out <directory> [options]");
    Console.WriteLine();
    Console.WriteLine("Required:");
    Console.WriteLine("  --mask, --input, -m <path>       Source mask image. White pixels are land.");
    Console.WriteLine("  --out, --output, -o <directory>  Output artifact directory.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --config <json>                  Load MapGenerationRunOptions JSON before later overrides.");
    Console.WriteLine("  --seed <int>                     Deterministic generation seed.");
    Console.WriteLine("  --target-area <uint>             Target region area. Default: 400.");
    Console.WriteLine("  --points-multiplier <number>     Region point multiplier. Default: 4.");
    Console.WriteLine("  --min-area-ratio <number>        Minimum area ratio. Default: 0.75.");
    Console.WriteLine("  --max-area-ratio <number>        Maximum area ratio. Default: 1.75.");
    Console.WriteLine("  --simplify-tolerance <number>    Shape simplify tolerance. Default: 1.");
    Console.WriteLine("  --boundary-detail <number>       Boundary distortion detail. Default: 0.25.");
    Console.WriteLine("  --max-offset <number>            Boundary max offset. Default: 3.25.");
    Console.WriteLine("  --min-line-length-to-curve <n>   Boundary curve threshold. Default: 7.");
    Console.WriteLine("  --projection <mode>              equirectangular-world, flat, regional.");
    Console.WriteLine("  --plate-count <int>              Override generated tectonic plate count.");
    Console.WriteLine("  --hotspot-count <int>            Override generated hotspot count.");
    Console.WriteLine("  --generate-small-lakes <bool>    Generate small terrain-derived lakes. Default: true.");
    Console.WriteLine("  --small-lake-count-multiplier <number>  Scales generated small-lake count. Default: 1.");
    Console.WriteLine("  --small-lake-scatter-multiplier <number>  Scales scattered standalone lakes. Default: 1.");
    Console.WriteLine("  --small-lake-size-multiplier <number>   Scales generated small-lake footprint size. Default: 0.1.");
    Console.WriteLine("  --river-density <number>        Scales visible river density. Default: 1.");
    Console.WriteLine("  --mountain-river-density <number>  Multiplies visible mountain source density. Default: 0.58.");
    Console.WriteLine("  --max-mountain-sources-per-cluster <n>  Optional hard cap for visible sources in one mountain cluster.");
    Console.WriteLine("  --min-mountain-source-spacing <n>  Optional minimum spacing for visible mountain sources.");
    Console.WriteLine("  --major-river-count-multiplier <number>  Scales max major river budget. Default: 1.5.");
    Console.WriteLine("  --long-river-count-multiplier <number>   Scales forced long river budget. Default: 1.3.");
    Console.WriteLine("  --tributary-density <number>    Scales visible tributary density. Default: 1.");
    Console.WriteLine("  --major-river-tributary-multiplier <number>  Scales guaranteed tributaries along major rivers. Default: 1.");
    Console.WriteLine("  --lake-outlet-inflow-force-multiplier <number>  Scales inflow count threshold for forced shallow-lake outlets. Default: 0.45.");
    Console.WriteLine("  --endorheic-basin-chance <0..1> Preserves closed basins/lakes. Default: 0.22.");
    Console.WriteLine("  --max-endorheic-basins <int>    Max endorheic river basins per map. Default: 3.");
    Console.WriteLine("  --delta-frequency <number>      Scales delta mouth classification. Default: 0.8.");
    Console.WriteLine("  --meander-strength <0..1>       Scales rendered river meanders. Default: 0.65.");
    Console.WriteLine("  --lake-outlet-strictness <0..1> Controls how many lakes remain closed. Default: 0.35.");
    Console.WriteLine("  --preserve-river-coastline <bool> Keep hydrology from changing coastline. Default: true.");
    Console.WriteLine("  --allow-river-carving <bool>    Allow future terrain carving. Current default: false.");
    Console.WriteLine("  --climate-polar-latitude-margin <0..1>  Keeps map edge below exact pole latitude. Default: 0.05.");
    Console.WriteLine("  --climate-equator-temperature <celsius> Base equator temperature. Default: 28.");
    Console.WriteLine("  --climate-pole-cooling <celsius> Latitude cooling at poles. Default: 55.");
    Console.WriteLine("  --climate-lapse-rate <celsius/meter> Temperature loss with height. Default: 0.0045.");
    Console.WriteLine("  --tectonic-json-mode <mode>      Summary, CompactDiagnostic, Diagnostic.");
    Console.WriteLine("  --elevation-json-mode <mode>     Summary, Diagnostic.");
    Console.WriteLine("  --climate-json-mode <mode>       Summary, Diagnostic.");
    Console.WriteLine("  --rasterize-regions [bool]       Export final region ids as regions.bin and regions.summary.json. Default: false.");
    Console.WriteLine("  --region-draft <geojson>         Import a compatible editable region draft.");
    Console.WriteLine("  --write-region-draft <geojson>   Export canonical raw regions as an editable draft.");
    Console.WriteLine("  --region-distortion <bool>       Override the draft's boundary-distortion setting.");
    Console.WriteLine("  --debug                          Print memory diagnostics per stage.");
}
