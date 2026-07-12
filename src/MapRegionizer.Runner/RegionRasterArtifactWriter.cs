using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.Runner;

internal static class RegionRasterArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(GeneratedMap map, string binaryPath, string summaryPath)
    {
        ArgumentNullException.ThrowIfNull(map);
        var raster = map.RegionRaster ?? throw new ArgumentException("Map does not contain a region raster.", nameof(map));

        WriteBinary(raster, binaryPath);

        var summary = BuildSummary(map, raster, binaryPath);
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, JsonOptions));
    }

    private static void WriteBinary(RegionRaster raster, string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        foreach (var regionId in raster.RegionIdsSpan)
            writer.Write(regionId);
    }

    private static RegionRasterArtifactSummary BuildSummary(GeneratedMap map, RegionRaster raster, string binaryPath)
    {
        var assigned = 0;
        var waterOrOutside = 0;
        var ids = new HashSet<int>();

        foreach (var value in raster.RegionIdsSpan)
        {
            if (value > 0)
            {
                assigned++;
                ids.Add(value);
            }
            else
            {
                waterOrOutside++;
            }
        }

        return new RegionRasterArtifactSummary(
            raster.Width,
            raster.Height,
            "int32",
            "little-endian",
            "row-major",
            0,
            Path.GetFullPath(binaryPath),
            raster.Width * raster.Height,
            assigned,
            waterOrOutside,
            map.Regions.Count,
            ids.Order().ToArray());
    }
}

internal sealed record RegionRasterArtifactSummary(
    int Width,
    int Height,
    string ValueType,
    string ByteOrder,
    string CellOrder,
    int WaterOrOutsideValue,
    string BinaryPath,
    int CellCount,
    int AssignedLandPixelCount,
    int WaterOrOutsidePixelCount,
    int RegionCount,
    IReadOnlyList<int> RegionIds);
