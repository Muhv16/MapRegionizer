using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace MapRegionizer.GeoJson;

public static class GeoJsonMapWriter
{
    public static string WriteRegions(GeneratedMap map) => Serialize(map.Regions.Select(r => r.Shape));

    public static string WriteLandmasses(GeneratedMap map) => Serialize(map.Landmasses.Select(l => l.Shape));

    public static string WriteWaterBodies(GeneratedMap map) => Serialize(map.WaterBodies.Select(w => w.Shape));

    public static void WriteRegionsToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteRegions(map));

    public static void WriteLandmassesToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteLandmasses(map));

    public static void WriteWaterBodiesToFile(GeneratedMap map, string filePath) => File.WriteAllText(filePath, WriteWaterBodies(map));

    private static string Serialize(IEnumerable<Geometry> geometries)
    {
        var serializer = GeoJsonSerializer.Create();
        using var stringWriter = new StringWriter();
        using var jsonWriter = new JsonTextWriter(stringWriter);
        serializer.Serialize(jsonWriter, geometries);
        return stringWriter.ToString();
    }
}
