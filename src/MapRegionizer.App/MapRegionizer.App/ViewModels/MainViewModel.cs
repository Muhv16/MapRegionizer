namespace MapRegionizer.App.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using MapRegionizer.App.Services;
using MapRegionizer.App.Views;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using MapRegionizer.Runner;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

public sealed class MainViewModel : ReactiveObject
{
    private readonly LocalizationService _localization = new();
    private readonly UserSettingsService _settingsService = new();
    private readonly GenerationWorkspaceService _workspace = new();
    private readonly GenerationExecutionService _execution = new();
    private readonly MapPreviewService _preview = new();
    private readonly UserSettings _settings;
    private CancellationTokenSource? _generationCts;
    private bool _sessionResetRequired = true;
    private bool _suppressDirty;

    private string _maskPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusMessage = string.Empty;
    private string _validationMessage = string.Empty;
    private string _statistics = string.Empty;
    private string _artifactSummary = string.Empty;
    private string _previewLegend = string.Empty;
    private string _previewTitle = string.Empty;
    private bool _isGenerating;
    private bool _showOnboarding;
    private Bitmap? _currentPreview;
    private PreviewLayerViewModel? _selectedPreviewLayer;
    private SettingsSectionViewModel? _selectedSettingsSection;
    private BottomInspectorTab _selectedInspectorTab;
    private string _selectedLanguage = "ru-RU";
    private string _selectedTheme = "System";

    private double _pixelSize = 1;
    private int? _seed;
    private string _seedText = string.Empty;
    private MapProjectionMode _projectionMode = MapProjectionMode.EquirectangularWorld;
    private double _simplifyTolerance = 1;
    private uint _targetArea = 400;
    private double _pointsMultiplier = 4;
    private double _minAreaRatio = 0.75;
    private double _maxAreaRatio = 1.75;
    private bool _boundaryDistortionEnabled = true;
    private double _boundaryDetail = 0.25;
    private double _maxOffset = 3.25;
    private double _minLineLengthToCurve = 7;
    private int? _plateCount;
    private int? _hotspotCount;
    private double _continentalSeedRatio = 0.4;
    private double _boundaryNoise = 0.2;
    private double _boundaryNoiseScale = 8.0;
    private double _landWaterTransitionPenalty = 0.2;
    private double _activity = 1.0;
    private double _earthLikeFactor = 0.8;
    private double _historyDepth = 0.8;
    private double _microplateRatio = 0.18;
    private double _minMicroplateAreaRatio = 0.0005;
    private double _maxMicroplateAreaRatio = 0.008;
    private int _minBoundarySegmentLength = 16;
    private double _activeMarginRatio = 0.45;
    private double _tectonicShelfWidthFactor = 1.0;
    private double _riftChance = 0.35;
    private bool _validateGeometry = true;
    private int _maxValidationCycles = 3;
    private int _minPlateSize = 100;
    private double _minPlateSizeRatio = 0.001;
    private double _reliefScale = 1.0;
    private double _mountaininess = 0.8;
    private double _erosion = 0.35;
    private double _roughness = 0.45;
    private double _seaDepthScale = 1.0;
    private double _elevationShelfWidthFactor = 1.0;
    private double _volcanismInfluence = 0.6;
    private double _smallIslandReliefFactor = 0.55;
    private bool _generateSmallLakes = false;
    private double _smallLakeCountMultiplier = 0.3;
    private double _smallLakeScatterMultiplier = 1.0;
    private double _smallLakeSizeMultiplier = 0.5;
    private double _riftInfluence = 0.5;
    private bool _preserveMaskCoastline = true;
    private double _maxElevationMeters = 8500;
    private double _minOceanDepthMeters = -7000;
    private double _minLandElevationMeters = 1;
    private double _maxSeaElevationMeters = -1;
    private double _riverDensity = 1;
    private double _majorRiverCountMultiplier = 1.5;
    private double _longRiverCountMultiplier = 1.3;
    private double _tributaryDensity = 1.0;
    private double _majorRiverTributaryMultiplier = 1.0;
    private double _lakeOutletInflowForceMultiplier = 0.45;
    private double _endorheicBasinChance = 0.22;
    private int _maxEndorheicBasins = 3;
    private double _deltaFrequency = 0.8;
    private double _meanderStrength = 0.65;
    private double _lakeOutletStrictness = 0.35;
    private bool _preserveRiverCoastline = true;
    private bool _allowRiverCarving;
    private double _oceanSeaMinAreaRatio = 0.12;
    private double _inlandSeaMinAreaRatio = 0.015;
    private int _oceanSeaNearOceanMaxDistanceCells = 13;
    private double _climatePolarLatitudeMargin = 0.05;
    private double _climateEquatorTemperatureCelsius = 28.0;
    private double _climatePoleCoolingCelsius = 55.0;
    private double _climateLatitudeCurveExponent = 1.35;
    private double _climateLapseRateCelsiusPerMeter = 0.0045;
    private double _climateBaseSeasonalityCelsius = 6.0;
    private double _climateLatitudeSeasonalityCelsius = 18.0;
    private double _climateContinentalSeasonalityCelsius = 13.0;
    private double _climateContinentalSummerBoostCelsius = 5.0;
    private double _climateContinentalWinterPenaltyCelsius = 8.0;
    private int _climateContinentalityDistanceCells = 96;
    private int _climateLargeLakeMinCellCount = 220;
    private double _climateOceanEvaporation = 1.30;
    private double _climateLakeEvaporation = 0.68;
    private double _climateLandEvapotranspiration = 0.08;
    private double _climateMoistureRetention = 0.86;
    private double _climateBaseRainfallEfficiency = 0.22;
    private double _climateOrographicStrength = 0.84;
    private double _climateDescentDrying = 0.27;
    private double _climateContinentalDrying = 0.17;
    private double _climateRiverMoistureBonus = 0.26;
    private double _climateRiverAgricultureBonus = 0.54;
    private double _climateMonsoonRainStrength = 0.38;
    private double _climateDrySeasonStrength = 0.22;
    private int _climateMonsoonOceanDistanceCells = 30;
    private int _climateMonsoonCoastProbeCells = 10;
    private double _climateSnowMeltThresholdCelsius = 2.0;
    private double _climateSnowPrecipitationScale = 0.42;
    private bool _preserveOceanCoastline = true;
    private bool _preserveInlandWaterMask = true;
    private bool _allowLakeExpansion;
    private bool _allowLakeDrainage;
    private double _lakeSurfacePercentile = 0.05;
    private double _minLakeSurfaceMarginMeters = 0.5;
    private double _maxLakeSurfaceMarginMeters = 8.0;
    private double _minLakeDepthMeters = 1.0;
    private double _maxLakeDepthMeters = 80.0;
    private double _maxRiftLakeDepthMeters = 320.0;
    private double _maxInlandSeaDepthMeters = 220.0;
    private double _mountainLakeElevationMeters = 900.0;
    private double _plateauLakeElevationMeters = 1400.0;
    private double _mountainLakeReliefMeters = 260.0;
    private double _lakeTectonicFaultThreshold = 0.28;
    private double _lakeVolcanicInfluenceThreshold = 0.34;
    private double _plainLakeKarstChance = 0.12;
    private double _lakeDepthRandomnessMin = 0.8;
    private double _lakeDepthRandomnessMax = 1.2;
    private int _largeLakeDepressionMinCellCount = 900;
    private double _mountainRiverDensity = 0.58;
    private int _maxMountainSourcesPerCluster;
    private int _minMountainSourceSpacing;
    private TectonicPlateJsonExportMode _tectonicJsonMode = TectonicPlateJsonExportMode.Summary;
    private ElevationJsonExportMode _elevationJsonMode = ElevationJsonExportMode.Summary;
    private ClimateJsonExportMode _climateJsonMode = ClimateJsonExportMode.Summary;
    private double _exportScale = 1.0;
    private double _exportRegionBorderWidth = 2.0;
    private double _exportTectonicBoundaryWidth = 1.0;
    private bool _exportDrawCrustPlateBoundaries;
    private bool _exportDrawFeaturePlateBoundaries;
    private bool _exportDrawElevationHillshade = true;
    private bool _exportDrawElevationPlateBoundaries;
    private bool _exportDrawClimateHillshade = true;
    private bool _exportDrawClimateRivers = true;
    private bool _exportDrawClimateRiverValleyAccents = true;

    public MainViewModel()
    {
        _settings = _settingsService.Load();

        BrowseMaskCommand = ReactiveCommand.CreateFromTask(BrowseMaskAsync);
        BrowseOutputCommand = ReactiveCommand.CreateFromTask(BrowseOutputAsync);
        RunFullCommand = ReactiveCommand.CreateFromTask(RunFullAsync);
        RunRegionsOnlyCommand = ReactiveCommand.CreateFromTask(RunRegionsOnlyAsync);
        OpenRegionEditorCommand = ReactiveCommand.CreateFromTask(() => OpenRegionEditorAsync(frozenVisibleRegions: false));
        OpenVisibleRegionEditorCommand = ReactiveCommand.CreateFromTask(() => OpenRegionEditorAsync(frozenVisibleRegions: true));
        ResetAutomaticRegionsCommand = ReactiveCommand.CreateFromTask(ResetAutomaticRegionsAsync);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        ExportPreviewCommand = ReactiveCommand.CreateFromTask(ExportPreviewAsync);
        CancelCommand = ReactiveCommand.Create(CancelGeneration);
        RandomizeSeedCommand = ReactiveCommand.Create(RandomizeSeed);
        CopySeedCommand = ReactiveCommand.CreateFromTask(CopySeedAsync);
        DismissOnboardingCommand = ReactiveCommand.Create(DismissOnboarding);
        SelectSettingsSectionCommand = ReactiveCommand.Create<SettingsSectionViewModel>(section => SelectedSettingsSection = section);
        ApplyFastPresetCommand = ReactiveCommand.Create(() => ApplyPreset("fast"));
        ApplyBalancedPresetCommand = ReactiveCommand.Create(() => ApplyPreset("balanced"));
        ApplyDetailedPresetCommand = ReactiveCommand.Create(() => ApplyPreset("detailed"));
        ApplyDiagnosticPresetCommand = ReactiveCommand.Create(() => ApplyPreset("diagnostic"));

        InitializeStages();
        InitializePreviewLayers();
        InitializeSettingsSections();
        LoadSettings();

        _localization.LanguageChanged += (_, _) => RefreshLocalization();
        StatusMessage = string.IsNullOrWhiteSpace(MaskPath) ? L["StatusChooseMask"] : L["Ready"];
        ValidateAll();
    }

    public LocalizationService L => _localization;
    public IReadOnlyList<string> LanguageOptions => _localization.SupportedLanguages;
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    public IReadOnlyList<MapProjectionMode> ProjectionModes { get; } = Enum.GetValues<MapProjectionMode>();
    public IReadOnlyList<TectonicPlateJsonExportMode> TectonicJsonModes { get; } = Enum.GetValues<TectonicPlateJsonExportMode>();
    public IReadOnlyList<ElevationJsonExportMode> ElevationJsonModes { get; } = Enum.GetValues<ElevationJsonExportMode>();
    public IReadOnlyList<ClimateJsonExportMode> ClimateJsonModes { get; } = Enum.GetValues<ClimateJsonExportMode>();

    public ObservableCollection<GenerationStageViewModel> Stages { get; } = [];
    public ObservableCollection<GenerationStageViewModel> FutureStages { get; } = [];
    public ObservableCollection<PreviewLayerViewModel> PreviewLayers { get; } = [];
    public ObservableCollection<RunHistoryEntryViewModel> History { get; } = [];
    public ObservableCollection<SettingsSectionViewModel> SettingsSections { get; } = [];
    public ObservableCollection<GenerationLogEntryViewModel> GenerationLog { get; } = [];

    public IEnumerable<SettingsSectionViewModel> ProjectSettingsSections => SettingsSections.Where(s => s.Kind == SettingsSectionKind.Project).OrderBy(s => s.Order);
    public IEnumerable<SettingsSectionViewModel> MapSettingsSections => SettingsSections.Where(s => s.Kind == SettingsSectionKind.Map).OrderBy(s => s.Order);
    public IEnumerable<SettingsSectionViewModel> AdvancedSettingsSections => SettingsSections.Where(s => s.Kind == SettingsSectionKind.Advanced).OrderBy(s => s.Order);

    public ReactiveCommand<Unit, Unit> BrowseMaskCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> RunFullCommand { get; }
    public ReactiveCommand<Unit, Unit> RunRegionsOnlyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenRegionEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenVisibleRegionEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetAutomaticRegionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportPreviewCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> RandomizeSeedCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySeedCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissOnboardingCommand { get; }
    public ReactiveCommand<SettingsSectionViewModel, Unit> SelectSettingsSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyFastPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyBalancedPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyDetailedPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyDiagnosticPresetCommand { get; }

    public string MaskPath { get => _maskPath; set => SetMaskPath(value); }
    public string MaskFileName => string.IsNullOrWhiteSpace(MaskPath) ? string.Empty : Path.GetFileName(MaskPath);
    public string OutputDirectory { get => _outputDirectory; set => SetAndSave(ref _outputDirectory, value); }
    public string StatusMessage { get => _statusMessage; set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
    public string ValidationMessage { get => _validationMessage; set => this.RaiseAndSetIfChanged(ref _validationMessage, value); }
    public string Statistics { get => _statistics; set => this.RaiseAndSetIfChanged(ref _statistics, value); }
    public string ArtifactSummary
    {
        get => _artifactSummary;
        set
        {
            this.RaiseAndSetIfChanged(ref _artifactSummary, value);
            this.RaisePropertyChanged(nameof(HasArtifacts));
        }
    }
    public string PreviewLegend { get => _previewLegend; set => this.RaiseAndSetIfChanged(ref _previewLegend, value); }
    public string PreviewTitle { get => _previewTitle; set => this.RaiseAndSetIfChanged(ref _previewTitle, value); }
    public bool HasManualRegionDraft => _workspace.Session?.UsesExternalRegionDraft == true;
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public string AppBackground => IsLightTheme ? "#F4F6FA" : "#101114";
    public string PanelBackground => IsLightTheme ? "#FFFFFF" : "#17191F";
    public string CardBackground => IsLightTheme ? "#F9FAFC" : "#20232B";
    public string SoftBorder => IsLightTheme ? "#D9DEE8" : "#30343D";
    public string PrimaryText => IsLightTheme ? "#151821" : "#E8EAF0";
    public string SecondaryText => IsLightTheme ? "#5F6878" : "#A6ADBA";
    public string CanvasBackground => IsLightTheme ? "#EEF2F7" : "#16181D";
    public string CanvasFooterBackground => IsLightTheme ? "#E6EAF1" : "#D017191F";
    public string CanvasFooterText => IsLightTheme ? "#2C3442" : "#D1D5DB";
    public bool IsGenerating { get => _isGenerating; set => this.RaiseAndSetIfChanged(ref _isGenerating, value); }
    public bool ShowOnboarding { get => _showOnboarding; set => this.RaiseAndSetIfChanged(ref _showOnboarding, value); }
    public Bitmap? CurrentPreview
    {
        get => _currentPreview;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPreview, value);
            this.RaisePropertyChanged(nameof(HasPreview));
            RaiseSelectedLayerDetailsChanged();
        }
    }

    public bool HasPreview => CurrentPreview is not null;
    public bool HasHistory => History.Count > 0;
    public bool HasArtifacts => !string.IsNullOrWhiteSpace(ArtifactSummary);
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public bool HasGenerationLog => GenerationLog.Count > 0;
    public string SelectedLayerName => SelectedPreviewLayer?.Name ?? string.Empty;
    public string SelectedLayerStatusText => SelectedPreviewLayer?.IsAvailable == true ? L["LayerReady"] : L["LayerUnavailable"];
    public string SelectedLayerStatusBrush => SelectedPreviewLayer?.IsAvailable == true ? "#22C55E" : "#94A3B8";
    public string SelectedLayerDataKeyText => SelectedPreviewLayer?.RequiredKey.ToString() ?? string.Empty;
    public string CurrentPreviewSizeText => CurrentPreview is null ? L["NoPreview"] : $"{CurrentPreview.PixelSize.Width} x {CurrentPreview.PixelSize.Height}";

    public BottomInspectorTab SelectedInspectorTab
    {
        get => _selectedInspectorTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedInspectorTab, value);
            this.RaisePropertyChanged(nameof(SelectedInspectorIndex));
        }
    }

    public int SelectedInspectorIndex
    {
        get => (int)SelectedInspectorTab;
        set
        {
            if (value < 0 || value > (int)BottomInspectorTab.Thumbnails)
                return;

            SelectedInspectorTab = (BottomInspectorTab)value;
        }
    }

    public SettingsSectionViewModel? SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        set
        {
            if (ReferenceEquals(_selectedSettingsSection, value))
                return;

            if (_selectedSettingsSection is not null)
                _selectedSettingsSection.IsSelected = false;

            this.RaiseAndSetIfChanged(ref _selectedSettingsSection, value);

            if (_selectedSettingsSection is not null)
                _selectedSettingsSection.IsSelected = true;

            RaiseSelectedSectionChanged();
        }
    }

    public bool IsInputOutputSectionSelected => IsSelectedSection("InputOutput");
    public bool IsGenerationSectionSelected => IsSelectedSection("Generation");
    public bool IsQualityProfileSectionSelected => IsSelectedSection("QualityProfile");
    public bool IsMaskSectionSelected => IsSelectedSection("Mask");
    public bool IsReliefSectionSelected => IsSelectedSection("Relief");
    public bool IsWaterRiversSectionSelected => IsSelectedSection("WaterRivers");
    public bool IsClimateSectionSelected => IsSelectedSection("Climate");
    public bool IsBiomesSectionSelected => IsSelectedSection("Biomes");
    public bool IsRegionsSectionSelected => IsSelectedSection("Regions");
    public bool IsExportSectionSelected => IsSelectedSection("Export");
    public bool IsTectonicsSectionSelected => IsSelectedSection("Tectonics");
    public bool IsErosionSectionSelected => IsSelectedSection("Erosion");
    public bool IsDiagnosticsSectionSelected => IsSelectedSection("Diagnostics");

    public PreviewLayerViewModel? SelectedPreviewLayer
    {
        get => _selectedPreviewLayer;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPreviewLayer, value);
            RefreshPreview();
            RaiseSelectedLayerDetailsChanged();
            SaveSettings();
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            _localization.Language = value;
            SavePreferences();
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTheme, value);
            ApplyTheme(value);
            RaiseThemePaletteChanged();
            SavePreferences();
        }
    }

    public double PixelSize { get => _pixelSize; set => SetOptionAndReset(ref _pixelSize, value); }
    public int? Seed
    {
        get => _seed;
        set
        {
            SetOption(ref _seed, value, MapDataKeys.RegionDraft, MapDataKeys.TectonicHistory);
            this.RaiseAndSetIfChanged(ref _seedText, value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty, nameof(SeedText));
            this.RaisePropertyChanged(nameof(SeedText));
        }
    }
    public string SeedText
    {
        get => _seedText;
        set
        {
            this.RaiseAndSetIfChanged(ref _seedText, value ?? string.Empty);
            if (string.IsNullOrWhiteSpace(value))
            {
                Seed = null;
                return;
            }

            if (int.TryParse(value, out var parsed) && parsed > 0)
                Seed = parsed;
        }
    }
    public MapProjectionMode ProjectionMode { get => _projectionMode; set => SetOptionAndReset(ref _projectionMode, value); }
    public double SimplifyTolerance { get => _simplifyTolerance; set => SetOption(ref _simplifyTolerance, value, MapDataKeys.Landmasses); }
    public uint TargetArea
    {
        get => _targetArea;
        set
        {
            SetOptionNamed(ref _targetArea, value, nameof(TargetArea), MapDataKeys.RegionDraft);
            this.RaisePropertyChanged(nameof(TargetAreaSlider));
        }
    }
    public double TargetAreaSlider { get => TargetArea; set => TargetArea = (uint)Math.Max(1, Math.Round(value)); }
    public double PointsMultiplier { get => _pointsMultiplier; set => SetOptionNamed(ref _pointsMultiplier, value, nameof(PointsMultiplier), MapDataKeys.RegionDraft); }
    public double MinAreaRatio { get => _minAreaRatio; set => SetOptionNamed(ref _minAreaRatio, value, nameof(MinAreaRatio), MapDataKeys.RegionDraft); }
    public double MaxAreaRatio { get => _maxAreaRatio; set => SetOptionNamed(ref _maxAreaRatio, value, nameof(MaxAreaRatio), MapDataKeys.RegionDraft); }
    public bool BoundaryDistortionEnabled { get => _boundaryDistortionEnabled; set => SetOption(ref _boundaryDistortionEnabled, value, MapDataKeys.Regions); }
    public double BoundaryDetail { get => _boundaryDetail; set => SetOptionNamed(ref _boundaryDetail, value, nameof(BoundaryDetail), MapDataKeys.Regions); }
    public double MaxOffset { get => _maxOffset; set => SetOption(ref _maxOffset, value, MapDataKeys.Regions); }
    public double MinLineLengthToCurve { get => _minLineLengthToCurve; set => SetOption(ref _minLineLengthToCurve, value, MapDataKeys.Regions); }
    public int? PlateCount { get => _plateCount; set => SetOption(ref _plateCount, value, MapDataKeys.TectonicHistory); }
    public int? HotspotCount { get => _hotspotCount; set => SetOption(ref _hotspotCount, value, MapDataKeys.TectonicHistory); }
    public double ContinentalSeedRatio { get => _continentalSeedRatio; set => SetOption(ref _continentalSeedRatio, value, MapDataKeys.TectonicHistory); }
    public double BoundaryNoise { get => _boundaryNoise; set => SetOption(ref _boundaryNoise, value, MapDataKeys.TectonicHistory); }
    public double BoundaryNoiseScale { get => _boundaryNoiseScale; set => SetOption(ref _boundaryNoiseScale, value, MapDataKeys.TectonicHistory); }
    public double LandWaterTransitionPenalty { get => _landWaterTransitionPenalty; set => SetOption(ref _landWaterTransitionPenalty, value, MapDataKeys.TectonicHistory); }
    public double Activity { get => _activity; set => SetOption(ref _activity, value, MapDataKeys.TectonicHistory); }
    public double EarthLikeFactor { get => _earthLikeFactor; set => SetOption(ref _earthLikeFactor, value, MapDataKeys.TectonicHistory); }
    public double HistoryDepth { get => _historyDepth; set => SetOption(ref _historyDepth, value, MapDataKeys.TectonicHistory); }
    public double MicroplateRatio { get => _microplateRatio; set => SetOption(ref _microplateRatio, value, MapDataKeys.TectonicHistory); }
    public double MinMicroplateAreaRatio { get => _minMicroplateAreaRatio; set => SetOption(ref _minMicroplateAreaRatio, value, MapDataKeys.TectonicHistory); }
    public double MaxMicroplateAreaRatio { get => _maxMicroplateAreaRatio; set => SetOption(ref _maxMicroplateAreaRatio, value, MapDataKeys.TectonicHistory); }
    public int MinBoundarySegmentLength { get => _minBoundarySegmentLength; set => SetOption(ref _minBoundarySegmentLength, value, MapDataKeys.TectonicBoundaries); }
    public double ActiveMarginRatio { get => _activeMarginRatio; set => SetOption(ref _activeMarginRatio, value, MapDataKeys.TectonicHistory); }
    public double TectonicShelfWidthFactor { get => _tectonicShelfWidthFactor; set => SetOption(ref _tectonicShelfWidthFactor, value, MapDataKeys.TectonicHistory); }
    public double RiftChance { get => _riftChance; set => SetOption(ref _riftChance, value, MapDataKeys.TectonicHistory); }
    public bool ValidateGeometry { get => _validateGeometry; set => SetOption(ref _validateGeometry, value, MapDataKeys.PlateDomains); }
    public int MaxValidationCycles { get => _maxValidationCycles; set => SetOption(ref _maxValidationCycles, value, MapDataKeys.PlateDomains); }
    public int MinPlateSize { get => _minPlateSize; set => SetOption(ref _minPlateSize, value, MapDataKeys.PlateDomains); }
    public double MinPlateSizeRatio { get => _minPlateSizeRatio; set => SetOption(ref _minPlateSizeRatio, value, MapDataKeys.PlateDomains); }
    public double ReliefScale { get => _reliefScale; set => SetOption(ref _reliefScale, value, MapDataKeys.BaseTerrain); }
    public double Mountaininess { get => _mountaininess; set => SetOption(ref _mountaininess, value, MapDataKeys.BaseTerrain); }
    public double Erosion { get => _erosion; set => SetOption(ref _erosion, value, MapDataKeys.BaseTerrain); }
    public double Roughness { get => _roughness; set => SetOption(ref _roughness, value, MapDataKeys.BaseTerrain); }
    public double SeaDepthScale { get => _seaDepthScale; set => SetOption(ref _seaDepthScale, value, MapDataKeys.BaseTerrain); }
    public double ElevationShelfWidthFactor { get => _elevationShelfWidthFactor; set => SetOption(ref _elevationShelfWidthFactor, value, MapDataKeys.BaseTerrain); }
    public double VolcanismInfluence { get => _volcanismInfluence; set => SetOption(ref _volcanismInfluence, value, MapDataKeys.BaseTerrain); }
    public double SmallIslandReliefFactor { get => _smallIslandReliefFactor; set => SetOption(ref _smallIslandReliefFactor, value, MapDataKeys.BaseTerrain); }
    public bool GenerateSmallLakes { get => _generateSmallLakes; set => SetOption(ref _generateSmallLakes, value, MapDataKeys.GeneratedLakes); }
    public double SmallLakeCountMultiplier { get => _smallLakeCountMultiplier; set => SetOption(ref _smallLakeCountMultiplier, value, MapDataKeys.GeneratedLakes); }
    public double SmallLakeScatterMultiplier { get => _smallLakeScatterMultiplier; set => SetOption(ref _smallLakeScatterMultiplier, value, MapDataKeys.GeneratedLakes); }
    public double SmallLakeSizeMultiplier { get => _smallLakeSizeMultiplier; set => SetOption(ref _smallLakeSizeMultiplier, value, MapDataKeys.GeneratedLakes); }
    public double RiftInfluence { get => _riftInfluence; set => SetOption(ref _riftInfluence, value, MapDataKeys.BaseTerrain); }
    public bool PreserveMaskCoastline { get => _preserveMaskCoastline; set => SetOption(ref _preserveMaskCoastline, value, MapDataKeys.BaseTerrain); }
    public double MaxElevationMeters { get => _maxElevationMeters; set => SetOption(ref _maxElevationMeters, value, MapDataKeys.BaseTerrain); }
    public double MinOceanDepthMeters { get => _minOceanDepthMeters; set => SetOption(ref _minOceanDepthMeters, value, MapDataKeys.BaseTerrain); }
    public double MinLandElevationMeters { get => _minLandElevationMeters; set => SetOption(ref _minLandElevationMeters, value, MapDataKeys.BaseTerrain); }
    public double MaxSeaElevationMeters { get => _maxSeaElevationMeters; set => SetOption(ref _maxSeaElevationMeters, value, MapDataKeys.BaseTerrain); }
    public double RiverDensity { get => _riverDensity; set => SetOption(ref _riverDensity, value, MapDataKeys.Hydrology); }
    public double MajorRiverCountMultiplier { get => _majorRiverCountMultiplier; set => SetOption(ref _majorRiverCountMultiplier, value, MapDataKeys.Hydrology); }
    public double LongRiverCountMultiplier { get => _longRiverCountMultiplier; set => SetOption(ref _longRiverCountMultiplier, value, MapDataKeys.Hydrology); }
    public double TributaryDensity { get => _tributaryDensity; set => SetOption(ref _tributaryDensity, value, MapDataKeys.Hydrology); }
    public double MajorRiverTributaryMultiplier { get => _majorRiverTributaryMultiplier; set => SetOption(ref _majorRiverTributaryMultiplier, value, MapDataKeys.Hydrology); }
    public double LakeOutletInflowForceMultiplier { get => _lakeOutletInflowForceMultiplier; set => SetOption(ref _lakeOutletInflowForceMultiplier, value, MapDataKeys.Hydrology); }
    public double EndorheicBasinChance { get => _endorheicBasinChance; set => SetOption(ref _endorheicBasinChance, value, MapDataKeys.Hydrology); }
    public int MaxEndorheicBasins { get => _maxEndorheicBasins; set => SetOption(ref _maxEndorheicBasins, value, MapDataKeys.Hydrology); }
    public double DeltaFrequency { get => _deltaFrequency; set => SetOption(ref _deltaFrequency, value, MapDataKeys.Hydrology); }
    public double MeanderStrength { get => _meanderStrength; set => SetOption(ref _meanderStrength, value, MapDataKeys.Hydrology); }
    public double LakeOutletStrictness { get => _lakeOutletStrictness; set => SetOption(ref _lakeOutletStrictness, value, MapDataKeys.Hydrology); }
    public bool PreserveRiverCoastline { get => _preserveRiverCoastline; set => SetOption(ref _preserveRiverCoastline, value, MapDataKeys.Hydrology); }
    public bool AllowRiverCarving { get => _allowRiverCarving; set => SetOption(ref _allowRiverCarving, value, MapDataKeys.Hydrology); }
    public double OceanSeaMinAreaRatio { get => _oceanSeaMinAreaRatio; set => SetOptionNamed(ref _oceanSeaMinAreaRatio, value, nameof(OceanSeaMinAreaRatio), MapDataKeys.WaterBodies); }
    public double InlandSeaMinAreaRatio { get => _inlandSeaMinAreaRatio; set => SetOptionNamed(ref _inlandSeaMinAreaRatio, value, nameof(InlandSeaMinAreaRatio), MapDataKeys.WaterBodies); }
    public int OceanSeaNearOceanMaxDistanceCells { get => _oceanSeaNearOceanMaxDistanceCells; set => SetOption(ref _oceanSeaNearOceanMaxDistanceCells, value, MapDataKeys.WaterBodies); }
    public double ClimatePolarLatitudeMargin { get => _climatePolarLatitudeMargin; set => SetOption(ref _climatePolarLatitudeMargin, value, MapDataKeys.Climate); }
    public double ClimateEquatorTemperatureCelsius { get => _climateEquatorTemperatureCelsius; set => SetOption(ref _climateEquatorTemperatureCelsius, value, MapDataKeys.Climate); }
    public double ClimatePoleCoolingCelsius { get => _climatePoleCoolingCelsius; set => SetOption(ref _climatePoleCoolingCelsius, value, MapDataKeys.Climate); }
    public double ClimateLatitudeCurveExponent { get => _climateLatitudeCurveExponent; set => SetOption(ref _climateLatitudeCurveExponent, value, MapDataKeys.Climate); }
    public double ClimateLapseRateCelsiusPerMeter { get => _climateLapseRateCelsiusPerMeter; set => SetOption(ref _climateLapseRateCelsiusPerMeter, value, MapDataKeys.Climate); }
    public double ClimateBaseSeasonalityCelsius { get => _climateBaseSeasonalityCelsius; set => SetOption(ref _climateBaseSeasonalityCelsius, value, MapDataKeys.Climate); }
    public double ClimateLatitudeSeasonalityCelsius { get => _climateLatitudeSeasonalityCelsius; set => SetOption(ref _climateLatitudeSeasonalityCelsius, value, MapDataKeys.Climate); }
    public double ClimateContinentalSeasonalityCelsius { get => _climateContinentalSeasonalityCelsius; set => SetOption(ref _climateContinentalSeasonalityCelsius, value, MapDataKeys.Climate); }
    public double ClimateContinentalSummerBoostCelsius { get => _climateContinentalSummerBoostCelsius; set => SetOption(ref _climateContinentalSummerBoostCelsius, value, MapDataKeys.Climate); }
    public double ClimateContinentalWinterPenaltyCelsius { get => _climateContinentalWinterPenaltyCelsius; set => SetOption(ref _climateContinentalWinterPenaltyCelsius, value, MapDataKeys.Climate); }
    public int ClimateContinentalityDistanceCells { get => _climateContinentalityDistanceCells; set => SetOption(ref _climateContinentalityDistanceCells, value, MapDataKeys.Climate); }
    public int ClimateLargeLakeMinCellCount { get => _climateLargeLakeMinCellCount; set => SetOption(ref _climateLargeLakeMinCellCount, value, MapDataKeys.Climate); }
    public double ClimateOceanEvaporation { get => _climateOceanEvaporation; set => SetOption(ref _climateOceanEvaporation, value, MapDataKeys.Climate); }
    public double ClimateLakeEvaporation { get => _climateLakeEvaporation; set => SetOption(ref _climateLakeEvaporation, value, MapDataKeys.Climate); }
    public double ClimateLandEvapotranspiration { get => _climateLandEvapotranspiration; set => SetOption(ref _climateLandEvapotranspiration, value, MapDataKeys.Climate); }
    public double ClimateMoistureRetention { get => _climateMoistureRetention; set => SetOption(ref _climateMoistureRetention, value, MapDataKeys.Climate); }
    public double ClimateBaseRainfallEfficiency { get => _climateBaseRainfallEfficiency; set => SetOption(ref _climateBaseRainfallEfficiency, value, MapDataKeys.Climate); }
    public double ClimateOrographicStrength { get => _climateOrographicStrength; set => SetOption(ref _climateOrographicStrength, value, MapDataKeys.Climate); }
    public double ClimateDescentDrying { get => _climateDescentDrying; set => SetOption(ref _climateDescentDrying, value, MapDataKeys.Climate); }
    public double ClimateContinentalDrying { get => _climateContinentalDrying; set => SetOption(ref _climateContinentalDrying, value, MapDataKeys.Climate); }
    public double ClimateRiverMoistureBonus { get => _climateRiverMoistureBonus; set => SetOption(ref _climateRiverMoistureBonus, value, MapDataKeys.Climate); }
    public double ClimateRiverAgricultureBonus { get => _climateRiverAgricultureBonus; set => SetOption(ref _climateRiverAgricultureBonus, value, MapDataKeys.Climate); }
    public double ClimateMonsoonRainStrength { get => _climateMonsoonRainStrength; set => SetOption(ref _climateMonsoonRainStrength, value, MapDataKeys.Climate); }
    public double ClimateDrySeasonStrength { get => _climateDrySeasonStrength; set => SetOption(ref _climateDrySeasonStrength, value, MapDataKeys.Climate); }
    public int ClimateMonsoonOceanDistanceCells { get => _climateMonsoonOceanDistanceCells; set => SetOption(ref _climateMonsoonOceanDistanceCells, value, MapDataKeys.Climate); }
    public int ClimateMonsoonCoastProbeCells { get => _climateMonsoonCoastProbeCells; set => SetOption(ref _climateMonsoonCoastProbeCells, value, MapDataKeys.Climate); }
    public double ClimateSnowMeltThresholdCelsius { get => _climateSnowMeltThresholdCelsius; set => SetOption(ref _climateSnowMeltThresholdCelsius, value, MapDataKeys.Climate); }
    public double ClimateSnowPrecipitationScale { get => _climateSnowPrecipitationScale; set => SetOption(ref _climateSnowPrecipitationScale, value, MapDataKeys.Climate); }
    public bool PreserveOceanCoastline { get => _preserveOceanCoastline; set => SetOption(ref _preserveOceanCoastline, value, MapDataKeys.BaseTerrain); }
    public bool PreserveInlandWaterMask { get => _preserveInlandWaterMask; set => SetOption(ref _preserveInlandWaterMask, value, MapDataKeys.BaseTerrain); }
    public bool AllowLakeExpansion { get => _allowLakeExpansion; set => SetOption(ref _allowLakeExpansion, value, MapDataKeys.WaterSurfaces); }
    public bool AllowLakeDrainage { get => _allowLakeDrainage; set => SetOption(ref _allowLakeDrainage, value, MapDataKeys.WaterSurfaces); }
    public double LakeSurfacePercentile { get => _lakeSurfacePercentile; set => SetOption(ref _lakeSurfacePercentile, value, MapDataKeys.WaterSurfaces); }
    public double MinLakeSurfaceMarginMeters { get => _minLakeSurfaceMarginMeters; set => SetOption(ref _minLakeSurfaceMarginMeters, value, MapDataKeys.WaterSurfaces); }
    public double MaxLakeSurfaceMarginMeters { get => _maxLakeSurfaceMarginMeters; set => SetOption(ref _maxLakeSurfaceMarginMeters, value, MapDataKeys.WaterSurfaces); }
    public double MinLakeDepthMeters { get => _minLakeDepthMeters; set => SetOption(ref _minLakeDepthMeters, value, MapDataKeys.WaterSurfaces); }
    public double MaxLakeDepthMeters { get => _maxLakeDepthMeters; set => SetOption(ref _maxLakeDepthMeters, value, MapDataKeys.WaterSurfaces); }
    public double MaxRiftLakeDepthMeters { get => _maxRiftLakeDepthMeters; set => SetOption(ref _maxRiftLakeDepthMeters, value, MapDataKeys.WaterSurfaces); }
    public double MaxInlandSeaDepthMeters { get => _maxInlandSeaDepthMeters; set => SetOption(ref _maxInlandSeaDepthMeters, value, MapDataKeys.WaterSurfaces); }
    public double MountainLakeElevationMeters { get => _mountainLakeElevationMeters; set => SetOption(ref _mountainLakeElevationMeters, value, MapDataKeys.WaterSurfaces); }
    public double PlateauLakeElevationMeters { get => _plateauLakeElevationMeters; set => SetOption(ref _plateauLakeElevationMeters, value, MapDataKeys.WaterSurfaces); }
    public double MountainLakeReliefMeters { get => _mountainLakeReliefMeters; set => SetOption(ref _mountainLakeReliefMeters, value, MapDataKeys.WaterSurfaces); }
    public double LakeTectonicFaultThreshold { get => _lakeTectonicFaultThreshold; set => SetOption(ref _lakeTectonicFaultThreshold, value, MapDataKeys.WaterSurfaces); }
    public double LakeVolcanicInfluenceThreshold { get => _lakeVolcanicInfluenceThreshold; set => SetOption(ref _lakeVolcanicInfluenceThreshold, value, MapDataKeys.WaterSurfaces); }
    public double PlainLakeKarstChance { get => _plainLakeKarstChance; set => SetOption(ref _plainLakeKarstChance, value, MapDataKeys.WaterSurfaces); }
    public double LakeDepthRandomnessMin { get => _lakeDepthRandomnessMin; set => SetOption(ref _lakeDepthRandomnessMin, value, MapDataKeys.WaterSurfaces); }
    public double LakeDepthRandomnessMax { get => _lakeDepthRandomnessMax; set => SetOption(ref _lakeDepthRandomnessMax, value, MapDataKeys.WaterSurfaces); }
    public int LargeLakeDepressionMinCellCount { get => _largeLakeDepressionMinCellCount; set => SetOption(ref _largeLakeDepressionMinCellCount, value, MapDataKeys.WaterSurfaces); }
    public double MountainRiverDensity { get => _mountainRiverDensity; set => SetOption(ref _mountainRiverDensity, value, MapDataKeys.Hydrology); }
    public int MaxMountainSourcesPerCluster { get => _maxMountainSourcesPerCluster; set => SetOption(ref _maxMountainSourcesPerCluster, value, MapDataKeys.Hydrology); }
    public int MinMountainSourceSpacing { get => _minMountainSourceSpacing; set => SetOption(ref _minMountainSourceSpacing, value, MapDataKeys.Hydrology); }
    public TectonicPlateJsonExportMode TectonicJsonMode { get => _tectonicJsonMode; set => SetAndSave(ref _tectonicJsonMode, value); }
    public ElevationJsonExportMode ElevationJsonMode { get => _elevationJsonMode; set => SetAndSave(ref _elevationJsonMode, value); }
    public ClimateJsonExportMode ClimateJsonMode { get => _climateJsonMode; set => SetAndSave(ref _climateJsonMode, value); }
    public double ExportScale { get => _exportScale; set => SetAndSave(ref _exportScale, value); }
    public double ExportRegionBorderWidth { get => _exportRegionBorderWidth; set => SetAndSave(ref _exportRegionBorderWidth, value); }
    public double ExportTectonicBoundaryWidth { get => _exportTectonicBoundaryWidth; set => SetAndSave(ref _exportTectonicBoundaryWidth, value); }
    public bool ExportDrawCrustPlateBoundaries { get => _exportDrawCrustPlateBoundaries; set => SetAndSave(ref _exportDrawCrustPlateBoundaries, value); }
    public bool ExportDrawFeaturePlateBoundaries { get => _exportDrawFeaturePlateBoundaries; set => SetAndSave(ref _exportDrawFeaturePlateBoundaries, value); }
    public bool ExportDrawElevationHillshade { get => _exportDrawElevationHillshade; set => SetAndSave(ref _exportDrawElevationHillshade, value); }
    public bool ExportDrawElevationPlateBoundaries { get => _exportDrawElevationPlateBoundaries; set => SetAndSave(ref _exportDrawElevationPlateBoundaries, value); }
    public bool ExportDrawClimateHillshade { get => _exportDrawClimateHillshade; set => SetAndSave(ref _exportDrawClimateHillshade, value); }
    public bool ExportDrawClimateRivers { get => _exportDrawClimateRivers; set => SetAndSave(ref _exportDrawClimateRivers, value); }
    public bool ExportDrawClimateRiverValleyAccents { get => _exportDrawClimateRiverValleyAccents; set => SetAndSave(ref _exportDrawClimateRiverValleyAccents, value); }

    private async Task BrowseMaskAsync()
    {
        var window = GetMainWindow();
        if (window is null)
            return;

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L["Mask"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] },
                FilePickerFileTypes.All
            ]
        });

        var file = result.Count == 0 ? null : result[0];
        if (file?.Path.LocalPath is { Length: > 0 } path)
            MaskPath = path;
    }

    private async Task BrowseOutputAsync()
    {
        var window = GetMainWindow();
        if (window is null)
            return;

        var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L["Output"],
            AllowMultiple = false
        });

        var folder = result.Count == 0 ? null : result[0];
        if (folder?.Path.LocalPath is { Length: > 0 } path)
            OutputDirectory = path;
    }

    private async Task RunFullAsync()
    {
        if (!PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        StatusMessage = L["Generating"];
        AddLog(L["LogGenerationStarted"]);

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            RefreshStageStates();
            RefreshLayerAvailability();

            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");
            var targets = Stages.Where(s => s.DataKey.HasValue).Select(s => s.DataKey!.Value).ToArray();
            await _execution.RunProgressiveAsync(
                session,
                targets,
                key => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var stage = FindStage(key);
                        if (stage is not null)
                        {
                            stage.Status = GenerationStageStatus.Running;
                            stage.Error = string.Empty;
                            AddLog($"{L["LogStageStarted"]}: {stage.Name}");
                        }
                    }).GetTask(),
                (key, duration) => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var stage = FindStage(key);
                        if (stage is not null)
                        {
                            stage.Status = GenerationStageStatus.Ready;
                            stage.Duration = duration;
                            AddLog($"{L["LogStageCompleted"]}: {stage.Name} ({stage.DurationText})");
                        }

                        RefreshStageStates();
                        RefreshLayerAvailability();
                        SelectBestAvailableLayer(key);
                        RefreshPreview();
                        RefreshStatistics();
                    }).GetTask(),
                _generationCts.Token);

            StatusMessage = L["Completed"];
            AddLog(L["LogGenerationCompleted"]);
            AddHistoryEntry();
            CompleteOnboarding();
            SaveSettings();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L["Ready"];
            AddLog(L["LogGenerationCancelled"], "warning");
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
            MarkRunningStageFailed(ex.Message);
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
            RefreshStageStates();
        }
    }

    private async Task RunRegionsOnlyAsync()
    {
        if (!PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        StatusMessage = L["GeneratingRegions"];
        AddLog(L["LogRegionsStarted"]);

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            RefreshStageStates();
            RefreshLayerAvailability();

            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");
            var targets = new[] { MapDataKeys.Landmasses, MapDataKeys.RawRegions, MapDataKeys.Regions };
            await _execution.RunProgressiveAsync(
                session,
                targets,
                key => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var stage = FindStage(key);
                        if (stage is not null)
                        {
                            stage.Status = GenerationStageStatus.Running;
                            stage.Error = string.Empty;
                            AddLog($"{L["LogStageStarted"]}: {stage.Name}");
                        }
                    }).GetTask(),
                (key, duration) => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var stage = FindStage(key);
                        if (stage is not null)
                        {
                            stage.Status = GenerationStageStatus.Ready;
                            stage.Duration = duration;
                            AddLog($"{L["LogStageCompleted"]}: {stage.Name} ({stage.DurationText})");
                        }

                        RefreshStageStates();
                        RefreshLayerAvailability();
                        SelectBestAvailableLayer(key);
                        RefreshPreview();
                        RefreshStatistics();
                    }).GetTask(),
                _generationCts.Token);

            StatusMessage = L["RegionsCompleted"];
            AddLog(L["LogRegionsCompleted"]);
            AddHistoryEntry(L["RegionsOnly"]);
            CompleteOnboarding();
            SaveSettings();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L["Ready"];
            AddLog(L["LogGenerationCancelled"], "warning");
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
            MarkRunningStageFailed(ex.Message);
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
            RefreshStageStates();
        }
    }

    private async Task OpenRegionEditorAsync(bool frozenVisibleRegions)
    {
        if (IsGenerating || string.IsNullOrWhiteSpace(MaskPath) || !File.Exists(MaskPath))
        {
            StatusMessage = L["ValidationMask"];
            return;
        }

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");
            await _execution.RunUntilAsync(session, frozenVisibleRegions ? MapDataKeys.Regions : MapDataKeys.RawRegions, CancellationToken.None);

            var source = frozenVisibleRegions ? session.Regions : session.RawRegions;
            var draft = RegionDraft.FromRegions(source, frozenVisibleRegions ? RegionDraftOrigin.GeneratedAndEdited : RegionDraftOrigin.Generated);
            var editor = new RegionEditorWindow
            {
                DataContext = new RegionEditorViewModel(session.Mask, session.Options, session.Landmasses, draft,
                    frozenVisibleRegions ? false : BoundaryDistortionEnabled)
            };
            var owner = GetMainWindow();
            var result = owner is null ? null : await editor.ShowDialog<RegionEditorResult?>(owner);
            if (result is null)
                return;

            BoundaryDistortionEnabled = result.ApplyBoundaryDistortion;
            session.SetRegionDraft(result.Draft);
            await _execution.RunUntilAsync(session, MapDataKeys.Regions, CancellationToken.None);
            RefreshStageStates();
            RefreshLayerAvailability();
            SelectBestAvailableLayer(MapDataKeys.Regions);
            RefreshPreview();
            RefreshStatistics();
            this.RaisePropertyChanged(nameof(HasManualRegionDraft));
            StatusMessage = L["ManualRegionsApplied"];
            AddLog(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog(StatusMessage, "error");
        }
    }

    private async Task ResetAutomaticRegionsAsync()
    {
        if (IsGenerating || !PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");

            session.SetRegionDraft(null);
            await _execution.RunUntilAsync(session, MapDataKeys.Regions, _generationCts.Token);
            RefreshStageStates();
            RefreshLayerAvailability();
            SelectBestAvailableLayer(MapDataKeys.Regions);
            RefreshPreview();
            RefreshStatistics();
            this.RaisePropertyChanged(nameof(HasManualRegionDraft));
            StatusMessage = L["AutomaticRegionsRestored"];
            AddLog(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L["Ready"];
            AddLog(L["LogGenerationCancelled"], "warning");
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog(StatusMessage, "error");
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    private async Task RunUntilStageAsync(GenerationStageViewModel stage)
    {
        if (stage.DataKey is null || !PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        stage.Status = GenerationStageStatus.Running;
        StatusMessage = L["Generating"];
        AddLog($"{L["LogStageStarted"]}: {stage.Name}");

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");

            var started = Stopwatch.StartNew();
            await _execution.RunUntilAsync(session, stage.DataKey.Value, _generationCts.Token);
            started.Stop();
            stage.Duration = started.Elapsed;
            stage.Status = GenerationStageStatus.Ready;
            AddLog($"{L["LogStageCompleted"]}: {stage.Name} ({stage.DurationText})");

            RefreshStageStates();
            RefreshLayerAvailability();
            SelectBestAvailableLayer(stage.DataKey.Value);
            RefreshPreview();
            RefreshStatistics();
            StatusMessage = L["Completed"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stage.Status = GenerationStageStatus.Failed;
            stage.Error = ex.Message;
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    private async Task RegenerateStageAsync(GenerationStageViewModel stage)
    {
        if (stage.DataKey is null || !PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        stage.Status = GenerationStageStatus.Running;
        AddLog($"{L["LogStageStarted"]}: {stage.Name}");

        try
        {
            var options = BuildOptions();
            _workspace.EnsureSession(MaskPath, options, _sessionResetRequired);
            _sessionResetRequired = false;
            var session = _workspace.Session ?? throw new InvalidOperationException("Generation session was not created.");

            var started = Stopwatch.StartNew();
            await _execution.RegenerateAsync(session, stage.DataKey.Value, _generationCts.Token);
            started.Stop();
            stage.Duration = started.Elapsed;
            stage.Status = session.IsDirty(stage.DataKey.Value) ? GenerationStageStatus.Dirty : GenerationStageStatus.Ready;
            AddLog($"{L["LogStageCompleted"]}: {stage.Name} ({stage.DurationText})");

            RefreshStageStates();
            RefreshLayerAvailability();
            SelectBestAvailableLayer(stage.DataKey.Value);
            RefreshPreview();
            RefreshStatistics();
            StatusMessage = L["Completed"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stage.Status = GenerationStageStatus.Failed;
            stage.Error = ex.Message;
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    private async Task ExportAsync()
    {
        if (!PrepareForGeneration())
            return;

        if (_workspace.Session is null)
            await RunFullAsync();

        if (_workspace.Session is null)
            return;

        try
        {
            var result = await Task.Run(() => MapGenerationArtifactWriter.Write(
                _workspace.Session.CurrentMap,
                MaskPath,
                OutputDirectory,
                BuildOptions(),
                TectonicJsonMode,
                ElevationJsonMode,
                ClimateJsonMode,
                BuildExportRenderOptions()));

            ArtifactSummary = FormatArtifactSummary(result.Artifacts);
            StatusMessage = L["StatusExported"];
            AddLog(L["StatusExported"]);
            this.RaisePropertyChanged(nameof(HasArtifacts));
            AddHistoryEntry();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
        }
    }

    private async Task ExportPreviewAsync()
    {
        if (_workspace.Session is null)
        {
            StatusMessage = L["StatusGenerateFirst"];
            return;
        }

        if (SelectedPreviewLayer is null)
            return;

        var window = GetMainWindow();
        if (window is null)
            return;

        var layerName = SelectedPreviewLayer.Name ?? "preview";
        var result = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["ExportPreview"],
            FileTypeChoices =
            [
                new FilePickerFileType("PNG") { Patterns = ["*.png"] },
                new FilePickerFileType("JPEG") { Patterns = ["*.jpg", "*.jpeg"] }
            ],
            DefaultExtension = "png",
            SuggestedFileName = $"{SanitizeFileName(layerName)}.png"
        });

        if (result?.Path.LocalPath is not { Length: > 0 } path)
            return;

        try
        {
            await _preview.SavePreviewToFileAsync(_workspace.Session, SelectedPreviewLayer, path, BuildExportRenderOptions());
            StatusMessage = $"{L["StatusPreviewExported"]}: {Path.GetFileName(path)}";
            AddLog(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
            AddLog($"{L["Failed"]}: {ex.Message}", "error");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private void CancelGeneration() => _generationCts?.Cancel();

    private MapArtifactRenderOptions BuildExportRenderOptions()
    {
        return new MapArtifactRenderOptions
        {
            Scale = (float)ExportScale,
            RegionBorderWidth = (float)ExportRegionBorderWidth,
            TectonicBoundaryWidth = (float)ExportTectonicBoundaryWidth,
            DrawCrustPlateBoundaries = ExportDrawCrustPlateBoundaries,
            DrawFeaturePlateBoundaries = ExportDrawFeaturePlateBoundaries,
            DrawElevationHillshade = ExportDrawElevationHillshade,
            DrawElevationPlateBoundaries = ExportDrawElevationPlateBoundaries,
            DrawClimateHillshade = ExportDrawClimateHillshade,
            DrawClimateRivers = ExportDrawClimateRivers,
            DrawClimateRiverValleyAccents = ExportDrawClimateRiverValleyAccents
        };
    }

    private void DismissOnboarding()
    {
        ShowOnboarding = false;
        _settings.HasCompletedOnboarding = true;
        SavePreferences();
    }

    private void CompleteOnboarding()
    {
        if (!ShowOnboarding && _settings.HasCompletedOnboarding)
            return;

        ShowOnboarding = false;
        _settings.HasCompletedOnboarding = true;
    }

    private void RandomizeSeed()
    {
        Seed = Random.Shared.Next(1, int.MaxValue);
    }

    private async Task CopySeedAsync()
    {
        var clipboard = GetMainWindow()?.Clipboard;
        if (clipboard is not null && Seed is not null)
            await clipboard.SetTextAsync(Seed.Value.ToString(CultureInfo.CurrentCulture));
    }

    private void ApplyPreset(string preset)
    {
        _suppressDirty = true;
        try
        {
            if (preset == "fast")
            {
                TargetArea = 900;
                PointsMultiplier = 2;
                BoundaryDetail = 0.18;
                PlateCount = null;
                ReliefScale = 0.85;
                GenerateSmallLakes = true;
                SmallLakeCountMultiplier = 0.65;
                SmallLakeScatterMultiplier = 0.65;
                SmallLakeSizeMultiplier = 0.85;
                RiverDensity = 6.0;
                TributaryDensity = 2.0;
                TectonicJsonMode = TectonicPlateJsonExportMode.Summary;
                ElevationJsonMode = ElevationJsonExportMode.Summary;
                ClimateJsonMode = ClimateJsonExportMode.Summary;
            }
            else if (preset == "detailed")
            {
                TargetArea = 260;
                PointsMultiplier = 5.5;
                BoundaryDetail = 0.35;
                BoundaryNoise = 0.28;
                Roughness = 0.55;
                ReliefScale = 1.1;
                GenerateSmallLakes = true;
                SmallLakeCountMultiplier = 1.25;
                SmallLakeScatterMultiplier = 1.0;
                SmallLakeSizeMultiplier = 1.15;
                RiverDensity = 12.0;
                TributaryDensity = 4.5;
            }
            else if (preset == "diagnostic")
            {
                GenerateSmallLakes = true;
                TectonicJsonMode = TectonicPlateJsonExportMode.CompactDiagnostic;
                ElevationJsonMode = ElevationJsonExportMode.Diagnostic;
                ClimateJsonMode = ClimateJsonExportMode.Diagnostic;
            }
            else
            {
                TargetArea = 400;
                PointsMultiplier = 4;
                BoundaryDetail = 0.25;
                BoundaryNoise = 0.2;
                ReliefScale = 1.0;
                Roughness = 0.45;
                SmallIslandReliefFactor = 0.55;
                GenerateSmallLakes = true;
                SmallLakeCountMultiplier = 1.0;
                SmallLakeScatterMultiplier = 0.5;
                SmallLakeSizeMultiplier = 1.0;
                RiverDensity = 10.0;
                TributaryDensity = 3.5;
                TectonicJsonMode = TectonicPlateJsonExportMode.Summary;
                ElevationJsonMode = ElevationJsonExportMode.Summary;
                ClimateJsonMode = ClimateJsonExportMode.Summary;
            }
        }
        finally
        {
            _suppressDirty = false;
        }

        MarkOptionsDirty(MapDataKeys.RegionDraft, MapDataKeys.TectonicHistory, MapDataKeys.BaseTerrain, MapDataKeys.GeneratedLakes, MapDataKeys.Hydrology, MapDataKeys.Climate);
        SaveSettings();
    }

    private bool PrepareForGeneration()
    {
        ValidateAll();
        if (HasValidationMessage)
            return false;

        EnsureSeedForRun();
        return true;
    }

    private void EnsureSeedForRun()
    {
        if (Seed.HasValue)
            return;

        Seed = Random.Shared.Next(1, int.MaxValue);
    }

    private void ValidateAll()
    {
        if (string.IsNullOrWhiteSpace(MaskPath) || !File.Exists(MaskPath))
            ValidationMessage = L["ValidationMask"];
        else if (string.IsNullOrWhiteSpace(OutputDirectory))
            ValidationMessage = L["ValidationOutput"];
        else
        {
            try
            {
                BuildOptions().Validate();
                if (ExportScale <= 0)
                    throw new ArgumentOutOfRangeException(nameof(ExportScale), "Export scale must be greater than zero.");
                if (ExportRegionBorderWidth < 0)
                    throw new ArgumentOutOfRangeException(nameof(ExportRegionBorderWidth), "Region border width cannot be negative.");
                if (ExportTectonicBoundaryWidth < 0)
                    throw new ArgumentOutOfRangeException(nameof(ExportTectonicBoundaryWidth), "Tectonic boundary width cannot be negative.");

                ValidationMessage = string.Empty;
            }
            catch (Exception ex)
            {
                ValidationMessage = $"{L["ValidationOptions"]} {ex.Message}";
            }
        }

        this.RaisePropertyChanged(nameof(HasValidationMessage));
    }

    private MapGenerationOptions BuildOptions()
    {
        return new MapGenerationOptions
        {
            PixelSize = PixelSize,
            Seed = Seed,
            ProjectionMode = ProjectionMode,
            ShapeExtraction = new ShapeExtractionOptions { SimplifyTolerance = SimplifyTolerance },
            WaterBodies = new WaterBodyClassificationOptions
            {
                OceanSeaMinAreaRatio = OceanSeaMinAreaRatio,
                InlandSeaMinAreaRatio = InlandSeaMinAreaRatio,
                OceanSeaNearOceanMaxDistanceCells = OceanSeaNearOceanMaxDistanceCells
            },
            Regions = new RegionGenerationOptions
            {
                TargetArea = TargetArea,
                PointsMultiplier = PointsMultiplier,
                MinAreaRatio = MinAreaRatio,
                MaxAreaRatio = MaxAreaRatio
            },
            Boundaries = new BoundaryDistortionOptions
            {
                Enabled = BoundaryDistortionEnabled,
                Detail = BoundaryDetail,
                MaxOffset = MaxOffset,
                MinLineLengthToCurve = MinLineLengthToCurve
            },
            TectonicPlates = new TectonicPlateGenerationOptions
            {
                PlateCount = PlateCount,
                ContinentalSeedRatio = ContinentalSeedRatio,
                BoundaryNoise = BoundaryNoise,
                BoundaryNoiseScale = BoundaryNoiseScale,
                LandWaterTransitionPenalty = LandWaterTransitionPenalty,
                Activity = Activity,
                EarthLikeFactor = EarthLikeFactor,
                HistoryDepth = HistoryDepth,
                MicroplateRatio = MicroplateRatio,
                MinMicroplateAreaRatio = MinMicroplateAreaRatio,
                MaxMicroplateAreaRatio = MaxMicroplateAreaRatio,
                MinBoundarySegmentLength = MinBoundarySegmentLength,
                ActiveMarginRatio = ActiveMarginRatio,
                ShelfWidthFactor = TectonicShelfWidthFactor,
                HotspotCount = HotspotCount,
                RiftChance = RiftChance,
                ValidateGeometry = ValidateGeometry,
                MaxValidationCycles = MaxValidationCycles,
                MinPlateSize = MinPlateSize,
                MinPlateSizeRatio = MinPlateSizeRatio
            },
            Elevation = new ElevationGenerationOptions
            {
                ReliefScale = ReliefScale,
                Mountaininess = Mountaininess,
                Erosion = Erosion,
                Roughness = Roughness,
                SeaDepthScale = SeaDepthScale,
                ShelfWidthFactor = ElevationShelfWidthFactor,
                VolcanismInfluence = VolcanismInfluence,
                SmallIslandReliefFactor = SmallIslandReliefFactor,
                RiftInfluence = RiftInfluence,
                GenerateSmallLakes = GenerateSmallLakes,
                SmallLakeCountMultiplier = SmallLakeCountMultiplier,
                SmallLakeScatterMultiplier = SmallLakeScatterMultiplier,
                SmallLakeSizeMultiplier = SmallLakeSizeMultiplier,
                PreserveMaskCoastline = PreserveMaskCoastline,
                PreserveOceanCoastline = PreserveOceanCoastline,
                PreserveInlandWaterMask = PreserveInlandWaterMask,
                AllowLakeExpansion = AllowLakeExpansion,
                AllowLakeDrainage = AllowLakeDrainage,
                MaxElevationMeters = MaxElevationMeters,
                MinOceanDepthMeters = MinOceanDepthMeters,
                MinLandElevationMeters = MinLandElevationMeters,
                MaxSeaElevationMeters = MaxSeaElevationMeters,
                LakeSurfacePercentile = LakeSurfacePercentile,
                MinLakeSurfaceMarginMeters = MinLakeSurfaceMarginMeters,
                MaxLakeSurfaceMarginMeters = MaxLakeSurfaceMarginMeters,
                MinLakeDepthMeters = MinLakeDepthMeters,
                MaxLakeDepthMeters = MaxLakeDepthMeters,
                MaxRiftLakeDepthMeters = MaxRiftLakeDepthMeters,
                MaxInlandSeaDepthMeters = MaxInlandSeaDepthMeters,
                MountainLakeElevationMeters = MountainLakeElevationMeters,
                PlateauLakeElevationMeters = PlateauLakeElevationMeters,
                MountainLakeReliefMeters = MountainLakeReliefMeters,
                LakeTectonicFaultThreshold = LakeTectonicFaultThreshold,
                LakeVolcanicInfluenceThreshold = LakeVolcanicInfluenceThreshold,
                PlainLakeKarstChance = PlainLakeKarstChance,
                LakeDepthRandomnessMin = LakeDepthRandomnessMin,
                LakeDepthRandomnessMax = LakeDepthRandomnessMax,
                LargeLakeDepressionMinCellCount = LargeLakeDepressionMinCellCount
            },
            Hydrology = new HydrologyGenerationOptions
            {
                RiverDensity = RiverDensity,
                MountainRiverDensity = MountainRiverDensity,
                MaxMountainSourcesPerCluster = MaxMountainSourcesPerCluster,
                MinMountainSourceSpacing = MinMountainSourceSpacing,
                MajorRiverCountMultiplier = MajorRiverCountMultiplier,
                LongRiverCountMultiplier = LongRiverCountMultiplier,
                TributaryDensity = TributaryDensity,
                MajorRiverTributaryMultiplier = MajorRiverTributaryMultiplier,
                LakeOutletInflowForceMultiplier = LakeOutletInflowForceMultiplier,
                EndorheicBasinChance = EndorheicBasinChance,
                MaxEndorheicBasins = MaxEndorheicBasins,
                DeltaFrequency = DeltaFrequency,
                MeanderStrength = MeanderStrength,
                LakeOutletStrictness = LakeOutletStrictness,
                PreserveCoastline = PreserveRiverCoastline,
                AllowRiverCarving = AllowRiverCarving
            },
            Climate = new ClimateGenerationOptions
            {
                PolarLatitudeMargin = ClimatePolarLatitudeMargin,
                EquatorTemperatureCelsius = ClimateEquatorTemperatureCelsius,
                PoleCoolingCelsius = ClimatePoleCoolingCelsius,
                LatitudeCurveExponent = ClimateLatitudeCurveExponent,
                LapseRateCelsiusPerMeter = ClimateLapseRateCelsiusPerMeter,
                BaseSeasonalityCelsius = ClimateBaseSeasonalityCelsius,
                LatitudeSeasonalityCelsius = ClimateLatitudeSeasonalityCelsius,
                ContinentalSeasonalityCelsius = ClimateContinentalSeasonalityCelsius,
                ContinentalSummerBoostCelsius = ClimateContinentalSummerBoostCelsius,
                ContinentalWinterPenaltyCelsius = ClimateContinentalWinterPenaltyCelsius,
                ContinentalityDistanceCells = ClimateContinentalityDistanceCells,
                LargeLakeMinCellCount = ClimateLargeLakeMinCellCount,
                OceanEvaporation = ClimateOceanEvaporation,
                LakeEvaporation = ClimateLakeEvaporation,
                LandEvapotranspiration = ClimateLandEvapotranspiration,
                MoistureRetention = ClimateMoistureRetention,
                BaseRainfallEfficiency = ClimateBaseRainfallEfficiency,
                OrographicStrength = ClimateOrographicStrength,
                DescentDrying = ClimateDescentDrying,
                ContinentalDrying = ClimateContinentalDrying,
                RiverMoistureBonus = ClimateRiverMoistureBonus,
                RiverAgricultureBonus = ClimateRiverAgricultureBonus,
                MonsoonRainStrength = ClimateMonsoonRainStrength,
                DrySeasonStrength = ClimateDrySeasonStrength,
                MonsoonOceanDistanceCells = ClimateMonsoonOceanDistanceCells,
                MonsoonCoastProbeCells = ClimateMonsoonCoastProbeCells,
                SnowMeltThresholdCelsius = ClimateSnowMeltThresholdCelsius,
                SnowPrecipitationScale = ClimateSnowPrecipitationScale
            }
        };
    }

    private void LoadOptions(MapGenerationOptions options)
    {
        _suppressDirty = true;
        try
        {
            PixelSize = options.PixelSize;
            Seed = options.Seed;
            ProjectionMode = options.ProjectionMode;
            SimplifyTolerance = options.ShapeExtraction.SimplifyTolerance;
            TargetArea = options.Regions.TargetArea;
            PointsMultiplier = options.Regions.PointsMultiplier;
            MinAreaRatio = options.Regions.MinAreaRatio;
            MaxAreaRatio = options.Regions.MaxAreaRatio;
            BoundaryDistortionEnabled = options.Boundaries.Enabled;
            BoundaryDetail = options.Boundaries.Detail;
            MaxOffset = options.Boundaries.MaxOffset;
            MinLineLengthToCurve = options.Boundaries.MinLineLengthToCurve;
            OceanSeaMinAreaRatio = options.WaterBodies.OceanSeaMinAreaRatio;
            InlandSeaMinAreaRatio = options.WaterBodies.InlandSeaMinAreaRatio;
            OceanSeaNearOceanMaxDistanceCells = options.WaterBodies.OceanSeaNearOceanMaxDistanceCells;
            PlateCount = options.TectonicPlates.PlateCount;
            HotspotCount = options.TectonicPlates.HotspotCount;
            ContinentalSeedRatio = options.TectonicPlates.ContinentalSeedRatio;
            BoundaryNoise = options.TectonicPlates.BoundaryNoise;
            BoundaryNoiseScale = options.TectonicPlates.BoundaryNoiseScale;
            LandWaterTransitionPenalty = options.TectonicPlates.LandWaterTransitionPenalty;
            Activity = options.TectonicPlates.Activity;
            EarthLikeFactor = options.TectonicPlates.EarthLikeFactor;
            HistoryDepth = options.TectonicPlates.HistoryDepth;
            MicroplateRatio = options.TectonicPlates.MicroplateRatio;
            MinMicroplateAreaRatio = options.TectonicPlates.MinMicroplateAreaRatio;
            MaxMicroplateAreaRatio = options.TectonicPlates.MaxMicroplateAreaRatio;
            MinBoundarySegmentLength = options.TectonicPlates.MinBoundarySegmentLength;
            ActiveMarginRatio = options.TectonicPlates.ActiveMarginRatio;
            TectonicShelfWidthFactor = options.TectonicPlates.ShelfWidthFactor;
            RiftChance = options.TectonicPlates.RiftChance;
            ValidateGeometry = options.TectonicPlates.ValidateGeometry;
            MaxValidationCycles = options.TectonicPlates.MaxValidationCycles;
            MinPlateSize = options.TectonicPlates.MinPlateSize;
            MinPlateSizeRatio = options.TectonicPlates.MinPlateSizeRatio;
            ReliefScale = options.Elevation.ReliefScale;
            Mountaininess = options.Elevation.Mountaininess;
            Erosion = options.Elevation.Erosion;
            Roughness = options.Elevation.Roughness;
            SeaDepthScale = options.Elevation.SeaDepthScale;
            ElevationShelfWidthFactor = options.Elevation.ShelfWidthFactor;
            VolcanismInfluence = options.Elevation.VolcanismInfluence;
            SmallIslandReliefFactor = options.Elevation.SmallIslandReliefFactor;
            RiftInfluence = options.Elevation.RiftInfluence;
            GenerateSmallLakes = options.Elevation.GenerateSmallLakes;
            SmallLakeCountMultiplier = options.Elevation.SmallLakeCountMultiplier;
            SmallLakeScatterMultiplier = options.Elevation.SmallLakeScatterMultiplier;
            SmallLakeSizeMultiplier = options.Elevation.SmallLakeSizeMultiplier;
            PreserveMaskCoastline = options.Elevation.PreserveMaskCoastline;
            PreserveOceanCoastline = options.Elevation.PreserveOceanCoastline;
            PreserveInlandWaterMask = options.Elevation.PreserveInlandWaterMask;
            AllowLakeExpansion = options.Elevation.AllowLakeExpansion;
            AllowLakeDrainage = options.Elevation.AllowLakeDrainage;
            MaxElevationMeters = options.Elevation.MaxElevationMeters;
            MinOceanDepthMeters = options.Elevation.MinOceanDepthMeters;
            MinLandElevationMeters = options.Elevation.MinLandElevationMeters;
            MaxSeaElevationMeters = options.Elevation.MaxSeaElevationMeters;
            LakeSurfacePercentile = options.Elevation.LakeSurfacePercentile;
            MinLakeSurfaceMarginMeters = options.Elevation.MinLakeSurfaceMarginMeters;
            MaxLakeSurfaceMarginMeters = options.Elevation.MaxLakeSurfaceMarginMeters;
            MinLakeDepthMeters = options.Elevation.MinLakeDepthMeters;
            MaxLakeDepthMeters = options.Elevation.MaxLakeDepthMeters;
            MaxRiftLakeDepthMeters = options.Elevation.MaxRiftLakeDepthMeters;
            MaxInlandSeaDepthMeters = options.Elevation.MaxInlandSeaDepthMeters;
            MountainLakeElevationMeters = options.Elevation.MountainLakeElevationMeters;
            PlateauLakeElevationMeters = options.Elevation.PlateauLakeElevationMeters;
            MountainLakeReliefMeters = options.Elevation.MountainLakeReliefMeters;
            LakeTectonicFaultThreshold = options.Elevation.LakeTectonicFaultThreshold;
            LakeVolcanicInfluenceThreshold = options.Elevation.LakeVolcanicInfluenceThreshold;
            PlainLakeKarstChance = options.Elevation.PlainLakeKarstChance;
            LakeDepthRandomnessMin = options.Elevation.LakeDepthRandomnessMin;
            LakeDepthRandomnessMax = options.Elevation.LakeDepthRandomnessMax;
            LargeLakeDepressionMinCellCount = options.Elevation.LargeLakeDepressionMinCellCount;
            RiverDensity = options.Hydrology.RiverDensity;
            MountainRiverDensity = options.Hydrology.MountainRiverDensity;
            MaxMountainSourcesPerCluster = options.Hydrology.MaxMountainSourcesPerCluster;
            MinMountainSourceSpacing = options.Hydrology.MinMountainSourceSpacing;
            MajorRiverCountMultiplier = options.Hydrology.MajorRiverCountMultiplier;
            LongRiverCountMultiplier = options.Hydrology.LongRiverCountMultiplier;
            TributaryDensity = options.Hydrology.TributaryDensity;
            MajorRiverTributaryMultiplier = options.Hydrology.MajorRiverTributaryMultiplier;
            LakeOutletInflowForceMultiplier = options.Hydrology.LakeOutletInflowForceMultiplier;
            EndorheicBasinChance = options.Hydrology.EndorheicBasinChance;
            MaxEndorheicBasins = options.Hydrology.MaxEndorheicBasins;
            DeltaFrequency = options.Hydrology.DeltaFrequency;
            MeanderStrength = options.Hydrology.MeanderStrength;
            LakeOutletStrictness = options.Hydrology.LakeOutletStrictness;
            PreserveRiverCoastline = options.Hydrology.PreserveCoastline;
            AllowRiverCarving = options.Hydrology.AllowRiverCarving;
            ClimatePolarLatitudeMargin = options.Climate.PolarLatitudeMargin;
            ClimateEquatorTemperatureCelsius = options.Climate.EquatorTemperatureCelsius;
            ClimatePoleCoolingCelsius = options.Climate.PoleCoolingCelsius;
            ClimateLatitudeCurveExponent = options.Climate.LatitudeCurveExponent;
            ClimateLapseRateCelsiusPerMeter = options.Climate.LapseRateCelsiusPerMeter;
            ClimateBaseSeasonalityCelsius = options.Climate.BaseSeasonalityCelsius;
            ClimateLatitudeSeasonalityCelsius = options.Climate.LatitudeSeasonalityCelsius;
            ClimateContinentalSeasonalityCelsius = options.Climate.ContinentalSeasonalityCelsius;
            ClimateContinentalSummerBoostCelsius = options.Climate.ContinentalSummerBoostCelsius;
            ClimateContinentalWinterPenaltyCelsius = options.Climate.ContinentalWinterPenaltyCelsius;
            ClimateContinentalityDistanceCells = options.Climate.ContinentalityDistanceCells;
            ClimateLargeLakeMinCellCount = options.Climate.LargeLakeMinCellCount;
            ClimateOceanEvaporation = options.Climate.OceanEvaporation;
            ClimateLakeEvaporation = options.Climate.LakeEvaporation;
            ClimateLandEvapotranspiration = options.Climate.LandEvapotranspiration;
            ClimateMoistureRetention = options.Climate.MoistureRetention;
            ClimateBaseRainfallEfficiency = options.Climate.BaseRainfallEfficiency;
            ClimateOrographicStrength = options.Climate.OrographicStrength;
            ClimateDescentDrying = options.Climate.DescentDrying;
            ClimateContinentalDrying = options.Climate.ContinentalDrying;
            ClimateRiverMoistureBonus = options.Climate.RiverMoistureBonus;
            ClimateRiverAgricultureBonus = options.Climate.RiverAgricultureBonus;
            ClimateMonsoonRainStrength = options.Climate.MonsoonRainStrength;
            ClimateDrySeasonStrength = options.Climate.DrySeasonStrength;
            ClimateMonsoonOceanDistanceCells = options.Climate.MonsoonOceanDistanceCells;
            ClimateMonsoonCoastProbeCells = options.Climate.MonsoonCoastProbeCells;
            ClimateSnowMeltThresholdCelsius = options.Climate.SnowMeltThresholdCelsius;
            ClimateSnowPrecipitationScale = options.Climate.SnowPrecipitationScale;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void InitializeStages()
    {
        Stages.Add(CreateStage(MapStageIds.ExtractLandmasses, "StageLandmasses", MapDataKeys.Landmasses));
        Stages.Add(CreateStage(MapStageIds.ExtractWaterBodies, "StageWaterBodies", MapDataKeys.WaterBodies));
        Stages.Add(CreateStage(MapStageIds.ClassifyWaterBodies, "StageWaterBodyTopology", MapDataKeys.WaterBodyTopology));
        Stages.Add(CreateStage(MapStageIds.GenerateTectonicHistory, "StageTectonicHistory", MapDataKeys.TectonicHistory));
        Stages.Add(CreateStage(MapStageIds.GenerateCrustFields, "StageCrustFields", MapDataKeys.CrustFields));
        Stages.Add(CreateStage(MapStageIds.GeneratePlateDomains, "StagePlateDomains", MapDataKeys.PlateDomains));
        Stages.Add(CreateStage(MapStageIds.GenerateTectonicBoundaries, "StageTectonicBoundaries", MapDataKeys.TectonicBoundaries));
        Stages.Add(CreateStage(MapStageIds.GenerateOrogenProvinces, "StageOrogenProvinces", MapDataKeys.OrogenProvinces));
        Stages.Add(CreateStage(MapStageIds.GenerateRiftProvinces, "StageRiftProvinces", MapDataKeys.RiftProvinces));
        Stages.Add(CreateStage(MapStageIds.GenerateTectonicFeatures, "StageTectonicFeatures", MapDataKeys.TectonicFeatures));
        Stages.Add(CreateStage(MapStageIds.GenerateElevation, "StageBaseTerrain", MapDataKeys.BaseTerrain));
        Stages.Add(CreateStage(MapStageIds.GenerateSmallLakes, "StageGeneratedLakes", MapDataKeys.GeneratedLakes));
        Stages.Add(CreateStage(MapStageIds.GenerateLakeLevels, "StageLakeLevels", MapDataKeys.WaterSurfaces));
        Stages.Add(CreateStage(MapStageIds.GenerateHydrology, "StageHydrology", MapDataKeys.Hydrology));
        Stages.Add(CreateStage(MapStageIds.GenerateClimate, "StageClimate", MapDataKeys.Climate));
        Stages.Add(CreateStage(MapStageIds.GenerateTectonicPlates, "StageTectonicPlates", MapDataKeys.TectonicPlates));
        Stages.Add(CreateStage(MapStageIds.GenerateRegions, "StageRawRegions", MapDataKeys.RegionDraft));
        Stages.Add(CreateStage(MapStageIds.DistortRegionBoundaries, "StageRegions", MapDataKeys.Regions));

        FutureStages.Add(CreateStage("resources", "Resources", null));
        foreach (var stage in FutureStages)
            stage.Status = GenerationStageStatus.Future;
    }

    private GenerationStageViewModel CreateStage(string id, string labelKey, MapDataKey? dataKey)
    {
        return new GenerationStageViewModel(id, labelKey, dataKey, Localize, RunUntilStageAsync, RegenerateStageAsync, CopySeedAsync);
    }

    private void InitializePreviewLayers()
    {
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Overview, "LayerOverview", MapDataKeys.Landmasses, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Regions, "LayerRegions", MapDataKeys.Regions, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.TectonicPlates, "LayerTectonicPlates", MapDataKeys.PlateDomains, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Crust, "LayerCrust", MapDataKeys.CrustFields, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.TectonicFeatures, "LayerFeatures", MapDataKeys.TectonicFeatures, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Elevation, "LayerElevation", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationBase, "LayerElevationBase", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationTectonic, "LayerElevationTectonic", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationRoughness, "LayerElevationRoughness", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationErosion, "LayerElevationErosion", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationZones, "LayerElevationZones", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationMountain, "LayerElevationMountain", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationBasin, "LayerElevationBasin", MapDataKeys.Elevation, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ElevationRivers, "LayerElevationRivers", MapDataKeys.Hydrology, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateBiomesDebug, "LayerClimateBiomesDebug", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateBiomesPresentation, "LayerClimateBiomesPresentation", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateTemperature, "LayerClimateTemperature", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateMoisture, "LayerClimateMoisture", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimatePrecipitation, "LayerClimatePrecipitation", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateSeasonality, "LayerClimateSeasonality", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateHabitability, "LayerClimateHabitability", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateAgriculture, "LayerClimateAgriculture", MapDataKeys.Climate, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.ClimateIce, "LayerClimateIce", MapDataKeys.Climate, Localize));
        SelectedPreviewLayer = PreviewLayers[0];
    }

    private void InitializeSettingsSections()
    {
        SettingsSections.Add(new SettingsSectionViewModel("InputOutput", "ProjectGroup", "InputOutput", SettingsSectionKind.Project, 0, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Generation", "ProjectGroup", "GenerationSection", SettingsSectionKind.Project, 1, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("QualityProfile", "ProjectGroup", "QualityProfile", SettingsSectionKind.Project, 2, Localize));

        SettingsSections.Add(new SettingsSectionViewModel("Mask", "MapGroup", "Mask", SettingsSectionKind.Map, 0, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Relief", "MapGroup", "Relief", SettingsSectionKind.Map, 1, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("WaterRivers", "MapGroup", "WaterRivers", SettingsSectionKind.Map, 2, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Climate", "MapGroup", "Climate", SettingsSectionKind.Map, 3, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Biomes", "MapGroup", "Biomes", SettingsSectionKind.Map, 4, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Regions", "MapGroup", "Regions", SettingsSectionKind.Map, 5, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Export", "MapGroup", "ExportSettings", SettingsSectionKind.Map, 6, Localize));

        SettingsSections.Add(new SettingsSectionViewModel("Tectonics", "AdvancedGroup", "Tectonics", SettingsSectionKind.Advanced, 0, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Erosion", "AdvancedGroup", "Erosion", SettingsSectionKind.Advanced, 1, Localize));
        SettingsSections.Add(new SettingsSectionViewModel("Diagnostics", "AdvancedGroup", "Diagnostics", SettingsSectionKind.Advanced, 2, Localize));

        SelectedSettingsSection = SettingsSections.FirstOrDefault(s => s.Id == "InputOutput");
    }

    private void LoadSettings()
    {
        _suppressDirty = true;
        try
        {
            MaskPath = _settings.LastMaskPath;
            OutputDirectory = string.IsNullOrWhiteSpace(_settings.LastOutputDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MapRegionizer")
                : _settings.LastOutputDirectory;
            SelectedLanguage = LanguageOptions.Contains(_settings.Language) ? _settings.Language : "ru-RU";
            SelectedTheme = ThemeOptions.Contains(_settings.Theme) ? _settings.Theme : "System";
            LoadOptions(_settings.GenerationOptions);
            ExportScale = _settings.ExportScale;
            ExportRegionBorderWidth = _settings.ExportRegionBorderWidth;
            ExportTectonicBoundaryWidth = _settings.ExportTectonicBoundaryWidth;
            ExportDrawCrustPlateBoundaries = _settings.ExportDrawCrustPlateBoundaries;
            ExportDrawFeaturePlateBoundaries = _settings.ExportDrawFeaturePlateBoundaries;
            ExportDrawElevationHillshade = _settings.ExportDrawElevationHillshade;
            ExportDrawElevationPlateBoundaries = _settings.ExportDrawElevationPlateBoundaries;
            ExportDrawClimateHillshade = _settings.ExportDrawClimateHillshade;
            ExportDrawClimateRivers = _settings.ExportDrawClimateRivers;
            ExportDrawClimateRiverValleyAccents = _settings.ExportDrawClimateRiverValleyAccents;
            ShowOnboarding = !_settings.HasCompletedOnboarding;
            SelectedPreviewLayer = PreviewLayers.FirstOrDefault(l => l.Kind.ToString().Equals(_settings.LastPreviewLayer, StringComparison.OrdinalIgnoreCase))
                ?? PreviewLayers[0];
        }
        finally
        {
            _suppressDirty = false;
        }

        _sessionResetRequired = true;
        CurrentPreview = _preview.RenderMask(MaskPath);
        PreviewTitle = MaskFileName;
        PreviewLegend = File.Exists(MaskPath)
            ? $"{L["SelectedMask"]}: {MaskFileName}"
            : L["StatusChooseMask"];
        ApplyTheme(SelectedTheme);
        RaiseThemePaletteChanged();
    }

    private void RefreshPreview()
    {
        try
        {
            var nextPreview = _preview.Render(_workspace.Session, SelectedPreviewLayer);
            if (nextPreview is not null)
            {
                CurrentPreview = nextPreview;
                PreviewTitle = SelectedPreviewLayer?.Name ?? string.Empty;
            }
            else if (CurrentPreview is null && File.Exists(MaskPath))
            {
                CurrentPreview = _preview.RenderMask(MaskPath);
                PreviewTitle = MaskFileName;
            }

            PreviewLegend = SelectedPreviewLayer?.IsAvailable == true
                ? _preview.GetLegend(_localization, SelectedPreviewLayer, true)
                : File.Exists(MaskPath)
                    ? $"{L["SelectedMask"]}: {MaskFileName}"
                    : _preview.GetLegend(_localization, SelectedPreviewLayer, false);
            this.RaisePropertyChanged(nameof(HasPreview));
            RaiseSelectedLayerDetailsChanged();
        }
        catch (Exception ex)
        {
            PreviewLegend = $"{L["LegendNotAvailable"]} {ex.Message}";
            RaiseSelectedLayerDetailsChanged();
        }
    }

    private void RefreshStageStates()
    {
        var session = _workspace.Session;
        foreach (var stage in Stages)
        {
            if (stage.Status == GenerationStageStatus.Running || stage.DataKey is null)
                continue;

            if (session is null || !session.IsAvailable(stage.DataKey.Value))
                stage.Status = GenerationStageStatus.NotStarted;
            else if (session.IsDirty(stage.DataKey.Value))
                stage.Status = GenerationStageStatus.Dirty;
            else
                stage.Status = GenerationStageStatus.Ready;
        }

        this.RaisePropertyChanged(nameof(HasManualRegionDraft));
    }

    private void RefreshLayerAvailability()
    {
        var session = _workspace.Session;
        foreach (var layer in PreviewLayers)
            layer.IsAvailable = session?.Has(layer.RequiredKey) == true;

        RaiseSelectedLayerDetailsChanged();
    }

    private void RefreshStatistics()
    {
        if (_workspace.Session is null)
        {
            Statistics = string.Empty;
            return;
        }

        var map = _workspace.Session.CurrentMap;
        var elevationRange = map.Elevation is null ? string.Empty : $"{Environment.NewLine}{L["Elevation"]}: {GetElevationRange(map.Elevation)} m";
        var lakeSummary = _workspace.Session.GeneratedLakes is null
            ? string.Empty
            : $"{Environment.NewLine}{L["GeneratedLakes"]}: {_workspace.Session.GeneratedLakes.Bodies.Count}";
        var waterSurfaceSummary = _workspace.Session.WaterSurfaces is null
            ? string.Empty
            : $"{Environment.NewLine}{L["WaterSurfaces"]}: {_workspace.Session.WaterSurfaces.Bodies.Count}";
        var riverSummary = _workspace.Session.Hydrology is null
            ? string.Empty
            : $"{Environment.NewLine}{L["Rivers"]}: {_workspace.Session.Hydrology.Rivers.Count}";
        var climateSummary = _workspace.Session.Climate is null
            ? string.Empty
            : $"{Environment.NewLine}{L["Climate"]}: {GetTemperatureRange(_workspace.Session.Climate)} C";
        Statistics =
            $"{L["Size"]}: {map.Bounds.Width:0} x {map.Bounds.Height:0}{Environment.NewLine}" +
            $"{L["Landmasses"]}: {map.Landmasses.Count}{Environment.NewLine}" +
            $"{L["WaterBodies"]}: {map.WaterBodies.Count}{Environment.NewLine}" +
            $"{L["Regions"]}: {map.Regions.Count}{Environment.NewLine}" +
            $"{L["Plates"]}: {map.TectonicPlates?.Plates.Count ?? 0}{Environment.NewLine}" +
            $"{L["Features"]}: {map.TectonicPlates?.Features?.Features.Count ?? 0}" +
            elevationRange +
            lakeSummary +
            waterSurfaceSummary +
            riverSummary +
            climateSummary;
    }

    private static string GetElevationRange(MapRegionizer.Core.Domain.ElevationMap elevation)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var y = 0; y < elevation.Height; y++)
        {
            for (var x = 0; x < elevation.Width; x++)
            {
                var value = elevation.GetElevation(x, y);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return $"{min:0}..{max:0}";
    }

    private static string GetTemperatureRange(MapRegionizer.Core.Domain.ClimateMap climate)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var y = 0; y < climate.Height; y++)
        {
            for (var x = 0; x < climate.Width; x++)
            {
                var value = climate.GetMeanAnnualTemperature(x, y);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return $"{min:0.0}..{max:0.0}";
    }

    private void SelectBestAvailableLayer(MapDataKey key)
    {
        var preferred = key == MapDataKeys.Regions
            ? PreviewLayerKind.Regions
            : key == MapDataKeys.CrustFields
                ? PreviewLayerKind.Crust
            : key == MapDataKeys.PlateDomains || key == MapDataKeys.TectonicBoundaries || key == MapDataKeys.TectonicPlates
                ? PreviewLayerKind.TectonicPlates
            : key == MapDataKeys.TectonicFeatures
                ? PreviewLayerKind.TectonicFeatures
            : key == MapDataKeys.BaseTerrain
                ? PreviewLayerKind.ElevationBase
            : key == MapDataKeys.Hydrology
                ? PreviewLayerKind.ElevationRivers
            : key == MapDataKeys.Climate
                ? PreviewLayerKind.ClimateBiomesPresentation
            : key == MapDataKeys.Elevation || key == MapDataKeys.WaterSurfaces || key == MapDataKeys.GeneratedLakes
                ? PreviewLayerKind.Elevation
                : PreviewLayerKind.Overview;

        var layer = PreviewLayers.FirstOrDefault(l => l.Kind == preferred && l.IsAvailable);
        if (layer is not null)
            SelectedPreviewLayer = layer;
    }

    private void AddHistoryEntry(string? mode = null)
    {
        var title = $"{DateTime.Now:g} - {Path.GetFileName(MaskPath)}";
        var details = $"Seed: {(Seed?.ToString(CultureInfo.CurrentCulture) ?? "random")} | {SelectedPreviewLayer?.Name}";
        if (!string.IsNullOrWhiteSpace(mode))
            details = $"{mode} | {details}";
        History.Insert(0, new RunHistoryEntryViewModel(title, details, OutputDirectory));
        while (History.Count > 8)
            History.RemoveAt(History.Count - 1);
        this.RaisePropertyChanged(nameof(HasHistory));
    }

    private static string FormatArtifactSummary(MapGenerationArtifactPaths artifacts)
    {
        var paths = new List<string> { artifacts.SummaryJson };
        if (artifacts.ClimateBiomesDebugImage is not null)
            paths.Add(artifacts.ClimateBiomesDebugImage);
        if (artifacts.ClimateBiomesPresentationImage is not null)
            paths.Add(artifacts.ClimateBiomesPresentationImage);

        return string.Join(Environment.NewLine, paths);
    }

    private void MarkRunningStageFailed(string error)
    {
        foreach (var stage in Stages.Where(s => s.Status == GenerationStageStatus.Running))
        {
            stage.Status = GenerationStageStatus.Failed;
            stage.Error = error;
        }
    }

    private GenerationStageViewModel? FindStage(MapDataKey key) => Stages.FirstOrDefault(s => s.DataKey == key);

    private void MarkOptionsDirty(params MapDataKey[] dirtyRoots)
    {
        if (_suppressDirty)
            return;

        try
        {
            _workspace.UpdateOptions(BuildOptions(), dirtyRoots);
            RefreshStageStates();
            RefreshLayerAvailability();
            RefreshPreview();
            StatusMessage = L["StatusDirty"];
        }
        catch
        {
            // Validation will surface the details beside the controls and before generation.
        }

        ValidateAll();
        SaveSettings();
    }

    private void SetOption<T>(ref T field, T value, params MapDataKey[] dirtyRoots)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        this.RaiseAndSetIfChanged(ref field, value);
        MarkOptionsDirty(dirtyRoots);
    }

    private void SetOptionNamed<T>(ref T field, T value, string propertyName, params MapDataKey[] dirtyRoots)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        this.RaiseAndSetIfChanged(ref field, value, propertyName);
        MarkOptionsDirty(dirtyRoots);
    }

    private void SetOptionAndReset<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        this.RaiseAndSetIfChanged(ref field, value);
        if (_suppressDirty)
            return;

        _sessionResetRequired = true;
        RefreshStageStates();
        RefreshLayerAvailability();
        StatusMessage = L["StatusSessionReset"];
        ValidateAll();
        SaveSettings();
    }

    private void SetMaskPath(string value)
    {
        if (string.Equals(_maskPath, value, StringComparison.Ordinal))
            return;

        this.RaiseAndSetIfChanged(ref _maskPath, value);
        this.RaisePropertyChanged(nameof(MaskFileName));
        if (_suppressDirty)
            return;

        _sessionResetRequired = true;
        _workspace.Reset();
        CurrentPreview = _preview.RenderMask(MaskPath);
        PreviewTitle = MaskFileName;
        PreviewLegend = File.Exists(MaskPath)
            ? $"{L["SelectedMask"]}: {MaskFileName}"
            : L["StatusChooseMask"];
        RefreshStageStates();
        RefreshLayerAvailability();
        ValidateAll();
        SaveSettings();
    }

    private void SetAndSave<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        this.RaiseAndSetIfChanged(ref field, value);
        ValidateAll();
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_suppressDirty)
            return;

        _settings.Language = SelectedLanguage;
        _settings.Theme = SelectedTheme;
        _settings.LastMaskPath = MaskPath;
        _settings.LastOutputDirectory = OutputDirectory;
        _settings.LastPreviewLayer = SelectedPreviewLayer?.Kind.ToString() ?? PreviewLayerKind.Overview.ToString();
        _settings.HasCompletedOnboarding = !ShowOnboarding;
        _settings.GenerationOptions = BuildOptions();
        _settings.ExportScale = ExportScale;
        _settings.ExportRegionBorderWidth = ExportRegionBorderWidth;
        _settings.ExportTectonicBoundaryWidth = ExportTectonicBoundaryWidth;
        _settings.ExportDrawCrustPlateBoundaries = ExportDrawCrustPlateBoundaries;
        _settings.ExportDrawFeaturePlateBoundaries = ExportDrawFeaturePlateBoundaries;
        _settings.ExportDrawElevationHillshade = ExportDrawElevationHillshade;
        _settings.ExportDrawElevationPlateBoundaries = ExportDrawElevationPlateBoundaries;
        _settings.ExportDrawClimateHillshade = ExportDrawClimateHillshade;
        _settings.ExportDrawClimateRivers = ExportDrawClimateRivers;
        _settings.ExportDrawClimateRiverValleyAccents = ExportDrawClimateRiverValleyAccents;
        _settingsService.Save(_settings);
    }

    private void SavePreferences()
    {
        if (_suppressDirty)
            return;

        _settings.Language = SelectedLanguage;
        _settings.Theme = SelectedTheme;
        _settings.HasCompletedOnboarding = !ShowOnboarding;
        _settingsService.Save(_settings);
    }

    private void RaiseThemePaletteChanged()
    {
        this.RaisePropertyChanged(nameof(IsLightTheme));
        this.RaisePropertyChanged(nameof(AppBackground));
        this.RaisePropertyChanged(nameof(PanelBackground));
        this.RaisePropertyChanged(nameof(CardBackground));
        this.RaisePropertyChanged(nameof(SoftBorder));
        this.RaisePropertyChanged(nameof(PrimaryText));
        this.RaisePropertyChanged(nameof(SecondaryText));
        this.RaisePropertyChanged(nameof(CanvasBackground));
        this.RaisePropertyChanged(nameof(CanvasFooterBackground));
        this.RaisePropertyChanged(nameof(CanvasFooterText));
    }

    private void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(L));
        foreach (var stage in Stages.Concat(FutureStages))
            stage.RefreshLocalization();
        foreach (var layer in PreviewLayers)
            layer.RefreshLocalization();
        foreach (var section in SettingsSections)
            section.RefreshLocalization();
        RefreshPreview();
        RaiseSelectedLayerDetailsChanged();
        ValidateAll();
    }

    private string Localize(string key) => L[key];

    private bool IsSelectedSection(string id) => string.Equals(SelectedSettingsSection?.Id, id, StringComparison.Ordinal);

    private void RaiseSelectedSectionChanged()
    {
        this.RaisePropertyChanged(nameof(IsInputOutputSectionSelected));
        this.RaisePropertyChanged(nameof(IsGenerationSectionSelected));
        this.RaisePropertyChanged(nameof(IsQualityProfileSectionSelected));
        this.RaisePropertyChanged(nameof(IsMaskSectionSelected));
        this.RaisePropertyChanged(nameof(IsReliefSectionSelected));
        this.RaisePropertyChanged(nameof(IsWaterRiversSectionSelected));
        this.RaisePropertyChanged(nameof(IsClimateSectionSelected));
        this.RaisePropertyChanged(nameof(IsBiomesSectionSelected));
        this.RaisePropertyChanged(nameof(IsRegionsSectionSelected));
        this.RaisePropertyChanged(nameof(IsExportSectionSelected));
        this.RaisePropertyChanged(nameof(IsTectonicsSectionSelected));
        this.RaisePropertyChanged(nameof(IsErosionSectionSelected));
        this.RaisePropertyChanged(nameof(IsDiagnosticsSectionSelected));
    }

    private void RaiseSelectedLayerDetailsChanged()
    {
        this.RaisePropertyChanged(nameof(SelectedLayerName));
        this.RaisePropertyChanged(nameof(SelectedLayerStatusText));
        this.RaisePropertyChanged(nameof(SelectedLayerStatusBrush));
        this.RaisePropertyChanged(nameof(SelectedLayerDataKeyText));
        this.RaisePropertyChanged(nameof(CurrentPreviewSizeText));
        this.RaisePropertyChanged(nameof(PreviewLegend));
    }

    private void AddLog(string message, string level = "info")
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        GenerationLog.Insert(0, new GenerationLogEntryViewModel(DateTimeOffset.Now, message, level));
        while (GenerationLog.Count > 200)
            GenerationLog.RemoveAt(GenerationLog.Count - 1);

        this.RaisePropertyChanged(nameof(HasGenerationLog));
    }

    private static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
            return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
