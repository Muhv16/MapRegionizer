using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapRegionizer.App.Services;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using MapRegionizer.Core.Spatial;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace MapRegionizer.App.ViewModels;

public enum ManualMapEditorTool
{
    Navigate,
    CreateRegion,
    Select,
    AddGeographicControlPoint
}

public sealed record ManualMapEditorToolOption(string Label, ManualMapEditorTool Tool)
{
    public override string ToString() => Label;
}

public sealed record ManualMapEditorResult(
    ManualMapDraft Draft,
    ManualMapFinalizationResult Finalization,
    bool ApplyBoundaryDistortion,
    MapSpatialOptions SpatialOptions);

public sealed record ManualMapEditorRegionViewModel(int Id, string Label, bool IsSelected);

public sealed record ManualMapDisplayRegion(int Id, string? Name, Polygon Shape);
public sealed record ManualMapControlPointMarker(int Id, string Name, MapPoint Position, bool IsActive);

/// <summary>
/// App-only editor state for manual geography. Core receives only the draft;
/// bitmap/background state never crosses that boundary.
/// </summary>
public sealed class ManualMapEditorViewModel : ReactiveObject, IDisposable
{
    private const int MaxBackgroundPixelDimension = 4096;

    private static readonly JsonSerializerOptions EditorStateOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new MapSpatialReferenceJsonConverter() }
    };

    private readonly ManualMapDraftValidator _validator = new();
    private readonly ManualMapDraftFinalizer _finalizer = new();
    private readonly ManualMapCanvasResizer _canvasResizer = new();
    private readonly ManualMapReprojectionService _reprojectionService = new();
    private readonly ParametricGeoreferenceSolver _georeferenceSolver = new();
    private MapSpatialReference _spatialReference;
    private readonly MapGenerationOptions _options;
    private ManualMapDraftSpatialIndex _spatialIndex;
    private readonly Stack<ManualMapEditorSnapshot> _undo = [];
    private readonly Stack<ManualMapEditorSnapshot> _redo = [];
    private readonly Dictionary<int, MapPoint> _vertexPositions = [];
    private STRtree<ManualMapDisplayRegion> _regionSelectionIndex = new();
    private ManualMapDraft _draft;
    private readonly List<MapPoint> _vertexMarkers = [];
    private readonly HashSet<Bitmap> _backgroundBitmaps = new(ReferenceEqualityComparer.Instance);
    private List<GeographicControlPoint> _controlPoints = [];
    private GeographicCalibrationParameters? _calibrationParameters;
    private GeographicCalibrationResult? _calibrationReport;
    private GeographicControlPointAnchorKind _controlPointAnchorKind = GeographicControlPointAnchorKind.MapUnits;
    private List<int> _currentVertexIds = [];
    private MapPoint? _pointerPreview;
    private int? _snapCandidateVertexId;
    private MapPoint? _snapCandidatePosition;
    private int? _selectedRegionId;
    private ManualMapFinalizationResult? _lastFinalization;
    private ManualMapValidationResult? _cachedDraftValidation;
    private ManualMapDraft? _validatedDraft;
    private MapSpatialReference? _validatedSpatialReference;
    private string _diagnostics = string.Empty;
    private string _validationSummary = string.Empty;
    private ManualMapEditorTool _selectedTool = ManualMapEditorTool.Navigate;
    private bool _applyBoundaryDistortion;
    private string _selectedRegionName = string.Empty;
    private Bitmap? _backgroundImage;
    private string? _backgroundPath;
    private string? _backgroundImageId;
    private bool _isBackgroundVisible = true;
    private bool _isBackgroundLocked;
    private double _backgroundOpacity = .55;
    private double _backgroundScale = 1;
    private double _backgroundMapX;
    private double _backgroundMapY;
    private double _backgroundMapWidth;
    private double _backgroundMapHeight;
    private double _backgroundRotation;
    private BackgroundImagePlacement? _backgroundWarpSourcePlacement;
    private BackgroundReprojectionState? _backgroundReprojectionState;
    private CancellationTokenSource? _backgroundWarpCancellation;
    private int _backgroundWarpVersion;
    private int _waterBodyCount;
    private int _nextVertexId;
    private bool _isValidating;
    private string? _pendingLegacyProjectPath;
    private bool _suppressSpatialConfigurationChanges;
    private CancellationTokenSource? _validationCancellation;
    private bool _disposed;

    public ManualMapEditorViewModel(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        MapGenerationOptions options,
        SpatialConfigurationViewModel spatialConfiguration)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(spatialConfiguration);
        _draft = draft;
        _spatialReference = spatialReference;
        _options = options;
        SpatialConfiguration = spatialConfiguration;
        SpatialConfiguration.PropertyChanged += OnSpatialConfigurationChanged;
        _spatialIndex = new ManualMapDraftSpatialIndex(
            draft,
            Math.Max(spatialReference.UnitsPerCell * 16, RegionGeometryPrecision.LengthTolerance * 4));
        _backgroundMapWidth = spatialReference.WidthInMapUnits;
        _backgroundMapHeight = spatialReference.HeightInMapUnits;
        _nextVertexId = GetNextVertexId(draft);
        _applyBoundaryDistortion = false;

        UndoCommand = ReactiveCommand.Create(Undo, this.WhenAnyValue(vm => vm.CanUndo));
        RedoCommand = ReactiveCommand.Create(Redo, this.WhenAnyValue(vm => vm.CanRedo));
        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateDraftAsync, this.WhenAnyValue(vm => vm.IsValidating, isValidating => !isValidating));
        DeleteRegionCommand = ReactiveCommand.Create(DeleteSelectedRegion, this.WhenAnyValue(vm => vm.HasSelection));
        FitBackgroundCommand = ReactiveCommand.Create(FitBackground);
        RefreshState();
    }

    public int GridWidth => _draft.GridWidth;
    public int GridHeight => _draft.GridHeight;
    public MapSpatialReference SpatialReference => _spatialReference;
    public IReadOnlyList<GeographicControlPoint> ControlPoints => _controlPoints;
    public GeographicCalibrationResult? CalibrationReport => _calibrationReport;
    public IReadOnlyList<ManualMapControlPointMarker> ControlPointMarkers => _controlPoints
        .Select(point => TryGetControlPointDisplayPosition(point) is { } position
            ? new ManualMapControlPointMarker(point.Id, point.Name, position, point.IsActive)
            : null)
        .Where(marker => marker is not null)
        .Select(marker => marker!)
        .ToArray();
    public GeographicCalibrationParameters? CalibrationParameters => _calibrationParameters;
    public IReadOnlyList<GeographicControlPointAnchorKind> ControlPointAnchorKinds { get; } = Enum.GetValues<GeographicControlPointAnchorKind>();
    public GeographicControlPointAnchorKind ControlPointAnchorKind
    {
        get => _controlPointAnchorKind;
        set => this.RaiseAndSetIfChanged(ref _controlPointAnchorKind, value);
    }
    public BackgroundImagePlacement? BackgroundPlacement => BackgroundImage is null
        ? null
        : new BackgroundImagePlacement(BackgroundMapX, BackgroundMapY, BackgroundMapWidth, BackgroundMapHeight, BackgroundRotation);
    public LocalizationService? L => SpatialConfiguration.L;
    public SpatialConfigurationViewModel SpatialConfiguration { get; }
    public MapBounds Bounds => new(_spatialReference.WidthInMapUnits, _spatialReference.HeightInMapUnits, _spatialReference.UnitsPerCell);
    public IReadOnlyList<ManualMapEditorToolOption> Tools { get; } =
    [
        new("Навигация", ManualMapEditorTool.Navigate),
        new("Создать регион", ManualMapEditorTool.CreateRegion),
        new("Выбрать регион", ManualMapEditorTool.Select),
        new("Добавить опорную точку", ManualMapEditorTool.AddGeographicControlPoint)
    ];
    public ManualMapEditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (_selectedTool == value)
                return;
            var hadPointerPreview = _currentVertexIds.Count > 0 && _pointerPreview.HasValue;
            var hadSnapCandidate = _snapCandidatePosition.HasValue || _snapCandidateVertexId.HasValue;
            // Tool changes are forwarded by the window's selector and consumed
            // directly by the canvas. Avoid publishing a synchronous binding
            // notification while ComboBox is completing its selection change.
            _selectedTool = value;
            _pointerPreview = null;
            _snapCandidateVertexId = null;
            _snapCandidatePosition = null;
            if (hadPointerPreview)
                this.RaisePropertyChanged(nameof(CurrentPolygon));
            if (hadSnapCandidate)
            {
                this.RaisePropertyChanged(nameof(SnapCandidatePosition));
                this.RaisePropertyChanged(nameof(SnapCandidateVertexId));
            }
        }
    }

    public ManualMapDraft Draft => _draft;
    public ObservableCollection<ManualMapEditorRegionViewModel> Regions { get; } = [];
    public IReadOnlyList<ManualMapDisplayRegion> DisplayRegions { get; private set; } = [];
    public IReadOnlyList<MapPoint> VertexMarkers => _vertexMarkers;
    public IReadOnlyList<int> CurrentVertexIds => _currentVertexIds;
    public IReadOnlyList<MapPoint> CurrentPolygon
    {
        get
        {
            var points = _currentVertexIds
                .Select(id => _vertexPositions.TryGetValue(id, out var position) ? position : (MapPoint?)null)
                .Where(position => position.HasValue)
                .Select(position => position!.Value)
                .ToList();
            if (_pointerPreview.HasValue && points.Count > 0)
                points.Add(SnapCandidatePosition ?? _pointerPreview.Value);
            return points;
        }
    }

    public MapPoint? SnapCandidatePosition => _snapCandidatePosition;
    public int? SnapCandidateVertexId => _snapCandidateVertexId;
    public int? SelectedRegionId
    {
        get => _selectedRegionId;
        set
        {
            if (_selectedRegionId == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedRegionId, value);
            RefreshSelection();
        }
    }

    public ManualMapEditorRegionViewModel? SelectedRegion
    {
        get => Regions.FirstOrDefault(region => region.Id == SelectedRegionId);
        set
        {
            if (value is not null)
                SelectedRegionId = value.Id;
        }
    }
    public bool HasSelection => SelectedRegionId.HasValue;
    public string SelectedRegionName
    {
        get => _selectedRegionName;
        set
        {
            if (_selectedRegionName == value)
                return;
            _selectedRegionName = value;
            this.RaisePropertyChanged();
            if (!SelectedRegionId.HasValue)
                return;
            var updatedRegions = _draft.Regions.Select(region => region.Id == SelectedRegionId.Value
                ? new ManualRegionFace(region.Id, region.VertexIds, value)
                : region).ToArray();
            PushUndo();
            _draft = new ManualMapDraft(_draft.GridWidth, _draft.GridHeight, _draft.Vertices, updatedRegions);
            DisplayRegions = DisplayRegions
                .Select(region => region.Id == SelectedRegionId.Value ? region with { Name = value } : region)
                .ToArray();
            RefreshRegions(DisplayRegions.ToDictionary(region => region.Id, region => region.Shape));
            _lastFinalization = null;
            this.RaisePropertyChanged(nameof(Draft));
            RefreshEditorProperties(includeDisplayRegions: true);
        }
    }

    public string Diagnostics { get => _diagnostics; private set => this.RaiseAndSetIfChanged(ref _diagnostics, value); }
    public string ValidationSummary { get => _validationSummary; private set => this.RaiseAndSetIfChanged(ref _validationSummary, value); }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool CanFinalize => _lastFinalization?.IsSuccessful == true && _currentVertexIds.Count == 0 && !IsValidating;
    public bool IsLegacyProjectConfirmationVisible => _pendingLegacyProjectPath is not null;
    public bool IsValidating
    {
        get => _isValidating;
        private set => this.RaiseAndSetIfChanged(ref _isValidating, value);
    }
    public bool HasCurrentPolygon => _currentVertexIds.Count > 0;
    public bool ApplyBoundaryDistortion
    {
        get => _applyBoundaryDistortion;
        set => this.RaiseAndSetIfChanged(ref _applyBoundaryDistortion, value);
    }

    public Bitmap? BackgroundImage { get => _backgroundImage; private set => this.RaiseAndSetIfChanged(ref _backgroundImage, value); }
    public string? BackgroundPath { get => _backgroundPath; private set => this.RaiseAndSetIfChanged(ref _backgroundPath, value); }
    public bool IsBackgroundVisible { get => _isBackgroundVisible; set => this.RaiseAndSetIfChanged(ref _isBackgroundVisible, value); }
    public bool IsBackgroundLocked
    {
        get => _isBackgroundLocked;
        set
        {
            if (_isBackgroundLocked == value)
                return;
            this.RaiseAndSetIfChanged(ref _isBackgroundLocked, value);
            this.RaisePropertyChanged(nameof(CanEditBackground));
        }
    }
    public bool CanEditBackground => !IsBackgroundLocked;
    public double BackgroundOpacity { get => _backgroundOpacity; set => this.RaiseAndSetIfChanged(ref _backgroundOpacity, value); }
    public double BackgroundScale
    {
        get => Bounds.Width > 0 ? _backgroundMapWidth / Bounds.Width : _backgroundScale;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;
            _backgroundScale = value;
            _backgroundMapWidth = Bounds.Width * value;
            _backgroundMapHeight = Bounds.Height * value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BackgroundMapWidth));
            this.RaisePropertyChanged(nameof(BackgroundMapHeight));
        }
    }
    public double BackgroundOffsetX
    {
        get => _backgroundMapX;
        set
        {
            if (!double.IsFinite(value) || Math.Abs(_backgroundMapX - value) < 1e-12)
                return;
            _backgroundMapX = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BackgroundMapX));
        }
    }
    public double BackgroundOffsetY
    {
        get => _backgroundMapY;
        set
        {
            if (!double.IsFinite(value) || Math.Abs(_backgroundMapY - value) < 1e-12)
                return;
            _backgroundMapY = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BackgroundMapY));
        }
    }
    public double BackgroundMapX => _backgroundMapX;
    public double BackgroundMapY => _backgroundMapY;
    public double BackgroundMapWidth => _backgroundMapWidth;
    public double BackgroundMapHeight => _backgroundMapHeight;
    public double BackgroundRotation { get => _backgroundRotation; set => this.RaiseAndSetIfChanged(ref _backgroundRotation, value); }
    public BackgroundImagePlacement? BackgroundWarpSourcePlacement => _backgroundWarpSourcePlacement;
    public BackgroundReprojectionState? BackgroundReprojectionState => _backgroundReprojectionState;
    public int WaterBodyCount { get => _waterBodyCount; private set => this.RaiseAndSetIfChanged(ref _waterBodyCount, value); }

    public void ShowDiagnostic(string message) => Diagnostics = message;

    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> ValidateCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteRegionCommand { get; }
    public ReactiveCommand<Unit, Unit> FitBackgroundCommand { get; }

    public ManualCanvasResizeDialogViewModel CreateCanvasResizeDialogViewModel() => new(
        _draft,
        _spatialReference,
        _canvasResizer,
        BackgroundImage,
        IsBackgroundVisible,
        BackgroundOpacity,
        _backgroundMapX,
        _backgroundMapY,
        _backgroundMapWidth,
        _backgroundMapHeight,
        BackgroundRotation);

    public void ApplyCanvasResize(ManualCanvasResizeRequest request)
    {
        if (_disposed)
            return;

        var result = _canvasResizer.Resize(_draft, _spatialReference, request);
        var resizedValidation = _validator.Validate(result.Draft, result.SpatialReference);
        ApplyCanvasResizeResult(result, resizedValidation);
    }

    public async Task ApplyCanvasResizeAsync(
        ManualCanvasResizeResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_disposed)
            return;

        var sourceDraft = _draft;
        var sourceSpatialReference = _spatialReference;
        var cachedValidation = ReferenceEquals(_validatedDraft, sourceDraft) &&
                               ReferenceEquals(_validatedSpatialReference, sourceSpatialReference)
            ? _cachedDraftValidation
            : null;
        var preparedState = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = TranslateCachedValidation(cachedValidation, sourceSpatialReference, result, cancellationToken)
                             ?? _validator.Validate(result.Draft, result.SpatialReference, cancellationToken);
            var spatialIndex = new ManualMapDraftSpatialIndex(
                result.Draft,
                Math.Max(result.SpatialReference.UnitsPerCell * 16, RegionGeometryPrecision.LengthTolerance * 4),
                cancellationToken);
            return (Validation: validation, SpatialIndex: spatialIndex);
        }, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
                return;
            if (!ReferenceEquals(_draft, sourceDraft) || !ReferenceEquals(_spatialReference, sourceSpatialReference))
                throw new InvalidOperationException("Карта изменилась во время изменения размера. Запустите действие ещё раз.");

            ApplyCanvasResizeResult(result, preparedState.Validation, preparedState.SpatialIndex);
        });
    }

    private void ApplyCanvasResizeResult(
        ManualCanvasResizeResult result,
        ManualMapValidationResult resizedValidation,
        ManualMapDraftSpatialIndex? preparedSpatialIndex = null)
    {
        _validationCancellation?.Cancel();
        PushUndo();
        _draft = result.Draft;
        _spatialReference = result.SpatialReference;
        _backgroundMapX += result.Request.LeftCells * result.SpatialReference.UnitsPerCell;
        _backgroundMapY += result.Request.TopCells * result.SpatialReference.UnitsPerCell;
        var resizeOffsetX = result.Request.LeftCells * result.SpatialReference.UnitsPerCell;
        var resizeOffsetY = result.Request.TopCells * result.SpatialReference.UnitsPerCell;
        _controlPoints = _controlPoints.Select(point => point.AnchorKind switch
        {
            GeographicControlPointAnchorKind.MapUnits when point.MapPosition is { } position => point with
            {
                MapPosition = new MapPoint(position.X + resizeOffsetX, position.Y + resizeOffsetY),
                SourceReferenceAtCapture = result.SpatialReference
            },
            GeographicControlPointAnchorKind.BackgroundImageUv when point.BackgroundPlacementAtCapture is { } placement => point with
            {
                BackgroundPlacementAtCapture = placement with { X = placement.X + resizeOffsetX, Y = placement.Y + resizeOffsetY },
                SourceReferenceAtCapture = result.SpatialReference
            },
            _ => point
        }).ToList();
        _currentVertexIds = _currentVertexIds
            .Where(id => _draft.Vertices.Any(vertex => vertex.Id == id))
            .ToList();
        _selectedRegionId = _draft.Regions.Any(region => region.Id == _selectedRegionId) ? _selectedRegionId : null;

        _suppressSpatialConfigurationChanges = true;
        try
        {
            SpatialConfiguration.LoadSpatialOptions(ToSpatialOptions(_spatialReference));
        }
        finally
        {
            _suppressSpatialConfigurationChanges = false;
        }

        _nextVertexId = GetNextVertexId(_draft);
        ClearPointerPreview();
        _lastFinalization = null;
        ValidationSummary = string.Empty;
        WaterBodyCount = 0;
        Diagnostics = "Размер холста изменён. Повторно проверьте карту перед формированием.";
        RefreshState(resizedValidation, preparedSpatialIndex);
        this.RaisePropertyChanged(nameof(GridWidth));
        this.RaisePropertyChanged(nameof(GridHeight));
        this.RaisePropertyChanged(nameof(Bounds));
        RaiseBackgroundTransformProperties();
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
    }

    public void HandlePointer(MapPoint point, double hitTolerance)
    {
        if (SelectedTool == ManualMapEditorTool.AddGeographicControlPoint)
        {
            AddGeographicControlPoint(point);
            return;
        }

        if (SelectedTool == ManualMapEditorTool.Select)
        {
            SelectRegion(point);
            return;
        }

        if (SelectedTool != ManualMapEditorTool.CreateRegion)
            return;

        var snap = FindSnap(point, Math.Max(hitTolerance, RegionGeometryPrecision.LengthTolerance));
        if (_currentVertexIds.Count >= 3 && snap.VertexId == _currentVertexIds[0])
        {
            CommitCurrentRegion();
            return;
        }

        if (snap.VertexId is { } vertexId)
        {
            if (_currentVertexIds.Contains(vertexId))
            {
                Diagnostics = "Эта вершина уже есть в текущем polygon.";
                return;
            }

            _currentVertexIds.Add(vertexId);
            ClearPointerPreview();
            RefreshEditorProperties();
            return;
        }

        if (snap.Edge is { } edge)
        {
            var newVertexId = NextVertexId();
            if (!ManualMapDraftTopology.TrySplitEdge(_draft, edge.StartVertexId, edge.EndVertexId, newVertexId, edge.Position, out var split, out var diagnostic))
            {
                Diagnostics = diagnostic?.Message ?? "Не удалось разделить общую грань.";
                return;
            }

            PushUndo();
            _draft = split!;
            _currentVertexIds.Add(newVertexId);
            ClearPointerPreview();
            RefreshInteractiveState(rebuildSpatialIndex: true);
            return;
        }

        var freeVertexId = NextVertexId();
        var freeVertex = new ManualMapVertex(freeVertexId, Canonicalize(point));
        PushUndo();
        _draft = new ManualMapDraft(
            _draft.GridWidth,
            _draft.GridHeight,
            _draft.Vertices.Append(freeVertex).ToArray(),
            _draft.Regions);
        _spatialIndex.AddVertex(freeVertex);
        _vertexPositions[freeVertex.Id] = freeVertex.Position;
        _vertexMarkers.Add(freeVertex.Position);
        _currentVertexIds.Add(freeVertexId);
        ClearPointerPreview();
        RefreshInteractiveState();
    }

    public void AddGeographicControlPoint(MapPoint mapPosition)
    {
        if (!double.IsFinite(mapPosition.X) || !double.IsFinite(mapPosition.Y))
        {
            Diagnostics = "Опорная точка должна находиться в конечных координатах карты.";
            return;
        }

        GeographicControlPoint point;
        if (ControlPointAnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv)
        {
            if (BackgroundImage is null)
            {
                Diagnostics = "Сначала загрузите фоновое изображение, чтобы размещать на нём опорные точки.";
                return;
            }
            if (!string.Equals(_backgroundImageId, GetBackgroundImageId(BackgroundPath), StringComparison.Ordinal))
            {
                Diagnostics = "Фоновый файл изменился. Загрузите его заново, чтобы добавить опорную точку.";
                return;
            }
            double u;
            double v;
            var sourcePlacement = _backgroundReprojectionState?.SourcePlacement ?? _backgroundWarpSourcePlacement ?? BackgroundPlacement;
            if (sourcePlacement is null)
            {
                Diagnostics = "Не удалось определить положение фонового изображения.";
                return;
            }
            try
            {
                var backgroundMapPosition = mapPosition;
                if (_backgroundReprojectionState is { } backgroundState)
                {
                    if (backgroundState.TargetReference != _spatialReference)
                        throw new InvalidOperationException("Сначала перепроецируйте подложку на текущую сетку.");
                    var targetReference = backgroundState.TargetReference;
                    var targetGeographic = MapSpatialContext.Create(targetReference).GridToGeographic(new GridCoordinate(
                        mapPosition.X / targetReference.UnitsPerCell,
                        mapPosition.Y / targetReference.UnitsPerCell));
                    if (backgroundState.CalibrationParameters is { } backgroundCalibration)
                    {
                        backgroundMapPosition = ParametricGeoreferenceSolver
                            .CreateTransform(backgroundState.SourceReference, backgroundCalibration)
                            .ToSourceMap(targetGeographic);
                    }
                    else
                    {
                        var sourceGeographic = targetGeographic with
                        {
                            LongitudeDegrees = LongitudeInterval.Normalize(targetGeographic.LongitudeDegrees - backgroundState.SourceLongitudeOffsetDegrees)
                        };
                        var sourceGrid = MapSpatialContext.Create(backgroundState.SourceReference).GeographicToGrid(sourceGeographic);
                        backgroundMapPosition = new MapPoint(
                            sourceGrid.X * backgroundState.SourceReference.UnitsPerCell,
                            sourceGrid.Y * backgroundState.SourceReference.UnitsPerCell);
                    }
                }
                else if (_calibrationParameters?.IsAppliedToBackground == true)
                {
                    var sourceReference = _calibrationParameters.SourceReference ?? _spatialReference;
                    var transform = ParametricGeoreferenceSolver.CreateTransform(sourceReference, _calibrationParameters);
                    backgroundMapPosition = transform.ToSourceMap(CurrentGeographicPosition(mapPosition));
                }
                (u, v) = sourcePlacement.ToImageUv(backgroundMapPosition);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                Diagnostics = $"Не удалось связать точку с исходным изображением: {exception.Message}";
                return;
            }
            point = new GeographicControlPoint(
                NextControlPointId(),
                $"GCP {_controlPoints.Count + 1}",
                GeographicControlPointAnchorKind.BackgroundImageUv,
                GeographicControlPointRole.Calibration,
                new GeoCoordinate(0, 0),
                BackgroundU: u,
                BackgroundV: v,
                BackgroundPlacementAtCapture: sourcePlacement,
                BackgroundImageId: _backgroundImageId,
                SourceReferenceAtCapture: _backgroundReprojectionState?.SourceReference ?? _calibrationParameters?.SourceReference ?? _spatialReference);
        }
        else
        {
            if (mapPosition.X < 0 || mapPosition.X > _spatialReference.WidthInMapUnits ||
                mapPosition.Y < 0 || mapPosition.Y > _spatialReference.HeightInMapUnits)
            {
                Diagnostics = "Опорную точку геометрии нужно разместить внутри холста карты.";
                return;
            }
            point = new GeographicControlPoint(
                NextControlPointId(),
                $"GCP {_controlPoints.Count + 1}",
                GeographicControlPointAnchorKind.MapUnits,
                GeographicControlPointRole.Calibration,
                CurrentGeographicPosition(mapPosition),
                MapPosition: mapPosition,
                SourceReferenceAtCapture: _spatialReference);
        }

        PushUndo();
        _controlPoints.Add(point);
        if (_calibrationParameters is not { IsApplied: true } and not { IsAppliedToBackground: true })
            _calibrationParameters = null;
        _calibrationReport = null;
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        Diagnostics = $"Добавлена опорная точка {point.Name}. Укажите её реальные широту и долготу в окне географической привязки.";
    }

    public void UpdateControlPoints(IEnumerable<GeographicControlPoint> controlPoints)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        var updated = controlPoints.OrderBy(point => point.Id).ToList();
        if (updated.Any(point => point.Id <= 0) || updated.Select(point => point.Id).Distinct().Count() != updated.Count)
            throw new ArgumentException("Идентификаторы опорных точек должны быть положительными и уникальными.", nameof(controlPoints));
        PushUndo();
        _controlPoints = updated;
        if (_calibrationParameters is not { IsApplied: true } and not { IsAppliedToBackground: true })
            _calibrationParameters = null;
        _calibrationReport = null;
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        Diagnostics = "Сеть опорных точек изменена. Рассчитайте модель заново.";
    }

    public void SetCalibrationParameters(GeographicCalibrationParameters? parameters)
    {
        PushUndo();
        _calibrationParameters = parameters;
        _calibrationReport = null;
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
    }

    public void SaveGeoreferencingData(
        IEnumerable<GeographicControlPoint> controlPoints,
        GeographicCalibrationParameters? parameters,
        GeographicCalibrationResult? calibrationReport = null)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        var updated = controlPoints.OrderBy(point => point.Id).ToList();
        if (updated.Any(point => point.Id <= 0) || updated.Select(point => point.Id).Distinct().Count() != updated.Count)
            throw new ArgumentException("Идентификаторы опорных точек должны быть положительными и уникальными.", nameof(controlPoints));
        PushUndo();
        _controlPoints = updated;
        _calibrationParameters = parameters;
        _calibrationReport = calibrationReport;
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
    }

    public GeographicCalibrationResult SolveCalibration(
        MapSpatialReference? sourceReference = null,
        IReadOnlyList<GeographicControlPoint>? controlPoints = null)
    {
        var result = _georeferenceSolver.Solve(new GeographicCalibrationRequest(
            sourceReference ?? _spatialReference,
            controlPoints ?? _controlPoints,
            BackgroundPlacement));
        if (result.IsSuccessful && result.Parameters is not null)
        {
            PushUndo();
            _calibrationParameters = result.Parameters;
            _calibrationReport = result;
            this.RaisePropertyChanged(nameof(CalibrationParameters));
            this.RaisePropertyChanged(nameof(CalibrationReport));
        }
        return result;
    }

    public async Task ApplyReprojectionAsync(
        ManualMapReprojectionRequest request,
        ManualMapReprojectionResult result,
        IReadOnlyList<GeographicControlPoint>? controlPointsToSave = null,
        GeographicCalibrationParameters? calibrationParameters = null,
        byte[]? reprojectedBackgroundPng = null,
        BackgroundImagePlacement? sourceBackgroundPlacement = null,
        GeographicCalibrationResult? calibrationReport = null,
        BackgroundReprojectionState? backgroundReprojectionState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsSuccessful || result.Draft is null)
            throw new InvalidOperationException("Предпросмотр перепроецирования не прошёл геометрическую проверку.");
        if (!ReferenceEquals(_draft, request.Draft))
            throw new InvalidOperationException("Карта изменилась во время предпросмотра. Рассчитайте его заново.");

        var sourceDraft = _draft;
        var prepared = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = _validator.Validate(result.Draft, result.TargetReference, cancellationToken);
            var finalization = result.Finalization ?? _finalizer.FinalizeDraft(result.Draft, result.TargetReference, cancellationToken);
            var spatialIndex = new ManualMapDraftSpatialIndex(
                result.Draft,
                Math.Max(result.TargetReference.UnitsPerCell * 16, RegionGeometryPrecision.LengthTolerance * 4),
                cancellationToken);
            return (Validation: validation, Finalization: finalization, SpatialIndex: spatialIndex);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!prepared.Validation.IsSuccessful || !prepared.Finalization.IsSuccessful)
            throw new InvalidOperationException("Результат перепроецирования не прошёл проверку или финализацию.");
        if (!ReferenceEquals(_draft, sourceDraft))
            throw new InvalidOperationException("Карта изменилась во время проверки. Рассчитайте перепроецирование заново.");

        CancelBackgroundWarp();
        var updatedControlPoints = ReprojectControlPoints(controlPointsToSave ?? _controlPoints, request, result.TargetReference);
        Bitmap? reprojectedBackground = null;
        if (reprojectedBackgroundPng is not null)
        {
            if (sourceBackgroundPlacement is null || string.IsNullOrWhiteSpace(BackgroundPath))
                throw new InvalidOperationException("The source background image and its placement are required to apply a background reprojection.");
            if (!string.Equals(_backgroundImageId, GetBackgroundImageId(BackgroundPath), StringComparison.Ordinal))
                throw new InvalidOperationException("The source background image changed. Reload it before applying this reprojection.");
            backgroundReprojectionState ??= new BackgroundReprojectionState(
                calibrationParameters?.SourceReference ?? request.SourceReference,
                result.TargetReference,
                sourceBackgroundPlacement,
                request.SourceLongitudeOffsetDegrees,
                calibrationParameters);
            backgroundReprojectionState = backgroundReprojectionState with { SourceImageId = _backgroundImageId };
            backgroundReprojectionState.SourceReference.Validate();
            backgroundReprojectionState.TargetReference.Validate();
            backgroundReprojectionState.SourcePlacement.Validate();
            using var stream = new MemoryStream(reprojectedBackgroundPng);
            reprojectedBackground = new Bitmap(stream);
        }

        _validationCancellation?.Cancel();
        PushUndo();
        _draft = result.Draft;
        _spatialReference = result.TargetReference;
        _calibrationParameters = calibrationParameters ?? _calibrationParameters;
        _calibrationReport = calibrationReport ?? _calibrationReport;
        _controlPoints = updatedControlPoints.Select(point => point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv && reprojectedBackground is null
            ? point with { IsActive = false }
            : point).ToList();
        if (request.GeoreferenceTransform is ParametricGeoreferenceTransform && _calibrationParameters is not null)
            _calibrationParameters = _calibrationParameters with { IsApplied = true };
        if (reprojectedBackground is not null)
        {
            _backgroundBitmaps.Add(reprojectedBackground);
            BackgroundImage = reprojectedBackground;
            _backgroundWarpSourcePlacement = backgroundReprojectionState!.SourcePlacement;
            _backgroundMapX = 0;
            _backgroundMapY = 0;
            _backgroundMapWidth = _spatialReference.WidthInMapUnits;
            _backgroundMapHeight = _spatialReference.HeightInMapUnits;
            _backgroundScale = 1;
            BackgroundRotation = 0;
            if (calibrationParameters is not null)
                _calibrationParameters = _calibrationParameters! with { IsAppliedToBackground = true };
            else if (_calibrationParameters is not null)
                _calibrationParameters = _calibrationParameters with { IsAppliedToBackground = backgroundReprojectionState!.CalibrationParameters is not null };
            _backgroundReprojectionState = backgroundReprojectionState! with
            {
                CalibrationParameters = calibrationParameters is null ? backgroundReprojectionState.CalibrationParameters : _calibrationParameters
            };
        }
        if (_calibrationReport is not null && _calibrationParameters is not null)
            _calibrationReport = _calibrationReport with { Parameters = _calibrationParameters };
        _suppressSpatialConfigurationChanges = true;
        try
        {
            SpatialConfiguration.LoadSpatialOptions(ToSpatialOptions(_spatialReference));
        }
        finally
        {
            _suppressSpatialConfigurationChanges = false;
        }
        _nextVertexId = GetNextVertexId(_draft);
        _selectedRegionId = _draft.Regions.Any(region => region.Id == _selectedRegionId) ? _selectedRegionId : null;
        ClearPointerPreview();
        _lastFinalization = null;
        ValidationSummary = string.Empty;
        WaterBodyCount = 0;
        var inactiveBackgroundPoints = _controlPoints.Count(point => point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv && !point.IsActive);
        Diagnostics = $"Карта перепроецирована. Добавлено общих вершин: {result.AddedVertices}. " +
            (reprojectedBackground is null
                ? $"Фон не перепроецирован; {inactiveBackgroundPoints} точек фона отключены до привязки. "
                : "Фоновое изображение преобразовано отдельно. ") +
            "Проверьте результат перед формированием.";
        RefreshState(prepared.Validation, prepared.SpatialIndex);
        this.RaisePropertyChanged(nameof(Draft));
        this.RaisePropertyChanged(nameof(GridWidth));
        this.RaisePropertyChanged(nameof(GridHeight));
        this.RaisePropertyChanged(nameof(SpatialReference));
        this.RaisePropertyChanged(nameof(Bounds));
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        RaiseBackgroundTransformProperties();
    }

    public void ApplyBackgroundReprojection(
        byte[] reprojectedBackgroundPng,
        BackgroundImagePlacement sourceBackgroundPlacement,
        GeographicCalibrationParameters parameters,
        IReadOnlyList<GeographicControlPoint>? controlPoints = null,
        GeographicCalibrationResult? calibrationReport = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ApplyBackgroundReprojection(
            reprojectedBackgroundPng,
            new BackgroundReprojectionState(
                parameters.SourceReference ?? _spatialReference,
                _spatialReference,
                sourceBackgroundPlacement,
                CalibrationParameters: parameters),
            controlPoints,
            calibrationReport);
    }

    public void ApplyBackgroundReprojection(
        byte[] reprojectedBackgroundPng,
        BackgroundReprojectionState reprojectionState,
        IReadOnlyList<GeographicControlPoint>? controlPoints = null,
        GeographicCalibrationResult? calibrationReport = null)
    {
        ArgumentNullException.ThrowIfNull(reprojectedBackgroundPng);
        ArgumentNullException.ThrowIfNull(reprojectionState);
        reprojectionState.SourceReference.Validate();
        reprojectionState.TargetReference.Validate();
        reprojectionState.SourcePlacement.Validate();
        if (string.IsNullOrWhiteSpace(BackgroundPath))
            throw new InvalidOperationException("Load and save a source background image before reprojecting it.");
        if (!string.Equals(_backgroundImageId, GetBackgroundImageId(BackgroundPath), StringComparison.Ordinal))
            throw new InvalidOperationException("The source background image changed. Reload it before applying this reprojection.");
        CancelBackgroundWarp();
        using var stream = new MemoryStream(reprojectedBackgroundPng);
        var image = new Bitmap(stream);
        PushUndo();
        _backgroundBitmaps.Add(image);
        BackgroundImage = image;
        _backgroundWarpSourcePlacement = reprojectionState.SourcePlacement;
        reprojectionState = reprojectionState with { SourceImageId = _backgroundImageId };
        _backgroundReprojectionState = reprojectionState;
        _backgroundMapX = 0;
        _backgroundMapY = 0;
        _backgroundMapWidth = reprojectionState.TargetReference.WidthInMapUnits;
        _backgroundMapHeight = reprojectionState.TargetReference.HeightInMapUnits;
        _backgroundScale = 1;
        BackgroundRotation = 0;
        var appliedCalibrationParameters = reprojectionState.CalibrationParameters is { } parameters
            ? parameters with { IsAppliedToBackground = true }
            : null;
        _backgroundReprojectionState = reprojectionState with { CalibrationParameters = appliedCalibrationParameters };
        _calibrationParameters = appliedCalibrationParameters ?? (_calibrationParameters is { } previousParameters
            ? previousParameters with { IsAppliedToBackground = false }
            : null);
        if (controlPoints is not null)
            _controlPoints = controlPoints.OrderBy(point => point.Id).ToList();
        _calibrationReport = calibrationReport is not null && appliedCalibrationParameters is not null
            ? calibrationReport with { Parameters = appliedCalibrationParameters }
            : calibrationReport;
        RaiseBackgroundTransformProperties();
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        Diagnostics = "Подложка перепроецирована. Геометрия регионов не изменена.";
    }

    private GeoCoordinate CurrentGeographicPosition(MapPoint mapPosition)
    {
        var geographic = MapSpatialContext.Create(_spatialReference).GridToGeographic(new GridCoordinate(
            mapPosition.X / _spatialReference.UnitsPerCell,
            mapPosition.Y / _spatialReference.UnitsPerCell));
        return geographic with { LongitudeDegrees = LongitudeInterval.Normalize(geographic.LongitudeDegrees) };
    }

    private MapPoint? TryGetControlPointDisplayPosition(GeographicControlPoint point)
    {
        if (point.AnchorKind == GeographicControlPointAnchorKind.MapUnits)
            return point.MapPosition;
        if (point.BackgroundU is not { } u || point.BackgroundV is not { } v ||
            (point.BackgroundPlacementAtCapture ?? _backgroundReprojectionState?.SourcePlacement ?? _backgroundWarpSourcePlacement ?? BackgroundPlacement) is not { } placement)
            return null;
        try
        {
            var sourcePosition = placement.ToMapPoint(u, v);
            if (_backgroundReprojectionState is { } backgroundState)
            {
                if (backgroundState.TargetReference != _spatialReference)
                    return null;
                GeoCoordinate sourceGeographic;
                if (backgroundState.CalibrationParameters is { } backgroundCalibration)
                    sourceGeographic = ParametricGeoreferenceSolver.CreateTransform(backgroundState.SourceReference, backgroundCalibration).ToUnwrappedGeographic(sourcePosition);
                else
                {
                    sourceGeographic = MapSpatialContext.Create(backgroundState.SourceReference).GridToGeographic(new GridCoordinate(
                        sourcePosition.X / backgroundState.SourceReference.UnitsPerCell,
                        sourcePosition.Y / backgroundState.SourceReference.UnitsPerCell));
                    sourceGeographic = sourceGeographic with
                    {
                        LongitudeDegrees = sourceGeographic.LongitudeDegrees + backgroundState.SourceLongitudeOffsetDegrees
                    };
                }
                var targetGrid = MapSpatialContext.Create(backgroundState.TargetReference).GeographicToGrid(sourceGeographic);
                return new MapPoint(
                    targetGrid.X * backgroundState.TargetReference.UnitsPerCell,
                    targetGrid.Y * backgroundState.TargetReference.UnitsPerCell);
            }
            if (_calibrationParameters?.IsAppliedToBackground == true)
            {
                var sourceReference = _calibrationParameters.SourceReference ?? _spatialReference;
                var transform = ParametricGeoreferenceSolver.CreateTransform(sourceReference, _calibrationParameters);
                var geographic = transform.ToUnwrappedGeographic(sourcePosition);
                var targetGrid = MapSpatialContext.Create(_spatialReference).GeographicToGrid(geographic);
                return new MapPoint(targetGrid.X * _spatialReference.UnitsPerCell, targetGrid.Y * _spatialReference.UnitsPerCell);
            }
            return sourcePosition;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private int NextControlPointId() => GetNextControlPointId(_controlPoints);

    private static List<GeographicControlPoint> ReprojectControlPoints(
        IReadOnlyList<GeographicControlPoint> controlPoints,
        ManualMapReprojectionRequest request,
        MapSpatialReference targetReference)
    {
        var sourceContext = MapSpatialContext.Create(request.SourceReference);
        var targetContext = MapSpatialContext.Create(targetReference);
        var longitudeCycleOffset = ManualMapReprojectionService.GetSourceLongitudeCycleOffset(request);
        return controlPoints.Select(point =>
        {
            if (point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv)
                return point;
            if (point.MapPosition is not { } sourcePosition)
                return point;
            var geographic = request.GeoreferenceTransform?.ToUnwrappedGeographic(sourcePosition)
                ?? sourceContext.GridToGeographic(new GridCoordinate(
                    sourcePosition.X / request.SourceReference.UnitsPerCell,
                    sourcePosition.Y / request.SourceReference.UnitsPerCell));
            geographic = geographic with
            {
                LongitudeDegrees = geographic.LongitudeDegrees + request.SourceLongitudeOffsetDegrees - longitudeCycleOffset
            };
            var targetGrid = targetContext.GeographicToGrid(geographic);
            return point with
            {
                MapPosition = new MapPoint(
                    targetGrid.X * targetReference.UnitsPerCell,
                    targetGrid.Y * targetReference.UnitsPerCell),
                SourceReferenceAtCapture = targetReference
            };
        }).ToList();
    }

    public void UpdatePointerPreview(MapPoint point, double hitTolerance)
    {
        if (_currentVertexIds.Count == 0)
            return;

        var nextPointerPreview = SelectedTool == ManualMapEditorTool.CreateRegion ? (MapPoint?)point : null;
        var snap = SelectedTool == ManualMapEditorTool.CreateRegion
            ? FindSnap(point, Math.Max(hitTolerance, RegionGeometryPrecision.LengthTolerance))
            : default;
        var pointerChanged = _pointerPreview != nextPointerPreview;
        var snapChanged = _snapCandidateVertexId != snap.VertexId || _snapCandidatePosition != snap.Position;
        _pointerPreview = nextPointerPreview;
        _snapCandidateVertexId = snap.VertexId;
        _snapCandidatePosition = snap.Position;

        if (pointerChanged && _currentVertexIds.Count > 0)
            this.RaisePropertyChanged(nameof(CurrentPolygon));
        if (snapChanged)
        {
            this.RaisePropertyChanged(nameof(SnapCandidatePosition));
            this.RaisePropertyChanged(nameof(SnapCandidateVertexId));
            if (!pointerChanged && _currentVertexIds.Count > 0)
                this.RaisePropertyChanged(nameof(CurrentPolygon));
        }
    }

    public void CompleteCurrentRegion()
    {
        if (_currentVertexIds.Count == 0)
            return;
        CommitCurrentRegion();
    }

    public void CancelCurrentRegion()
    {
        _currentVertexIds.Clear();
        ClearPointerPreview();
        Diagnostics = string.Empty;
        RefreshEditorProperties();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _validationCancellation?.Cancel();
        CancelBackgroundWarp();
        SpatialConfiguration.PropertyChanged -= OnSpatialConfigurationChanged;
        BackgroundImage = null;
        foreach (var image in _backgroundBitmaps)
            image.Dispose();
        _backgroundBitmaps.Clear();
    }

    public void RemoveLastCurrentVertex()
    {
        if (_currentVertexIds.Count == 0)
            return;
        _currentVertexIds.RemoveAt(_currentVertexIds.Count - 1);
        ClearPointerPreview();
        RefreshEditorProperties();
    }

    public void SelectRegionAt(MapPoint point) => SelectRegion(point);

    public void LoadBackground(string path)
    {
        if (_disposed)
            return;

        CancelBackgroundWarp();
        var sourceId = GetBackgroundImageId(path);
        var sourceImageChanged = BackgroundPath is not null &&
            !string.Equals(GetBackgroundImageId(BackgroundPath), sourceId, StringComparison.Ordinal);
        var stalePoints = _controlPoints.Any(point =>
            point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv && point.IsActive &&
            !string.Equals(point.BackgroundImageId, sourceId, StringComparison.Ordinal));
        if (stalePoints || sourceImageChanged)
        {
            PushUndo();
            _controlPoints = _controlPoints.Select(point => point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv &&
                (sourceImageChanged || !string.Equals(point.BackgroundImageId, sourceId, StringComparison.Ordinal))
                ? point with { IsActive = false }
                : point).ToList();
            if (_calibrationParameters?.IsAppliedToBackground == true)
                _calibrationParameters = _calibrationParameters with { IsAppliedToBackground = false };
            _backgroundWarpSourcePlacement = null;
            _backgroundReprojectionState = null;
            this.RaisePropertyChanged(nameof(ControlPoints));
            this.RaisePropertyChanged(nameof(ControlPointMarkers));
            this.RaisePropertyChanged(nameof(CalibrationParameters));
            this.RaisePropertyChanged(nameof(CalibrationReport));
        }

        LoadBackgroundImage(path);
        FitBackground();
        if (_backgroundReprojectionState is not null)
            QueueBackgroundWarpRestore(_backgroundReprojectionState);
        else if (_calibrationParameters?.IsAppliedToBackground == true && _backgroundWarpSourcePlacement is not null)
            QueueBackgroundWarpRestore(new BackgroundReprojectionState(
                _calibrationParameters.SourceReference ?? _spatialReference,
                _spatialReference,
                _backgroundWarpSourcePlacement,
                CalibrationParameters: _calibrationParameters));
        else if (stalePoints || sourceImageChanged)
            Diagnostics = "Подложка заменена. Опорные точки, отмеченные на прежнем изображении, отключены.";
    }

    private void LoadBackgroundImage(string path)
    {
        var bitmap = DecodeBackgroundImage(path);
        _backgroundBitmaps.Add(bitmap);
        BackgroundImage = bitmap;
        BackgroundPath = Path.GetFullPath(path);
        _backgroundImageId = GetBackgroundImageId(BackgroundPath);
    }

    private void QueueBackgroundWarpRestore(BackgroundReprojectionState state)
    {
        CancelBackgroundWarp();
        if (_disposed || string.IsNullOrWhiteSpace(BackgroundPath) || !File.Exists(BackgroundPath))
            return;

        var sourcePath = BackgroundPath;
        var sourceReference = state.SourceReference;
        var targetReference = state.TargetReference;
        var version = Interlocked.Increment(ref _backgroundWarpVersion);
        var cancellation = new CancellationTokenSource();
        _backgroundWarpCancellation = cancellation;
        var token = cancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var transform = state.CalibrationParameters is null
                    ? null
                    : ParametricGeoreferenceSolver.CreateTransform(sourceReference, state.CalibrationParameters);
                var png = ManualMapBackgroundReprojector.ReprojectToPng(
                    sourcePath,
                    state.SourcePlacement,
                    sourceReference,
                    targetReference,
                    state.SourceLongitudeOffsetDegrees,
                    georeferenceTransform: transform,
                    cancellationToken: token);
                token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || version != _backgroundWarpVersion || BackgroundPath != sourcePath)
                        return;
                    using var stream = new MemoryStream(png);
                    var bitmap = new Bitmap(stream);
                    _backgroundBitmaps.Add(bitmap);
                    BackgroundImage = bitmap;
                    this.RaisePropertyChanged(nameof(ControlPointMarkers));
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed && version == _backgroundWarpVersion)
                        Diagnostics = $"Не удалось восстановить географическую подложку: {exception.Message}";
                });
            }
            finally
            {
                if (version == _backgroundWarpVersion)
                {
                    _backgroundWarpCancellation?.Dispose();
                    _backgroundWarpCancellation = null;
                }
            }
        });
    }

    private void CancelBackgroundWarp()
    {
        Interlocked.Increment(ref _backgroundWarpVersion);
        _backgroundWarpCancellation?.Cancel();
        _backgroundWarpCancellation?.Dispose();
        _backgroundWarpCancellation = null;
    }

    private static Bitmap DecodeBackgroundImage(string path)
    {
        var imageInfo = ImageSharpImage.Identify(path);
        if (imageInfo is not null
            && Math.Max(imageInfo.Width, imageInfo.Height) > MaxBackgroundPixelDimension)
        {
            var targetWidth = imageInfo.Width >= imageInfo.Height
                ? MaxBackgroundPixelDimension
                : Math.Max(1, (int)Math.Round(imageInfo.Width * (double)MaxBackgroundPixelDimension / imageInfo.Height));
            using var scaledStream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(
                scaledStream,
                targetWidth,
                BitmapInterpolationMode.HighQuality);
        }
        else
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
    }

    private static string? GetBackgroundImageId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(path));
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveProject(string path)
    {
        ManualMapJson.Save(path, _draft, _spatialReference, _controlPoints, _calibrationParameters, _calibrationReport);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var relativeBackground = string.IsNullOrWhiteSpace(BackgroundPath)
            ? null
            : Path.GetRelativePath(directory, Path.GetFullPath(BackgroundPath));
        File.WriteAllText(path + ".editor.json", JsonSerializer.Serialize(
            new ManualMapEditorProjectState(
                relativeBackground,
                IsBackgroundVisible,
                IsBackgroundLocked,
                BackgroundOpacity,
                BackgroundScale,
                BackgroundOffsetX,
                BackgroundOffsetY,
                BackgroundRotation,
                _currentVertexIds.ToArray(),
                BackgroundMapX,
                BackgroundMapY,
                BackgroundMapWidth,
                BackgroundMapHeight,
                BackgroundWarpSourcePlacement: _backgroundWarpSourcePlacement,
                BackgroundReprojectionState: _backgroundReprojectionState),
            EditorStateOptions));
        Diagnostics = "Ручной проект сохранён.";
    }

    public static bool ProjectNeedsSpatialReferenceConfirmation(string path) =>
        !ManualMapJson.LoadProject(path).HasSpatialReference;

    public void RequestLoadProject(string path)
    {
        if (_disposed)
            return;

        try
        {
            var project = ManualMapJson.LoadProject(path);
            if (!project.HasSpatialReference)
            {
                _pendingLegacyProjectPath = path;
                this.RaisePropertyChanged(nameof(IsLegacyProjectConfirmationVisible));
                Diagnostics = "Старый проект не содержит пространственные настройки. Подтвердите загрузку с настройками текущего редактора.";
                return;
            }

            LoadProject(path);
        }
        catch (Exception exception)
        {
            Diagnostics = $"Проект не загружен: {exception.Message}";
        }
    }

    public void CancelPendingProjectLoad()
    {
        if (_pendingLegacyProjectPath is null)
            return;

        _pendingLegacyProjectPath = null;
        this.RaisePropertyChanged(nameof(IsLegacyProjectConfirmationVisible));
        Diagnostics = "Загрузка проекта отменена.";
    }

    public void ConfirmPendingLegacyProjectLoad()
    {
        var path = _pendingLegacyProjectPath;
        if (path is null || _disposed)
            return;

        _pendingLegacyProjectPath = null;
        this.RaisePropertyChanged(nameof(IsLegacyProjectConfirmationVisible));
        LoadProject(path, useCurrentSpatialReferenceForLegacy: true);
    }

    public void LoadProject(string path, bool useCurrentSpatialReferenceForLegacy = false)
    {
        if (_disposed)
            return;

        try
        {
            CancelBackgroundWarp();
            var project = ManualMapJson.LoadProject(path);
            if (!project.HasSpatialReference && !useCurrentSpatialReferenceForLegacy)
            {
                Diagnostics = "Проект старого формата не содержит географическую привязку. Подтвердите загрузку с текущими пространственными настройками.";
                return;
            }

            var draft = project.Draft;
            var spatialReference = project.SpatialReference ?? _spatialReference with
            {
                GridWidth = draft.GridWidth,
                GridHeight = draft.GridHeight
            };
            spatialReference.Validate();
            var projectValidation = _validator.Validate(draft, spatialReference);
            var editorState = ReadEditorState(path);
            var backgroundBitmap = PrepareBackgroundImage(path, editorState?.BackgroundPath);

            _validationCancellation?.Cancel();
            _draft = draft;
            _spatialReference = spatialReference;
            _controlPoints = project.ControlPoints?.OrderBy(point => point.Id).ToList() ?? [];
            _calibrationParameters = project.CalibrationParameters;
            _calibrationReport = project.CalibrationReport;
            _nextVertexId = GetNextVertexId(draft);
            _currentVertexIds = GetCurrentVertexIds(draft, editorState);
            _selectedRegionId = null;
            ClearPointerPreview();
            _undo.Clear();
            _redo.Clear();
            _suppressSpatialConfigurationChanges = true;
            try
            {
                SpatialConfiguration.LoadSpatialOptions(ToSpatialOptions(_spatialReference));
            }
            finally
            {
                _suppressSpatialConfigurationChanges = false;
            }
            ApplyEditorState(path, editorState, backgroundBitmap);
            var staleBackgroundReferences = DeactivateStaleBackgroundReferences();
            if (_backgroundReprojectionState is not null && BackgroundImage is not null)
                QueueBackgroundWarpRestore(_backgroundReprojectionState);
            else if (_calibrationParameters?.IsAppliedToBackground == true && _backgroundWarpSourcePlacement is not null && BackgroundImage is not null)
                QueueBackgroundWarpRestore(new BackgroundReprojectionState(
                    _calibrationParameters.SourceReference ?? _spatialReference,
                    _spatialReference,
                    _backgroundWarpSourcePlacement,
                    CalibrationParameters: _calibrationParameters));
            RefreshState(projectValidation);
            this.RaisePropertyChanged(nameof(GridWidth));
            this.RaisePropertyChanged(nameof(GridHeight));
            this.RaisePropertyChanged(nameof(Bounds));
            this.RaisePropertyChanged(nameof(SpatialReference));
            this.RaisePropertyChanged(nameof(ControlPoints));
            this.RaisePropertyChanged(nameof(ControlPointMarkers));
            this.RaisePropertyChanged(nameof(CalibrationParameters));
            this.RaisePropertyChanged(nameof(CalibrationReport));
            RaiseBackgroundTransformProperties();
            Diagnostics = project.HasSpatialReference
                ? "Ручной проект загружен."
                : "Старый проект загружен с текущими пространственными настройками; географическая привязка исходного файла неизвестна.";
            if (staleBackgroundReferences)
                Diagnostics = "Фоновый файл изменился или отсутствует. Привязка этого изображения и его GCP отключены; проверьте и привяжите подложку заново.";
        }
        catch (Exception exception)
        {
            Diagnostics = $"Проект не загружен: {exception.Message}";
        }
    }

    public ManualMapEditorResult CreateResult()
    {
        if (IsValidating)
            throw new InvalidOperationException("Дождитесь завершения проверки ручной карты.");
        if (!CanFinalize || _lastFinalization is null)
            throw new InvalidOperationException("Сначала исправьте ошибки ручной карты.");
        return new ManualMapEditorResult(
            _draft,
            _lastFinalization,
            ApplyBoundaryDistortion,
            SpatialConfiguration.BuildSpatialOptions());
    }

    public void FitBackground()
    {
        _backgroundMapX = 0;
        _backgroundMapY = 0;
        _backgroundMapWidth = Bounds.Width;
        _backgroundMapHeight = Bounds.Height;
        BackgroundScale = 1;
        this.RaisePropertyChanged(nameof(BackgroundOffsetX));
        this.RaisePropertyChanged(nameof(BackgroundOffsetY));
        this.RaisePropertyChanged(nameof(BackgroundMapX));
        this.RaisePropertyChanged(nameof(BackgroundMapY));
        this.RaisePropertyChanged(nameof(BackgroundMapWidth));
        this.RaisePropertyChanged(nameof(BackgroundMapHeight));
        BackgroundRotation = 0;
    }

    private void CommitCurrentRegion()
    {
        var ids = _currentVertexIds.Distinct().ToArray();
        if (ids.Length < 3)
        {
            Diagnostics = "Регион должен содержать минимум три уникальные вершины.";
            return;
        }

        var regionId = NextRegionId();
        var candidate = new ManualMapDraft(
            _draft.GridWidth,
            _draft.GridHeight,
            _draft.Vertices,
            _draft.Regions.Append(new ManualRegionFace(regionId, ids, $"Region {regionId}")).ToArray());
        var validation = _validator.Validate(candidate, _spatialReference);
        if (!validation.IsSuccessful)
        {
            Diagnostics = FormatDiagnostics(validation.Diagnostics);
            return;
        }

        PushUndo();
        _draft = candidate;
        _currentVertexIds.Clear();
        _selectedRegionId = regionId;
        ClearPointerPreview();
        Diagnostics = $"Регион {regionId} создан.";
        RefreshState(validation);
    }

    private void DeleteSelectedRegion()
    {
        if (!SelectedRegionId.HasValue)
            return;
        PushUndo();
        var draftWithoutRegion = new ManualMapDraft(
            _draft.GridWidth,
            _draft.GridHeight,
            _draft.Vertices,
            _draft.Regions.Where(region => region.Id != SelectedRegionId.Value).ToArray());
        _draft = ManualMapDraftTopology.PruneUnreferencedVertices(draftWithoutRegion, _currentVertexIds);
        _selectedRegionId = null;
        RefreshState();
    }

    private void SelectRegion(MapPoint point)
    {
        var pointGeometry = new Point(new Coordinate(point.X, point.Y));
        var candidates = _regionSelectionIndex.Query(new Envelope(point.X, point.X, point.Y, point.Y));
        var selected = candidates
            .Where(region => region.Shape.Covers(pointGeometry))
            .OrderBy(region => region.Id)
            .LastOrDefault();
        SelectedRegionId = selected?.Id;
    }

    private async Task ValidateDraftAsync()
    {
        ManualMapValidationInput? input = null;
        CancellationTokenSource? validationCancellation = null;
        var started = false;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsValidating)
                    return;

                if (_currentVertexIds.Count != 0)
                {
                    Diagnostics = "Завершите или отмените текущий polygon перед формированием карты.";
                    return;
                }

                var options = _options.WithSpatial(SpatialConfiguration.BuildSpatialOptions());
                validationCancellation = new CancellationTokenSource();
                _validationCancellation = validationCancellation;
                input = new ManualMapValidationInput(_draft, _spatialReference, options, validationCancellation.Token);
                IsValidating = true;
                started = true;
                _lastFinalization = null;
                WaterBodyCount = 0;
                ValidationSummary = "Проверка карты выполняется в фоне…";
                Diagnostics = string.Empty;
                RefreshEditorProperties();
            });

            if (input is null)
                return;

            var validationInput = input;
            var output = await Task.Run(() =>
            {
                validationInput.CancellationToken.ThrowIfCancellationRequested();
                var finalization = _finalizer.FinalizeDraft(
                    validationInput.Draft,
                    validationInput.SpatialReference,
                    validationInput.CancellationToken);
                var waterBodyCount = finalization.IsSuccessful
                    ? ComputeWaterBodyCount(finalization, validationInput.Options, validationInput.CancellationToken)
                    : 0;
                return new ManualMapValidationOutput(finalization, waterBodyCount);
            }, validationInput.CancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                if (!ReferenceEquals(_draft, validationInput.Draft) || !ReferenceEquals(_spatialReference, validationInput.SpatialReference))
                {
                    _lastFinalization = null;
                    ValidationSummary = string.Empty;
                    Diagnostics = "Карта изменилась во время проверки. Запустите проверку ещё раз.";
                    return;
                }

                _lastFinalization = output.Finalization;
                Diagnostics = FormatDiagnostics(output.Finalization.Diagnostics);
                ValidationSummary = output.Finalization.IsSuccessful
                    ? $"Regions: {output.Finalization.RegionCount}   Landmasses: {output.Finalization.LandmassCount}   Water bodies: {output.WaterBodyCount}\n✓ Geometry valid\n✓ No region overlaps\n✓ Shared edges validated\n✓ Regions inside map bounds"
                    : "Карта не может быть сформирована: есть блокирующие ошибки.";
                WaterBodyCount = output.WaterBodyCount;
                RefreshEditorProperties();
            });
        }
        catch (OperationCanceledException) when (validationCancellation?.IsCancellationRequested == true)
        {
            // Resize and editor teardown cancel stale validation runs. Their
            // results and cancellation details are intentionally discarded.
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                _lastFinalization = null;
                Diagnostics = $"Проверка не выполнена: {exception.Message}";
                ValidationSummary = string.Empty;
                WaterBodyCount = 0;
                RefreshEditorProperties();
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_validationCancellation, validationCancellation))
                    _validationCancellation = null;

                if (_disposed || !started)
                    return;

                IsValidating = false;
                RefreshEditorProperties();
            });
            validationCancellation?.Dispose();
        }
    }

    private void OnSpatialConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressSpatialConfigurationChanges)
            return;

        if (_draft.Vertices.Count > 0 || _draft.Regions.Count > 0)
        {
            _suppressSpatialConfigurationChanges = true;
            try
            {
                SpatialConfiguration.LoadSpatialOptions(ToSpatialOptions(_spatialReference));
            }
            finally
            {
                _suppressSpatialConfigurationChanges = false;
            }
            Diagnostics = "Пространственные параметры не изменены: у карты уже есть геометрия. Используйте «Перепроецировать карту…», чтобы перенести её явно.";
            return;
        }

        if (e.PropertyName is not (nameof(SpatialConfigurationViewModel.WorldModel)
            or nameof(SpatialConfigurationViewModel.UnitsPerCell)
            or nameof(SpatialConfigurationViewModel.CoverageKind)
            or nameof(SpatialConfigurationViewModel.WestLongitude)
            or nameof(SpatialConfigurationViewModel.EastLongitude)
            or nameof(SpatialConfigurationViewModel.SouthLatitude)
            or nameof(SpatialConfigurationViewModel.NorthLatitude)
            or nameof(SpatialConfigurationViewModel.GridMapping)
            or nameof(SpatialConfigurationViewModel.Topology)))
        {
            return;
        }

        _cachedDraftValidation = null;
        _validatedDraft = null;
        _validatedSpatialReference = null;

        try
        {
            var spatial = SpatialConfiguration.BuildSpatialOptions();
            _spatialReference = MapSpatialContext.Create(_draft.GridWidth, _draft.GridHeight, spatial).SpatialReference;
            if (e.PropertyName == nameof(SpatialConfigurationViewModel.UnitsPerCell))
            {
                _spatialIndex = new ManualMapDraftSpatialIndex(
                    _draft,
                    Math.Max(_spatialReference.UnitsPerCell * 16, RegionGeometryPrecision.LengthTolerance * 4));
            }
            this.RaisePropertyChanged(nameof(Bounds));
            _lastFinalization = null;
            Diagnostics = "Пространственные параметры изменены. Проверьте карту перед формированием.";
            ValidationSummary = string.Empty;
            WaterBodyCount = 0;
            RefreshEditorProperties();
        }
        catch (Exception exception)
        {
            _lastFinalization = null;
            Diagnostics = $"Пространственные параметры некорректны: {exception.Message}";
            ValidationSummary = string.Empty;
            WaterBodyCount = 0;
            RefreshEditorProperties();
        }
    }

    private static int ComputeWaterBodyCount(
        ManualMapFinalizationResult finalization,
        MapGenerationOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = MapGenerationRequest.Isolated(
                new RequestedDomain(finalization.DerivedMask.Window),
                finalization.DerivedMask,
                options);
            var session = MapGenerationSession.Create(
                request,
                new MapGeometrySeed(finalization.Landmasses, finalization.RegionDraft));
            session.RunUntil(MapDataKeys.WaterBodies, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return session.WaterBodies.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    private void RefreshState(
        ManualMapValidationResult? cachedValidation = null,
        ManualMapDraftSpatialIndex? preparedSpatialIndex = null)
    {
        var validation = cachedValidation ?? _validator.Validate(_draft, _spatialReference);
        _cachedDraftValidation = validation;
        _validatedDraft = _draft;
        _validatedSpatialReference = _spatialReference;
        DisplayRegions = validation.RegionPolygons
            .OrderBy(pair => pair.Key)
            .Select(pair => new ManualMapDisplayRegion(
                pair.Key,
                _draft.Regions.FirstOrDefault(region => region.Id == pair.Key)?.Name,
                pair.Value))
            .ToArray();
        RebuildRegionSelectionIndex();
        RefreshRegions(validation.RegionPolygons);
        if (preparedSpatialIndex is null)
            _spatialIndex.Rebuild(_draft);
        else
            _spatialIndex = preparedSpatialIndex;
        RefreshVertexCache();
        _lastFinalization = null;
        this.RaisePropertyChanged(nameof(Draft));
        RefreshEditorProperties(includeDisplayRegions: true);
    }

    private static ManualMapValidationResult? TranslateCachedValidation(
        ManualMapValidationResult? cachedValidation,
        MapSpatialReference sourceSpatialReference,
        ManualCanvasResizeResult resizeResult,
        CancellationToken cancellationToken)
    {
        if (cachedValidation is not { IsSuccessful: true })
            return null;

        var deltaX = resizeResult.Request.LeftCells * sourceSpatialReference.UnitsPerCell;
        var deltaY = resizeResult.Request.TopCells * sourceSpatialReference.UnitsPerCell;
        if (!IsCanonicalTranslation(deltaX) || !IsCanonicalTranslation(deltaY))
            return null;

        var transform = AffineTransformation.TranslationInstance(deltaX, deltaY);
        var polygons = new Dictionary<int, Polygon>(cachedValidation.RegionPolygons.Count);
        foreach (var (regionId, polygon) in cachedValidation.RegionPolygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            polygons.Add(regionId, (Polygon)transform.Transform(polygon));
        }

        return new ManualMapValidationResult(cachedValidation.Diagnostics, polygons);
    }

    private static bool IsCanonicalTranslation(double value)
    {
        var scaled = value * RegionGeometryPrecision.Scale;
        return double.IsFinite(scaled) &&
               Math.Abs(scaled - Math.Round(scaled, MidpointRounding.AwayFromZero)) <= 1e-6;
    }

    private void RefreshInteractiveState(bool rebuildSpatialIndex = false)
    {
        if (rebuildSpatialIndex)
        {
            _spatialIndex.Rebuild(_draft);
            RefreshVertexCache();
        }
        _lastFinalization = null;
        this.RaisePropertyChanged(nameof(Draft));
        RefreshEditorProperties();
    }

    private void RefreshVertexCache()
    {
        _vertexPositions.Clear();
        foreach (var vertex in _draft.Vertices)
            _vertexPositions[vertex.Id] = vertex.Position;
        _vertexMarkers.Clear();
        _vertexMarkers.AddRange(_draft.Vertices.Select(vertex => vertex.Position));
    }

    private void RebuildRegionSelectionIndex()
    {
        var index = new STRtree<ManualMapDisplayRegion>();
        foreach (var region in DisplayRegions)
            index.Insert(region.Shape.EnvelopeInternal, region);
        index.Build();
        _regionSelectionIndex = index;
    }

    private void RefreshRegions(IReadOnlyDictionary<int, Polygon> polygons)
    {
        var regionViewModels = _draft.Regions
            .OrderBy(region => region.Id)
            .Select(region => new ManualMapEditorRegionViewModel(
                region.Id,
                string.IsNullOrWhiteSpace(region.Name) ? $"Region {region.Id}" : region.Name!,
                region.Id == SelectedRegionId))
            .ToArray();
        for (var index = 0; index < regionViewModels.Length; index++)
        {
            if (index < Regions.Count)
            {
                if (!Equals(Regions[index], regionViewModels[index]))
                    Regions[index] = regionViewModels[index];
            }
            else
            {
                Regions.Add(regionViewModels[index]);
            }
        }

        while (Regions.Count > regionViewModels.Length)
            Regions.RemoveAt(Regions.Count - 1);
        _ = polygons;
        RefreshSelectionName();
    }

    private void RefreshSelection()
    {
        RefreshRegions(DisplayRegions.ToDictionary(region => region.Id, region => region.Shape));
        RefreshEditorProperties();
    }

    private void RefreshSelectionName()
    {
        _selectedRegionName = _draft.Regions.FirstOrDefault(region => region.Id == SelectedRegionId)?.Name ?? string.Empty;
        this.RaisePropertyChanged(nameof(SelectedRegionName));
        this.RaisePropertyChanged(nameof(SelectedRegion));
        this.RaisePropertyChanged(nameof(HasSelection));
    }

    private void RefreshEditorProperties(bool includeDisplayRegions = false)
    {
        if (includeDisplayRegions)
            this.RaisePropertyChanged(nameof(DisplayRegions));
        this.RaisePropertyChanged(nameof(VertexMarkers));
        this.RaisePropertyChanged(nameof(CurrentPolygon));
        this.RaisePropertyChanged(nameof(SnapCandidatePosition));
        this.RaisePropertyChanged(nameof(SnapCandidateVertexId));
        this.RaisePropertyChanged(nameof(CurrentVertexIds));
        this.RaisePropertyChanged(nameof(HasCurrentPolygon));
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(CanRedo));
        this.RaisePropertyChanged(nameof(CanFinalize));
    }

    private void PushUndo()
    {
        _undo.Push(Capture());
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    private ManualMapEditorSnapshot Capture() => new(
        _draft,
        _currentVertexIds.ToArray(),
        _selectedRegionId,
        _spatialReference,
        _backgroundMapX,
        _backgroundMapY,
        _backgroundMapWidth,
        _backgroundMapHeight,
        BackgroundRotation,
        _controlPoints.ToArray(),
        _calibrationParameters,
        _calibrationReport,
        BackgroundImage,
        BackgroundPath,
        IsBackgroundVisible,
        IsBackgroundLocked,
        BackgroundOpacity,
        BackgroundScale,
        _backgroundWarpSourcePlacement,
        _backgroundReprojectionState);

    private void Restore(ManualMapEditorSnapshot snapshot)
    {
        CancelBackgroundWarp();
        _validationCancellation?.Cancel();
        _draft = snapshot.Draft;
        _spatialReference = snapshot.SpatialReference;
        _nextVertexId = GetNextVertexId(_draft);
        _currentVertexIds = snapshot.CurrentVertexIds.ToList();
        _selectedRegionId = snapshot.SelectedRegionId;
        _backgroundMapX = snapshot.BackgroundMapX;
        _backgroundMapY = snapshot.BackgroundMapY;
        _backgroundMapWidth = snapshot.BackgroundMapWidth;
        _backgroundMapHeight = snapshot.BackgroundMapHeight;
        BackgroundRotation = snapshot.BackgroundRotation;
        BackgroundImage = snapshot.BackgroundImage;
        BackgroundPath = snapshot.BackgroundPath;
        _backgroundImageId = GetBackgroundImageId(BackgroundPath);
        IsBackgroundVisible = snapshot.IsBackgroundVisible;
        IsBackgroundLocked = snapshot.IsBackgroundLocked;
        BackgroundOpacity = snapshot.BackgroundOpacity;
        _backgroundScale = snapshot.BackgroundScale;
        _backgroundWarpSourcePlacement = snapshot.BackgroundWarpSourcePlacement;
        _backgroundReprojectionState = snapshot.BackgroundReprojectionState;
        _controlPoints = snapshot.ControlPoints.ToList();
        _calibrationParameters = snapshot.CalibrationParameters;
        _calibrationReport = snapshot.CalibrationReport;
        _suppressSpatialConfigurationChanges = true;
        try
        {
            SpatialConfiguration.LoadSpatialOptions(ToSpatialOptions(_spatialReference));
        }
        finally
        {
            _suppressSpatialConfigurationChanges = false;
        }
        ClearPointerPreview();
        RefreshState();
        this.RaisePropertyChanged(nameof(GridWidth));
        this.RaisePropertyChanged(nameof(GridHeight));
        this.RaisePropertyChanged(nameof(Bounds));
        this.RaisePropertyChanged(nameof(SpatialReference));
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        RaiseBackgroundTransformProperties();
    }

    private SnapResult FindSnap(MapPoint point, double tolerance)
    {
        var vertex = _spatialIndex.FindNearestVertex(point, tolerance);
        if (vertex.HasValue)
            return new SnapResult(vertex.Value.VertexId, vertex.Value.Position, null);

        var edge = _spatialIndex.FindNearestEdge(point, tolerance);
        return edge.HasValue
            ? new SnapResult(null, edge.Value.Position, edge.Value)
            : new SnapResult(null, null, null);
    }

    private int NextVertexId() => _nextVertexId++;
    private int NextRegionId() => _draft.Regions.Select(region => region.Id).DefaultIfEmpty(0).Max() + 1;

    private static int GetNextVertexId(ManualMapDraft draft) =>
        draft.Vertices.Select(vertex => vertex.Id).DefaultIfEmpty(0).Max() + 1;

    private static int GetNextControlPointId(IReadOnlyList<GeographicControlPoint> controlPoints) =>
        controlPoints.Select(point => point.Id).DefaultIfEmpty(0).Max() + 1;

    private static MapPoint Canonicalize(MapPoint point) => new(
        RegionGeometryPrecision.Canonicalize(point.X),
        RegionGeometryPrecision.Canonicalize(point.Y));

    private void ClearPointerPreview()
    {
        _pointerPreview = null;
        _snapCandidateVertexId = null;
        _snapCandidatePosition = null;
    }

    private static string FormatDiagnostics(IEnumerable<ManualMapDiagnostic> diagnostics)
    {
        var list = diagnostics.ToArray();
        return list.Length == 0
            ? "✓ Geometry valid."
            : string.Join(Environment.NewLine, list.Select(diagnostic =>
                $"{(diagnostic.IsBlocking ? "✗" : "•")} {diagnostic.Code}: {diagnostic.Message}"));
    }

    private static ManualMapEditorProjectState? ReadEditorState(string path)
    {
        var statePath = path + ".editor.json";
        if (!File.Exists(statePath))
            return null;
        return JsonSerializer.Deserialize<ManualMapEditorProjectState>(File.ReadAllText(statePath), EditorStateOptions);
    }

    private static Bitmap? PrepareBackgroundImage(string path, string? relativeBackgroundPath)
    {
        if (string.IsNullOrWhiteSpace(relativeBackgroundPath))
            return null;
        var backgroundPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, relativeBackgroundPath));
        return File.Exists(backgroundPath) ? DecodeBackgroundImage(backgroundPath) : null;
    }

    private void ApplyEditorState(string projectPath, ManualMapEditorProjectState? state, Bitmap? backgroundBitmap)
    {
        var previousImage = BackgroundImage;
        BackgroundImage = backgroundBitmap;
        if (backgroundBitmap is not null)
            _backgroundBitmaps.Add(backgroundBitmap);
        if (previousImage is not null && _backgroundBitmaps.Remove(previousImage))
            previousImage.Dispose();
        _backgroundReprojectionState = state?.BackgroundReprojectionState;
        _backgroundWarpSourcePlacement = state?.BackgroundWarpSourcePlacement ?? _backgroundReprojectionState?.SourcePlacement;
        BackgroundPath = backgroundBitmap is null || string.IsNullOrWhiteSpace(state?.BackgroundPath)
            ? null
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, state.BackgroundPath));
        _backgroundImageId = GetBackgroundImageId(BackgroundPath);

        IsBackgroundVisible = state?.IsBackgroundVisible ?? true;
        IsBackgroundLocked = state?.IsBackgroundLocked ?? false;
        BackgroundOpacity = state?.BackgroundOpacity ?? .55;
        BackgroundRotation = state?.BackgroundRotation ?? 0;
        _backgroundScale = state?.BackgroundScale is > 0 and var scale && double.IsFinite(scale) ? scale : 1;

        if (state?.BackgroundMapX is { } x && state.BackgroundMapY is { } y &&
            state.BackgroundMapWidth is > 0 and var width && state.BackgroundMapHeight is > 0 and var height &&
            double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(width) && double.IsFinite(height))
        {
            _backgroundMapX = x;
            _backgroundMapY = y;
            _backgroundMapWidth = width;
            _backgroundMapHeight = height;
        }
        else if (state is not null)
        {
            // Version 1 sidecars stored offsets in canvas pixels and a scale
            // relative to the then-current map bounds. Treat those offsets as
            // map units once, then persist the stable map-space rectangle.
            _backgroundMapX = double.IsFinite(state.BackgroundOffsetX) ? state.BackgroundOffsetX : 0;
            _backgroundMapY = double.IsFinite(state.BackgroundOffsetY) ? state.BackgroundOffsetY : 0;
            _backgroundMapWidth = Bounds.Width * _backgroundScale;
            _backgroundMapHeight = Bounds.Height * _backgroundScale;
        }
        else
        {
            _backgroundMapX = 0;
            _backgroundMapY = 0;
            _backgroundMapWidth = Bounds.Width;
            _backgroundMapHeight = Bounds.Height;
            _backgroundScale = 1;
        }

        this.RaisePropertyChanged(nameof(BackgroundOffsetX));
        this.RaisePropertyChanged(nameof(BackgroundOffsetY));
        this.RaisePropertyChanged(nameof(BackgroundMapX));
        this.RaisePropertyChanged(nameof(BackgroundMapY));
        this.RaisePropertyChanged(nameof(BackgroundMapWidth));
        this.RaisePropertyChanged(nameof(BackgroundMapHeight));
        this.RaisePropertyChanged(nameof(BackgroundScale));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
    }

    private bool DeactivateStaleBackgroundReferences()
    {
        var loadedImageId = GetBackgroundImageId(BackgroundPath);
        var staleState = _backgroundReprojectionState?.SourceImageId is { } savedImageId &&
            !string.Equals(savedImageId, loadedImageId, StringComparison.Ordinal);
        var stalePoints = _controlPoints.Any(point =>
            point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv && point.IsActive &&
            point.BackgroundImageId is { } pointImageId && !string.Equals(pointImageId, loadedImageId, StringComparison.Ordinal));
        if (!staleState && !stalePoints)
            return false;

        if (stalePoints)
        {
            _controlPoints = _controlPoints.Select(point => point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv &&
                point.BackgroundImageId is { } pointImageId && !string.Equals(pointImageId, loadedImageId, StringComparison.Ordinal)
                ? point with { IsActive = false }
                : point).ToList();
        }
        if (staleState || stalePoints)
        {
            _backgroundReprojectionState = null;
            _backgroundWarpSourcePlacement = null;
            if (_calibrationParameters?.IsAppliedToBackground == true)
                _calibrationParameters = _calibrationParameters with { IsAppliedToBackground = false };
        }
        this.RaisePropertyChanged(nameof(ControlPoints));
        this.RaisePropertyChanged(nameof(ControlPointMarkers));
        this.RaisePropertyChanged(nameof(CalibrationParameters));
        this.RaisePropertyChanged(nameof(CalibrationReport));
        RaiseBackgroundTransformProperties();
        return true;
    }

    private static List<int> GetCurrentVertexIds(ManualMapDraft draft, ManualMapEditorProjectState? state)
    {
        var knownVertexIds = draft.Vertices.Select(vertex => vertex.Id).ToHashSet();
        return state?.CurrentVertexIds is null
            ? []
            : state.CurrentVertexIds.Where(knownVertexIds.Contains).Distinct().ToList();
    }

    private static MapSpatialOptions ToSpatialOptions(MapSpatialReference reference) => new()
    {
        WorldModel = reference.WorldModel,
        Coverage = reference.Coverage,
        GridMapping = reference.GridMapping,
        Topology = reference.Topology,
        UnitsPerCell = reference.UnitsPerCell,
        PreserveProjectedCellAspectRatio = reference.PreserveProjectedCellAspectRatio,
        LegacyCompatibility = reference.LegacyCompatibility
    };

    private void RaiseBackgroundTransformProperties()
    {
        this.RaisePropertyChanged(nameof(BackgroundOffsetX));
        this.RaisePropertyChanged(nameof(BackgroundOffsetY));
        this.RaisePropertyChanged(nameof(BackgroundMapX));
        this.RaisePropertyChanged(nameof(BackgroundMapY));
        this.RaisePropertyChanged(nameof(BackgroundMapWidth));
        this.RaisePropertyChanged(nameof(BackgroundMapHeight));
        this.RaisePropertyChanged(nameof(BackgroundScale));
        this.RaisePropertyChanged(nameof(BackgroundWarpSourcePlacement));
        this.RaisePropertyChanged(nameof(BackgroundReprojectionState));
    }

    private readonly record struct SnapResult(int? VertexId, MapPoint? Position, ManualMapEdgeSnap? Edge);
    private sealed record ManualMapValidationInput(
        ManualMapDraft Draft,
        MapSpatialReference SpatialReference,
        MapGenerationOptions Options,
        CancellationToken CancellationToken);
    private sealed record ManualMapValidationOutput(ManualMapFinalizationResult Finalization, int WaterBodyCount);
    private sealed record ManualMapEditorSnapshot(
        ManualMapDraft Draft,
        IReadOnlyList<int> CurrentVertexIds,
        int? SelectedRegionId,
        MapSpatialReference SpatialReference,
        double BackgroundMapX,
        double BackgroundMapY,
        double BackgroundMapWidth,
        double BackgroundMapHeight,
        double BackgroundRotation,
        IReadOnlyList<GeographicControlPoint> ControlPoints,
        GeographicCalibrationParameters? CalibrationParameters,
        GeographicCalibrationResult? CalibrationReport,
        Bitmap? BackgroundImage,
        string? BackgroundPath,
        bool IsBackgroundVisible,
        bool IsBackgroundLocked,
        double BackgroundOpacity,
        double BackgroundScale,
        BackgroundImagePlacement? BackgroundWarpSourcePlacement,
        BackgroundReprojectionState? BackgroundReprojectionState);
}

public sealed record ManualMapEditorProjectState(
    string? BackgroundPath,
    bool IsBackgroundVisible,
    bool IsBackgroundLocked,
    double BackgroundOpacity,
    double BackgroundScale,
    double BackgroundOffsetX,
    double BackgroundOffsetY,
    double BackgroundRotation,
    IReadOnlyList<int>? CurrentVertexIds = null,
    double? BackgroundMapX = null,
    double? BackgroundMapY = null,
    double? BackgroundMapWidth = null,
    double? BackgroundMapHeight = null,
    string SchemaVersion = "3.0",
    BackgroundImagePlacement? BackgroundWarpSourcePlacement = null,
    BackgroundReprojectionState? BackgroundReprojectionState = null);
