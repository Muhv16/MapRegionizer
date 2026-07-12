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

        return new JObject
        {
            ["type"] = "FeatureCollection",
            ["schemaVersion"] = document.SchemaVersion,
            ["projectionMode"] = document.ProjectionMode.ToString(),
            ["bounds"] = new JObject
            {
                ["width"] = document.Bounds.Width,
                ["height"] = document.Bounds.Height,
                ["pixelSize"] = document.Bounds.PixelSize
            },
            ["maskFingerprint"] = document.MaskFingerprint,
            ["landmassFingerprint"] = document.LandmassFingerprint,
            ["applyBoundaryDistortion"] = document.ApplyBoundaryDistortion,
            ["features"] = features
        }.ToString(Formatting.Indented);
    }

    public static void WriteToFile(RegionDraftDocument document, string filePath) => File.WriteAllText(filePath, Write(document));

    public static RegionDraftDocument Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JObject.Parse(json);
        if (!string.Equals(root.Value<string>("type"), "FeatureCollection", StringComparison.Ordinal))
            throw new InvalidOperationException("A region draft must be a GeoJSON FeatureCollection.");

        var bounds = root["bounds"] as JObject ?? throw new InvalidOperationException("Region draft bounds are required.");
        var geometrySerializer = GeoJsonSerializer.Create();
        var regions = (root["features"] as JArray ?? throw new InvalidOperationException("Region draft features are required."))
            .Select(feature => ReadRegion((JObject)feature, geometrySerializer)).ToList();

        return new RegionDraftDocument(
            RequiredString(root, "schemaVersion"),
            ParseEnum<MapProjectionMode>(RequiredString(root, "projectionMode"), "projectionMode"),
            new MapBounds(RequiredDouble(bounds, "width"), RequiredDouble(bounds, "height"), RequiredDouble(bounds, "pixelSize")),
            RequiredString(root, "maskFingerprint"),
            RequiredString(root, "landmassFingerprint"),
            root.Value<bool?>("applyBoundaryDistortion") ?? false,
            new RegionDraft(regions));
    }

    public static RegionDraftDocument ReadFromFile(string filePath) => Read(File.ReadAllText(filePath));

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
