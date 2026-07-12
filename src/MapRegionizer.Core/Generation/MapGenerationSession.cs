using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationSession
{
    private readonly MapGenerationPipeline _pipeline;
    private readonly MapGenerationContext _context;

    private MapGenerationSession(MapGenerationContext context, MapGenerationPipeline pipeline)
    {
        _context = context;
        _pipeline = pipeline;
    }

    public static MapGenerationSession Create(MapMask mask, MapGenerationOptions? options = null, MapGenerationPipeline? pipeline = null, GeometryFactory? geometryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        options ??= new MapGenerationOptions();
        options.Validate();

        geometryFactory ??= new GeometryFactory();
        pipeline ??= MapGenerationPipelineBuilder.CreateDefault().Build();
        var random = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var context = new MapGenerationContext(mask, options, geometryFactory, random);

        return new MapGenerationSession(context, pipeline);
    }

    public GeneratedMap CurrentMap => _context.ToGeneratedMap();
    public IReadOnlyList<Landmass> Landmasses => _context.Landmasses;
    public IReadOnlyList<WaterBody> WaterBodies => _context.WaterBodies;
    public WaterBodyTopology? WaterBodyTopology => _context.WaterBodyTopology;
    public IReadOnlyList<MapRegion> RawRegions => _context.RawRegions;
    public RegionDraft? RegionDraft => _context.RegionDraft;
    public IReadOnlyList<RegionDiagnostic> RegionDiagnostics => _context.RegionDiagnostics;
    public IReadOnlyList<MapRegion> Regions => _context.Regions;
    public TectonicHistory? TectonicHistory => _context.TectonicHistory;
    public CrustFieldMap? CrustFields => _context.CrustFields;
    public PlateDomainMap? PlateDomains => _context.PlateDomains;
    public TectonicBoundaryMap? TectonicBoundaries => _context.TectonicBoundaries;
    public OrogenProvinceMap? OrogenProvinces => _context.OrogenProvinces;
    public RiftProvinceMap? RiftProvinces => _context.RiftProvinces;
    public TectonicFeatureMap? TectonicFeatures => _context.TectonicFeatures;
    public ElevationMap? BaseTerrain => _context.BaseTerrain;
    public GeneratedLakeMap? GeneratedLakes => _context.GeneratedLakes;
    public ElevationMap? Elevation => _context.Elevation;
    public WaterSurfaceMap? WaterSurfaces => _context.WaterSurfaces;
    public HydrologyMap? Hydrology => _context.Hydrology;
    public ClimateMap? Climate => _context.Climate;
    public TectonicPlateMap? TectonicPlates => _context.TectonicPlates;
    public RegionRaster? RegionRaster => _context.RegionRaster;

    public bool IsAvailable(MapDataKey key) => _context.IsAvailable(key);
    public bool IsDirty(MapDataKey key) => _context.IsDirty(key);
    public bool Has(MapDataKey key) => _context.Has(key);

    public void RunFull() => _pipeline.RunFull(_context);

    public void RunUntil(MapDataKey target) => _pipeline.RunUntil(_context, target);

    public void Regenerate(MapDataKey target) => _pipeline.Regenerate(_context, target);

    public void UpdateOptions(MapGenerationOptions options, IEnumerable<MapDataKey> dirtyRoots)
    {
        _context.UpdateOptions(options);
        _pipeline.MarkDirty(_context, dirtyRoots);
    }

    /// <summary>
    /// Uses a user/imported draft as the single source of future raw regions.
    /// Only the region branch becomes dirty; terrain and climate remain available.
    /// Pass <see langword="null"/> to return to automatic region drafts.
    /// </summary>
    public void SetRegionDraft(RegionDraft? draft)
    {
        _context.SetExternalRegionDraft(draft);
        _pipeline.MarkDirty(_context, [MapDataKeys.RegionDraft]);
    }
}
