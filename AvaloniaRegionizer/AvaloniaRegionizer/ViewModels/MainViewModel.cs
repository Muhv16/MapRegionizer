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
    private bool _isGenerating;
    private bool _isCompareMode;
    private Bitmap? _currentPreview;
    private Bitmap? _previousPreview;
    private PreviewLayerViewModel? _selectedPreviewLayer;
    private string _selectedLanguage = "ru-RU";
    private string _selectedTheme = "System";

    private double _pixelSize = 1;
    private int? _seed;
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
    public ReactiveCommand<Unit, Unit> ApplyFastPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyBalancedPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyDetailedPresetCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyDiagnosticPresetCommand { get; }

    public string MaskPath { get => _maskPath; set => SetAndResetSession(ref _maskPath, value); }
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
    public bool IsGenerating { get => _isGenerating; set => this.RaiseAndSetIfChanged(ref _isGenerating, value); }
    public bool IsCompareMode { get => _isCompareMode; set => this.RaiseAndSetIfChanged(ref _isCompareMode, value); }
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
            SaveSettings();
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTheme, value);
            ApplyTheme(value);
            SaveSettings();
        }
    }

    public double PixelSize { get => _pixelSize; set => SetOptionAndReset(ref _pixelSize, value); }
    public int? Seed { get => _seed; set => SetOption(ref _seed, value, MapDataKeys.RawRegions, MapDataKeys.TectonicHistory, MapDataKeys.Regions); }
    public MapProjectionMode ProjectionMode { get => _projectionMode; set => SetOptionAndReset(ref _projectionMode, value); }
    public double SimplifyTolerance { get => _simplifyTolerance; set => SetOption(ref _simplifyTolerance, value, MapDataKeys.Landmasses); }
    public uint TargetArea { get => _targetArea; set => SetOption(ref _targetArea, value, MapDataKeys.RawRegions); }
    public double PointsMultiplier { get => _pointsMultiplier; set => SetOption(ref _pointsMultiplier, value, MapDataKeys.RawRegions); }
    public double MinAreaRatio { get => _minAreaRatio; set => SetOption(ref _minAreaRatio, value, MapDataKeys.RawRegions); }
    public double MaxAreaRatio { get => _maxAreaRatio; set => SetOption(ref _maxAreaRatio, value, MapDataKeys.RawRegions); }
    public bool BoundaryDistortionEnabled { get => _boundaryDistortionEnabled; set => SetOption(ref _boundaryDistortionEnabled, value, MapDataKeys.Regions); }
    public double BoundaryDetail { get => _boundaryDetail; set => SetOption(ref _boundaryDetail, value, MapDataKeys.Regions); }
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
    public double ReliefScale { get => _reliefScale; set => SetOption(ref _reliefScale, value, MapDataKeys.Elevation); }
    public double Mountaininess { get => _mountaininess; set => SetOption(ref _mountaininess, value, MapDataKeys.Elevation); }
    public double Erosion { get => _erosion; set => SetOption(ref _erosion, value, MapDataKeys.Elevation); }
    public double Roughness { get => _roughness; set => SetOption(ref _roughness, value, MapDataKeys.Elevation); }
    public double SeaDepthScale { get => _seaDepthScale; set => SetOption(ref _seaDepthScale, value, MapDataKeys.Elevation); }
    public double ElevationShelfWidthFactor { get => _elevationShelfWidthFactor; set => SetOption(ref _elevationShelfWidthFactor, value, MapDataKeys.Elevation); }
    public double VolcanismInfluence { get => _volcanismInfluence; set => SetOption(ref _volcanismInfluence, value, MapDataKeys.Elevation); }
    public double RiftInfluence { get => _riftInfluence; set => SetOption(ref _riftInfluence, value, MapDataKeys.Elevation); }
    public bool PreserveMaskCoastline { get => _preserveMaskCoastline; set => SetOption(ref _preserveMaskCoastline, value, MapDataKeys.Elevation); }
    public double MaxElevationMeters { get => _maxElevationMeters; set => SetOption(ref _maxElevationMeters, value, MapDataKeys.Elevation); }
    public double MinOceanDepthMeters { get => _minOceanDepthMeters; set => SetOption(ref _minOceanDepthMeters, value, MapDataKeys.Elevation); }
    public double MinLandElevationMeters { get => _minLandElevationMeters; set => SetOption(ref _minLandElevationMeters, value, MapDataKeys.Elevation); }
    public double MaxSeaElevationMeters { get => _maxSeaElevationMeters; set => SetOption(ref _maxSeaElevationMeters, value, MapDataKeys.Elevation); }
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
                key =>
                {
                    var stage = FindStage(key);
                    if (stage is not null)
                    {
                        stage.Status = GenerationStageStatus.Running;
                        stage.Error = string.Empty;
                    }
                    return Task.CompletedTask;
                },
                (key, duration) =>
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
                    return Task.CompletedTask;
                },
                _generationCts.Token);

            StatusMessage = L["Completed"];
            AddHistoryEntry();
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
            }
            else if (preset == "diagnostic")
            {
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
                TectonicJsonMode = TectonicPlateJsonExportMode.Summary;
                ElevationJsonMode = ElevationJsonExportMode.Summary;
            }
        }
        finally
        {
            _suppressDirty = false;
        }

        MarkOptionsDirty(MapDataKeys.RawRegions, MapDataKeys.TectonicHistory, MapDataKeys.Elevation);
        SaveSettings();
    }

    private bool PrepareForGeneration()
    {
        ValidateAll();
        if (HasValidationMessage)
            return false;

        return true;
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
        Stages.Add(new GenerationStageViewModel(MapStageIds.ExtractLandmasses, "StageLandmasses", MapDataKeys.Landmasses, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.ExtractWaterBodies, "StageWaterBodies", MapDataKeys.WaterBodies, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateTectonicHistory, "StageTectonicHistory", MapDataKeys.TectonicHistory, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateCrustFields, "StageCrustFields", MapDataKeys.CrustFields, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GeneratePlateDomains, "StagePlateDomains", MapDataKeys.PlateDomains, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateTectonicBoundaries, "StageTectonicBoundaries", MapDataKeys.TectonicBoundaries, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateTectonicFeatures, "StageTectonicFeatures", MapDataKeys.TectonicFeatures, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateElevation, "StageElevation", MapDataKeys.Elevation, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateTectonicPlates, "StageTectonicPlates", MapDataKeys.TectonicPlates, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.GenerateRegions, "StageRawRegions", MapDataKeys.RawRegions, Localize, RunUntilStageAsync, RegenerateStageAsync));
        Stages.Add(new GenerationStageViewModel(MapStageIds.DistortRegionBoundaries, "StageRegions", MapDataKeys.Regions, Localize, RunUntilStageAsync, RegenerateStageAsync));

        FutureStages.Add(new GenerationStageViewModel("rivers", "Rivers", null, Localize, RunUntilStageAsync, RegenerateStageAsync) { Status = GenerationStageStatus.Future });
        FutureStages.Add(new GenerationStageViewModel("climate", "Climate", null, Localize, RunUntilStageAsync, RegenerateStageAsync) { Status = GenerationStageStatus.Future });
        FutureStages.Add(new GenerationStageViewModel("resources", "Resources", null, Localize, RunUntilStageAsync, RegenerateStageAsync) { Status = GenerationStageStatus.Future });
    }

    private void InitializePreviewLayers()
    {
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Overview, "LayerOverview", MapDataKeys.Landmasses, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Regions, "LayerRegions", MapDataKeys.Regions, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.TectonicPlates, "LayerTectonicPlates", MapDataKeys.TectonicPlates, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.Crust, "LayerCrust", MapDataKeys.TectonicPlates, Localize));
        PreviewLayers.Add(new PreviewLayerViewModel(PreviewLayerKind.TectonicFeatures, "LayerFeatures", MapDataKeys.TectonicPlates, Localize));
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
            SelectedLanguage = _settings.Language;
            SelectedTheme = _settings.Theme;
            LoadOptions(_settings.GenerationOptions);
            SelectedPreviewLayer = PreviewLayers.FirstOrDefault(l => l.Kind.ToString().Equals(_settings.LastPreviewLayer, StringComparison.OrdinalIgnoreCase))
                ?? PreviewLayers[0];
        }
        finally
        {
            _suppressDirty = false;
        }

        _sessionResetRequired = true;
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
            }

            PreviewLegend = _preview.GetLegend(_localization, SelectedPreviewLayer, SelectedPreviewLayer?.IsAvailable == true);
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
        Statistics =
            $"{L["Size"]}: {map.Bounds.Width:0} x {map.Bounds.Height:0}{Environment.NewLine}" +
            $"{L["Landmasses"]}: {map.Landmasses.Count}{Environment.NewLine}" +
            $"{L["WaterBodies"]}: {map.WaterBodies.Count}{Environment.NewLine}" +
            $"{L["Regions"]}: {map.Regions.Count}{Environment.NewLine}" +
            $"{L["Plates"]}: {map.TectonicPlates?.Plates.Count ?? 0}{Environment.NewLine}" +
            $"{L["Features"]}: {map.TectonicPlates?.Features?.Features.Count ?? 0}" +
            elevationRange;
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
            : key == MapDataKeys.TectonicPlates
                ? PreviewLayerKind.TectonicPlates
                : key == MapDataKeys.Elevation
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

    private void SetAndResetSession<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        this.RaiseAndSetIfChanged(ref field, value);
        if (_suppressDirty)
            return;

        _sessionResetRequired = true;
        _workspace.Reset();
        CurrentPreview = null;
        PreviousPreview = null;
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
        _settings.GenerationOptions = BuildOptions();
        _settingsService.Save(_settings);
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
