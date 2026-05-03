using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class TectonicPlateJsonWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(TectonicPlateMap tectonicPlates)
    {
        ArgumentNullException.ThrowIfNull(tectonicPlates);
        return JsonSerializer.Serialize(ToDto(tectonicPlates), SerializerOptions);
    }

    public static string Write(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.TectonicPlates is null)
            throw new InvalidOperationException("The map does not contain tectonic plate data.");

        return Write(map.TectonicPlates);
    }

    public static void WriteToFile(TectonicPlateMap tectonicPlates, string filePath) => File.WriteAllText(filePath, Write(tectonicPlates));

    public static void WriteToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, Write(map));

    private static TectonicPlateMapDto ToDto(TectonicPlateMap tectonicPlates)
    {
        return new TectonicPlateMapDto(
            tectonicPlates.Width,
            tectonicPlates.Height,
            tectonicPlates.Plates.Select(ToDto).ToArray(),
            tectonicPlates.Boundaries.Select(ToDto).ToArray(),
            tectonicPlates.PlateByPoint
                .OrderBy(kv => kv.Key.Y)
                .ThenBy(kv => kv.Key.X)
                .Select(kv => new PlatePointDto(kv.Key.X, kv.Key.Y, kv.Value.Value, tectonicPlates.CrustByPoint[kv.Key]))
                .ToArray());
    }

    private static TectonicPlateDto ToDto(TectonicPlate plate)
    {
        return new TectonicPlateDto(
            plate.Id.Value,
            plate.Kind,
            plate.Motion.X,
            plate.Motion.Y,
            plate.Activity,
            plate.Density,
            plate.Thickness,
            plate.Points.Count,
            plate.Points.Select(p => new PointDto(p.X, p.Y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray());
    }

    private static PlateBoundaryDto ToDto(PlateBoundary boundary)
    {
        return new PlateBoundaryDto(
            boundary.PlateA.Value,
            boundary.PlateB.Value,
            boundary.Kind,
            boundary.Convergence,
            boundary.Divergence,
            boundary.Shear,
            boundary.SubductingPlate?.Value,
            boundary.Points.Select(p => new PointDto(p.X, p.Y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray());
    }

    private sealed record TectonicPlateMapDto(
        int Width,
        int Height,
        IReadOnlyList<TectonicPlateDto> Plates,
        IReadOnlyList<PlateBoundaryDto> Boundaries,
        IReadOnlyList<PlatePointDto> PlateByPoint);

    private sealed record TectonicPlateDto(
        int Id,
        TectonicPlateKind Kind,
        double MotionX,
        double MotionY,
        double Activity,
        double Density,
        double Thickness,
        int PointCount,
        IReadOnlyList<PointDto> Points);

    private sealed record PlateBoundaryDto(
        int PlateA,
        int PlateB,
        PlateBoundaryKind Kind,
        double Convergence,
        double Divergence,
        double Shear,
        int? SubductingPlate,
        IReadOnlyList<PointDto> Points);

    private sealed record PlatePointDto(int X, int Y, int PlateId, CrustKind Crust);

    private sealed record PointDto(int X, int Y);
}
