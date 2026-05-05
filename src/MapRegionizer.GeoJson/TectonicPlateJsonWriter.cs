using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

public static class TectonicPlateJsonWriter
{
    public static string Write(TectonicPlateMap tectonicPlates, TectonicPlateJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tectonicPlates);
        options ??= new TectonicPlateJsonExportOptions();
        return JsonSerializer.Serialize(ToDto(tectonicPlates, options), CreateSerializerOptions(options));
    }

    public static string Write(GeneratedMap map, TectonicPlateJsonExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.TectonicPlates is null)
            throw new InvalidOperationException("The map does not contain tectonic plate data.");

        return Write(map.TectonicPlates, options);
    }

    public static void WriteToFile(TectonicPlateMap tectonicPlates, string filePath, TectonicPlateJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(tectonicPlates, options));

    public static void WriteToFile(GeneratedMap map, string filePath, TectonicPlateJsonExportOptions? options = null) =>
        File.WriteAllText(filePath, Write(map, options));

    private static JsonSerializerOptions CreateSerializerOptions(TectonicPlateJsonExportOptions options)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = options.WriteIndented ?? options.Mode == TectonicPlateJsonExportMode.Diagnostic,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private static TectonicPlateMapDto ToDto(TectonicPlateMap tectonicPlates, TectonicPlateJsonExportOptions options)
    {
        return new TectonicPlateMapDto(
            tectonicPlates.Width,
            tectonicPlates.Height,
            tectonicPlates.Plates.Select(ToDto).ToArray(),
            tectonicPlates.Boundaries.Select(b => ToDto(b, options)).ToArray(),
            new RasterDto(
                EncodePlateRows(tectonicPlates.Raster),
                EncodeCrustRows(tectonicPlates.Raster)),
            ToLayersDto(tectonicPlates, options));
    }

    private static TectonicLayersDto? ToLayersDto(TectonicPlateMap tectonicPlates, TectonicPlateJsonExportOptions options)
    {
        if (tectonicPlates.CrustFields is null && tectonicPlates.BoundaryMap is null && tectonicPlates.Features is null)
            return null;

        return new TectonicLayersDto(
            tectonicPlates.CrustFields is null ? null : ToDto(tectonicPlates.CrustFields, options),
            tectonicPlates.BoundaryMap?.Segments.Select(s => ToDto(s, options)).ToArray(),
            tectonicPlates.Features?.Features.Select(f => ToDto(f, options)).ToArray(),
            tectonicPlates.Features?.Islands.Select(ToDto).ToArray());
    }

    private static CrustFieldDto ToDto(CrustFieldMap crustFields, TectonicPlateJsonExportOptions options)
    {
        var diagnostic = options.Mode == TectonicPlateJsonExportMode.Diagnostic;
        return new CrustFieldDto(
            EncodeCrustRows(crustFields),
            EncodeCoastalZoneRows(crustFields),
            EncodeAgeRows(crustFields, crustFields.GetOceanicAge, diagnostic ? 1 : 2),
            EncodeAgeRows(crustFields, crustFields.GetContinentalAge, diagnostic ? 1 : 50),
            EncodeAgeRows(crustFields, crustFields.GetLastRiftingAge, diagnostic ? 1 : 5),
            EncodeAgeRows(crustFields, crustFields.GetLastOrogenyAge, diagnostic ? 1 : 10),
            EncodeAgeRows(crustFields, crustFields.GetLastVolcanismAge, diagnostic ? 1 : 5));
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

    private static PlateBoundaryDto ToDto(PlateBoundary boundary, TectonicPlateJsonExportOptions options)
    {
        var includeAggregatePoints = options.Mode == TectonicPlateJsonExportMode.Diagnostic && options.IncludeDuplicatePointClouds;
        return new PlateBoundaryDto(
            boundary.PlateA.Value,
            boundary.PlateB.Value,
            boundary.Kind,
            boundary.Convergence,
            boundary.Divergence,
            boundary.Shear,
            boundary.SubductingPlate?.Value,
            boundary.SegmentIds,
            includeAggregatePoints ? ToPoints(boundary.Points) : null,
            includeAggregatePoints ? boundary.Segments?.Select(s => ToDto(s, options)).ToArray() : null);
    }

    private static PlateBoundarySegmentDto ToDto(PlateBoundarySegment segment, TectonicPlateJsonExportOptions options)
    {
        var includePoints = options.Mode != TectonicPlateJsonExportMode.Summary;
        return new PlateBoundarySegmentDto(
            segment.Id,
            segment.PlateA.Value,
            segment.PlateB.Value,
            segment.Kind,
            segment.Convergence,
            segment.Divergence,
            segment.Shear,
            segment.SubductingPlate?.Value,
            includePoints ? ToPoints(segment.Points) : null);
    }

    private static TectonicFeatureDto ToDto(TectonicFeature feature, TectonicPlateJsonExportOptions options)
    {
        var includePoints = options.Mode == TectonicPlateJsonExportMode.Diagnostic
            || (options.Mode == TectonicPlateJsonExportMode.CompactDiagnostic && feature.SourceSegmentId is null);

        return new TectonicFeatureDto(
            feature.Id,
            feature.Kind,
            feature.Age,
            feature.Intensity,
            feature.SourceSegmentId,
            includePoints ? ToPoints(feature.Points) : null);
    }

    private static TectonicIslandDto ToDto(TectonicIsland island)
    {
        return new TectonicIslandDto(new PointDto(island.Center.X, island.Center.Y), island.Kind, island.Area, island.PlateId.Value);
    }

    private static IReadOnlyList<PointDto> ToPoints(IEnumerable<GridPoint> points)
    {
        return points.Select(p => new PointDto(p.X, p.Y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
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

                AppendRun(sb, current.ToString(CultureInfo.InvariantCulture), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current.ToString(CultureInfo.InvariantCulture), raster.Width - runStart);
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

                AppendRun(sb, CrustCode(current).ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, CrustCode(current).ToString(), raster.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeCrustRows(CrustFieldMap crustFields)
    {
        var rows = new string[crustFields.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < crustFields.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = crustFields.GetCrust(0, y);

            for (var x = 1; x < crustFields.Width; x++)
            {
                var value = crustFields.GetCrust(x, y);
                if (value == current)
                    continue;

                AppendRun(sb, CrustCode(current).ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, CrustCode(current).ToString(), crustFields.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeCoastalZoneRows(CrustFieldMap crustFields)
    {
        var rows = new string[crustFields.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < crustFields.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = crustFields.GetCoastalZone(0, y);

            for (var x = 1; x < crustFields.Width; x++)
            {
                var value = crustFields.GetCoastalZone(x, y);
                if (value == current)
                    continue;

                AppendRun(sb, CoastalCode(current).ToString(), x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, CoastalCode(current).ToString(), crustFields.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static IReadOnlyList<string> EncodeAgeRows(CrustFieldMap crustFields, Func<int, int, double> readAge, int binSize)
    {
        var rows = new string[crustFields.Height];
        var sb = new StringBuilder();

        for (var y = 0; y < crustFields.Height; y++)
        {
            sb.Clear();
            var runStart = 0;
            var current = AgeCode(readAge(0, y), binSize);

            for (var x = 1; x < crustFields.Width; x++)
            {
                var value = AgeCode(readAge(x, y), binSize);
                if (value == current)
                    continue;

                AppendRun(sb, current, x - runStart);
                current = value;
                runStart = x;
            }

            AppendRun(sb, current, crustFields.Width - runStart);
            rows[y] = sb.ToString();
        }

        return rows;
    }

    private static void AppendRun(StringBuilder sb, string value, int length)
    {
        if (sb.Length > 0)
            sb.Append(',');

        sb.Append(value).Append('x').Append(length);
    }

    private static char CrustCode(CrustKind kind) => kind switch
    {
        CrustKind.Continental => 'C',
        CrustKind.Oceanic => 'O',
        CrustKind.Shelf => 'S',
        CrustKind.Arc => 'A',
        CrustKind.Rift => 'R',
        CrustKind.Terrane => 'T',
        _ => '?'
    };

    private static char CoastalCode(CoastalZoneKind kind) => kind switch
    {
        CoastalZoneKind.None => 'N',
        CoastalZoneKind.Shelf => 'S',
        CoastalZoneKind.Slope => 'L',
        CoastalZoneKind.PassiveMargin => 'P',
        CoastalZoneKind.ActiveMargin => 'A',
        CoastalZoneKind.ShallowSea => 'M',
        _ => '?'
    };

    private static string AgeCode(double age, int binSize)
    {
        if (double.IsNaN(age))
            return "n";

        var bin = (int)Math.Clamp(Math.Round(age / Math.Max(1, binSize)), 0, ushort.MaxValue);
        return bin.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record TectonicPlateMapDto(
        int Width,
        int Height,
        IReadOnlyList<TectonicPlateDto> Plates,
        IReadOnlyList<PlateBoundaryDto> Boundaries,
        RasterDto Raster,
        TectonicLayersDto? Layers);

    private sealed record RasterDto(
        IReadOnlyList<string> PlateRows,
        IReadOnlyList<string> CrustRows);

    private sealed record TectonicLayersDto(
        CrustFieldDto? CrustFields,
        IReadOnlyList<PlateBoundarySegmentDto>? BoundarySegments,
        IReadOnlyList<TectonicFeatureDto>? Features,
        IReadOnlyList<TectonicIslandDto>? Islands);

    private sealed record CrustFieldDto(
        IReadOnlyList<string> CrustRows,
        IReadOnlyList<string> CoastalZoneRows,
        IReadOnlyList<string> OceanicAgeRows,
        IReadOnlyList<string> ContinentalAgeRows,
        IReadOnlyList<string> LastRiftingAgeRows,
        IReadOnlyList<string> LastOrogenyAgeRows,
        IReadOnlyList<string> LastVolcanismAgeRows);

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
        IReadOnlyList<int>? SegmentIds,
        IReadOnlyList<PointDto>? Points,
        IReadOnlyList<PlateBoundarySegmentDto>? Segments);

    private sealed record PlateBoundarySegmentDto(
        int Id,
        int PlateA,
        int PlateB,
        BoundarySegmentKind Kind,
        double Convergence,
        double Divergence,
        double Shear,
        int? SubductingPlate,
        IReadOnlyList<PointDto>? Points);

    private sealed record TectonicFeatureDto(
        int Id,
        TectonicFeatureKind Kind,
        double Age,
        double Intensity,
        int? SourceSegmentId,
        IReadOnlyList<PointDto>? Points);

    private sealed record TectonicIslandDto(
        PointDto Center,
        IslandKind Kind,
        double Area,
        int PlateId);

    private sealed record PointDto(int X, int Y);
}

public sealed class TectonicPlateJsonExportOptions
{
    public TectonicPlateJsonExportMode Mode { get; init; } = TectonicPlateJsonExportMode.Summary;
    public bool? WriteIndented { get; init; }
    public bool IncludeDuplicatePointClouds { get; init; }
}

public enum TectonicPlateJsonExportMode
{
    Summary,
    CompactDiagnostic,
    Diagnostic
}
