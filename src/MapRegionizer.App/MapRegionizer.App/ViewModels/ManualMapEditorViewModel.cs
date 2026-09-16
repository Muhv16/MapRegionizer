using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using ReactiveUI;
using System.Reactive;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace MapRegionizer.App.ViewModels;

public enum ManualMapEditorTool
{
    Navigate,
    CreateRegion,
    Select
}

public sealed record ManualMapEditorResult(
    ManualMapDraft Draft,
    ManualMapFinalizationResult Finalization,
    bool ApplyBoundaryDistortion);

public sealed record ManualMapEditorRegionViewModel(int Id, string Label, bool IsSelected);

public sealed record ManualMapDisplayRegion(int Id, string? Name, Polygon Shape);

/// <summary>
/// App-only editor state for manual geography. Core receives only the draft;
/// bitmap/background state never crosses that boundary.
/// </summary>
public sealed class ManualMapEditorViewModel : ReactiveObject
{
    private static readonly JsonSerializerOptions EditorStateOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ManualMapDraftValidator _validator = new();
    private readonly ManualMapDraftFinalizer _finalizer = new();
    private readonly MapSpatialReference _spatialReference;
    private readonly MapGenerationOptions _options;
    private readonly ManualMapDraftSpatialIndex _spatialIndex;
    private readonly Stack<ManualMapEditorSnapshot> _undo = [];
    private readonly Stack<ManualMapEditorSnapshot> _redo = [];
    private readonly Dictionary<int, MapPoint> _vertexPositions = [];
    private STRtree<ManualMapDisplayRegion> _regionSelectionIndex = new();
    private ManualMapDraft _draft;
    private readonly List<MapPoint> _vertexMarkers = [];
    private List<int> _currentVertexIds = [];
    private MapPoint? _pointerPreview;
    private int? _snapCandidateVertexId;
    private MapPoint? _snapCandidatePosition;
    private int? _selectedRegionId;
    private ManualMapFinalizationResult? _lastFinalization;
    private string _diagnostics = string.Empty;
    private string _validationSummary = string.Empty;
    private ManualMapEditorTool _selectedTool = ManualMapEditorTool.Navigate;
    private bool _applyBoundaryDistortion;
    private string _selectedRegionName = string.Empty;
    private Bitmap? _backgroundImage;
    private string? _backgroundPath;
    private bool _isBackgroundVisible = true;
    private bool _isBackgroundLocked;
    private double _backgroundOpacity = .55;
    private double _backgroundScale = 1;
    private double _backgroundOffsetX;
    private double _backgroundOffsetY;
    private double _backgroundRotation;
    private int _waterBodyCount;
    private int _nextVertexId;

    public ManualMapEditorViewModel(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        MapGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        ArgumentNullException.ThrowIfNull(options);
        _draft = draft;
        _spatialReference = spatialReference;
        _options = options;
        _spatialIndex = new ManualMapDraftSpatialIndex(
            draft,
            Math.Max(spatialReference.UnitsPerCell * 16, RegionGeometryPrecision.LengthTolerance * 4));
        _nextVertexId = GetNextVertexId(draft);
        _applyBoundaryDistortion = false;

        UndoCommand = ReactiveCommand.Create(Undo, this.WhenAnyValue(vm => vm.CanUndo));
        RedoCommand = ReactiveCommand.Create(Redo, this.WhenAnyValue(vm => vm.CanRedo));
        ValidateCommand = ReactiveCommand.Create(ValidateDraft);
        DeleteRegionCommand = ReactiveCommand.Create(DeleteSelectedRegion, this.WhenAnyValue(vm => vm.HasSelection));
        FitBackgroundCommand = ReactiveCommand.Create(FitBackground);
        RefreshState();
    }

    public int GridWidth => _draft.GridWidth;
    public int GridHeight => _draft.GridHeight;
    public MapBounds Bounds => new(_spatialReference.WidthInMapUnits, _spatialReference.HeightInMapUnits, _spatialReference.UnitsPerCell);
    public IReadOnlyList<ManualMapEditorTool> Tools { get; } = Enum.GetValues<ManualMapEditorTool>();
    public ManualMapEditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (_selectedTool == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedTool, value);
            _pointerPreview = null;
            _snapCandidateVertexId = null;
            _snapCandidatePosition = null;
            this.RaisePropertyChanged(nameof(CurrentPolygon));
            this.RaisePropertyChanged(nameof(SnapCandidatePosition));
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
    public bool CanFinalize => _lastFinalization?.IsSuccessful == true && _currentVertexIds.Count == 0;
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
    public double BackgroundScale { get => _backgroundScale; set => this.RaiseAndSetIfChanged(ref _backgroundScale, value); }
    public double BackgroundOffsetX { get => _backgroundOffsetX; set => this.RaiseAndSetIfChanged(ref _backgroundOffsetX, value); }
    public double BackgroundOffsetY { get => _backgroundOffsetY; set => this.RaiseAndSetIfChanged(ref _backgroundOffsetY, value); }
    public double BackgroundRotation { get => _backgroundRotation; set => this.RaiseAndSetIfChanged(ref _backgroundRotation, value); }
    public int WaterBodyCount { get => _waterBodyCount; private set => this.RaiseAndSetIfChanged(ref _waterBodyCount, value); }

    public void ShowDiagnostic(string message) => Diagnostics = message;

    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> ValidateCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteRegionCommand { get; }
    public ReactiveCommand<Unit, Unit> FitBackgroundCommand { get; }

    public void HandlePointer(MapPoint point, double hitTolerance)
    {
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

    public void UpdatePointerPreview(MapPoint point, double hitTolerance)
    {
        var nextPointerPreview = SelectedTool == ManualMapEditorTool.CreateRegion ? (MapPoint?)point : null;
        var snap = SelectedTool == ManualMapEditorTool.CreateRegion
            ? FindSnap(point, Math.Max(hitTolerance, RegionGeometryPrecision.LengthTolerance))
            : default;
        var pointerChanged = _pointerPreview != nextPointerPreview;
        var snapChanged = _snapCandidateVertexId != snap.VertexId || _snapCandidatePosition != snap.Position;
        _pointerPreview = nextPointerPreview;
        _snapCandidateVertexId = snap.VertexId;
        _snapCandidatePosition = snap.Position;

        if (pointerChanged)
            this.RaisePropertyChanged(nameof(CurrentPolygon));
        if (snapChanged)
        {
            this.RaisePropertyChanged(nameof(SnapCandidatePosition));
            this.RaisePropertyChanged(nameof(SnapCandidateVertexId));
            if (!pointerChanged)
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
        LoadBackgroundImage(path);
        FitBackground();
    }

    private void LoadBackgroundImage(string path)
    {
        using var stream = File.OpenRead(path);
        BackgroundImage = new Bitmap(stream);
        BackgroundPath = Path.GetFullPath(path);
    }

    public void SaveProject(string path)
    {
        ManualMapJson.Save(path, _draft);
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
                _currentVertexIds.ToArray()),
            EditorStateOptions));
        Diagnostics = "Ручной проект сохранён.";
    }

    public void LoadProject(string path)
    {
        try
        {
            var draft = ManualMapJson.Load(path);
            if (draft.GridWidth != GridWidth || draft.GridHeight != GridHeight)
            {
                Diagnostics = "Проект не загружен: размеры grid не совпадают с текущим редактором.";
                return;
            }

            _draft = draft;
            _nextVertexId = GetNextVertexId(draft);
            _currentVertexIds.Clear();
            _undo.Clear();
            _redo.Clear();
            LoadEditorState(path);
            RefreshState();
            Diagnostics = "Ручной проект загружен.";
        }
        catch (Exception exception)
        {
            Diagnostics = $"Проект не загружен: {exception.Message}";
        }
    }

    public ManualMapEditorResult CreateResult()
    {
        ValidateDraft();
        if (!CanFinalize || _lastFinalization is null)
            throw new InvalidOperationException("Сначала исправьте ошибки ручной карты.");
        return new ManualMapEditorResult(_draft, _lastFinalization, ApplyBoundaryDistortion);
    }

    public void FitBackground()
    {
        BackgroundScale = 1;
        BackgroundOffsetX = 0;
        BackgroundOffsetY = 0;
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
        _draft = new ManualMapDraft(
            _draft.GridWidth,
            _draft.GridHeight,
            _draft.Vertices,
            _draft.Regions.Where(region => region.Id != SelectedRegionId.Value).ToArray());
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

    private void ValidateDraft()
    {
        if (_currentVertexIds.Count != 0)
        {
            Diagnostics = "Завершите или отмените текущий polygon перед формированием карты.";
            return;
        }

        try
        {
            _lastFinalization = _finalizer.FinalizeDraft(_draft, _spatialReference);
            Diagnostics = FormatDiagnostics(_lastFinalization.Diagnostics);
            ValidationSummary = _lastFinalization.IsSuccessful
                ? $"Regions: {_lastFinalization.RegionCount}   Landmasses: {_lastFinalization.LandmassCount}   Water bodies: {ComputeWaterBodyCount(_lastFinalization)}\n✓ Geometry valid\n✓ No region overlaps\n✓ Shared edges validated\n✓ Regions inside map bounds"
                : "Карта не может быть сформирована: есть блокирующие ошибки.";
            WaterBodyCount = _lastFinalization.IsSuccessful ? ComputeWaterBodyCount(_lastFinalization) : 0;
            RefreshEditorProperties();
        }
        catch (Exception exception)
        {
            _lastFinalization = null;
            Diagnostics = $"Проверка не выполнена: {exception.Message}";
            ValidationSummary = string.Empty;
            RefreshEditorProperties();
        }
    }

    private int ComputeWaterBodyCount(ManualMapFinalizationResult finalization)
    {
        try
        {
            var options = _options.WithSpatial(_options.EffectiveSpatial);
            var request = MapGenerationRequest.Isolated(
                new RequestedDomain(finalization.DerivedMask.Window),
                finalization.DerivedMask,
                options);
            var session = MapGenerationSession.Create(
                request,
                new MapGeometrySeed(finalization.Landmasses, finalization.RegionDraft));
            session.RunUntil(MapDataKeys.WaterBodies);
            return session.WaterBodies.Count;
        }
        catch
        {
            return 0;
        }
    }

    private void RefreshState(ManualMapValidationResult? cachedValidation = null)
    {
        var validation = cachedValidation ?? _validator.Validate(_draft, _spatialReference);
        DisplayRegions = validation.RegionPolygons
            .OrderBy(pair => pair.Key)
            .Select(pair => new ManualMapDisplayRegion(
                pair.Key,
                _draft.Regions.FirstOrDefault(region => region.Id == pair.Key)?.Name,
                pair.Value))
            .ToArray();
        RebuildRegionSelectionIndex();
        RefreshRegions(validation.RegionPolygons);
        _spatialIndex.Rebuild(_draft);
        RefreshVertexCache();
        _lastFinalization = null;
        this.RaisePropertyChanged(nameof(Draft));
        RefreshEditorProperties(includeDisplayRegions: true);
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

    private ManualMapEditorSnapshot Capture() => new(_draft, _currentVertexIds.ToArray(), _selectedRegionId);

    private void Restore(ManualMapEditorSnapshot snapshot)
    {
        _draft = snapshot.Draft;
        _nextVertexId = GetNextVertexId(_draft);
        _currentVertexIds = snapshot.CurrentVertexIds.ToList();
        _selectedRegionId = snapshot.SelectedRegionId;
        ClearPointerPreview();
        RefreshState();
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

    private void LoadEditorState(string path)
    {
        var statePath = path + ".editor.json";
        if (!File.Exists(statePath))
            return;
        try
        {
            var state = JsonSerializer.Deserialize<ManualMapEditorProjectState>(File.ReadAllText(statePath), EditorStateOptions);
            if (state is null)
                return;
            var knownVertexIds = _draft.Vertices.Select(vertex => vertex.Id).ToHashSet();
            _currentVertexIds = state.CurrentVertexIds is null
                ? []
                : state.CurrentVertexIds
                    .Where(knownVertexIds.Contains)
                    .Distinct()
                    .ToList();
            IsBackgroundVisible = state.IsBackgroundVisible;
            IsBackgroundLocked = state.IsBackgroundLocked;
            BackgroundOpacity = state.BackgroundOpacity;
            BackgroundScale = state.BackgroundScale;
            BackgroundOffsetX = state.BackgroundOffsetX;
            BackgroundOffsetY = state.BackgroundOffsetY;
            BackgroundRotation = state.BackgroundRotation;
            if (!string.IsNullOrWhiteSpace(state.BackgroundPath))
            {
                var backgroundPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, state.BackgroundPath));
                if (File.Exists(backgroundPath))
                    LoadBackgroundImage(backgroundPath);
            }
        }
        catch (Exception exception)
        {
            Diagnostics = $"Проект загружен, но состояние фона не восстановлено: {exception.Message}";
        }
    }

    private readonly record struct SnapResult(int? VertexId, MapPoint? Position, ManualMapEdgeSnap? Edge);
    private sealed record ManualMapEditorSnapshot(ManualMapDraft Draft, IReadOnlyList<int> CurrentVertexIds, int? SelectedRegionId);
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
    IReadOnlyList<int>? CurrentVertexIds = null);
