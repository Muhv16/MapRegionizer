using MapRegionizer.Domain;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace MapRegionizer;

internal class JsonSaver
{
    public void Save(Continent[] continents)
    {
        var serializer = GeoJsonSerializer.Create();
        string geoJson;

        using (var stringWriter = new StringWriter())
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            serializer.Serialize(jsonWriter, continents.SelectMany(c => c.Regions));
            geoJson = stringWriter.ToString();
        }
        File.WriteAllText("provinces.json", geoJson);

        using (var stringWriter = new StringWriter())
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            serializer.Serialize(jsonWriter, continents.Select(c => c.ContinentPolygon));
            geoJson = stringWriter.ToString();
        }
        File.WriteAllText("continents.json", geoJson);
    }
}
