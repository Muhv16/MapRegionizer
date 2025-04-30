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

        public Polygon? MapShapePolygon { get; private set; }
        public List<Polygon>? MapPolygons { get; private set; }
        private List<LineString>? regionalBoundaries;

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
            MapShapePolygon = mapBuilder.ContinentPolygon;
        }

        public void CreateRegions()
        {
            if (MapShapePolygon == null) return;

            Regionizer regionzer = new Regionizer(factory, options);
            BoundaryDistortioner distortioner = new BoundaryDistortioner(factory, options);

            MapPolygons = regionzer.Regionize(MapShapePolygon);
            MapPolygons = distortioner.Distortion(MapPolygons);
            regionalBoundaries = distortioner.RegionalBoundaries!;
        }

        public void SaveMapToPng(string outputFile)
        {
            if (MapPolygons == null) return;

            ImageService.DrawMap(MapPolygons, regionalBoundaries!, mapWidth, mapHeight, outputFile);
        }
    }
}
