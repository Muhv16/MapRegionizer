using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.Runner;

public static class MapGenerationArtifactWriter
{
    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static MapGenerationRunResult Write(
        GeneratedMap map,
        string maskPath,
        string outputDirectory,
        MapGenerationOptions options,
        TectonicPlateJsonExportMode tectonicJsonMode = TectonicPlateJsonExportMode.Summary,
        ElevationJsonExportMode elevationJsonMode = ElevationJsonExportMode.Summary,
        ClimateJsonExportMode climateJsonMode = ClimateJsonExportMode.Summary,
        MapArtifactRenderOptions? renderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);
        renderOptions ??= new MapArtifactRenderOptions();

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var artifacts = BuildArtifactPaths(outputDirectory, map);
        WriteArtifacts(map, artifacts, outputDirectory, tectonicJsonMode, elevationJsonMode, climateJsonMode, renderOptions);

        var summary = BuildSummary(map, artifacts, Path.GetFullPath(maskPath), outputDirectory, options, tectonicJsonMode, elevationJsonMode, climateJsonMode);
        File.WriteAllText(artifacts.SummaryJson, JsonSerializer.Serialize(summary, SummaryJsonOptions));

        return new MapGenerationRunResult(map, artifacts, summary);
    }

    public static MapGenerationArtifactPaths BuildArtifactPaths(string outputDirectory, GeneratedMap map)
    {
        var hasTectonics = map.TectonicPlates is not null;
        var hasElevation = map.Elevation is not null;
        var hasLakes = map.WaterSurfaces is not null || map.Elevation?.WaterSurfaces is not null;
        var hasHydrology = map.Hydrology is not null;
        var hasClimate = map.Climate is not null;
        var hasRegionRaster = map.RegionRaster is not null;

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
            ElevationRiversImage: hasHydrology ? Path.Combine(outputDirectory, "elevation-rivers.png") : null,
            ClimateImage: hasClimate ? Path.Combine(outputDirectory, "climate-biomes-presentation.png") : null,
            ClimateBiomesDebugImage: hasClimate ? Path.Combine(outputDirectory, "climate-biomes-debug.png") : null,
            ClimateBiomesPresentationImage: hasClimate ? Path.Combine(outputDirectory, "climate-biomes-presentation.png") : null,
            ClimateTemperatureImage: hasClimate ? Path.Combine(outputDirectory, "climate-temperature.png") : null,
            ClimateMoistureImage: hasClimate ? Path.Combine(outputDirectory, "climate-moisture.png") : null,
            ClimatePrecipitationImage: hasClimate ? Path.Combine(outputDirectory, "climate-precipitation.png") : null,
            ClimateHabitabilityImage: hasClimate ? Path.Combine(outputDirectory, "climate-habitability.png") : null,
            ClimateAgricultureImage: hasClimate ? Path.Combine(outputDirectory, "climate-agriculture.png") : null,
            ClimateIceImage: hasClimate ? Path.Combine(outputDirectory, "climate-ice.png") : null,
            RegionsGeoJson: Path.Combine(outputDirectory, "regions.geojson"),
            LandmassesGeoJson: Path.Combine(outputDirectory, "landmasses.geojson"),
            WaterBodiesGeoJson: Path.Combine(outputDirectory, "water-bodies.geojson"),
            TectonicPlatesJson: hasTectonics ? Path.Combine(outputDirectory, "tectonic-plates.json") : null,
            ElevationJson: hasElevation ? Path.Combine(outputDirectory, "elevation.json") : null,
            LakesJson: hasLakes ? Path.Combine(outputDirectory, "lakes.json") : null,
            RiversJson: hasHydrology ? Path.Combine(outputDirectory, "rivers.json") : null,
            ClimateJson: hasClimate ? Path.Combine(outputDirectory, "climate.json") : null,
            RegionsBin: hasRegionRaster ? Path.Combine(outputDirectory, "regions.bin") : null,
            RegionsSummaryJson: hasRegionRaster ? Path.Combine(outputDirectory, "regions.summary.json") : null,
            SummaryJson: Path.Combine(outputDirectory, "summary.json"));
    }

    private static void WriteArtifacts(
        GeneratedMap map,
        MapGenerationArtifactPaths artifacts,
        string outputDirectory,
        TectonicPlateJsonExportMode tectonicJsonMode,
        ElevationJsonExportMode elevationJsonMode,
        ClimateJsonExportMode climateJsonMode,
        MapArtifactRenderOptions renderOptions)
    {
        MapImageRenderer.RenderToFile(map, artifacts.ResultImage, new MapRenderOptions
        {
            Scale = renderOptions.Scale,
            BorderWidth = renderOptions.RegionBorderWidth
        });

        if (map.TectonicPlates is not null)
        {
            MapImageRenderer.RenderTectonicPlatesToFile(map, artifacts.TectonicPlatesImage!, new TectonicPlateRenderOptions
            {
                Scale = renderOptions.Scale,
                PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
            });
            MapImageRenderer.RenderCrustToFile(map, artifacts.TectonicCrustImage!, new CrustRenderOptions
            {
                Scale = renderOptions.Scale,
                PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth,
                DrawPlateBoundaries = renderOptions.DrawCrustPlateBoundaries
            });
            MapImageRenderer.RenderTectonicFeaturesToFile(map, artifacts.TectonicFeaturesImage!, new TectonicFeatureRenderOptions
            {
                Scale = renderOptions.Scale,
                PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth,
                DrawPlateBoundaries = renderOptions.DrawFeaturePlateBoundaries
            });
            TectonicPlateJsonWriter.WriteToFile(map, artifacts.TectonicPlatesJson!, new TectonicPlateJsonExportOptions
            {
                Mode = tectonicJsonMode
            });
        }

        if (map.Elevation is not null)
        {
            var elevationOptions = new ElevationRenderOptions
            {
                Scale = renderOptions.Scale,
                DrawHillshade = renderOptions.DrawElevationHillshade,
                DrawPlateBoundaries = renderOptions.DrawElevationPlateBoundaries,
                PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
            };
            var riverOptions = new RiverRenderOptions { Scale = renderOptions.Scale };

            MapImageRenderer.RenderElevationToFile(map, artifacts.ElevationImage!, elevationOptions);
            MapImageRenderer.RenderElevationDebugToFiles(map, outputDirectory, options: elevationOptions, riverOptions: riverOptions);
            ElevationJsonWriter.WriteToFile(map, artifacts.ElevationJson!, new ElevationJsonExportOptions
            {
                Mode = elevationJsonMode
            });
        }

        if (artifacts.LakesJson is not null)
            LakeJsonWriter.WriteToFile(map, artifacts.LakesJson);

        if (artifacts.RiversJson is not null)
        {
            MapImageRenderer.RenderElevationRiversToFile(map, artifacts.ElevationRiversImage!, new RiverRenderOptions
            {
                Scale = renderOptions.Scale
            });
            RiverJsonWriter.WriteToFile(map, artifacts.RiversJson);
        }

        if (artifacts.ClimateJson is not null)
        {
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Biomes));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateBiomesDebugImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.DebugBiomes, drawRivers: false, drawHillshade: false));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateTemperatureImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Temperature));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateMoistureImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Moisture));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimatePrecipitationImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Precipitation));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateHabitabilityImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Habitability));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateAgricultureImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Agriculture));
            MapImageRenderer.RenderClimateToFile(map, artifacts.ClimateIceImage!, CreateClimateOptions(renderOptions, ClimateRenderMode.Ice));
            ClimateJsonWriter.WriteToFile(map, artifacts.ClimateJson, new ClimateJsonExportOptions
            {
                Mode = climateJsonMode
            });
        }

        GeoJsonMapWriter.WriteRegionsToFile(map, artifacts.RegionsGeoJson);
        GeoJsonMapWriter.WriteLandmassesToFile(map, artifacts.LandmassesGeoJson);
        GeoJsonMapWriter.WriteWaterBodiesToFile(map, artifacts.WaterBodiesGeoJson);

        if (map.RegionRaster is not null)
            RegionRasterArtifactWriter.Write(map, artifacts.RegionsBin!, artifacts.RegionsSummaryJson!);
    }

    private static ClimateRenderOptions CreateClimateOptions(
        MapArtifactRenderOptions renderOptions,
        ClimateRenderMode mode,
        bool? drawRivers = null,
        bool? drawHillshade = null)
    {
        return new ClimateRenderOptions
        {
            Scale = renderOptions.Scale,
            Mode = mode,
            DrawHillshade = drawHillshade ?? renderOptions.DrawClimateHillshade,
            DrawRivers = drawRivers ?? renderOptions.DrawClimateRivers,
            DrawRiverValleyAccents = renderOptions.DrawClimateRiverValleyAccents,
            Elevation = new ElevationRenderOptions
            {
                Scale = renderOptions.Scale,
                DrawHillshade = drawHillshade ?? renderOptions.DrawClimateHillshade
            }
        };
    }

    private static MapGenerationRunSummary BuildSummary(
        GeneratedMap map,
        MapGenerationArtifactPaths artifacts,
        string maskPath,
        string outputDirectory,
        MapGenerationOptions options,
        TectonicPlateJsonExportMode tectonicJsonMode,
        ElevationJsonExportMode elevationJsonMode,
        ClimateJsonExportMode climateJsonMode)
    {
        var (minElevation, maxElevation) = GetElevationRange(map.Elevation);
        var (minTemperature, maxTemperature) = GetTemperatureRange(map.Climate);
        var (assignedRegionPixels, waterOrOutsidePixels) = GetRegionRasterCounts(map.RegionRaster);
        var hydrology = map.Hydrology;

        return new MapGenerationRunSummary(
            DateTimeOffset.UtcNow,
            maskPath,
            outputDirectory,
            new MapGenerationRunOptionSummary(
                options.PixelSize,
                options.ShapeExtraction.SimplifyTolerance,
                options.Regions.TargetArea,
                options.Regions.PointsMultiplier,
                options.Regions.MinAreaRatio,
                options.Regions.MaxAreaRatio,
                options.Boundaries.Detail,
                options.Boundaries.MaxOffset,
                options.Boundaries.MinLineLengthToCurve,
                options.Seed,
                options.ProjectionMode.ToString(),
                options.TectonicPlates.PlateCount,
                options.TectonicPlates.HotspotCount,
                options.Elevation.GenerateSmallLakes,
                options.Elevation.SmallLakeCountMultiplier,
                options.Elevation.SmallLakeScatterMultiplier,
                options.Elevation.SmallLakeSizeMultiplier,
                options.Hydrology.RiverDensity,
                options.Hydrology.MountainRiverDensity,
                options.Hydrology.MaxMountainSourcesPerCluster,
                options.Hydrology.MinMountainSourceSpacing,
                options.Hydrology.MajorRiverCountMultiplier,
                options.Hydrology.LongRiverCountMultiplier,
                options.Hydrology.TributaryDensity,
                options.Hydrology.MajorRiverTributaryMultiplier,
                options.Hydrology.LakeOutletInflowForceMultiplier,
                options.Hydrology.EndorheicBasinChance,
                options.Hydrology.MaxEndorheicBasins,
                options.Hydrology.DeltaFrequency,
                options.Hydrology.MeanderStrength,
                options.Hydrology.LakeOutletStrictness,
                options.Hydrology.PreserveCoastline,
                options.Hydrology.AllowRiverCarving,
                options.Climate.PolarLatitudeMargin,
                options.Climate.EquatorTemperatureCelsius,
                options.Climate.PoleCoolingCelsius,
                options.Climate.LapseRateCelsiusPerMeter,
                tectonicJsonMode.ToString(),
                elevationJsonMode.ToString(),
                climateJsonMode.ToString()),
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
                maxElevation,
                hydrology?.Rivers.Count,
                hydrology?.Rivers.Count(r => r.IsMajor),
                hydrology?.Basins.Count(b => b.TargetKind == DrainageTargetKind.EndorheicDryBasin),
                hydrology?.Mouths.Count(m => m.Kind is RiverMouthKind.Delta or RiverMouthKind.MarshDelta or RiverMouthKind.InlandDelta),
                map.Climate?.Width,
                map.Climate?.Height,
                minTemperature,
                maxTemperature,
                map.RegionRaster?.Width,
                map.RegionRaster?.Height,
                assignedRegionPixels,
                waterOrOutsidePixels),
            artifacts);
    }

    private static (int? AssignedRegionPixels, int? WaterOrOutsidePixels) GetRegionRasterCounts(RegionRaster? raster)
    {
        if (raster is null)
            return (null, null);

        var assigned = 0;
        var waterOrOutside = 0;
        foreach (var value in raster.RegionIdsSpan)
        {
            if (value > 0)
                assigned++;
            else
                waterOrOutside++;
        }

        return (assigned, waterOrOutside);
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

    private static (double? Min, double? Max) GetTemperatureRange(ClimateMap? climate)
    {
        if (climate is null)
            return (null, null);

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var y = 0; y < climate.Height; y++)
        {
            for (var x = 0; x < climate.Width; x++)
            {
                var value = climate.GetMeanAnnualTemperature(x, y);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return (min, max);
    }
}

public sealed class MapArtifactRenderOptions
{
    public float Scale { get; init; } = 1;
    public float RegionBorderWidth { get; init; } = 2;
    public float TectonicBoundaryWidth { get; init; } = 1;
    public bool DrawCrustPlateBoundaries { get; init; }
    public bool DrawFeaturePlateBoundaries { get; init; }
    public bool DrawElevationHillshade { get; init; } = true;
    public bool DrawElevationPlateBoundaries { get; init; }
    public bool DrawClimateHillshade { get; init; } = true;
    public bool DrawClimateRivers { get; init; } = true;
    public bool DrawClimateRiverValleyAccents { get; init; } = true;
}
