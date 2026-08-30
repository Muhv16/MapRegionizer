using System.Globalization;
using MapRegionizer.App.Services;
using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.Core.Terrain;
using ReactiveUI;

namespace MapRegionizer.App.ViewModels;

/// <summary>
/// User-facing spatial configuration.  This type intentionally contains no
/// projection mathematics: it only validates and assembles Core request
/// objects.  Coordinate transforms remain in Core output adapters.
/// </summary>
public sealed class SpatialConfigurationViewModel : ReactiveObject
{
    private readonly LocalizationService? _localization;
    private WorldModelKind _worldModel = WorldModelKind.Spherical;
    private MapCoverageKind _coverageKind = MapCoverageKind.Global;
    private GridMappingKind _gridMapping = GridMappingKind.Equirectangular;
    private GridTopologyKind _topology = GridTopologyKind.CylindricalX;
    private double _unitsPerCell = 1.0;
    private double _westLongitude = -180;
    private double _eastLongitude = 180;
    private double _southLatitude = -90;
    private double _northLatitude = 90;
    // The App exposes only the two supported user choices. Legacy and Custom
    // remain readable through the obsolete compatibility alias below, but are
    // deliberately normalized at this UI boundary.
    private WorldContextMode _worldContextMode = WorldContextMode.Isolated;
    private int _workingHaloCells;
    private int _requestedOriginX;
    private int _requestedOriginY;
    private OutputCoordinateSystem _outputCoordinates = OutputCoordinateSystem.GridMapUnits;
    private LatitudeOverflowPolicy _outputLatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject;
    private AntimeridianOutputPolicy _outputAntimeridianPolicy = AntimeridianOutputPolicy.Auto;

    public SpatialConfigurationViewModel(LocalizationService? localization = null)
    {
        _localization = localization;
    }

    // These filtered lists are kept for non-XAML consumers. The App UI binds
    // to the human-facing radio properties below, so unsupported values never
    // appear as selectable choices.
    public IReadOnlyList<WorldModelKind> WorldModels { get; } = Enum.GetValues<WorldModelKind>();
    public IReadOnlyList<MapCoverageKind> CoverageKinds { get; } = Enum.GetValues<MapCoverageKind>();
    public IReadOnlyList<GridMappingKind> GridMappings { get; } = Enum.GetValues<GridMappingKind>();
    public IReadOnlyList<GridTopologyKind> Topologies { get; } = [GridTopologyKind.OpenRectangular, GridTopologyKind.CylindricalX];
    public IReadOnlyList<WorldContextMode> WorldContextModes { get; } =
        [WorldContextMode.Isolated, WorldContextMode.Automatic];
    public IReadOnlyList<OutputCoordinateSystem> OutputCoordinateSystems { get; } =
        [OutputCoordinateSystem.GridMapUnits, OutputCoordinateSystem.GeographicLongitudeLatitude, OutputCoordinateSystem.WebMercator3857];
    public IReadOnlyList<LatitudeOverflowPolicy> OutputLatitudeOverflowPolicies { get; } = Enum.GetValues<LatitudeOverflowPolicy>();
    public IReadOnlyList<AntimeridianOutputPolicy> OutputAntimeridianPolicies { get; } = Enum.GetValues<AntimeridianOutputPolicy>();

    public WorldModelKind WorldModel
    {
        get => _worldModel;
        set
        {
            if (_worldModel == value)
                return;

            this.RaiseAndSetIfChanged(ref _worldModel, value);
            RaiseWorldModelChoicesChanged();
        }
    }

    public bool IsSpherical
    {
        get => WorldModel == WorldModelKind.Spherical;
        set
        {
            if (value)
                WorldModel = WorldModelKind.Spherical;
        }
    }

    public bool IsFlat
    {
        get => WorldModel == WorldModelKind.Planar;
        set
        {
            if (value)
                WorldModel = WorldModelKind.Planar;
        }
    }

    // Planar is the Core term; Flat is the human-facing term used in the App.
    public bool IsPlanar
    {
        get => IsFlat;
        set => IsFlat = value;
    }

    public MapCoverageKind CoverageKind
    {
        get => _coverageKind;
        set
        {
            if (_coverageKind == value)
                return;
            this.RaiseAndSetIfChanged(ref _coverageKind, value);
            if (value == MapCoverageKind.Regional && _topology == GridTopologyKind.CylindricalX)
                Topology = GridTopologyKind.OpenRectangular;
            RaiseCoverageChoicesChanged();
        }
    }

    public bool IsWholeWorld
    {
        get => CoverageKind == MapCoverageKind.Global;
        set
        {
            if (value)
                CoverageKind = MapCoverageKind.Global;
        }
    }

    public bool IsRegion
    {
        get => IsRegional;
        set
        {
            if (value)
                CoverageKind = MapCoverageKind.Regional;
        }
    }

    public GridMappingKind GridMapping
    {
        get => _gridMapping;
        set
        {
            if (_gridMapping == value)
                return;

            var previous = _gridMapping;
            if (value == GridMappingKind.WebMercator &&
                IsFullLatitudeCoverage(SouthLatitude, NorthLatitude))
            {
                SouthLatitude = -WebMercatorGridMapping.WebMercatorLatitudeLimit;
                NorthLatitude = WebMercatorGridMapping.WebMercatorLatitudeLimit;
            }
            else if (previous == GridMappingKind.WebMercator &&
                     value != GridMappingKind.WebMercator &&
                     IsMercatorLatitudeCoverage(SouthLatitude, NorthLatitude))
            {
                SouthLatitude = -90;
                NorthLatitude = 90;
            }

            this.RaiseAndSetIfChanged(ref _gridMapping, value);
            RaiseMappingChoicesChanged();
        }
    }

    public bool IsEquirectangular
    {
        get => GridMapping == GridMappingKind.Equirectangular;
        set
        {
            if (value)
                GridMapping = GridMappingKind.Equirectangular;
        }
    }

    public bool IsWebMercator
    {
        get => GridMapping == GridMappingKind.WebMercator;
        set
        {
            if (value)
                GridMapping = GridMappingKind.WebMercator;
        }
    }

    public GridTopologyKind Topology
    {
        get => _topology;
        set
        {
            if (_topology == value)
                return;

            this.RaiseAndSetIfChanged(ref _topology, value);
            RaiseTopologyChoicesChanged();
        }
    }

    public bool IsOpenRectangular
    {
        get => Topology == GridTopologyKind.OpenRectangular;
        set
        {
            if (value)
                Topology = GridTopologyKind.OpenRectangular;
        }
    }

    public bool IsCylindrical
    {
        get => Topology == GridTopologyKind.CylindricalX;
        set
        {
            if (value)
                Topology = GridTopologyKind.CylindricalX;
        }
    }

    public bool WrapHorizontalEdges
    {
        get => IsCylindrical;
        set
        {
            if (value)
                IsCylindrical = true;
            else
                IsOpenRectangular = true;
        }
    }

    public bool IsHorizontalWrapping
    {
        get => WrapHorizontalEdges;
        set => WrapHorizontalEdges = value;
    }

    public double UnitsPerCell { get => _unitsPerCell; set => this.RaiseAndSetIfChanged(ref _unitsPerCell, value); }
    public double WestLongitude { get => _westLongitude; set => this.RaiseAndSetIfChanged(ref _westLongitude, value); }
    public double EastLongitude { get => _eastLongitude; set => this.RaiseAndSetIfChanged(ref _eastLongitude, value); }
    public double SouthLatitude { get => _southLatitude; set => this.RaiseAndSetIfChanged(ref _southLatitude, value); }
    public double NorthLatitude { get => _northLatitude; set => this.RaiseAndSetIfChanged(ref _northLatitude, value); }
    public WorldContextMode WorldContextMode
    {
        get => _worldContextMode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            var normalized = value == WorldContextMode.Custom
                ? WorldContextMode.Isolated
                : value;
            if (_worldContextMode == normalized)
                return;
            this.RaiseAndSetIfChanged(ref _worldContextMode, normalized);
            RaiseWorldContextChoicesChanged();
        }
    }

    public WorldContextMode WorldContextSelection { get => WorldContextMode; set => WorldContextMode = value; }

    public bool IsIsolated
    {
        get => WorldContextMode == WorldContextMode.Isolated;
        set
        {
            if (value)
                WorldContextMode = WorldContextMode.Isolated;
        }
    }

    public bool IsAutomatic
    {
        get => WorldContextMode == WorldContextMode.Automatic;
        set
        {
            if (value)
                WorldContextMode = WorldContextMode.Automatic;
        }
    }

    public int WorkingHaloCells { get => _workingHaloCells; set => this.RaiseAndSetIfChanged(ref _workingHaloCells, value); }
    public int RequestedOriginX { get => _requestedOriginX; set => this.RaiseAndSetIfChanged(ref _requestedOriginX, value); }
    public int RequestedOriginY { get => _requestedOriginY; set => this.RaiseAndSetIfChanged(ref _requestedOriginY, value); }
    public int RegionOriginX { get => RequestedOriginX; set => RequestedOriginX = value; }
    public int RegionOriginY { get => RequestedOriginY; set => RequestedOriginY = value; }
    public OutputCoordinateSystem OutputCoordinates
    {
        get => _outputCoordinates;
        set
        {
            if (_outputCoordinates == value)
                return;

            this.RaiseAndSetIfChanged(ref _outputCoordinates, value);
            this.RaisePropertyChanged(nameof(IsGridMapUnitsOutput));
            this.RaisePropertyChanged(nameof(IsGeographicOutput));
            this.RaisePropertyChanged(nameof(IsWebMercatorOutput));
            this.RaisePropertyChanged(nameof(ShowOutputLatitudeOverflow));
            this.RaisePropertyChanged(nameof(ShowOutputAntimeridian));
        }
    }

    public bool IsGridMapUnitsOutput
    {
        get => OutputCoordinates == OutputCoordinateSystem.GridMapUnits;
        set
        {
            if (value)
                OutputCoordinates = OutputCoordinateSystem.GridMapUnits;
        }
    }

    public bool IsGeographicOutput
    {
        get => OutputCoordinates == OutputCoordinateSystem.GeographicLongitudeLatitude;
        set
        {
            if (value)
                OutputCoordinates = OutputCoordinateSystem.GeographicLongitudeLatitude;
        }
    }

    public bool IsWebMercatorOutput
    {
        get => OutputCoordinates == OutputCoordinateSystem.WebMercator3857;
        set
        {
            if (value)
                OutputCoordinates = OutputCoordinateSystem.WebMercator3857;
        }
    }
    public bool ShowOutputLatitudeOverflow => IsWebMercatorOutput;
    public bool ShowOutputAntimeridian => IsGeographicOutput || IsWebMercatorOutput;

    public bool OutputLatitudeReject
    {
        get => OutputLatitudeOverflowPolicy == LatitudeOverflowPolicy.Reject;
        set
        {
            if (value)
                OutputLatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject;
        }
    }

    public bool OutputLatitudeClip
    {
        get => OutputLatitudeOverflowPolicy == LatitudeOverflowPolicy.Clip;
        set
        {
            if (value)
                OutputLatitudeOverflowPolicy = LatitudeOverflowPolicy.Clip;
        }
    }

    public bool OutputAntimeridianAuto
    {
        get => OutputAntimeridianPolicy == AntimeridianOutputPolicy.Auto;
        set
        {
            if (value)
                OutputAntimeridianPolicy = AntimeridianOutputPolicy.Auto;
        }
    }

    public bool OutputAntimeridianUnwrap
    {
        get => OutputAntimeridianPolicy == AntimeridianOutputPolicy.Unwrap;
        set
        {
            if (value)
                OutputAntimeridianPolicy = AntimeridianOutputPolicy.Unwrap;
        }
    }

    public bool OutputAntimeridianSplit
    {
        get => OutputAntimeridianPolicy == AntimeridianOutputPolicy.Split;
        set
        {
            if (value)
                OutputAntimeridianPolicy = AntimeridianOutputPolicy.Split;
        }
    }

    public LatitudeOverflowPolicy OutputLatitudeOverflowPolicy
    {
        get => _outputLatitudeOverflowPolicy;
        set
        {
            if (_outputLatitudeOverflowPolicy == value)
                return;
            this.RaiseAndSetIfChanged(ref _outputLatitudeOverflowPolicy, value);
            this.RaisePropertyChanged(nameof(OutputLatitudeReject));
            this.RaisePropertyChanged(nameof(OutputLatitudeClip));
        }
    }

    public AntimeridianOutputPolicy OutputAntimeridianPolicy
    {
        get => _outputAntimeridianPolicy;
        set
        {
            if (_outputAntimeridianPolicy == value)
                return;
            this.RaiseAndSetIfChanged(ref _outputAntimeridianPolicy, value);
            this.RaisePropertyChanged(nameof(OutputAntimeridianAuto));
            this.RaisePropertyChanged(nameof(OutputAntimeridianUnwrap));
            this.RaisePropertyChanged(nameof(OutputAntimeridianSplit));
        }
    }

    // Clear names for UI bindings and tests that use the geographic wording.
    public double RegionWest { get => WestLongitude; set => WestLongitude = value; }
    public double RegionEast { get => EastLongitude; set => EastLongitude = value; }
    public double RegionSouth { get => SouthLatitude; set => SouthLatitude = value; }
    public double RegionNorth { get => NorthLatitude; set => NorthLatitude = value; }
    public bool IsRegional => CoverageKind == MapCoverageKind.Regional;
    public bool RequiresMaskSource => IsAutomatic;
    public bool ShowWorldContext => true;

    public bool ShowWorldMask => IsAutomatic;
    public bool ShowRequestedOrigin => IsAutomatic;
    public bool ShowWorkingHalo => IsAutomatic;
    public bool ShowRegionBounds => IsRegional;
    public bool ShowHorizontalWrapping => !IsRegional;

    public string WebMercatorLatitudeValidationMessage => Format(
        "ValidationWebMercatorLatitude",
        $"Web Mercator latitude must be between ±{WebMercatorGridMapping.WebMercatorLatitudeLimit.ToString("0.###############", CultureInfo.CurrentCulture)}°.",
        WebMercatorGridMapping.WebMercatorLatitudeLimit.ToString("0.###############", CultureInfo.CurrentCulture));

    public MapSpatialOptions BuildSpatialOptions()
    {
        if (WorkingHaloCells < 0)
            throw new ArgumentOutOfRangeException(nameof(WorkingHaloCells), "Working halo cannot be negative.");
        if (GridMapping == GridMappingKind.WebMercator &&
            (!double.IsFinite(SouthLatitude) || !double.IsFinite(NorthLatitude) ||
             SouthLatitude < -WebMercatorGridMapping.WebMercatorLatitudeLimit ||
             NorthLatitude > WebMercatorGridMapping.WebMercatorLatitudeLimit))
        {
            // Validate in the App boundary first so users see a localized
            // explanation instead of a Core implementation exception.
            throw new ArgumentException(WebMercatorLatitudeValidationMessage, nameof(SouthLatitude));
        }

        var coverage = CoverageKind == MapCoverageKind.Global
            ? MapCoverage.Global(SouthLatitude, NorthLatitude)
            : MapCoverage.Regional(WestLongitude, EastLongitude, SouthLatitude, NorthLatitude);
        var topology = CoverageKind == MapCoverageKind.Regional && Topology == GridTopologyKind.CylindricalX
            ? GridTopologyKind.OpenRectangular
            : Topology;
        var result = new MapSpatialOptions
        {
            WorldModel = WorldModel == WorldModelKind.Spherical
                ? WorldModelDescriptor.Spherical()
                : WorldModelDescriptor.Planar(),
            Coverage = coverage,
            GridMapping = GridMapping,
            Topology = topology,
            UnitsPerCell = UnitsPerCell,
            // The App treats the selected raster as the source of truth for
            // dimensions. Core keeps its strict aspect-ratio default for
            // callers that need metric projected cells.
            PreserveProjectedCellAspectRatio = GridMapping != GridMappingKind.WebMercator,
            LegacyCompatibility = LegacyCompatibilityProfile.None
        };
        result.Validate();
        return result;
    }

    public MapOutputOptions BuildOutputOptions() => new()
    {
        CoordinateSystem = OutputCoordinates,
        LatitudeOverflowPolicy = OutputLatitudeOverflowPolicy,
        AntimeridianPolicy = OutputAntimeridianPolicy
    };

    public void LoadOutputOptions(MapOutputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        OutputCoordinates = options.CoordinateSystem;
        OutputLatitudeOverflowPolicy = options.LatitudeOverflowPolicy;
        OutputAntimeridianPolicy = options.AntimeridianPolicy;
    }

    /// <summary>
    /// Builds the request that the App passes to Core.  A file-backed mask is
    /// a valid world/mask source for surrounding-world mode when it covers the working
    /// window. Custom mode is intentionally strict and needs caller-owned
    /// boundary providers. The App does not manufacture an exact-window
    /// fallback: surrounding-world mode must be given an explicit world/mask source.
    /// </summary>
    public MapGenerationRequest BuildRequest(
        MapMask mask,
        MapGenerationOptions options,
        IMapMaskSource? worldMaskSource = null,
        IClimateBoundaryContext? climateBoundary = null,
        IHydrologyBoundaryContext? hydrologyBoundary = null,
        RequestedDomain? requestedDomain = null,
        WorkingDomain? workingDomain = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);
        var spatialOptions = BuildSpatialOptions();
        var configuredOptions = options.WithSpatial(spatialOptions);
        var requested = requestedDomain ?? new RequestedDomain(mask.Window);
        if (!mask.Window.Equals(requested.Window))
            throw new ArgumentException("The selected mask must exactly cover the requested domain.", nameof(mask));

        var working = workingDomain ?? WorkingDomain.ForRequested(requested, WorkingHaloCells);
        if (!working.Contains(requested))
            throw new ArgumentException("Working domain must contain the requested domain.", nameof(workingDomain));

        return WorldContextMode switch
        {
            WorldContextMode.Isolated => MapGenerationRequest.Isolated(requested, mask, configuredOptions),
            WorldContextMode.Automatic when worldMaskSource is not null => MapGenerationRequest.Automatic(
                requested,
                working,
                worldMaskSource,
                configuredOptions,
                climateBoundary,
                hydrologyBoundary),
            WorldContextMode.Automatic => throw new InvalidOperationException(Format(
                "ValidationWorldMask",
                "Surrounding-world mode requires an explicit world-context mask that covers the working domain.")),
            _ => throw new ArgumentOutOfRangeException(nameof(mask), "Unknown world context mode.")
        };
    }

    /// <summary>Loads persisted spatial controls without performing any projection work.</summary>
    public void LoadSpatialOptions(MapSpatialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        WorldModel = options.WorldModel.Kind;
        CoverageKind = options.Coverage.Kind;
        UnitsPerCell = options.UnitsPerCell;
        WestLongitude = options.Coverage.West;
        EastLongitude = options.Coverage.East;
        SouthLatitude = options.Coverage.South;
        NorthLatitude = options.Coverage.North;
        GridMapping = options.GridMapping;
        Topology = options.Coverage.Kind == MapCoverageKind.Regional && options.Topology == GridTopologyKind.CylindricalX
            ? GridTopologyKind.OpenRectangular
            : options.Topology;
    }

    private static bool IsFullLatitudeCoverage(double south, double north) =>
        NearlyEqual(south, -90) && NearlyEqual(north, 90);

    private static bool IsMercatorLatitudeCoverage(double south, double north) =>
        NearlyEqual(south, -WebMercatorGridMapping.WebMercatorLatitudeLimit) &&
        NearlyEqual(north, WebMercatorGridMapping.WebMercatorLatitudeLimit);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 1e-10;

    private string Format(string key, string fallback, params object[] arguments)
    {
        var template = _localization?[key];
        if (string.IsNullOrWhiteSpace(template) || string.Equals(template, key, StringComparison.OrdinalIgnoreCase))
            template = fallback;

        return arguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }

    private void RaiseWorldModelChoicesChanged()
    {
        this.RaisePropertyChanged(nameof(IsSpherical));
        this.RaisePropertyChanged(nameof(IsFlat));
        this.RaisePropertyChanged(nameof(IsPlanar));
    }

    private void RaiseCoverageChoicesChanged()
    {
        this.RaisePropertyChanged(nameof(IsRegional));
        this.RaisePropertyChanged(nameof(IsWholeWorld));
        this.RaisePropertyChanged(nameof(IsRegion));
        this.RaisePropertyChanged(nameof(ShowRegionBounds));
        this.RaisePropertyChanged(nameof(ShowHorizontalWrapping));
        this.RaisePropertyChanged(nameof(ShowWorldContext));
    }

    private void RaiseMappingChoicesChanged()
    {
        this.RaisePropertyChanged(nameof(IsEquirectangular));
        this.RaisePropertyChanged(nameof(IsWebMercator));
    }

    private void RaiseTopologyChoicesChanged()
    {
        this.RaisePropertyChanged(nameof(IsOpenRectangular));
        this.RaisePropertyChanged(nameof(IsCylindrical));
        this.RaisePropertyChanged(nameof(WrapHorizontalEdges));
        this.RaisePropertyChanged(nameof(IsHorizontalWrapping));
    }

    private void RaiseWorldContextChoicesChanged()
    {
        this.RaisePropertyChanged(nameof(IsIsolated));
        this.RaisePropertyChanged(nameof(IsAutomatic));
        this.RaisePropertyChanged(nameof(WorldContextSelection));
        this.RaisePropertyChanged(nameof(RequiresMaskSource));
        this.RaisePropertyChanged(nameof(ShowWorldMask));
        this.RaisePropertyChanged(nameof(ShowRequestedOrigin));
        this.RaisePropertyChanged(nameof(ShowWorkingHalo));
    }

}
