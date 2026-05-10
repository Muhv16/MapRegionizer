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

        var mask = ImageMaskReader.Read(maskPath);
        var map = new MapGenerator().Generate(mask, options.GenerationOptions);

        return MapGenerationArtifactWriter.Write(
            map,
            maskPath,
            outputDirectory,
            options.GenerationOptions,
            options.TectonicJsonMode,
            options.ElevationJsonMode,
            options.ClimateJsonMode);
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
