using System.Text;
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
            new RasterDto(
                EncodePlateRows(tectonicPlates.Raster),
                EncodeCrustRows(tectonicPlates.Raster)));
    }

    private static IReadOnlyList<string> EncodePlateRows(TectonicPlateRaster raster)
    {
        var rows = new string[raster.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < raster.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = raster.GetPlate(0, y).Value;

            for (var x = 1; x < raster.Width; x++)
            {
                var value = raster.GetPlate(x, y).Value;
                if (value == current)
                    continue;

                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(current).Append('x').Append(x - runStart);
                current = value;
                runStart = x;
            }

            if (sb.Length > 0)
                sb.Append(',');
            sb.Append(current).Append('x').Append(raster.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeCrustRows(TectonicPlateRaster raster)
    {
        var rows = new string[raster.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < raster.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = raster.GetCrust(0, y);

            for (var x = 1; x < raster.Width; x++)
            {
                var value = raster.GetCrust(x, y);
                if (value == current)
                    continue;

                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(current == CrustKind.Continental ? 'C' : 'O').Append('x').Append(x - runStart);
                current = value;
                runStart = x;
            }

            if (sb.Length > 0)
                sb.Append(',');
            sb.Append(current == CrustKind.Continental ? 'C' : 'O').Append('x').Append(raster.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
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
            plate.PointCount,
            new PointDto(plate.Centroid.X, plate.Centroid.Y));
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
        RasterDto Raster);

    private sealed record RasterDto(
        IReadOnlyList<string> PlateRows,
        IReadOnlyList<string> CrustRows);

    private sealed record TectonicPlateDto(
        int Id,
        TectonicPlateKind Kind,
        double MotionX,
        double MotionY,
        double Activity,
        double Density,
        double Thickness,
        int PointCount,
        PointDto Centroid);

    private sealed record PlateBoundaryDto(
        int PlateA,
        int PlateB,
        PlateBoundaryKind Kind,
        double Convergence,
        double Divergence,
        double Shear,
        int? SubductingPlate,
        IReadOnlyList<PointDto> Points);

    private sealed record PointDto(int X, int Y);
}
