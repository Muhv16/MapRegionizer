using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Terrain;
using MapRegionizer.Core.Climate;

namespace MapRegionizer.Core.Generation;

/// <summary>
/// Immutable request for a world-aligned generation window.  The old
/// MapMask-based entry point creates a legacy request automatically.
/// </summary>
public sealed record MapGenerationRequest
{
    /// <summary>
    /// Compatibility overload for callers using the shorter <see cref="GenerationMode"/>
    /// enum name.  The canonical request state remains <see cref="RegionalGenerationMode"/>.
    /// </summary>
    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options,
        GenerationMode Mode)
        : this(RequestedDomain, WorkingDomain, MaskSource, Options, (RegionalGenerationMode)Mode)
    {
    }

    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        WorkingDomain WorkingDomain,
        IMapMaskSource? MaskSource,
        MapGenerationOptions? Options = null,
        RegionalGenerationMode Mode = RegionalGenerationMode.Automatic,
        IClimateBoundaryContext? ClimateBoundary = null,
        IHydrologyBoundaryContext? HydrologyBoundary = null)
    {
        ArgumentNullException.ThrowIfNull(RequestedDomain);
        ArgumentNullException.ThrowIfNull(WorkingDomain);
        if (!WorkingDomain.Contains(RequestedDomain))
            throw new ArgumentException("Working domain must contain requested domain.", nameof(WorkingDomain));
        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode));

        var validatedOptions = Options ?? new MapGenerationOptions();
        if (Mode != RegionalGenerationMode.Legacy &&
            validatedOptions.EffectiveSpatial.Coverage.Kind == MapCoverageKind.Regional &&
            validatedOptions.EffectiveSpatial.Topology == GridTopologyKind.CylindricalX &&
            validatedOptions.EffectiveSpatial.LegacyCompatibility == LegacyCompatibilityProfile.None)
        {
            validatedOptions = validatedOptions.WithSpatial(validatedOptions.EffectiveSpatial with
            {
                Topology = GridTopologyKind.OpenRectangular
            });
        }
        validatedOptions.Validate();
        if (Mode == RegionalGenerationMode.Automatic && MaskSource is null)
            throw new ArgumentException("Automatic regional generation requires an IMapMaskSource for the working domain.", nameof(MaskSource));

        this.RequestedDomain = RequestedDomain;
        this.WorkingDomain = WorkingDomain;
        this.MaskSource = MaskSource;
        this.Options = validatedOptions;
        this.Mode = Mode;
        this.ClimateBoundary = ClimateBoundary;
        this.HydrologyBoundary = HydrologyBoundary;
    }

    public MapGenerationRequest(
        RequestedDomain RequestedDomain,
        IMapMaskSource MaskSource,
        MapGenerationOptions? Options = null,
        RegionalGenerationMode Mode = RegionalGenerationMode.Automatic,
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
    public RegionalGenerationMode Mode { get; }
    public RegionalGenerationMode RegionalMode => Mode;
    public GenerationMode GenerationMode => (GenerationMode)Mode;
    public IClimateBoundaryContext? ClimateBoundary { get; }
    public IHydrologyBoundaryContext? HydrologyBoundary { get; }
    public IClimateBoundaryContext? ClimateBoundaryContext => ClimateBoundary;
    public IHydrologyBoundaryContext? HydrologyBoundaryContext => HydrologyBoundary;
    public int WorldSeed => Options.WorldSeed ?? Options.Seed ?? 0;

    public void ValidateFiniteHalo(MapGenerationPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var required = StageBoundaryContracts.RequiredFiniteHalo(pipeline.Stages);
        if (WorkingDomain.HaloCells(RequestedDomain) < required)
            throw new ArgumentException($"Working domain provides {WorkingDomain.HaloCells(RequestedDomain)} finite halo cells, but the pipeline requires {required}.", nameof(pipeline));
    }

    public static MapGenerationRequest Legacy(MapMask mask, MapGenerationOptions? options = null) =>
        new(
            new RequestedDomain(mask.Window),
            new WorkingDomain(mask.Window),
            new SingleMaskSource(mask),
            options,
            RegionalGenerationMode.Legacy,
            IsolatedClimateBoundaryContext.Instance,
            IsolatedHydrologyBoundaryContext.Instance);

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
            RegionalGenerationMode.Isolated,
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
        new(requestedDomain, workingDomain, maskSource, options, RegionalGenerationMode.Automatic, climateBoundary, hydrologyBoundary);

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
