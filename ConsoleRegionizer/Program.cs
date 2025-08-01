using MapRegionizer;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using System.Text.Json;
using SixLabors.ImageSharp;

namespace ConsoleRegionizer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            CultureInfo culture = new CultureInfo("en-US");
            culture.NumberFormat.NumberDecimalSeparator = ".";
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;

            MapManager mapManager = new MapManager();
            using var image = Image.Load<Rgba32>("source.png");
            Console.WriteLine("Parsing Image");
            mapManager.CreateMapFromImage(image);
            Console.WriteLine("Creating regions");
            mapManager.CreateRegions();
            mapManager.SaveMapToPng("beforeDistr.png");
            Console.WriteLine("Distort region boundaries");
            mapManager.Distort();
            mapManager.SaveMapToPng("result.png");

            var serializer = GeoJsonSerializer.Create();
            string geoJson;

            using (var stringWriter = new StringWriter())
            using (var jsonWriter = new JsonTextWriter(stringWriter))
            {
                serializer.Serialize(jsonWriter, mapManager.Continents.SelectMany(c => c.Regions));
                geoJson = stringWriter.ToString();
            }
            File.WriteAllText("provinces.json", geoJson);

            using (var stringWriter = new StringWriter())
            using (var jsonWriter = new JsonTextWriter(stringWriter))
            {
                serializer.Serialize(jsonWriter, mapManager.Continents.Select(c => c.ContinentPolygon));
                geoJson = stringWriter.ToString();
            }
            File.WriteAllText("continents.json", geoJson);

            Console.WriteLine("Complete");
        }
    }
}
