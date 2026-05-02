using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

internal sealed class MapGenerationContext
{
    private int _nextRegionId = 1;

    public MapGenerationContext(MapMask mask, MapGenerationOptions options, GeometryFactory geometryFactory, Random random)
    {
        Mask = mask;
        Options = options;
        GeometryFactory = geometryFactory;
        Random = random;
        Bounds = new MapBounds(mask.Width * options.PixelSize, mask.Height * options.PixelSize, options.PixelSize);
    }

    public MapMask Mask { get; }
    public MapGenerationOptions Options { get; }
    public GeometryFactory GeometryFactory { get; }
    public Random Random { get; }
    public MapBounds Bounds { get; }
    public List<Landmass> Landmasses { get; } = [];
    public List<WaterBody> WaterBodies { get; } = [];
    public List<MapRegion> Regions { get; } = [];

    public RegionId CreateRegionId() => new(_nextRegionId++);

    public GeneratedMap ToGeneratedMap() => new(Bounds, Landmasses, WaterBodies, Regions);
}
