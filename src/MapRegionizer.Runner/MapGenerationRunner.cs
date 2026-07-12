using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.Runner;

public sealed class MapGenerationRunner
{
    public MapGenerationRunResult Run(MapGenerationRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Run(options.ToRequestOptions());
    }

    public MapGenerationRunResult Run(MapGenerationRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRunOptions(options);

        var maskPath = Path.GetFullPath(options.MaskPath);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var mask = ImageMaskReader.Read(maskPath);
        var importedDocument = options.RegionDraftPath is null ? null : RegionDraftGeoJson.ReadFromFile(options.RegionDraftPath);
        var generationOptions = WithRegionDistortion(
            options.GenerationOptions,
            options.RegionDraftDistortionEnabled ?? importedDocument?.ApplyBoundaryDistortion);
        if (options.Debug)
            return RunWithDiagnostics(options, generationOptions, importedDocument, mask, maskPath, outputDirectory);

        var session = Generate(mask, generationOptions, options.RasterizeRegions, importedDocument);
        return WriteResults(
            session,
            maskPath,
            outputDirectory,
            generationOptions,
            options,
            importedDocument,
            mask);
    }

    private static MapGenerationRunResult RunWithDiagnostics(
        MapGenerationRequestOptions options,
        MapGenerationOptions generationOptions,
        RegionDraftDocument? importedDocument,
        MapMask mask,
        string maskPath,
        string outputDirectory)
    {
        var baseline = MemorySnapshot.Capture();
        WriteMemLine("baseline", baseline, baseline);

        var afterGen = MemorySnapshot.Capture();
        var session = Generate(mask, generationOptions, options.RasterizeRegions, importedDocument);
        WriteMemLine("generation", afterGen, MemorySnapshot.Capture());

        var afterArtifacts = MemorySnapshot.Capture();
        var result = WriteResults(
            session,
            maskPath,
            outputDirectory,
            generationOptions,
            options,
            importedDocument,
            mask);
        WriteMemLine("artifacts", afterArtifacts, MemorySnapshot.Capture());

        return result;
    }

    private static MapGenerationSession Generate(
        MapMask mask,
        MapGenerationOptions options,
        bool rasterizeRegions,
        RegionDraftDocument? importedDocument)
    {
        var builder = MapGenerationPipelineBuilder.CreateDefault();
        if (rasterizeRegions)
            builder.AddRegionRasterization();
        var session = MapGenerationSession.Create(mask, options, builder.Build());
        if (importedDocument is not null)
        {
            session.RunUntil(MapDataKeys.Landmasses);
            RegionDraftCompatibility.EnsureCompatible(importedDocument, mask, options, session.Landmasses);
            session.SetRegionDraft(importedDocument.Draft);
        }
        session.RunFull();
        return session;
    }

    private static MapGenerationRunResult WriteResults(
        MapGenerationSession session,
        string maskPath,
        string outputDirectory,
        MapGenerationOptions generationOptions,
        MapGenerationRequestOptions requestOptions,
        RegionDraftDocument? importedDocument,
        MapMask mask)
    {
        var result = MapGenerationArtifactWriter.Write(
            session.CurrentMap,
            maskPath,
            outputDirectory,
            generationOptions,
            requestOptions.TectonicJsonMode,
            requestOptions.ElevationJsonMode,
            requestOptions.ClimateJsonMode);
        if (string.IsNullOrWhiteSpace(requestOptions.RegionDraftOutputPath))
            return result;

        var draftPath = Path.GetFullPath(requestOptions.RegionDraftOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);
        var document = RegionDraftCompatibility.CreateDocument(
            mask,
            generationOptions,
            session.Landmasses,
            CreateExportDraft(session.RawRegions, importedDocument),
            generationOptions.Boundaries.Enabled);
        RegionDraftGeoJson.WriteToFile(document, draftPath);
        var artifacts = result.Artifacts with { RegionDraftGeoJson = draftPath };
        var summary = result.Summary with { Artifacts = artifacts };
        MapGenerationArtifactWriter.WriteSummary(summary);
        return result with { Artifacts = artifacts, Summary = summary };
    }

    private static RegionDraft CreateExportDraft(IReadOnlyList<MapRegionizer.Core.Domain.MapRegion> regions, RegionDraftDocument? importedDocument)
    {
        var imported = importedDocument?.Draft.Regions.Where(region => region.Id.HasValue)
            .ToDictionary(region => region.Id!.Value) ?? [];
        return new RegionDraft(regions.Select(region =>
        {
            if (imported.TryGetValue(region.Id, out var existing))
                return existing with { Id = region.Id, LandmassId = region.LandmassId, Shape = region.Shape.Copy() };
            return new RegionDraftRegion(region.Id, region.LandmassId, region.Shape.Copy(), RegionDraftOrigin.Generated);
        }).ToList());
    }

    private static MapGenerationOptions WithRegionDistortion(MapGenerationOptions options, bool? enabled)
    {
        if (!enabled.HasValue || options.Boundaries.Enabled == enabled.Value)
            return options;
        return new MapGenerationOptions
        {
            PixelSize = options.PixelSize,
            Seed = options.Seed,
            Debug = options.Debug,
            ShapeExtraction = options.ShapeExtraction,
            WaterBodies = options.WaterBodies,
            Regions = options.Regions,
            ProjectionMode = options.ProjectionMode,
            TectonicPlates = options.TectonicPlates,
            Elevation = options.Elevation,
            Hydrology = options.Hydrology,
            Climate = options.Climate,
            Boundaries = new BoundaryDistortionOptions
            {
                Enabled = enabled.Value,
                Detail = options.Boundaries.Detail,
                MaxOffset = options.Boundaries.MaxOffset,
                MinLineLengthToCurve = options.Boundaries.MinLineLengthToCurve
            }
        };
    }

    private static void WriteMemLine(string label, MemorySnapshot before, MemorySnapshot after)
    {
        var delta = after.DeltaFrom(before);
        Console.Error.WriteLine($"[MEM] {label,-20} | {delta.Format(after.ManagedBytes, after.WorkingSetBytes)}");
    }

    private static void ValidateRunOptions(MapGenerationRequestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MaskPath))
            throw new ArgumentException("Mask path is required.", nameof(options));

        if (!File.Exists(options.MaskPath))
            throw new FileNotFoundException("Mask file was not found.", options.MaskPath);

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(options));

        if (!string.IsNullOrWhiteSpace(options.RegionDraftPath) && !File.Exists(options.RegionDraftPath))
            throw new FileNotFoundException("Region draft file was not found.", options.RegionDraftPath);
        options.GenerationOptions.Validate();
    }
}
