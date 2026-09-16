using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MapRegionizer.GeoJson;

/// <summary>
/// Writes the versioned, geometry-oriented portable map package. The package
/// always stores canonical <c>GridMapUnits</c> geometry plus the complete
/// canonical spatial reference, so consumers choose their own output
/// representation. It represents a finalized <see cref="GeneratedMap"/> and is
/// identical for mask-generated and manually authored maps.
/// </summary>
public static class MapPackageWriter
{
    public static string Write(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Bounds is null)
            throw new ArgumentException("The map does not contain bounds.", nameof(map));

        var reference = map.SpatialReference ?? GeoJsonMapWriter.CreateLegacyReference(map);
        reference.Validate();

        var landmasses = (map.Landmasses ?? throw new ArgumentException("The map does not contain a landmass list.", nameof(map)))
            .OrderBy(landmass => landmass.Id.Value)
            .Select(landmass => new MapPackageLandmass(landmass.Id, landmass.Shape))
            .ToArray();
        var regions = (map.Regions ?? throw new ArgumentException("The map does not contain a region list.", nameof(map)))
            .OrderBy(region => region.Id.Value)
            .Select(region => new MapPackageRegion(region.Id, region.LandmassId, region.Shape))
            .ToArray();
        ValidatePackage(landmasses, regions);

        var geometrySerializer = GeoJsonSerializer.Create();
        var document = new JObject
        {
            ["schema"] = MapPackageDocument.CurrentSchemaId,
            ["generator"] = WriteGenerator(),
            ["spatialReference"] = SpatialReferenceJsonFormat.Write(reference),
            ["bounds"] = new JObject
            {
                ["width"] = map.Bounds.Width,
                ["height"] = map.Bounds.Height,
                ["unitsPerCell"] = map.Bounds.UnitsPerCell
            },
            ["landmasses"] = new JArray(landmasses.Select(landmass => new JObject
            {
                ["id"] = landmass.Id.Value,
                ["shape"] = GeoJsonGeometryJson.ToGeoJson(landmass.Shape, geometrySerializer)
            })),
            ["regions"] = new JArray(regions.Select(region => new JObject
            {
                ["id"] = region.Id.Value,
                ["landmassId"] = region.LandmassId.Value,
                ["shape"] = GeoJsonGeometryJson.ToGeoJson(region.Shape, geometrySerializer)
            }))
        };
        return document.ToString(Formatting.None);
    }

    public static void WriteToFile(GeneratedMap map, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        File.WriteAllText(filePath, Write(map));
    }

    private static JObject WriteGenerator()
    {
        var generator = new JObject { ["name"] = MapPackageDocument.GeneratorName };
        var version = typeof(MapPackageWriter).Assembly.GetName().Version?.ToString();
        if (version is not null)
            generator["version"] = version;
        return generator;
    }

    /// <summary>
    /// Light structural validation of the package geometry contract. Heavy
    /// invariants such as coverage and overlap already hold by the region
    /// generation contract and are not rechecked here.
    /// </summary>
    internal static void ValidatePackage(
        IReadOnlyList<MapPackageLandmass> landmasses,
        IReadOnlyList<MapPackageRegion> regions)
    {
        var landmassIds = new HashSet<int>();
        foreach (var landmass in landmasses)
        {
            if (!landmassIds.Add(landmass.Id.Value))
                throw new InvalidOperationException($"Map package landmass id {landmass.Id.Value} is not unique.");
            ValidateShape(landmass.Shape, $"Landmass {landmass.Id.Value}");
        }

        var regionIds = new HashSet<int>();
        foreach (var region in regions)
        {
            if (!regionIds.Add(region.Id.Value))
                throw new InvalidOperationException($"Map package region id {region.Id.Value} is not unique.");
            if (!landmassIds.Contains(region.LandmassId.Value))
                throw new InvalidOperationException($"Map package region {region.Id.Value} references unknown landmass {region.LandmassId.Value}.");
            ValidateShape(region.Shape, $"Region {region.Id.Value}");
        }
    }

    private static void ValidateShape(Polygon? shape, string owner)
    {
        if (shape is null || shape.IsEmpty || shape.Area <= 0 || !shape.IsValid)
            throw new InvalidOperationException($"{owner} is not a valid non-empty polygon.");
        foreach (var coordinate in shape.Coordinates)
        {
            if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y))
                throw new InvalidOperationException($"{owner} contains non-finite coordinates.");
        }
    }
}

/// <summary>
/// Reads a portable map package into the geometry-only document model. A
/// package intentionally contains no generation state, so no
/// <see cref="GeneratedMap"/> is materialized; consumers adapt the document
/// to their own models.
/// </summary>
public static class MapPackageReader
{
    public static MapPackageDocument Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JObject.Parse(json);
        var schema = root.Value<string>("schema")
            ?? throw new InvalidOperationException("The map package document is missing its schema identifier.");
        if (!string.Equals(schema, MapPackageDocument.CurrentSchemaId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported map package schema '{schema}'. Expected '{MapPackageDocument.CurrentSchemaId}'.");

        var reference = SpatialReferenceJsonFormat.Read(root["spatialReference"], "Map package");
        var bounds = root["bounds"] as JObject ?? throw new InvalidOperationException("Map package bounds are required.");
        var geometrySerializer = GeoJsonSerializer.Create();
        var landmasses = (root["landmasses"] as JArray ?? throw new InvalidOperationException("Map package landmasses are required."))
            .Select(entry => ReadLandmass((JObject)entry, geometrySerializer))
            .ToArray();
        var regions = (root["regions"] as JArray ?? throw new InvalidOperationException("Map package regions are required."))
            .Select(entry => ReadRegion((JObject)entry, geometrySerializer))
            .ToArray();
        MapPackageWriter.ValidatePackage(landmasses, regions);

        var generator = root["generator"] as JObject;
        return new MapPackageDocument(
            schema,
            reference,
            new MapBounds(
                RequiredDouble(bounds, "width"),
                RequiredDouble(bounds, "height"),
                RequiredDouble(bounds, "unitsPerCell")),
            landmasses,
            regions,
            generator is null
                ? null
                : new MapPackageGeneratorInfo(
                    RequiredString(generator, "name"),
                    generator.Value<string>("version")));
    }

    public static MapPackageDocument ReadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Read(File.ReadAllText(filePath));
    }

    private static MapPackageLandmass ReadLandmass(JObject entry, JsonSerializer geometrySerializer) => new(
        new LandmassId(RequiredInt(entry, "id")),
        ReadPolygon(entry, geometrySerializer));

    private static MapPackageRegion ReadRegion(JObject entry, JsonSerializer geometrySerializer) => new(
        new RegionId(RequiredInt(entry, "id")),
        new LandmassId(RequiredInt(entry, "landmassId")),
        ReadPolygon(entry, geometrySerializer));

    private static Polygon ReadPolygon(JObject entry, JsonSerializer geometrySerializer) =>
        (Polygon)GeoJsonGeometryJson.FromGeoJson(
            entry["shape"] ?? throw new InvalidOperationException("Map package shape is required."),
            geometrySerializer);

    private static string RequiredString(JObject source, string property) => source.Value<string>(property)
        ?? throw new InvalidOperationException($"Map package property '{property}' is required.");

    private static int RequiredInt(JObject source, string property) => source.Value<int?>(property)
        ?? throw new InvalidOperationException($"Map package property '{property}' is required.");

    private static double RequiredDouble(JObject source, string property) => source.Value<double?>(property)
        ?? throw new InvalidOperationException($"Map package property '{property}' is required.");
}

/// <summary>
/// Geometry-only portable representation of a finalized MapRegionizer map.
/// Water is derived data (map bounds minus landmasses) and region adjacency is
/// recoverable from shared polygon edges, so neither is stored in v1.
/// </summary>
public sealed record MapPackageDocument(
    string SchemaId,
    MapSpatialReference SpatialReference,
    MapBounds Bounds,
    IReadOnlyList<MapPackageLandmass> Landmasses,
    IReadOnlyList<MapPackageRegion> Regions,
    MapPackageGeneratorInfo? Generator = null)
{
    public const string CurrentSchemaId = "MapRegionizer.MapPackage.v1";
    public const string GeneratorName = "MapRegionizer";
}

public sealed record MapPackageGeneratorInfo(string Name, string? Version);

public sealed record MapPackageLandmass(LandmassId Id, Polygon Shape);

public sealed record MapPackageRegion(RegionId Id, LandmassId LandmassId, Polygon Shape);

/// <summary>Shared GeoJSON geometry conversion for the serialization adapters.</summary>
internal static class GeoJsonGeometryJson
{
    public static JObject ToGeoJson(Geometry geometry, JsonSerializer serializer) =>
        JToken.FromObject(geometry, serializer) as JObject
            ?? throw new InvalidOperationException("Only single GeoJSON geometries can be serialized here.");

    public static Geometry FromGeoJson(JToken token, JsonSerializer serializer)
    {
        var geometry = token.ToObject<Geometry>(serializer)
            ?? throw new InvalidOperationException("The GeoJSON geometry is empty.");
        if (geometry is not Polygon polygon)
            throw new InvalidOperationException($"The map package requires a Polygon geometry, got {geometry.GeometryType}.");
        return polygon;
    }
}

