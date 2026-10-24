using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.GeoJson;

/// <summary>Serializes a complete spatial reference without relying on computed coverage aliases.</summary>
public sealed class MapSpatialReferenceJsonConverter : JsonConverter<MapSpatialReference>
{
    public override MapSpatialReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var document = JsonSerializer.Deserialize<SpatialReferenceDocument>(ref reader, options)
            ?? throw new JsonException("The spatial reference is empty.");
        var reference = document.ToSpatialReference();
        reference.Validate();
        return reference;
    }

    public override void Write(Utf8JsonWriter writer, MapSpatialReference value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        JsonSerializer.Serialize(writer, SpatialReferenceDocument.From(value), options);
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
                _ => throw new JsonException($"Unknown world model '{WorldModelKind}'.")
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
}
