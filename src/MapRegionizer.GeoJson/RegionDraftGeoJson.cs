#pragma warning disable CS0618

using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MapRegionizer.GeoJson;

/// <summary>Reads and writes the versioned, editable GeoJSON region-draft format.</summary>
public static class RegionDraftGeoJson
{
    public static string Write(RegionDraftDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document = MigrateToCurrentSchema(document);
        var spatialReference = document.SpatialReference
            ?? throw new InvalidOperationException("Schema 2.0 region drafts require a canonical spatial reference.");
        spatialReference.Validate();
        var geometrySerializer = GeoJsonSerializer.Create();
        var features = new JArray(document.Draft.Regions.OrderBy(region => region.Id?.Value ?? int.MaxValue).Select(region =>
        {
            var properties = new JObject
            {
                ["regionId"] = region.Id?.Value,
                ["landmassId"] = region.LandmassId?.Value,
                ["origin"] = region.Origin.ToString()
            };
            if (region.Name is not null)
                properties["name"] = region.Name;
            if (region.Metadata is { Count: > 0 })
            {
                var metadata = new JObject();
                foreach (var pair in region.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    metadata[pair.Key] = pair.Value;
                properties["metadata"] = metadata;
            }

            return new JObject
            {
                ["type"] = "Feature",
                ["id"] = region.Id?.Value,
                ["properties"] = properties,
                ["geometry"] = JToken.FromObject(region.Shape, geometrySerializer)
            };
        }));

        var root = new JObject
        {
            ["type"] = "FeatureCollection",
            ["schemaVersion"] = document.SchemaVersion,
            // Kept as a read-only compatibility hint for older tools.  The
            // v2 identity is the descriptor below, never this legacy enum.
            ["projectionMode"] = document.ProjectionMode.ToString(),
            ["spatialReference"] = WriteSpatialReference(spatialReference),
            ["bounds"] = new JObject
            {
                ["width"] = document.Bounds.Width,
                ["height"] = document.Bounds.Height,
                ["pixelSize"] = document.Bounds.UnitsPerCell
            },
            ["maskFingerprint"] = document.MaskFingerprint,
            ["landmassFingerprint"] = document.LandmassFingerprint,
            ["applyBoundaryDistortion"] = document.ApplyBoundaryDistortion,
            ["features"] = features
        };
        return root.ToString(Formatting.Indented);
    }

    public static void WriteToFile(RegionDraftDocument document, string filePath) => File.WriteAllText(filePath, Write(document));

    public static RegionDraftDocument Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JObject.Parse(json);
        if (!string.Equals(root.Value<string>("type"), "FeatureCollection", StringComparison.Ordinal))
            throw new InvalidOperationException("A region draft must be a GeoJSON FeatureCollection.");

        var schemaVersion = RequiredString(root, "schemaVersion");
        var bounds = root["bounds"] as JObject ?? throw new InvalidOperationException("Region draft bounds are required.");
        var geometrySerializer = GeoJsonSerializer.Create();
        var regions = (root["features"] as JArray ?? throw new InvalidOperationException("Region draft features are required."))
            .Select(feature => ReadRegion((JObject)feature, geometrySerializer)).ToList();

        // v1 requires this historical enum. In v2 it is only a compatibility
        // hint, so descriptor-only documents remain valid.
        var projectionMode = root.Value<string>("projectionMode") is { Length: > 0 } projection
            ? ParseEnum<MapProjectionMode>(projection, "projectionMode")
            : MapProjectionMode.EquirectangularWorld;
        if (string.Equals(schemaVersion, "1.0", StringComparison.Ordinal) &&
            root.Value<string>("projectionMode") is not { Length: > 0 })
        {
            throw new InvalidOperationException("Schema 1.0 region drafts require projectionMode.");
        }
        var mapBounds = new MapBounds(
            RequiredDouble(bounds, "width"),
            RequiredDouble(bounds, "height"),
            bounds.Value<double?>("pixelSize") ?? RequiredDouble(bounds, "unitsPerCell"));
        var document = new RegionDraftDocument(
            schemaVersion,
            projectionMode,
            mapBounds,
            RequiredString(root, "maskFingerprint"),
            RequiredString(root, "landmassFingerprint"),
            root.Value<bool?>("applyBoundaryDistortion") ?? false,
            new RegionDraft(regions));

        return schemaVersion switch
        {
            RegionDraftDocument.CurrentSchemaVersion => document with
            {
                SpatialReference = ReadSpatialReference(root["spatialReference"] as JObject
                    ?? throw new InvalidOperationException("Schema 2.0 region drafts require a spatialReference descriptor."))
            },
            "1.0" => document with
            {
                // v1 did not store a descriptor.  Materialize the exact
                // historical semantics so Flat/Regional are not silently
                // interpreted as the new open regional model.
                SpatialReference = BuildLegacyReference(projectionMode, mapBounds),
                IsMigratedFromV1 = true
            },
            _ => throw new InvalidOperationException($"Unsupported region draft schema version '{schemaVersion}'.")
        };
    }

    public static RegionDraftDocument ReadFromFile(string filePath) => Read(File.ReadAllText(filePath));

    /// <summary>
    /// Normalizes a legacy document to the current portable schema. The
    /// historical projection enum is converted through
    /// <see cref="LegacyCompatibilityProfile"/> semantics before writing; no
    /// output coordinate choice is introduced into the draft identity.
    /// </summary>
    public static RegionDraftDocument MigrateToCurrentSchema(RegionDraftDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.Equals(document.SchemaVersion, RegionDraftDocument.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            if (document.SpatialReference is null)
                throw new InvalidOperationException("Schema 2.0 region drafts require a canonical spatial reference.");
            document.SpatialReference.Validate();
            return document;
        }

        if (!string.Equals(document.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported region draft schema version '{document.SchemaVersion}'.");

        // A v1 descriptor, if attached by a caller, is not authoritative:
        // the historical enum is the source of truth for its edge semantics.
        var spatialReference = BuildLegacyReference(document.ProjectionMode, document.Bounds);
        spatialReference.Validate();
        return document with
        {
            SchemaVersion = RegionDraftDocument.CurrentSchemaVersion,
            SpatialReference = spatialReference,
            IsMigratedFromV1 = true
        };
    }

    private static RegionDraftRegion ReadRegion(JObject feature, JsonSerializer geometrySerializer)
    {
        if (!string.Equals(feature.Value<string>("type"), "Feature", StringComparison.Ordinal))
            throw new InvalidOperationException("Region draft contains a non-feature entry.");
        var properties = feature["properties"] as JObject ?? throw new InvalidOperationException("Region draft feature properties are required.");
        var geometry = feature["geometry"]?.ToObject<Geometry>(geometrySerializer)
            ?? throw new InvalidOperationException("Region draft feature geometry is required.");
        var metadata = (properties["metadata"] as JObject)?.Properties()
            .ToDictionary(property => property.Name, property => property.Value.Value<string>() ?? string.Empty, StringComparer.Ordinal);

        return new RegionDraftRegion(
            properties.Value<int?>("regionId") is { } regionId ? new RegionId(regionId) : null,
            properties.Value<int?>("landmassId") is { } landmassId ? new LandmassId(landmassId) : null,
            geometry,
            ParseEnum<RegionDraftOrigin>(properties.Value<string>("origin") ?? nameof(RegionDraftOrigin.Manual), "origin", RegionDraftOrigin.Manual),
            properties.Value<string>("name"),
            metadata);
    }

    private static string RequiredString(JObject source, string property) => source.Value<string>(property)
        ?? throw new InvalidOperationException($"Region draft property '{property}' is required.");

    private static double RequiredDouble(JObject source, string property) => source.Value<double?>(property)
        ?? throw new InvalidOperationException($"Region draft property '{property}' is required.");

    private static JObject WriteSpatialReference(MapSpatialReference reference) => SpatialReferenceJsonFormat.Write(reference);

    private static MapSpatialReference ReadSpatialReference(JObject source) =>
        SpatialReferenceJsonFormat.Read(source, "Region draft");

    private static MapSpatialReference BuildLegacyReference(MapProjectionMode projectionMode, MapBounds bounds)
    {
        if (!double.IsFinite(bounds.UnitsPerCell) || bounds.UnitsPerCell <= 0)
            throw new InvalidOperationException("Legacy region draft bounds contain an invalid pixel size.");
        var width = bounds.Width / bounds.UnitsPerCell;
        var height = bounds.Height / bounds.UnitsPerCell;
        if (width <= 0 || height <= 0 ||
            Math.Abs(width - Math.Round(width)) > 1e-9 || Math.Abs(height - Math.Round(height)) > 1e-9)
            throw new InvalidOperationException("Legacy region draft bounds do not describe an integral canonical grid.");

        var spatial = MapSpatialOptions.FromLegacy(projectionMode, bounds.UnitsPerCell);
        var reference = spatial.CreateReference((int)Math.Round(width), (int)Math.Round(height));
        reference.Validate();
        return reference;
    }

    private static int RequiredInt(JObject source, string property) => source.Value<int?>(property)
        ?? throw new InvalidOperationException($"Region draft property '{property}' is required.");

    private static T ParseEnum<T>(string value, string property, T? fallback = null)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
            return result;
        if (fallback.HasValue)
            return fallback.Value;
        throw new InvalidOperationException($"Region draft property '{property}' has unsupported value '{value}'.");
    }
}
