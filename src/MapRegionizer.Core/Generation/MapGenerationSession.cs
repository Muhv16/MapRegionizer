using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.Core.Spatial;
using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Tectonics;
using MapRegionizer.Core.Terrain;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationSession
{
    private readonly MapGenerationPipeline _pipeline;
    private readonly MapGenerationContext _context;
    private readonly MapGenerationRequest _request;

    private MapGenerationSession(MapGenerationContext context, MapGenerationPipeline pipeline, MapGenerationRequest request)
    {
        _context = context;
        _pipeline = pipeline;
        _request = request;
    }

    public static MapGenerationSession Create(MapMask mask, MapGenerationOptions? options = null, MapGenerationPipeline? pipeline = null, GeometryFactory? geometryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var request = MapGenerationRequest.Legacy(mask, options);
        return Create(request, pipeline, geometryFactory);
    }

    public static MapGenerationSession Create(MapGenerationRequest request, MapGenerationPipeline? pipeline = null, GeometryFactory? geometryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        geometryFactory ??= new GeometryFactory();
        pipeline ??= MapGenerationPipelineBuilder.CreateDefault().Build();
        request.ValidateFiniteHalo(pipeline);
        var mask = request.MaskSource?.GetMask(request.WorkingDomain.Window)
            ?? throw new InvalidOperationException("A mask source is required to materialize the working domain.");
        if (!mask.Window.Equals(request.WorkingDomain.Window))
            throw new ArgumentException($"Mask source returned {mask.Window}; expected {request.WorkingDomain.Window}.", nameof(request));

        var requestedSpatial = request.Options.EffectiveSpatial;
        var workingOptions = PrepareWorkingOptions(request, requestedSpatial);
        var randomSeed = workingOptions.Seed ?? (request.Mode == RegionalGenerationMode.Automatic
            ? request.WorldSeed
            : Random.Shared.Next());
        var context = new MapGenerationContext(
            mask,
            workingOptions,
            geometryFactory,
            randomSeed,
            request.RequestedDomain,
            request.WorkingDomain,
            request.Mode,
            request.ClimateBoundary,
            request.HydrologyBoundary,
            requestedSpatial);

        return new MapGenerationSession(context, pipeline, request);
    }

    public GeneratedMap CurrentMap => _context.ToGeneratedMap();
    public MapMask Mask => _context.Mask;
    public MapMask WorkingMask => _context.Mask;
    public RequestedDomain RequestedDomain => _context.RequestedDomain;
    public WorkingDomain WorkingDomain => _context.WorkingDomain;
    public RegionalGenerationMode GenerationMode => _context.GenerationMode;
    public int WorldSeed => _context.WorldSeed;
    public IClimateBoundaryContext ClimateBoundary => _context.ClimateBoundary;
    public IHydrologyBoundaryContext HydrologyBoundary => _context.HydrologyBoundary;
    public IClimateBoundaryContext ClimateBoundaryContext => _context.ClimateBoundary;
    public IHydrologyBoundaryContext HydrologyBoundaryContext => _context.HydrologyBoundary;
    public TectonicWorldContext? TectonicWorldContext => _context.TectonicWorldContext;
    public ClimateWorldContext? ClimateWorldContext => _context.ClimateWorldContext;
    public MapGenerationOptions Options => _context.Options;
    public MapSpatialContext SpatialContext => _context.SpatialContext;
    public MapSpatialReference SpatialReference => _context.SpatialReference;
    public IReadOnlyList<Landmass> Landmasses => _context.Landmasses;
    public IReadOnlyList<WaterBody> WaterBodies => _context.WaterBodies;
    public WaterBodyTopology? WaterBodyTopology => _context.WaterBodyTopology;
    public IReadOnlyList<MapRegion> RawRegions => _context.RawRegions;
    public RegionDraft? RegionDraft => _context.RegionDraft;
    public bool UsesExternalRegionDraft => _context.ExternalRegionDraft is not null;
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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dirtyRoots);

        var spatialChanged = !Equals(_context.RequestedSpatialOptions, options.EffectiveSpatial);
        var worldSeedChanged = _context.Options.WorldSeed != options.WorldSeed || _context.Options.Seed != options.Seed;
        var workingOptions = PrepareWorkingOptions(options, _request, options.EffectiveSpatial);
        _context.UpdateOptions(workingOptions, options.EffectiveSpatial);
        var roots = dirtyRoots.ToHashSet();
        if (spatialChanged)
            roots.Add(MapDataKeys.SpatialContext);
        if (worldSeedChanged)
            roots.Add(MapDataKeys.WorldSeed);

        _pipeline.MarkDirty(_context, roots);
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

    private static MapGenerationOptions PrepareWorkingOptions(MapGenerationRequest request, MapSpatialOptions requestedSpatial) =>
        PrepareWorkingOptions(request.Options, request, requestedSpatial);

    private static MapGenerationOptions PrepareWorkingOptions(MapGenerationOptions options, MapGenerationRequest request, MapSpatialOptions requestedSpatial)
    {
        var spatial = requestedSpatial;
        if (request.Mode != RegionalGenerationMode.Legacy &&
            spatial.Coverage.Kind == MapCoverageKind.Regional &&
            spatial.Topology == GridTopologyKind.CylindricalX &&
            spatial.LegacyCompatibility == LegacyCompatibilityProfile.None)
        {
            spatial = spatial with { Topology = GridTopologyKind.OpenRectangular };
        }

        if (!request.RequestedDomain.Window.Equals(request.WorkingDomain.Window) && spatial.Coverage.Kind == MapCoverageKind.Regional)
            spatial = ExpandRegionalCoverage(spatial, request.RequestedDomain, request.WorkingDomain);

        return options.WithSpatial(spatial);
    }

    private static MapSpatialOptions ExpandRegionalCoverage(MapSpatialOptions spatial, RequestedDomain requested, WorkingDomain working)
    {
        var (offsetX, offsetY) = working.OffsetOf(requested);
        var coverage = spatial.Coverage;
        var lonCell = coverage.Longitude.SpanDegrees / requested.Width;
        var latCell = coverage.LatitudeSpan / requested.Height;
        var start = coverage.Longitude.StartLongitudeDegrees - offsetX * lonCell;
        var span = working.Width * lonCell;
        var north = coverage.NorthLatitude + offsetY * latCell;
        var south = north - working.Height * latCell;
        return spatial with
        {
            Coverage = MapCoverage.Regional(new LongitudeInterval(start, span), south, north)
        };
    }
}
