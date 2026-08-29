#pragma warning disable CS0618

using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MapRegionizer.GeoJson;

public static class GeoJsonMapWriter
{
    /// <summary>
    /// Legacy geometry-array export. Coordinates are explicitly canonical
    /// GridMapUnits; callers that need a CRS-labelled GeoJSON document must
    /// use the overload accepting <see cref="MapOutputOptions"/>.
    /// </summary>
    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static string WriteRegions(GeneratedMap map) => Serialize(map.Regions.Select(r => r.Shape));

    public static string WriteRegions(GeneratedMap map, MapOutputOptions options) =>
        SerializeWithSpatialReference(map, map.Regions.Select(r => r.Shape), options);

    /// <summary>Legacy canonical GridMapUnits geometry-array export.</summary>
    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static string WriteLandmasses(GeneratedMap map) => Serialize(map.Landmasses.Select(l => l.Shape));

    public static string WriteLandmasses(GeneratedMap map, MapOutputOptions options) =>
        SerializeWithSpatialReference(map, map.Landmasses.Select(l => l.Shape), options);

    /// <summary>Legacy canonical GridMapUnits geometry-array export.</summary>
    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static string WriteWaterBodies(GeneratedMap map) => Serialize(map.WaterBodies.Select(w => w.Shape));

    public static string WriteWaterBodies(GeneratedMap map, MapOutputOptions options) =>
        SerializeWithSpatialReference(map, map.WaterBodies.Select(w => w.Shape), options);

    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static void WriteRegionsToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteRegions(map));

    public static void WriteRegionsToFile(GeneratedMap map, string filePath, MapOutputOptions options) =>
        File.WriteAllText(filePath, WriteRegions(map, options));

    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static void WriteLandmassesToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteLandmasses(map));

    public static void WriteLandmassesToFile(GeneratedMap map, string filePath, MapOutputOptions options) =>
        File.WriteAllText(filePath, WriteLandmasses(map, options));

    [Obsolete("Use the overload accepting MapOutputOptions to select and label an output coordinate system.")]
    public static void WriteWaterBodiesToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteWaterBodies(map));

    public static void WriteWaterBodiesToFile(GeneratedMap map, string filePath, MapOutputOptions options) =>
        File.WriteAllText(filePath, WriteWaterBodies(map, options));

    private static string SerializeWithSpatialReference(
        GeneratedMap map,
        IEnumerable<Geometry> geometries,
        MapOutputOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var reference = map.SpatialReference ?? CreateLegacyReference(map);
        var transformer = new MapCoordinateTransformer(reference, options);
        var features = geometries.Select(geometry =>
        {
            var transformed = transformer.Transform(geometry);
            return new JObject
            {
                ["type"] = "Feature",
                ["properties"] = new JObject(),
                ["geometry"] = JToken.Parse(SerializeSingle(transformed))
            };
        }).ToArray();

        var document = new JObject
        {
            ["type"] = "FeatureCollection",
            ["features"] = new JArray(features),
            ["spatialReference"] = SpatialReferenceJson(reference, options)
        };
        return document.ToString(Formatting.None);
    }

    private static string SerializeSingle(Geometry geometry)
    {
        var serializer = GeoJsonSerializer.Create();
        using var stringWriter = new StringWriter();
        using var jsonWriter = new JsonTextWriter(stringWriter);
        serializer.Serialize(jsonWriter, geometry);
        return stringWriter.ToString();
    }

    private static JObject SpatialReferenceJson(MapSpatialReference reference, MapOutputOptions options) =>
        new()
        {
            ["gridWidth"] = reference.GridWidth,
            ["gridHeight"] = reference.GridHeight,
            ["unitsPerCell"] = reference.UnitsPerCell,
            ["worldModel"] = new JObject
            {
                ["kind"] = reference.WorldModel.Kind.ToString(),
                ["planetRadius"] = reference.WorldModel.PlanetRadius
            },
            ["coverage"] = new JObject
            {
                ["kind"] = reference.Coverage.Kind.ToString(),
                ["southLatitude"] = reference.Coverage.SouthLatitude,
                ["northLatitude"] = reference.Coverage.NorthLatitude,
                ["longitudeStart"] = reference.Longitude.StartLongitudeDegrees,
                ["longitudeSpan"] = reference.Longitude.SpanDegrees,
                ["longitudeEnd"] = reference.Longitude.EndLongitudeDegrees,
                ["crossesAntimeridian"] = reference.Longitude.CrossesAntimeridian
            },
            ["gridMapping"] = reference.GridMapping.ToString(),
            ["topology"] = reference.Topology.ToString(),
            ["canonicalCoordinates"] = reference.CanonicalCoordinates.ToString(),
            ["preserveProjectedCellAspectRatio"] = reference.PreserveProjectedCellAspectRatio,
            ["legacyCompatibility"] = reference.LegacyCompatibility.ToString(),
            ["outputCoordinates"] = options.CoordinateSystem.ToString(),
            ["projection"] = ProjectionName(options),
            ["latitudeOverflowPolicy"] = options.LatitudeOverflowPolicy.ToString(),
            ["antimeridianPolicy"] = options.AntimeridianPolicy.ToString(),
            ["effectiveAntimeridianPolicy"] = EffectiveAntimeridianPolicy(options).ToString(),
            ["adaptiveDensification"] = options.EnableAdaptiveDensification,
            ["projectionErrorTolerance"] = options.ProjectionErrorTolerance,
            ["maxDensificationDepth"] = options.MaxDensificationDepth,
            ["minDensificationSegmentLength"] = options.MinDensificationSegmentLength
        };

    private static string ProjectionName(MapOutputOptions options) =>
        options.CoordinateSystem is OutputCoordinateSystem.WebMercator or OutputCoordinateSystem.WebMercator3857
            ? nameof(OutputCoordinateSystem.WebMercator3857)
            : options.CoordinateSystem.ToString();

    private static AntimeridianOutputPolicy EffectiveAntimeridianPolicy(MapOutputOptions options) =>
        options.AntimeridianPolicy == AntimeridianOutputPolicy.Auto
            ? options.CoordinateSystem is OutputCoordinateSystem.WebMercator or OutputCoordinateSystem.WebMercator3857
                ? AntimeridianOutputPolicy.Split
                : AntimeridianOutputPolicy.Unwrap
            : options.AntimeridianPolicy;

    private static MapSpatialReference CreateLegacyReference(GeneratedMap map)
    {
        var width = map.Elevation?.Width ?? map.Climate?.Width ?? map.Hydrology?.Width ??
            (int)Math.Round(map.Bounds.Width / map.Bounds.UnitsPerCell);
        var height = map.Elevation?.Height ?? map.Climate?.Height ?? map.Hydrology?.Height ??
            (int)Math.Round(map.Bounds.Height / map.Bounds.UnitsPerCell);
        return new MapSpatialReference
        {
            GridWidth = Math.Max(1, width),
            GridHeight = Math.Max(1, height),
            UnitsPerCell = map.Bounds.UnitsPerCell,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Global(),
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.CylindricalX
        };
    }

    private static string Serialize(IEnumerable<Geometry> geometries)
    {
        var serializer = GeoJsonSerializer.Create();
        using var stringWriter = new StringWriter();
        using var jsonWriter = new JsonTextWriter(stringWriter);
        serializer.Serialize(jsonWriter, geometries);
        return stringWriter.ToString();
    }
}
