using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.GeoJson;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.Runner;

public sealed class MapGenerationRunner
{
    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public MapGenerationRunResult Run(MapGenerationRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRunOptions(options);

        var maskPath = Path.GetFullPath(options.MaskPath);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var mask = ImageMaskReader.Read(maskPath);
        var generationOptions = options.ToGenerationOptions();
        var map = new MapGenerator().Generate(mask, generationOptions);

        var artifacts = BuildArtifactPaths(outputDirectory, map);
        WriteArtifacts(map, artifacts, outputDirectory, options);

        var summary = BuildSummary(map, artifacts, maskPath, outputDirectory, options);
        File.WriteAllText(artifacts.SummaryJson, JsonSerializer.Serialize(summary, SummaryJsonOptions));

        return new MapGenerationRunResult(map, artifacts, summary);
    }

    private static void ValidateRunOptions(MapGenerationRunOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MaskPath))
            throw new ArgumentException("Mask path is required.", nameof(options));

        if (!File.Exists(options.MaskPath))
            throw new FileNotFoundException("Mask file was not found.", options.MaskPath);

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(options));

        options.ToGenerationOptions().Validate();
    }

    private static MapGenerationArtifactPaths BuildArtifactPaths(string outputDirectory, GeneratedMap map)
    {
        var hasTectonics = map.TectonicPlates is not null;
        var hasElevation = map.Elevation is not null;

        return new MapGenerationArtifactPaths(
            ResultImage: Path.Combine(outputDirectory, "result.png"),
            TectonicPlatesImage: hasTectonics ? Path.Combine(outputDirectory, "tectonic-plates.png") : null,
            TectonicCrustImage: hasTectonics ? Path.Combine(outputDirectory, "tectonic-crust.png") : null,
            TectonicFeaturesImage: hasTectonics ? Path.Combine(outputDirectory, "tectonic-features.png") : null,
            ElevationImage: hasElevation ? Path.Combine(outputDirectory, "elevation.png") : null,
            ElevationFinalImage: hasElevation ? Path.Combine(outputDirectory, "elevation-final.png") : null,
            ElevationBaseImage: hasElevation ? Path.Combine(outputDirectory, "elevation-base.png") : null,
            ElevationTectonicImage: hasElevation ? Path.Combine(outputDirectory, "elevation-tectonic.png") : null,
            ElevationRoughnessImage: hasElevation ? Path.Combine(outputDirectory, "elevation-roughness.png") : null,
            ElevationErosionImage: hasElevation ? Path.Combine(outputDirectory, "elevation-erosion.png") : null,
            ElevationTerrainZonesImage: hasElevation ? Path.Combine(outputDirectory, "elevation-terrain-zones.png") : null,
            ElevationMountainImage: hasElevation ? Path.Combine(outputDirectory, "elevation-mountain.png") : null,
            ElevationBasinImage: hasElevation ? Path.Combine(outputDirectory, "elevation-basin.png") : null,
            RegionsGeoJson: Path.Combine(outputDirectory, "regions.geojson"),
            LandmassesGeoJson: Path.Combine(outputDirectory, "landmasses.geojson"),
            WaterBodiesGeoJson: Path.Combine(outputDirectory, "water-bodies.geojson"),
            TectonicPlatesJson: hasTectonics ? Path.Combine(outputDirectory, "tectonic-plates.json") : null,
            ElevationJson: hasElevation ? Path.Combine(outputDirectory, "elevation.json") : null,
            SummaryJson: Path.Combine(outputDirectory, "summary.json"));
    }

    private static void WriteArtifacts(GeneratedMap map, MapGenerationArtifactPaths artifacts, string outputDirectory, MapGenerationRunOptions options)
    {
        MapImageRenderer.RenderToFile(map, artifacts.ResultImage);

        if (map.TectonicPlates is not null)
        {
            MapImageRenderer.RenderTectonicPlatesToFile(map, artifacts.TectonicPlatesImage!);
            MapImageRenderer.RenderCrustToFile(map, artifacts.TectonicCrustImage!);
            MapImageRenderer.RenderTectonicFeaturesToFile(map, artifacts.TectonicFeaturesImage!);
            TectonicPlateJsonWriter.WriteToFile(map, artifacts.TectonicPlatesJson!, new TectonicPlateJsonExportOptions
            {
                Mode = options.TectonicJsonMode
            });
        }

        if (map.Elevation is not null)
        {
            MapImageRenderer.RenderElevationToFile(map, artifacts.ElevationImage!);
            MapImageRenderer.RenderElevationDebugToFiles(map, outputDirectory);
            ElevationJsonWriter.WriteToFile(map, artifacts.ElevationJson!, new ElevationJsonExportOptions
            {
                Mode = options.ElevationJsonMode
            });
        }

        GeoJsonMapWriter.WriteRegionsToFile(map, artifacts.RegionsGeoJson);
        GeoJsonMapWriter.WriteLandmassesToFile(map, artifacts.LandmassesGeoJson);
        GeoJsonMapWriter.WriteWaterBodiesToFile(map, artifacts.WaterBodiesGeoJson);
    }

    private static MapGenerationRunSummary BuildSummary(
        GeneratedMap map,
        MapGenerationArtifactPaths artifacts,
        string maskPath,
        string outputDirectory,
        MapGenerationRunOptions options)
    {
        var (minElevation, maxElevation) = GetElevationRange(map.Elevation);

        return new MapGenerationRunSummary(
            DateTimeOffset.UtcNow,
            maskPath,
            outputDirectory,
            new MapGenerationRunOptionSummary(
                options.PixelSize,
                options.SimplifyTolerance,
                options.TargetArea,
                options.PointsMultiplier,
                options.MinAreaRatio,
                options.MaxAreaRatio,
                options.BoundaryDetail,
                options.MaxOffset,
                options.MinLineLengthToCurve,
                options.Seed,
                options.ProjectionMode.ToString(),
                options.PlateCount,
                options.HotspotCount,
                options.TectonicJsonMode.ToString(),
                options.ElevationJsonMode.ToString()),
            new MapGenerationMapSummary(
                map.Bounds.Width,
                map.Bounds.Height,
                map.Landmasses.Count,
                map.WaterBodies.Count,
                map.Regions.Count,
                map.TectonicPlates?.Plates.Count,
                map.TectonicPlates?.Boundaries.Count,
                map.TectonicPlates?.Features?.Features.Count,
                map.TectonicPlates?.Features?.Islands.Count,
                map.Elevation?.Width,
                map.Elevation?.Height,
                minElevation,
                maxElevation),
            artifacts);
    }

    private static (double? Min, double? Max) GetElevationRange(ElevationMap? elevation)
    {
        if (elevation is null)
            return (null, null);

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
            {
                var value = elevation.GetElevation(x, y);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return (min, max);
    }
}
