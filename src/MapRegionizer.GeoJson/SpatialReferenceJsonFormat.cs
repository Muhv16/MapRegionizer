using MapRegionizer.Core.Domain;
using Newtonsoft.Json.Linq;

namespace MapRegionizer.GeoJson;

/// <summary>
/// Shared JSON representation of the canonical <see cref="MapSpatialReference"/>
/// descriptor. Every versioned document format that stores a complete spatial
/// descriptor must use this single shape so consumers never have to reconcile
/// two dialects of the same metadata.
/// </summary>
internal static class SpatialReferenceJsonFormat
{
    public static JObject Write(MapSpatialReference reference) =>
        new()
        {
            ["worldModel"] = new JObject
            {
                ["kind"] = reference.WorldModel.Kind.ToString(),
                ["planetRadius"] = reference.WorldModel.PlanetRadius
            },
            ["gridMapping"] = reference.GridMapping.ToString(),
            ["topology"] = reference.Topology.ToString(),
            ["coverage"] = new JObject
            {
                ["kind"] = reference.Coverage.Kind.ToString(),
                ["longitudeStart"] = reference.Longitude.StartLongitudeDegrees,
                ["longitudeSpan"] = reference.Longitude.SpanDegrees,
                ["southLatitude"] = reference.Coverage.SouthLatitude,
                ["northLatitude"] = reference.Coverage.NorthLatitude
            },
            ["gridWidth"] = reference.GridWidth,
            ["gridHeight"] = reference.GridHeight,
            ["unitsPerCell"] = reference.UnitsPerCell,
            ["canonicalCoordinates"] = reference.CanonicalCoordinates.ToString(),
            ["preserveProjectedCellAspectRatio"] = reference.PreserveProjectedCellAspectRatio,
            ["legacyCompatibility"] = reference.LegacyCompatibility.ToString()
        };

    public static MapSpatialReference Read(JToken? source, string documentKind)
    {
        var spatial = source as JObject
            ?? throw new InvalidOperationException($"{documentKind} spatialReference is required.");
        var world = spatial["worldModel"] as JObject
            ?? throw new InvalidOperationException($"{documentKind} spatialReference.worldModel is required.");
        var coverage = spatial["coverage"] as JObject
            ?? throw new InvalidOperationException($"{documentKind} spatialReference.coverage is required.");

        var worldKind = ParseEnum<WorldModelKind>(RequiredString(world, "kind"), "worldModel.kind", documentKind);
        var planetRadius = world.Value<double?>("planetRadius");
        var longitudeStart = RequiredDouble(coverage, "longitudeStart");
        var longitudeSpan = RequiredDouble(coverage, "longitudeSpan");
        var coverageKind = ParseEnum<MapCoverageKind>(RequiredString(coverage, "kind"), "coverage.kind", documentKind);
        var reference = new MapSpatialReference
        {
            GridWidth = RequiredInt(spatial, "gridWidth"),
            GridHeight = RequiredInt(spatial, "gridHeight"),
            UnitsPerCell = RequiredDouble(spatial, "unitsPerCell"),
            WorldModel = worldKind == WorldModelKind.Spherical
                ? WorldModelDescriptor.Spherical(planetRadius)
                : WorldModelDescriptor.Planar(),
            Coverage = MapCoverage.Create(
                coverageKind,
                new LongitudeInterval(longitudeStart, longitudeSpan),
                RequiredDouble(coverage, "southLatitude"),
                RequiredDouble(coverage, "northLatitude")),
            GridMapping = ParseEnum<GridMappingKind>(RequiredString(spatial, "gridMapping"), "gridMapping", documentKind),
            Topology = ParseEnum<GridTopologyKind>(RequiredString(spatial, "topology"), "topology", documentKind),
            CanonicalCoordinates = ParseEnum<CoordinateSpaceKind>(RequiredString(spatial, "canonicalCoordinates"), "canonicalCoordinates", documentKind),
            PreserveProjectedCellAspectRatio = spatial.Value<bool?>("preserveProjectedCellAspectRatio") ?? true,
            LegacyCompatibility = ParseEnum<LegacyCompatibilityProfile>(
                spatial.Value<string>("legacyCompatibility") ?? nameof(LegacyCompatibilityProfile.None),
                "legacyCompatibility",
                documentKind,
                LegacyCompatibilityProfile.None)
        };
        reference.Validate();
        return reference;
    }

    private static string RequiredString(JObject source, string property) => source.Value<string>(property)
        ?? throw new InvalidOperationException($"spatialReference property '{property}' is required.");

    private static double RequiredDouble(JObject source, string property) => source.Value<double?>(property)
        ?? throw new InvalidOperationException($"spatialReference property '{property}' is required.");

    private static int RequiredInt(JObject source, string property) => source.Value<int?>(property)
        ?? throw new InvalidOperationException($"spatialReference property '{property}' is required.");

    private static T ParseEnum<T>(string value, string property, string documentKind, T? fallback = null)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
            return result;
        if (fallback.HasValue)
            return fallback.Value;
        throw new InvalidOperationException($"{documentKind} spatialReference property '{property}' has unsupported value '{value}'.");
    }
}
