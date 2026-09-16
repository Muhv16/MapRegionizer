using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;

namespace MapRegionizer.GeoJson;

/// <summary>
/// Versioned persistence adapter for incomplete manual-map projects. It is
/// intentionally separate from the RegionDraft GeoJSON lifecycle.
/// </summary>
public static class ManualMapJson
{
    public const string CurrentSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(ManualMapDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var document = new ManualMapDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Grid = new ManualMapGrid { Width = draft.GridWidth, Height = draft.GridHeight },
            Vertices = draft.Vertices
                .OrderBy(vertex => vertex.Id)
                .Select(vertex => new ManualMapVertexDocument
                {
                    Id = vertex.Id,
                    X = vertex.Position.X,
                    Y = vertex.Position.Y
                })
                .ToArray(),
            Regions = draft.Regions
                .OrderBy(region => region.Id)
                .Select(region => new ManualMapRegionDocument
                {
                    Id = region.Id,
                    Name = region.Name,
                    Vertices = region.VertexIds.ToArray()
                })
                .ToArray()
        };

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static ManualMapDraft Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ManualMapDocument>(json, SerializerOptions)
            ?? throw new ArgumentException("The manual-map document is empty.", nameof(json));
        if (!string.Equals(document.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported manual-map schema version '{document.SchemaVersion}'. Expected '{CurrentSchemaVersion}'.",
                nameof(json));
        }

        if (document.Grid is null || document.Grid.Width <= 0 || document.Grid.Height <= 0)
            throw new ArgumentException("The manual-map document must contain a positive grid size.", nameof(json));

        var vertices = document.Vertices?.Select(vertex => new ManualMapVertex(
            vertex.Id,
            new MapPoint(vertex.X, vertex.Y))).ToArray() ?? [];
        var regions = document.Regions?.Select(region => new ManualRegionFace(
            region.Id,
            region.Vertices ?? [],
            region.Name)).ToArray() ?? [];
        return new ManualMapDraft(document.Grid.Width, document.Grid.Height, vertices, regions);
    }

    public static void Save(string path, ManualMapDraft draft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Write(draft));
    }

    public static ManualMapDraft Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllText(path));
    }

    private sealed class ManualMapDocument
    {
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public ManualMapGrid? Grid { get; set; }
        public ManualMapVertexDocument[]? Vertices { get; set; }
        public ManualMapRegionDocument[]? Regions { get; set; }
    }

    private sealed class ManualMapGrid
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private sealed class ManualMapVertexDocument
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed class ManualMapRegionDocument
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int[]? Vertices { get; set; }
    }
}
