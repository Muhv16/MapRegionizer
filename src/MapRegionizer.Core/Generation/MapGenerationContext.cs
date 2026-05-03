using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationContext
{
    private int _nextRegionId = 1;
    private readonly HashSet<MapDataKey> _availableData = [MapDataKeys.Mask];
    private readonly HashSet<MapDataKey> _dirtyData = [];

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
    public List<MapRegion> RawRegions { get; } = [];
    public List<MapRegion> Regions { get; } = [];
    public TectonicPlateMap? TectonicPlates { get; set; }
    public IReadOnlySet<MapDataKey> AvailableData => _availableData;
    public IReadOnlySet<MapDataKey> DirtyData => _dirtyData;

    public RegionId CreateRegionId() => new(_nextRegionId++);

    public GeneratedMap ToGeneratedMap() => new(Bounds, Landmasses, WaterBodies, Regions, TectonicPlates);

    public bool Has(MapDataKey key) => _availableData.Contains(key) && !_dirtyData.Contains(key);

    public bool IsAvailable(MapDataKey key) => _availableData.Contains(key);

    public bool IsDirty(MapDataKey key) => _dirtyData.Contains(key);

    public void MarkProduced(MapDataKey key)
    {
        _availableData.Add(key);
        _dirtyData.Remove(key);
    }

    public void MarkDirty(MapDataKey key)
    {
        if (_availableData.Contains(key))
            _dirtyData.Add(key);
    }

    public void ClearData(MapDataKey key)
    {
        if (key == MapDataKeys.Landmasses)
            Landmasses.Clear();
        else if (key == MapDataKeys.WaterBodies)
            WaterBodies.Clear();
        else if (key == MapDataKeys.RawRegions)
            RawRegions.Clear();
        else if (key == MapDataKeys.Regions)
            Regions.Clear();
        else if (key == MapDataKeys.TectonicPlates)
            TectonicPlates = null;
    }
}
