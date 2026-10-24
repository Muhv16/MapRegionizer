using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;

namespace MapRegionizer.GeoJson;

/// <summary>
/// Versioned persistence adapter for incomplete manual-map projects. It is
/// intentionally separate from the RegionDraft GeoJSON lifecycle.
/// </summary>
public static class ManualMapJson
{
    public const string CurrentSchemaVersion = "3.0";
    public const string SpatialReferenceSchemaVersion = "2.0";
    public const string LegacySchemaVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(), new MapSpatialReferenceJsonConverter() }
    };

    public static string Write(ManualMapDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        // Preserve the original overload as an explicit legacy export. New
        // editor projects must use the overload that stores their geography.
        return JsonSerializer.Serialize(CreateDocument(draft, LegacySchemaVersion, null), SerializerOptions);
    }

    public static string Write(ManualMapDraft draft, MapSpatialReference spatialReference)
        => Write(draft, spatialReference, [], null);

    public static string Write(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        IReadOnlyList<GeographicControlPoint> controlPoints,
        GeographicCalibrationParameters? calibrationParameters,
        GeographicCalibrationResult? calibrationReport = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        ArgumentNullException.ThrowIfNull(controlPoints);
        spatialReference.Validate();
        if (spatialReference.GridWidth != draft.GridWidth || spatialReference.GridHeight != draft.GridHeight)
            throw new ArgumentException("Draft and spatial reference grid dimensions must match.", nameof(spatialReference));

        return JsonSerializer.Serialize(
            CreateDocument(draft, CurrentSchemaVersion, SpatialReferenceDocument.From(spatialReference), controlPoints, calibrationParameters, calibrationReport),
            SerializerOptions);
    }

    public static ManualMapDraft Read(string json) => ReadProject(json).Draft;

    public static ManualMapProject ReadProject(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ManualMapDocument>(json, SerializerOptions)
            ?? throw new ArgumentException("The manual-map document is empty.", nameof(json));
        if (!string.Equals(document.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal) &&
            !string.Equals(document.SchemaVersion, SpatialReferenceSchemaVersion, StringComparison.Ordinal) &&
            !string.Equals(document.SchemaVersion, LegacySchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported manual-map schema version '{document.SchemaVersion}'. Expected '{LegacySchemaVersion}', '{SpatialReferenceSchemaVersion}' or '{CurrentSchemaVersion}'.",
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
        var draft = new ManualMapDraft(document.Grid.Width, document.Grid.Height, vertices, regions);
        MapSpatialReference? spatialReference = null;
        if (string.Equals(document.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal) ||
            string.Equals(document.SchemaVersion, SpatialReferenceSchemaVersion, StringComparison.Ordinal))
        {
            if (document.SpatialReference is null)
                throw new ArgumentException("A version 2.0 manual-map document must contain a spatial reference.", nameof(json));
            spatialReference = document.SpatialReference.ToSpatialReference();
            if (spatialReference.GridWidth != draft.GridWidth || spatialReference.GridHeight != draft.GridHeight)
                throw new ArgumentException("The spatial reference dimensions do not match the manual-map grid.", nameof(json));
            spatialReference.Validate();
        }

        return new ManualMapProject(
            draft,
            spatialReference,
            document.SchemaVersion,
            document.ControlPoints ?? [],
            document.CalibrationParameters,
            document.CalibrationReport);
    }

    public static void Save(string path, ManualMapDraft draft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Write(draft));
    }

    public static void Save(string path, ManualMapDraft draft, MapSpatialReference spatialReference)
        => Save(path, draft, spatialReference, [], null);

    public static void Save(
        string path,
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        IReadOnlyList<GeographicControlPoint> controlPoints,
        GeographicCalibrationParameters? calibrationParameters,
        GeographicCalibrationResult? calibrationReport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Write(draft, spatialReference, controlPoints, calibrationParameters, calibrationReport));
    }

    public static ManualMapDraft Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllText(path));
    }

    public static ManualMapProject LoadProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadProject(File.ReadAllText(path));
    }

    private static ManualMapDocument CreateDocument(
        ManualMapDraft draft,
        string schemaVersion,
        SpatialReferenceDocument? spatialReference,
        IReadOnlyList<GeographicControlPoint>? controlPoints = null,
        GeographicCalibrationParameters? calibrationParameters = null,
        GeographicCalibrationResult? calibrationReport = null) => new()
        {
            SchemaVersion = schemaVersion,
            Grid = new ManualMapGrid { Width = draft.GridWidth, Height = draft.GridHeight },
            SpatialReference = spatialReference,
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
            .ToArray(),
            ControlPoints = controlPoints?.OrderBy(point => point.Id).ToArray(),
            CalibrationParameters = calibrationParameters,
            CalibrationReport = calibrationReport
        };

    private sealed class ManualMapDocument
    {
        public string SchemaVersion { get; set; } = LegacySchemaVersion;
        public ManualMapGrid? Grid { get; set; }
        public SpatialReferenceDocument? SpatialReference { get; set; }
        public ManualMapVertexDocument[]? Vertices { get; set; }
        public ManualMapRegionDocument[]? Regions { get; set; }
        public GeographicControlPoint[]? ControlPoints { get; set; }
        public GeographicCalibrationParameters? CalibrationParameters { get; set; }
        public GeographicCalibrationResult? CalibrationReport { get; set; }
    }

    private sealed class SpatialReferenceDocument
    {
        public int GridWidth { get; set; }
        public int GridHeight { get; set; }
        public double UnitsPerCell { get; set; }
        public WorldModelKind WorldModelKind { get; set; }
        public double? PlanetRadius { get; set; }
        public MapCoverageKind CoverageKind { get; set; }
        public double LongitudeStartDegrees { get; set; }
        public double LongitudeSpanDegrees { get; set; }
        public double SouthLatitude { get; set; }
        public double NorthLatitude { get; set; }
        public GridMappingKind GridMapping { get; set; }
        public GridTopologyKind Topology { get; set; }
        public CoordinateSpaceKind CanonicalCoordinates { get; set; }
        public bool PreserveProjectedCellAspectRatio { get; set; }
        public LegacyCompatibilityProfile LegacyCompatibility { get; set; }

        public static SpatialReferenceDocument From(MapSpatialReference reference) => new()
        {
            GridWidth = reference.GridWidth,
            GridHeight = reference.GridHeight,
            UnitsPerCell = reference.UnitsPerCell,
            WorldModelKind = reference.WorldModel.Kind,
            PlanetRadius = reference.WorldModel.PlanetRadius,
            CoverageKind = reference.Coverage.Kind,
            LongitudeStartDegrees = reference.Longitude.StartLongitudeDegrees,
            LongitudeSpanDegrees = reference.Longitude.SpanDegrees,
            SouthLatitude = reference.SouthLatitude,
            NorthLatitude = reference.NorthLatitude,
            GridMapping = reference.GridMapping,
            Topology = reference.Topology,
            CanonicalCoordinates = reference.CanonicalCoordinates,
            PreserveProjectedCellAspectRatio = reference.PreserveProjectedCellAspectRatio,
            LegacyCompatibility = reference.LegacyCompatibility
        };

        public MapSpatialReference ToSpatialReference()
        {
            var longitude = new LongitudeInterval(LongitudeStartDegrees, LongitudeSpanDegrees);
            var coverage = MapCoverage.Create(CoverageKind, longitude, SouthLatitude, NorthLatitude);
            var world = WorldModelKind switch
            {
                global::MapRegionizer.Core.Domain.WorldModelKind.Planar => WorldModelDescriptor.Planar(),
                global::MapRegionizer.Core.Domain.WorldModelKind.Spherical => WorldModelDescriptor.Spherical(PlanetRadius),
                _ => throw new ArgumentException($"Unknown world model '{WorldModelKind}'.")
            };
            return new MapSpatialReference
            {
                GridWidth = GridWidth,
                GridHeight = GridHeight,
                UnitsPerCell = UnitsPerCell,
                WorldModel = world,
                Coverage = coverage,
                GridMapping = GridMapping,
                Topology = Topology,
                CanonicalCoordinates = CanonicalCoordinates,
                PreserveProjectedCellAspectRatio = PreserveProjectedCellAspectRatio,
                LegacyCompatibility = LegacyCompatibility
            };
        }
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

public sealed record ManualMapProject(
    ManualMapDraft Draft,
    MapSpatialReference? SpatialReference,
    string SchemaVersion,
    IReadOnlyList<GeographicControlPoint>? ControlPoints = null,
    GeographicCalibrationParameters? CalibrationParameters = null,
    GeographicCalibrationResult? CalibrationReport = null)
{
    public bool HasSpatialReference => SpatialReference is not null;
}
