using MapRegionizer;
using System.Globalization;

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
            mapManager.CreateMapFromImage("source.png");
            mapManager.CreateRegions();
            mapManager.SaveMapToPng("result.png");
        }
    }
}
