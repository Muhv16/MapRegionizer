namespace AvaloniaRegionizer.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaRegionizer.Services;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using MapRegionizer.Runner;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private bool _isCompareMode;
    private bool _showOnboarding;
    private Bitmap? _currentPreview;
    private Bitmap? _previousPreview;
    private PreviewLayerViewModel? _selectedPreviewLayer;
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
    private bool _generateSmallLakes = true;
    private double _smallLakeCountMultiplier = 0.5;
    private double _smallLakeSizeMultiplier = 0.2;
    private double _riftInfluence = 0.5;
    private bool _preserveMaskCoastline = true;
    private double _maxElevationMeters = 8500;
    private double _minOceanDepthMeters = -7000;
    private double _minLandElevationMeters = 1;
    private double _maxSeaElevationMeters = -1;
    private TectonicPlateJsonExportMode _tectonicJsonMode = TectonicPlateJsonExportMode.Summary;
    private ElevationJsonExportMode _elevationJsonMode = ElevationJsonExportMode.Summary;

    public MainViewModel()
    {
        _settings = _settingsService.Load();

        BrowseMaskCommand = ReactiveCommand.CreateFromTask(BrowseMaskAsync);
        BrowseOutputCommand = ReactiveCommand.CreateFromTask(BrowseOutputAsync);
        RunFullCommand = ReactiveCommand.CreateFromTask(RunFullAsync);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        CancelCommand = ReactiveCommand.Create(CancelGeneration);
        RandomizeSeedCommand = ReactiveCommand.Create(RandomizeSeed);
        CopySeedCommand = ReactiveCommand.CreateFromTask(CopySeedAsync);
        DismissOnboardingCommand = ReactiveCommand.Create(DismissOnboarding);
        ApplyFastPresetCommand = ReactiveCommand.Create(() => ApplyPreset("fast"));
        ApplyBalancedPresetCommand = ReactiveCommand.Create(() => ApplyPreset("balanced"));
        ApplyDetailedPresetCommand = ReactiveCommand.Create(() => ApplyPreset("detailed"));
        ApplyDiagnosticPresetCommand = ReactiveCommand.Create(() => ApplyPreset("diagnostic"));

        InitializeStages();
        InitializePreviewLayers();
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

    public ObservableCollection<GenerationStageViewModel> Stages { get; } = [];
    public ObservableCollection<GenerationStageViewModel> FutureStages { get; } = [];
    public ObservableCollection<PreviewLayerViewModel> PreviewLayers { get; } = [];
    public ObservableCollection<RunHistoryEntryViewModel> History { get; } = [];

    public ReactiveCommand<Unit, Unit> BrowseMaskCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> RunFullCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> RandomizeSeedCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySeedCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissOnboardingCommand { get; }
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
    public bool IsCompareMode { get => _isCompareMode; set => this.RaiseAndSetIfChanged(ref _isCompareMode, value); }
    public bool ShowOnboarding { get => _showOnboarding; set => this.RaiseAndSetIfChanged(ref _showOnboarding, value); }
    public Bitmap? CurrentPreview
    {
        get => _currentPreview;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPreview, value);
            this.RaisePropertyChanged(nameof(HasPreview));
        }
    }

    public Bitmap? PreviousPreview
    {
        get => _previousPreview;
        set
        {
            this.RaiseAndSetIfChanged(ref _previousPreview, value);
            this.RaisePropertyChanged(nameof(HasPreviousPreview));
        }
    }
    public bool HasPreview => CurrentPreview is not null;
    public bool HasPreviousPreview => PreviousPreview is not null;
    public bool HasHistory => History.Count > 0;
    public bool HasArtifacts => !string.IsNullOrWhiteSpace(ArtifactSummary);
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public PreviewLayerViewModel? SelectedPreviewLayer
    {
        get => _selectedPreviewLayer;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPreviewLayer, value);
            RefreshPreview();
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
            SetOption(ref _seed, value, MapDataKeys.RawRegions, MapDataKeys.TectonicHistory, MapDataKeys.Regions);
            this.RaiseAndSetIfChanged(ref _seedText, value?.ToString() ?? string.Empty, nameof(SeedText));
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
            SetOptionNamed(ref _targetArea, value, nameof(TargetArea), MapDataKeys.RawRegions);
            this.RaisePropertyChanged(nameof(TargetAreaSlider));
        }
    }
    public double TargetAreaSlider { get => TargetArea; set => TargetArea = (uint)Math.Max(1, Math.Round(value)); }
    public double PointsMultiplier { get => _pointsMultiplier; set => SetOptionNamed(ref _pointsMultiplier, value, nameof(PointsMultiplier), MapDataKeys.RawRegions); }
    public double MinAreaRatio { get => _minAreaRatio; set => SetOptionNamed(ref _minAreaRatio, value, nameof(MinAreaRatio), MapDataKeys.RawRegions); }
    public double MaxAreaRatio { get => _maxAreaRatio; set => SetOptionNamed(ref _maxAreaRatio, value, nameof(MaxAreaRatio), MapDataKeys.RawRegions); }
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
    public double SmallLakeSizeMultiplier { get => _smallLakeSizeMultiplier; set => SetOption(ref _smallLakeSizeMultiplier, value, MapDataKeys.GeneratedLakes); }
    public double RiftInfluence { get => _riftInfluence; set => SetOption(ref _riftInfluence, value, MapDataKeys.BaseTerrain); }
    public bool PreserveMaskCoastline { get => _preserveMaskCoastline; set => SetOption(ref _preserveMaskCoastline, value, MapDataKeys.BaseTerrain); }
    public double MaxElevationMeters { get => _maxElevationMeters; set => SetOption(ref _maxElevationMeters, value, MapDataKeys.BaseTerrain); }
    public double MinOceanDepthMeters { get => _minOceanDepthMeters; set => SetOption(ref _minOceanDepthMeters, value, MapDataKeys.BaseTerrain); }
    public double MinLandElevationMeters { get => _minLandElevationMeters; set => SetOption(ref _minLandElevationMeters, value, MapDataKeys.BaseTerrain); }
    public double MaxSeaElevationMeters { get => _maxSeaElevationMeters; set => SetOption(ref _maxSeaElevationMeters, value, MapDataKeys.BaseTerrain); }
    public TectonicPlateJsonExportMode TectonicJsonMode { get => _tectonicJsonMode; set => SetAndSave(ref _tectonicJsonMode, value); }
    public ElevationJsonExportMode ElevationJsonMode { get => _elevationJsonMode; set => SetAndSave(ref _elevationJsonMode, value); }

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

        var file = result.FirstOrDefault();
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

        var folder = result.FirstOrDefault();
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
                        }
                    }).GetTask(),
                (key, duration) => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var stage = FindStage(key);
                        if (stage is not null)
                        {
                            stage.Status = GenerationStageStatus.Ready;
                            stage.Duration = duration;
                        }

                        RefreshStageStates();
                        RefreshLayerAvailability();
                        SelectBestAvailableLayer(key);
                        RefreshPreview();
                        RefreshStatistics();
                    }).GetTask(),
                _generationCts.Token);

            StatusMessage = L["Completed"];
            AddHistoryEntry();
            CompleteOnboarding();
            SaveSettings();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L["Ready"];
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
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

    private async Task RunUntilStageAsync(GenerationStageViewModel stage)
    {
        if (stage.DataKey is null || !PrepareForGeneration())
            return;

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        stage.Status = GenerationStageStatus.Running;
        StatusMessage = L["Generating"];

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
                ElevationJsonMode));

            ArtifactSummary = result.Artifacts.SummaryJson;
            StatusMessage = L["StatusExported"];
            this.RaisePropertyChanged(nameof(HasArtifacts));
            AddHistoryEntry();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L["Failed"]}: {ex.Message}";
        }
    }

    private void CancelGeneration() => _generationCts?.Cancel();

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
            await clipboard.SetTextAsync(Seed.Value.ToString());
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
                SmallLakeSizeMultiplier = 0.85;
                TectonicJsonMode = TectonicPlateJsonExportMode.Summary;
                ElevationJsonMode = ElevationJsonExportMode.Summary;
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
                SmallLakeSizeMultiplier = 1.15;
            }
            else if (preset == "diagnostic")
            {
                GenerateSmallLakes = true;
                TectonicJsonMode = TectonicPlateJsonExportMode.CompactDiagnostic;
                ElevationJsonMode = ElevationJsonExportMode.Diagnostic;
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
                SmallLakeSizeMultiplier = 1.0;
                TectonicJsonMode = TectonicPlateJsonExportMode.Summary;
                ElevationJsonMode = ElevationJsonExportMode.Summary;
            }
        }
        finally
        {
            _suppressDirty = false;
        }

        MarkOptionsDirty(MapDataKeys.RawRegions, MapDataKeys.TectonicHistory, MapDataKeys.BaseTerrain, MapDataKeys.GeneratedLakes);
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
                GenerateSmallLakes = GenerateSmallLakes,
                SmallLakeCountMultiplier = SmallLakeCountMultiplier,
                SmallLakeSizeMultiplier = SmallLakeSizeMultiplier,
                RiftInfluence = RiftInfluence,
                PreserveMaskCoastline = PreserveMaskCoastline,
                MaxElevationMeters = MaxElevationMeters,
                MinOceanDepthMeters = MinOceanDepthMeters,
                MinLandElevationMeters = MinLandElevationMeters,
                MaxSeaElevationMeters = MaxSeaElevationMeters
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
            GenerateSmallLakes = options.Elevation.GenerateSmallLakes;
            SmallLakeCountMultiplier = options.Elevation.SmallLakeCountMultiplier;
            SmallLakeSizeMultiplier = options.Elevation.SmallLakeSizeMultiplier;
            RiftInfluence = options.Elevation.RiftInfluence;
            PreserveMaskCoastline = options.Elevation.PreserveMaskCoastline;
            MaxElevationMeters = options.Elevation.MaxElevationMeters;
            MinOceanDepthMeters = options.Elevation.MinOceanDepthMeters;
            MinLandElevationMeters = options.Elevation.MinLandElevationMeters;
            MaxSeaElevationMeters = options.Elevation.MaxSeaElevationMeters;
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
        Stages.Add(CreateStage(MapStageIds.GenerateTectonicPlates, "StageTectonicPlates", MapDataKeys.TectonicPlates));
        Stages.Add(CreateStage(MapStageIds.GenerateRegions, "StageRawRegions", MapDataKeys.RawRegions));
        Stages.Add(CreateStage(MapStageIds.DistortRegionBoundaries, "StageRegions", MapDataKeys.Regions));

        FutureStages.Add(CreateStage("rivers", "Rivers", null));
        FutureStages.Add(CreateStage("climate", "Climate", null));
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
        SelectedPreviewLayer = PreviewLayers[0];
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
                PreviousPreview = CurrentPreview;
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
            this.RaisePropertyChanged(nameof(HasPreviousPreview));
        }
        catch (Exception ex)
        {
            PreviewLegend = $"{L["LegendNotAvailable"]} {ex.Message}";
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
    }

    private void RefreshLayerAvailability()
    {
        var session = _workspace.Session;
        foreach (var layer in PreviewLayers)
            layer.IsAvailable = session?.Has(layer.RequiredKey) == true;
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
        Statistics =
            $"{L["Size"]}: {map.Bounds.Width:0} x {map.Bounds.Height:0}{Environment.NewLine}" +
            $"{L["Landmasses"]}: {map.Landmasses.Count}{Environment.NewLine}" +
            $"{L["WaterBodies"]}: {map.WaterBodies.Count}{Environment.NewLine}" +
            $"{L["Regions"]}: {map.Regions.Count}{Environment.NewLine}" +
            $"{L["Plates"]}: {map.TectonicPlates?.Plates.Count ?? 0}{Environment.NewLine}" +
            $"{L["Features"]}: {map.TectonicPlates?.Features?.Features.Count ?? 0}" +
            elevationRange +
            lakeSummary +
            waterSurfaceSummary;
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
            : key == MapDataKeys.Elevation || key == MapDataKeys.WaterSurfaces || key == MapDataKeys.GeneratedLakes
                ? PreviewLayerKind.Elevation
                : PreviewLayerKind.Overview;

        var layer = PreviewLayers.FirstOrDefault(l => l.Kind == preferred && l.IsAvailable);
        if (layer is not null)
            SelectedPreviewLayer = layer;
    }

    private void AddHistoryEntry()
    {
        var title = $"{DateTime.Now:g} - {Path.GetFileName(MaskPath)}";
        var details = $"Seed: {(Seed?.ToString() ?? "random")} | {SelectedPreviewLayer?.Name}";
        History.Insert(0, new RunHistoryEntryViewModel(title, details, OutputDirectory));
        while (History.Count > 8)
            History.RemoveAt(History.Count - 1);
        this.RaisePropertyChanged(nameof(HasHistory));
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
        PreviousPreview = null;
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
        RefreshPreview();
        ValidateAll();
    }

    private string Localize(string key) => L[key];

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
