using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.Core.Spatial;
using MapRegionizer.Core.Tectonics;
using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Terrain;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationContext
{
    private int _nextRegionId = 1;
    private int _randomSeed;
    private Random _random;
    private readonly bool _climateBoundaryIsDefault;
    private readonly HashSet<MapDataKey> _availableData =
    [
        MapDataKeys.Mask,
        MapDataKeys.SpatialContext,
        MapDataKeys.WorldSeed,
        MapDataKeys.RequestedDomain,
        MapDataKeys.WorkingDomain,
        MapDataKeys.ClimateBoundaryContext,
        MapDataKeys.HydrologyBoundaryContext
    ];
    private readonly HashSet<MapDataKey> _dirtyData = [];

    public MapGenerationContext(MapMask mask, MapGenerationOptions options, GeometryFactory geometryFactory, int randomSeed)
        : this(
            mask,
            options,
            geometryFactory,
            randomSeed,
            new RequestedDomain(mask.Window),
            new WorkingDomain(mask.Window),
            WorldContextMode.Isolated,
            IsolatedClimateBoundaryContext.Instance,
            IsolatedHydrologyBoundaryContext.Instance,
            options.EffectiveSpatial,
            legacyCompatibilityRequest: true)
    {
    }

    /// <summary>Creates a generation context using the canonical world-context model.</summary>
    public MapGenerationContext(
        MapMask mask,
        MapGenerationOptions options,
        GeometryFactory geometryFactory,
        int randomSeed,
        RequestedDomain requestedDomain,
        WorkingDomain workingDomain,
        WorldContextMode WorldContextMode,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null,
        MapSpatialOptions? requestedSpatialOptions = null)
        : this(
            mask,
            options,
            geometryFactory,
            randomSeed,
            requestedDomain,
            workingDomain,
            WorldContextMode,
            climateBoundary,
            hydrologyBoundary,
            requestedSpatialOptions,
            legacyCompatibilityRequest: false)
    {
    }

    /// <summary>Compatibility constructor for the old regional-mode enum.</summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public MapGenerationContext(
        MapMask mask,
        MapGenerationOptions options,
        GeometryFactory geometryFactory,
        int randomSeed,
        RequestedDomain requestedDomain,
        WorkingDomain workingDomain,
        RegionalGenerationMode generationMode,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null,
        MapSpatialOptions? requestedSpatialOptions = null)
        : this(
            mask,
            options,
            geometryFactory,
            randomSeed,
            requestedDomain,
            workingDomain,
            generationMode.ToWorldContextMode(),
            climateBoundary,
            hydrologyBoundary,
            requestedSpatialOptions,
            legacyCompatibilityRequest: generationMode == RegionalGenerationMode.Legacy)
    {
    }

    internal MapGenerationContext(
        MapMask mask,
        MapGenerationOptions options,
        GeometryFactory geometryFactory,
        int randomSeed,
        RequestedDomain requestedDomain,
        WorkingDomain workingDomain,
        WorldContextMode worldContextMode,
        IClimateBoundaryContext? climateBoundary,
        IHydrologyBoundaryContext? hydrologyBoundary,
        MapSpatialOptions? requestedSpatialOptions,
        bool legacyCompatibilityRequest)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(geometryFactory);
        ArgumentNullException.ThrowIfNull(requestedDomain);
        ArgumentNullException.ThrowIfNull(workingDomain);
        if (!workingDomain.Contains(requestedDomain))
            throw new ArgumentException("Working domain must contain requested domain.", nameof(workingDomain));

        Mask = mask;
        Options = options;
        GeometryFactory = geometryFactory;
        _randomSeed = randomSeed;
        _random = new Random(randomSeed);
        RequestedDomain = requestedDomain;
        WorkingDomain = workingDomain;
        if (!Enum.IsDefined(worldContextMode))
            throw new ArgumentOutOfRangeException(nameof(worldContextMode));

        WorldContextMode = worldContextMode;
        IsLegacyCompatibilityRequest = legacyCompatibilityRequest;
        RequestedSpatialOptions = requestedSpatialOptions ?? options.EffectiveSpatial;
        if (climateBoundary is not null)
        {
            ClimateBoundary = climateBoundary;
            _climateBoundaryIsDefault = false;
        }
        else if (worldContextMode == WorldContextMode.Isolated)
        {
            ClimateBoundary = IsolatedClimateBoundaryContext.Instance;
            _climateBoundaryIsDefault = true;
        }
        else if (worldContextMode == WorldContextMode.Automatic)
        {
            ClimateBoundary = new AnalyticalClimateBoundaryContext(options.WorldSeed ?? options.Seed ?? randomSeed);
            _climateBoundaryIsDefault = true;
        }
        else
        {
            throw new ArgumentException("Custom world context requires an explicit climate boundary context.", nameof(climateBoundary));
        }

        if (hydrologyBoundary is not null)
        {
            HydrologyBoundary = hydrologyBoundary;
        }
        else if (worldContextMode == WorldContextMode.Isolated)
        {
            HydrologyBoundary = IsolatedHydrologyBoundaryContext.Instance;
        }
        else if (worldContextMode == WorldContextMode.Automatic)
        {
            HydrologyBoundary = new HydrologyBoundaryContext();
        }
        else
        {
            throw new ArgumentException("Custom world context requires an explicit hydrology boundary context.", nameof(hydrologyBoundary));
        }
        SpatialContext = MapSpatialContext.Create(mask.Width, mask.Height, options.EffectiveSpatial);
        Bounds = new MapBounds(SpatialContext.SpatialReference.WidthInMapUnits, SpatialContext.SpatialReference.HeightInMapUnits, SpatialContext.SpatialReference.UnitsPerCell);
    }

    public MapMask Mask { get; }
    /// <summary>The final crop requested by the caller.</summary>
    public RequestedDomain RequestedDomain { get; }
    /// <summary>The world-aligned raster on which all stages execute.</summary>
    public WorkingDomain WorkingDomain { get; }
    public WorldContextMode WorldContextMode { get; }
    public WorldContextMode ContextMode => WorldContextMode;
    internal bool IsLegacyCompatibilityRequest { get; }
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public RegionalGenerationMode GenerationMode => IsLegacyCompatibilityRequest
        ? RegionalGenerationMode.Legacy
        : WorldContextMode.ToRegionalGenerationMode();
    internal MapSpatialOptions RequestedSpatialOptions { get; private set; }
    public int WorldOriginX => WorkingDomain.X;
    public int WorldOriginY => WorkingDomain.Y;
    public int WorldSeed => Options.WorldSeed ?? Options.Seed ?? _randomSeed;
    public IClimateBoundaryContext ClimateBoundary { get; private set; }
    public IHydrologyBoundaryContext HydrologyBoundary { get; private set; }
    public IClimateBoundaryContext ClimateBoundaryContext => ClimateBoundary;
    public IHydrologyBoundaryContext HydrologyBoundaryContext => HydrologyBoundary;
    public TectonicWorldContext? TectonicWorldContext { get; set; }
    public ClimateWorldContext? ClimateWorldContext { get; set; }
    public MapGenerationOptions Options { get; private set; }
    public GeometryFactory GeometryFactory { get; }
    /// <summary>
    /// The legacy shared random stream. New generation stages should use
    /// <see cref="CreateStageRandom"/> with their stable stage id.
    /// </summary>
    public Random Random => _random;
    public MapBounds Bounds { get; private set; }
    /// <summary>Immutable spatial services shared by all generation stages.</summary>
    public MapSpatialContext SpatialContext { get; private set; }
    public MapSpatialReference SpatialReference => SpatialContext.SpatialReference;
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

    public GeneratedMap ToGeneratedMap() => GeneratedMapCropper.Create(this);

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
        // SpatialContext is an initial input, not a generated artifact.  A
        // spatial options update replaces the immutable context in-place and
        // dirties its consumers, but the input itself must remain available
        // because there is no producer stage for it.
        if (key == MapDataKeys.SpatialContext ||
            key == MapDataKeys.WorldSeed ||
            key == MapDataKeys.RequestedDomain ||
            key == MapDataKeys.WorkingDomain ||
            key == MapDataKeys.ClimateBoundaryContext ||
            key == MapDataKeys.HydrologyBoundaryContext)
            return;

        if (_availableData.Contains(key))
            _dirtyData.Add(key);
    }

    public void UpdateOptions(MapGenerationOptions options, MapSpatialOptions? requestedSpatialOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (Options.Seed != options.Seed)
        {
            _randomSeed = options.Seed ?? Random.Shared.Next();
            _random = new Random(_randomSeed);
        }

        Options = options;
        SpatialContext = MapSpatialContext.Create(Mask.Width, Mask.Height, options.EffectiveSpatial);
        RequestedSpatialOptions = requestedSpatialOptions ?? options.EffectiveSpatial;
        Bounds = new MapBounds(SpatialContext.SpatialReference.WidthInMapUnits, SpatialContext.SpatialReference.HeightInMapUnits, SpatialContext.SpatialReference.UnitsPerCell);
        // An explicitly supplied boundary adapter is part of the request's
        // immutable world context.  Options updates may rebuild the default
        // analytical adapter when its seed changes, but must never discard a
        // caller-provided coarse/custom boundary source.
        if (WorldContextMode == WorldContextMode.Automatic &&
            _climateBoundaryIsDefault &&
            ClimateBoundary is AnalyticalClimateBoundaryContext)
        {
            ClimateBoundary = new AnalyticalClimateBoundaryContext(options.WorldSeed ?? options.Seed ?? _randomSeed);
        }
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
        else if (key == MapDataKeys.TectonicWorldContext)
            TectonicWorldContext = null;
        else if (key == MapDataKeys.ClimateWorldContext)
            ClimateWorldContext = null;
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
