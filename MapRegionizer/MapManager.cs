using MapRegionizer.BoundaryServices;
using MapRegionizer.Domain;
using NetTopologySuite.Geometries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer
{
    public class MapManager
    {
        private GeometryFactory factory;
        public MapOptions Options { get; set; }

        private int mapHeight;
        private int mapWidth;
        public List<Polygon>? ContinentShapePolygons { get; private set; }

        public List<Continent> Continents { get; private set; } = [];

        public MapManager()
        {
            factory = new GeometryFactory();
            Options = new MapOptions();
        }

        public MapManager(MapOptions options)
        {
            factory = new GeometryFactory();
            Options = options;
        }

        public void CreateMapFromImage(Image<Rgba32> image)
        {
            mapHeight = image.Height;
            mapWidth = image.Width;

            var parser = new MapParser();
            var continentsCoords = parser.ParseMapContinents(image);

            MapBuilder mapBuilder = new MapBuilder(factory, Options);
            mapBuilder.BuildMapFromCoords(continentsCoords);
            ContinentShapePolygons = mapBuilder.MapPolygons;
        }

        public void CreateMapFromImage(string filePath)
        {
            using var image = Image.Load<Rgba32>(filePath);
            CreateMapFromImage(image);
        }

        public void CreateRegions()
        {
            if (ContinentShapePolygons == null) return;

            Regionizer regionzer = new Regionizer(factory, Options);

            Parallel.ForEach(ContinentShapePolygons.Where(p => p.IsValid), continentsShape =>
            {
                var continentRegions = regionzer.Regionize(continentsShape);

                Continents.Add(new Continent(continentsShape, continentRegions));
            });
        }

        public void Distort()
        {
            if (Continents.Count == 0) return;

            BorderFinder borderFinder = new BorderFinder(factory);
            BorderCurver boundaryService = new BorderCurver(factory, Options);
            PolygonUpdater polygonUpdater = new PolygonUpdater(factory);

            Parallel.For(0, Continents.Count, i =>
            {
                Continent continent = Continents[i];
                var internalBorders = borderFinder.FindSharedBorders(continent.Regions);
                var distortedBorders = boundaryService.Distortion(internalBorders, continent.ContinentPolygon);
                var newRegions = polygonUpdater.UpdatePolygons(continent.Regions, distortedBorders);
                Continents[i] = new Continent(continent.ContinentPolygon, newRegions);
            });
        }

        public void SaveMapToPng(string outputFile)
        {
            if (ContinentShapePolygons == null) return;
            BorderFinder borderFinder = new BorderFinder(factory);
            var mapBoundaries = borderFinder.FindSharedBorders(Continents.SelectMany(c => c.Regions).ToList());
            ImageService.DrawMap(ContinentShapePolygons, mapBoundaries, mapWidth, mapHeight, outputFile);
        }

        public void SaveMapToJson()
        {
            if (!Continents.Any()) return;
            var saver = new JsonSaver();
            saver.Save(Continents.ToArray());
        }
    }
}
