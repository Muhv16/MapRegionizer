using MapRegionizer.Domain;
using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer
{
    public class MapManager
    {
        private GeometryFactory factory;
        private MapOptions options;

        private int mapHeight;
        private int mapWidth;
        public List<Polygon>? ContinentShapePolygons { get; private set; }
        private List<LineString>? regionalBoundaries;

        public List<Continent> Continents { get; private set; } = [];

        public MapManager()
        {
            factory = new GeometryFactory();
            options = new MapOptions();
        }
        
        public void CreateMapFromImage(string fileName)
        {
            using var image = Image.Load<Rgba32>(fileName);
            mapHeight = image.Height;
            mapWidth = image.Width;
            var continentsCoords = ImageService.ParseMapContinents(image);

            MapBuilder mapBuilder = new MapBuilder(factory, options);
            mapBuilder.BuildMapFromCoords(continentsCoords);
            ContinentShapePolygons = mapBuilder.MapPolygons;
        }

        public void CreateRegions()
        {
            if (ContinentShapePolygons == null) return;

            Regionizer regionzer = new Regionizer(factory, options);
            BoundaryService distortioner = new BoundaryService(factory, options);

            foreach(var continentsShape in ContinentShapePolygons.Where(p => p.IsValid))
            {
                var regionizedContinent = regionzer.Regionize(continentsShape);
                regionizedContinent = distortioner.Distortion(regionizedContinent);
                regionalBoundaries = distortioner.RegionalBoundaries!;
                Continents.Add(new Continent(continentsShape, regionalBoundaries));
            }

        }

        public void SaveMapToPng(string outputFile)
        {
            if (ContinentShapePolygons == null) return;
            var mapBoundaries = Continents.SelectMany(c => c.ContinentBoundaries).ToList();
            ImageService.DrawMap(ContinentShapePolygons, mapBoundaries, mapWidth, mapHeight, outputFile);
        }
    }
}
