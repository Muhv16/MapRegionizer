#pragma warning disable CS0618

using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Terrain;

namespace MapRegionizer.Core.Generation;

/// <summary>
/// Immutable request for a world-aligned generation window. Coverage is part
/// of <see cref="MapSpatialOptions"/>; world context is an independent axis.
/// The old MapMask-based entry point creates an isolated request with legacy
/// spatial compatibility enabled.
/// </summary>
public sealed record MapGenerationRequest
{
    /// <summary>Creates a request using the canonical world-context model.</summary>
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options = null,
        WorldContextMode WorldContextMode = WorldContextMode.Automatic,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
        : this(
            RequestedDomain,
            WorkingDomain,
            MaskSource,
            Options,
            WorldContextMode,
            ClimateBoundary,
            HydrologyBoundary,
            legacyCompatibilityRequest: false)
    {
    }

    /// <summary>
    /// Compatibility constructor for callers using the old regional-mode
    /// enum. Legacy is translated to isolated context plus legacy spatial
    /// compatibility; all other values are translated at this boundary.
    /// </summary>
    [Obsolete("Use the WorldContextMode constructor. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options,
        RegionalGenerationMode Mode,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
        : this(
            RequestedDomain,
            WorkingDomain,
            MaskSource,
            PrepareCompatibilityOptions(Options),
            Mode.ToWorldContextMode(),
            ClimateBoundary,
            HydrologyBoundary,
            legacyCompatibilityRequest: Mode == RegionalGenerationMode.Legacy)
    {
    }

    /// <summary>Compatibility constructor for the former short enum alias.</summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy spatial compatibility.")]
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options,
        GenerationMode Mode,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
        : this(
            RequestedDomain,
            WorkingDomain,
            MaskSource,
            PrepareCompatibilityOptions(Options),
            Mode.ToWorldContextMode(),
            ClimateBoundary,
            HydrologyBoundary,
            legacyCompatibilityRequest: Mode == GenerationMode.Legacy)
    {
    }

    private MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options,
        WorldContextMode WorldContextMode,
        IClimateBoundaryContext? ClimateBoundary,
        IHydrologyBoundaryContext? HydrologyBoundary,
        bool legacyCompatibilityRequest)
    {
        ArgumentNullException.ThrowIfNull(RequestedDomain);
        ArgumentNullException.ThrowIfNull(WorkingDomain);
        if (!WorkingDomain.Contains(RequestedDomain))
            throw new ArgumentException("Working domain must contain requested domain.", nameof(WorkingDomain));
        if (!Enum.IsDefined(WorldContextMode))
            throw new ArgumentOutOfRangeException(nameof(WorldContextMode));

        var validatedOptions = Options ?? new MapGenerationOptions();
        var spatial = validatedOptions.EffectiveSpatial;
        if (spatial.Coverage.Kind == MapCoverageKind.Regional &&
            spatial.Topology == GridTopologyKind.CylindricalX &&
            spatial.LegacyCompatibility == LegacyCompatibilityProfile.None)
        {
            validatedOptions = validatedOptions.WithSpatial(spatial with
            {
                Topology = GridTopologyKind.OpenRectangular
            });
        }

        validatedOptions.Validate();
        if ((WorldContextMode is WorldContextMode.Automatic or WorldContextMode.Custom) && MaskSource is null)
            throw new ArgumentException("Automatic and custom world context require an IMapMaskSource for the working domain.", nameof(MaskSource));
        if (WorldContextMode == WorldContextMode.Custom && (ClimateBoundary is null || HydrologyBoundary is null))
        {
            throw new ArgumentException(
                "Custom world context requires both climate and hydrology boundary contexts.",
                nameof(ClimateBoundary));
        }

        this.RequestedDomain = RequestedDomain;
        this.WorkingDomain = WorkingDomain;
        this.MaskSource = MaskSource;
        this.Options = validatedOptions;
        this.WorldContextMode = WorldContextMode;
        this.IsLegacyCompatibilityRequest = legacyCompatibilityRequest;
        this.ClimateBoundary = ClimateBoundary;
        this.HydrologyBoundary = HydrologyBoundary;
    }

    /// <summary>Creates a request with a finite working-domain halo.</summary>
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        IMapMaskSource MaskSource,
        MapGenerationOptions? Options = null,
        WorldContextMode WorldContextMode = WorldContextMode.Automatic,
        int finiteHaloCells = 0,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
        : this(
            RequestedDomain,
            WorkingDomain.ForRequested(RequestedDomain, finiteHaloCells),
            MaskSource,
            Options,
            WorldContextMode,
            ClimateBoundary,
            HydrologyBoundary)
    {
    }

    /// <summary>Compatibility overload for the old regional-mode enum.</summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy spatial compatibility.")]
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        IMapMaskSource MaskSource,
        MapGenerationOptions? Options,
        RegionalGenerationMode Mode,
        int finiteHaloCells = 0,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
        : this(
            RequestedDomain,
            WorkingDomain.ForRequested(RequestedDomain, finiteHaloCells),
            MaskSource,
            Options,
            Mode,
            ClimateBoundary,
            HydrologyBoundary)
    {
    }

    public RequestedDomain RequestedDomain { get; }
    public WorkingDomain WorkingDomain { get; }
    public IMapMaskSource? MaskSource { get; }
    public MapGenerationOptions Options { get; }
    public WorldContextMode WorldContextMode { get; }
    public WorldContextMode ContextMode => WorldContextMode;
    internal bool IsLegacyCompatibilityRequest { get; }
    public IClimateBoundaryContext? ClimateBoundary { get; }
    public IHydrologyBoundaryContext? HydrologyBoundary { get; }
    public IClimateBoundaryContext? ClimateBoundaryContext => ClimateBoundary;
    public IHydrologyBoundaryContext? HydrologyBoundaryContext => HydrologyBoundary;
    public int WorldSeed => Options.WorldSeed ?? Options.Seed ?? 0;

    /// <summary>Obsolete compatibility projection of the canonical mode.</summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
    public RegionalGenerationMode Mode => IsLegacyCompatibilityRequest
        ? RegionalGenerationMode.Legacy
        : WorldContextMode.ToRegionalGenerationMode();

    [Obsolete("Use WorldContextMode.")]
    public RegionalGenerationMode RegionalMode => Mode;

    [Obsolete("Use WorldContextMode.")]
    public GenerationMode GenerationMode => IsLegacyCompatibilityRequest
        ? GenerationMode.Legacy
        : WorldContextMode.ToGenerationMode();

    public void ValidateFiniteHalo(MapGenerationPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var required = StageBoundaryContracts.RequiredFiniteHalo(pipeline.Stages);
        if (WorkingDomain.HaloCells(RequestedDomain) < required)
            throw new ArgumentException($"Working domain provides {WorkingDomain.HaloCells(RequestedDomain)} finite halo cells, but the pipeline requires {required}.", nameof(pipeline));
    }

    /// <summary>
    /// Legacy MapMask adapter. It uses isolated context and explicitly marks
    /// historical spatial semantics so old generated output remains stable.
    /// </summary>
    public static MapGenerationRequest Legacy(MapMask mask, MapGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return new MapGenerationRequest(
            new RequestedDomain(mask.Window),
            new WorkingDomain(mask.Window),
            new SingleMaskSource(mask),
            PrepareCompatibilityOptions(options),
            WorldContextMode.Isolated,
            IsolatedClimateBoundaryContext.Instance,
            IsolatedHydrologyBoundaryContext.Instance,
            legacyCompatibilityRequest: true);
    }

    public static MapGenerationRequest Isolated(
        RequestedDomain requestedDomain,
        MapMask mask,
        MapGenerationOptions? options = null,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null)
    {
        ArgumentNullException.ThrowIfNull(requestedDomain);
        ArgumentNullException.ThrowIfNull(mask);
        if (!mask.Window.Equals(requestedDomain.Window))
            throw new ArgumentException("An isolated mask must exactly cover the requested domain.", nameof(mask));

        return new MapGenerationRequest(
            requestedDomain,
            new WorkingDomain(requestedDomain.Window),
            new SingleMaskSource(mask),
            options,
            WorldContextMode.Isolated,
            climateBoundary ?? IsolatedClimateBoundaryContext.Instance,
            hydrologyBoundary ?? IsolatedHydrologyBoundaryContext.Instance);
    }

    public static MapGenerationRequest Automatic(
        RequestedDomain requestedDomain,
        WorkingDomain workingDomain,
        IMapMaskSource maskSource,
        MapGenerationOptions? options = null,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null) =>
        new(requestedDomain, workingDomain, maskSource, options, WorldContextMode.Automatic, climateBoundary, hydrologyBoundary);

    /// <summary>Creates a request with caller-owned world and boundary context.</summary>
    public static MapGenerationRequest Custom(
        RequestedDomain requestedDomain,
        WorkingDomain workingDomain,
        IMapMaskSource maskSource,
        IClimateBoundaryContext climateBoundary,
        IHydrologyBoundaryContext hydrologyBoundary,
        MapGenerationOptions? options = null) =>
        new(requestedDomain, workingDomain, maskSource, options, WorldContextMode.Custom, climateBoundary, hydrologyBoundary);

    private static MapGenerationOptions PrepareCompatibilityOptions(MapGenerationOptions? options)
    {
        // A null options argument means the caller is using the historical
        // defaults, which already carry the equirectangular compatibility
        // profile.  Preserve an explicitly supplied spatial descriptor as-is:
        // the obsolete mode constructor is only an adapter for the mode, not
        // permission to overwrite modern spatial choices.
        return options ?? new MapGenerationOptions();
    }

    private sealed class SingleMaskSource : IMapMaskSource
    {
        private readonly MapMask _mask;

        public SingleMaskSource(MapMask mask) => _mask = mask;

        public MapMask GetMask(GridWindow window)
        {
            if (!_mask.Window.Equals(window))
                throw new InvalidOperationException($"The supplied legacy mask covers {_mask.Window}, not {window}.");
            return _mask;
        }
    }
}
