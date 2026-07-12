using MapRegionizer.Core.Generation;
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

        if (options.Debug)
            return RunWithDiagnostics(options, maskPath, outputDirectory);

        var mask = ImageMaskReader.Read(maskPath);
        var map = CreateGenerator(options).Generate(mask, options.GenerationOptions);
        return MapGenerationArtifactWriter.Write(
            map,
            maskPath,
            outputDirectory,
            options.GenerationOptions,
            options.TectonicJsonMode,
            options.ElevationJsonMode,
            options.ClimateJsonMode);
    }

    private static MapGenerationRunResult RunWithDiagnostics(
        MapGenerationRequestOptions options,
        string maskPath,
        string outputDirectory)
    {
        var baseline = MemorySnapshot.Capture();
        WriteMemLine("baseline", baseline, baseline);

        var afterMask = MemorySnapshot.Capture();
        var mask = ImageMaskReader.Read(maskPath);
        WriteMemLine("load mask", afterMask, MemorySnapshot.Capture());

        var afterGen = MemorySnapshot.Capture();
        var map = CreateGenerator(options).Generate(mask, options.GenerationOptions);
        WriteMemLine("generation", afterGen, MemorySnapshot.Capture());

        var afterArtifacts = MemorySnapshot.Capture();
        var result = MapGenerationArtifactWriter.Write(
            map,
            maskPath,
            outputDirectory,
            options.GenerationOptions,
            options.TectonicJsonMode,
            options.ElevationJsonMode,
            options.ClimateJsonMode);
        WriteMemLine("artifacts", afterArtifacts, MemorySnapshot.Capture());

        return result;
    }

    private static MapGenerator CreateGenerator(MapGenerationRequestOptions options)
    {
        if (!options.RasterizeRegions)
            return new MapGenerator();

        var pipeline = MapGenerationPipelineBuilder.CreateDefault()
            .AddRegionRasterization()
            .Build();

        return new MapGenerator(pipeline);
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

        options.GenerationOptions.Validate();
    }
}
