using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class LakeJsonWriter
{
    public static string Write(WaterSurfaceMap waterSurfaces, LakeJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(waterSurfaces);
        options ??= new LakeJsonExportOptions();
        return JsonSerializer.Serialize(ToDto(waterSurfaces, options), CreateSerializerOptions(options));
    }

    public static string Write(GeneratedMap map, LakeJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var waterSurfaces = map.WaterSurfaces ?? map.Elevation?.WaterSurfaces;
        if (waterSurfaces is null)
            throw new InvalidOperationException("The map does not contain lake surface data.");

        return Write(waterSurfaces, options);
    }

    public static void WriteToFile(WaterSurfaceMap waterSurfaces, string filePath, LakeJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(waterSurfaces, options));

    public static void WriteToFile(GeneratedMap map, string filePath, LakeJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(map, options));

    private static JsonSerializerOptions CreateSerializerOptions(LakeJsonExportOptions options) =>
        new()
        {
            WriteIndented = options.WriteIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

    private static LakeMapDto ToDto(WaterSurfaceMap waterSurfaces, LakeJsonExportOptions options)
    {
        var lakes = waterSurfaces.Bodies
            .Where(body => body.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
            .Where(body => options.IncludeUnclassified || body.LakeOrigin.HasValue)
            .OrderBy(body => body.Id.Value)
            .Select(ToLakeDto)
            .ToList();

        return new LakeMapDto(waterSurfaces.Width, waterSurfaces.Height, lakes);
    }

    private static LakeDto ToLakeDto(WaterBodySurface body)
    {
        var centroid = body.Centroid;
        return new LakeDto(
            body.Id.Value,
            body.Kind,
            body.CellCount,
            centroid?.X,
            centroid?.Y,
            body.LakeLocation,
            body.LakeOrigin,
            body.LakeProfile,
            Math.Round(body.SurfaceElevationMeters, 2),
            Math.Round(body.SpillElevationMeters, 2),
            Math.Round(body.MarginMeters, 2),
            Math.Round(body.MaxDepthMeters, 2),
            body.ShorelineCellCount,
            Math.Round(body.MeanShorelineElevationMeters, 2),
            Math.Round(body.ShorelineReliefMeters, 2),
            Math.Round(body.TectonicInfluence, 3),
            Math.Round(body.VolcanicInfluence, 3));
    }

    private sealed record LakeMapDto(int Width, int Height, IReadOnlyList<LakeDto> Lakes);

    private sealed record LakeDto(
        int Id,
        WaterBodyKind Kind,
        int CellCount,
        int? CentroidX,
        int? CentroidY,
        LakeLocationKind? Location,
        LakeOriginKind? Origin,
        LakeProfileKind? Profile,
        double SurfaceElevationMeters,
        double SpillElevationMeters,
        double MarginMeters,
        double MaxDepthMeters,
        int ShorelineCellCount,
        double MeanShorelineElevationMeters,
        double ShorelineReliefMeters,
        double TectonicInfluence,
        double VolcanicInfluence);
}

public sealed class LakeJsonExportOptions
{
    public bool WriteIndented { get; init; } = true;
    public bool IncludeUnclassified { get; init; }
}
