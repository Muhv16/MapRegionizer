using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationContext
{
    private int _nextRegionId = 1;
    private int _randomSeed;
    private Random _random;
    private readonly HashSet<MapDataKey> _availableData = [MapDataKeys.Mask];
    private readonly HashSet<MapDataKey> _dirtyData = [];

    public MapGenerationContext(MapMask mask, MapGenerationOptions options, GeometryFactory geometryFactory, int randomSeed)
    {
        Mask = mask;
        Options = options;
        GeometryFactory = geometryFactory;
        _randomSeed = randomSeed;
        _random = new Random(randomSeed);
        Bounds = new MapBounds(mask.Width * options.PixelSize, mask.Height * options.PixelSize, options.PixelSize);
    }

    public MapMask Mask { get; }
    public MapGenerationOptions Options { get; private set; }
    public GeometryFactory GeometryFactory { get; }
    /// <summary>
    /// The legacy shared random stream. New generation stages should use
    /// <see cref="CreateStageRandom"/> with their stable stage id.
    /// </summary>
    public Random Random => _random;
    public MapBounds Bounds { get; private set; }
    public List<Landmass> Landmasses { get; } = [];
    public List<WaterBody> WaterBodies { get; } = [];
    public WaterBodyTopology? WaterBodyTopology { get; set; }
    public List<MapRegion> RawRegions { get; } = [];
    public RegionDraft? RegionDraft { get; set; }
    public RegionDraft? ExternalRegionDraft { get; private set; }
    public IReadOnlyList<RegionDiagnostic> RegionDiagnostics { get; set; } = [];
    public List<MapRegion> Regions { get; } = [];
    public TectonicHistory? TectonicHistory { get; set; }
    public CrustFieldMap? CrustFields { get; set; }
    public PlateDomainMap? PlateDomains { get; set; }
    public TectonicBoundaryMap? TectonicBoundaries { get; set; }
    public OrogenProvinceMap? OrogenProvinces { get; set; }
    public RiftProvinceMap? RiftProvinces { get; set; }
    public TectonicFeatureMap? TectonicFeatures { get; set; }
    public ElevationMap? BaseTerrain { get; set; }
    public GeneratedLakeMap? GeneratedLakes { get; set; }
    public ElevationMap? Elevation { get; set; }
    public WaterSurfaceMap? WaterSurfaces { get; set; }
    public HydrologyMap? Hydrology { get; set; }
    public ClimateMap? Climate { get; set; }
    public TectonicPlateMap? TectonicPlates { get; set; }
    public RegionRaster? RegionRaster { get; set; }
    public IReadOnlySet<MapDataKey> AvailableData => _availableData;
    public IReadOnlySet<MapDataKey> DirtyData => _dirtyData;

    public RegionId CreateRegionId() => new(_nextRegionId++);

    /// <summary>
    /// Creates an independent deterministic random stream for one generation stage.
    /// This keeps a stage's output stable when the pipeline is run incrementally.
    /// </summary>
    public Random CreateStageRandom(string stageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        return new Random(CombineSeed(_randomSeed, stageId));
    }

    public void SetExternalRegionDraft(RegionDraft? draft) => ExternalRegionDraft = draft;

    public GeneratedMap ToGeneratedMap() => new(Bounds, Landmasses, WaterBodies, Regions, TectonicPlates, Elevation, WaterBodyTopology, WaterSurfaces, Hydrology, Climate, RegionRaster);

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

    public void UpdateOptions(MapGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (Options.Seed != options.Seed)
        {
            _randomSeed = options.Seed ?? Random.Shared.Next();
            _random = new Random(_randomSeed);
        }

        Options = options;
        Bounds = new MapBounds(Mask.Width * options.PixelSize, Mask.Height * options.PixelSize, options.PixelSize);
    }

    private static int CombineSeed(int seed, string stageId)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)seed) * 16777619;

            foreach (var character in stageId)
                hash = (hash ^ character) * 16777619;

            return (int)hash;
        }
    }

    public void ClearData(MapDataKey key)
    {
        if (key == MapDataKeys.Landmasses)
            Landmasses.Clear();
        else if (key == MapDataKeys.WaterBodies)
            WaterBodies.Clear();
        else if (key == MapDataKeys.WaterBodyTopology)
            WaterBodyTopology = null;
        else if (key == MapDataKeys.RawRegions)
        {
            RawRegions.Clear();
        }
        else if (key == MapDataKeys.RegionDraft)
        {
            RegionDraft = null;
            RegionDiagnostics = [];
            _nextRegionId = 1;
        }
        else if (key == MapDataKeys.Regions)
            Regions.Clear();
        else if (key == MapDataKeys.RegionRaster)
            RegionRaster = null;
        else if (key == MapDataKeys.TectonicHistory)
            TectonicHistory = null;
        else if (key == MapDataKeys.CrustFields)
            CrustFields = null;
        else if (key == MapDataKeys.PlateDomains)
            PlateDomains = null;
        else if (key == MapDataKeys.TectonicBoundaries)
            TectonicBoundaries = null;
        else if (key == MapDataKeys.OrogenProvinces)
            OrogenProvinces = null;
        else if (key == MapDataKeys.RiftProvinces)
            RiftProvinces = null;
        else if (key == MapDataKeys.TectonicFeatures)
            TectonicFeatures = null;
        else if (key == MapDataKeys.BaseTerrain)
            BaseTerrain = null;
        else if (key == MapDataKeys.GeneratedLakes)
            GeneratedLakes = null;
        else if (key == MapDataKeys.Elevation)
            Elevation = null;
        else if (key == MapDataKeys.WaterSurfaces)
            WaterSurfaces = null;
        else if (key == MapDataKeys.Hydrology)
            Hydrology = null;
        else if (key == MapDataKeys.Climate)
            Climate = null;
        else if (key == MapDataKeys.TectonicPlates)
            TectonicPlates = null;
    }
}
