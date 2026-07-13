using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using ReactiveUI;
using System.Reactive;

namespace MapRegionizer.App.ViewModels;

public enum RegionEditorTool { Navigate, Select, Split, MoveVertex, AddVertex, DeleteVertex }

public sealed record RegionEditorResult(RegionDraft Draft, bool ApplyBoundaryDistortion);

public sealed class RegionEditorViewModel : ReactiveObject
{
    private readonly MapMask _mask;
    private readonly MapGenerationOptions _options;
    private readonly IReadOnlyList<Landmass> _landmasses;
    private readonly Stack<RegionDraft> _undo = [];
    private readonly Stack<RegionDraft> _redo = [];
    private RegionDraft _draft;
    private IReadOnlyList<MapRegion> _canonicalRegions = [];
    private IReadOnlyList<MapRegion> _displayRegions = [];
    private RegionTopology? _topology;
    private RegionId? _selectedRegionId;
    private RegionEditorRegionViewModel? _selectedRegion;
    private RegionEditorTool _selectedTool = RegionEditorTool.Navigate;
    private MapPoint? _firstPoint;
    private MapPoint? _splitPreviewPoint;
    private RegionId? _mergeSourceRegionId;
    private RegionTopologyVertexId? _vertexToMove;
    private RegionDraft? _dragDraft;
    private RegionTopologyVertexId? _draggedVertexId;
    private MapPoint? _draggedVertexPosition;
    private readonly SemaphoreSlim _dragValidationGate = new(1, 1);
    private CancellationTokenSource? _dragValidationCancellation;
    private RegionDraft? _dragValidationDraft;
    private Task<RegionCanonicalizationResult?>? _dragValidationTask;
    private int _dragValidationRevision;
    private bool _isCompletingVertexDrag;
    private bool? _isVertexDragValid;
    private string _diagnostics = string.Empty;
    private bool _applyBoundaryDistortion;
    private bool _showDistortionPreview;
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

    public RegionEditorViewModel(MapMask mask, MapGenerationOptions options, IReadOnlyList<Landmass> landmasses, RegionDraft draft, bool applyBoundaryDistortion)
    {
        _mask = mask;
        _options = options;
        _landmasses = landmasses;
        _draft = draft;
        _applyBoundaryDistortion = applyBoundaryDistortion;
        UndoCommand = ReactiveCommand.Create(Undo, this.WhenAnyValue(vm => vm.CanUndo));
        RedoCommand = ReactiveCommand.Create(Redo, this.WhenAnyValue(vm => vm.CanRedo));
        MergeCommand = ReactiveCommand.Create(BeginMerge, this.WhenAnyValue(vm => vm.HasSelection));
        DeleteRegionCommand = ReactiveCommand.Create(MergeSelectedWithFirstNeighbour, this.WhenAnyValue(vm => vm.HasSelection));
        FitBackgroundCommand = ReactiveCommand.Create(FitBackground);
        RefreshFromDraft();
    }

    public MapBounds Bounds => new(_mask.Width * _options.PixelSize, _mask.Height * _options.PixelSize, _options.PixelSize);
    public IReadOnlyList<RegionEditorTool> Tools { get; } = Enum.GetValues<RegionEditorTool>();
    public ObservableCollection<RegionEditorRegionViewModel> Regions { get; } = [];
    public IReadOnlyList<MapRegion> DisplayRegions => _displayRegions;
    public RegionId? SelectedRegionId
    {
        get => _selectedRegionId;
        set
        {
            if (_selectedRegionId == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedRegionId, value);
            RefreshSelection();
            CompleteMergeIfTargetSelected();
        }
    }
    public RegionEditorRegionViewModel? SelectedRegion { get => _selectedRegion; set { this.RaiseAndSetIfChanged(ref _selectedRegion, value); if (value is not null && value.Id != SelectedRegionId) SelectedRegionId = value.Id; } }
    public RegionEditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTool, value);
            _firstPoint = null;
            _splitPreviewPoint = null;
            _vertexToMove = null;
            this.RaisePropertyChanged(nameof(ShowVertexMarkers));
            this.RaisePropertyChanged(nameof(VertexMarkers));
            this.RaisePropertyChanged(nameof(SplitPreviewLine));
        }
    }
    public string Diagnostics { get => _diagnostics; private set => this.RaiseAndSetIfChanged(ref _diagnostics, value); }
    public bool HasSelection => SelectedRegionId.HasValue;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsAwaitingMergeTarget => _mergeSourceRegionId.HasValue;
    public bool ShowVertexMarkers => SelectedTool is RegionEditorTool.MoveVertex or RegionEditorTool.AddVertex or RegionEditorTool.DeleteVertex;
    public IReadOnlyList<MapPoint> VertexMarkers => !ShowVertexMarkers || _topology is null
        ? []
        : _topology.Vertices.Where(vertex => !vertex.IsCoastal).Select(vertex =>
            vertex.Id == _draggedVertexId && _draggedVertexPosition.HasValue ? _draggedVertexPosition.Value : vertex.Position).ToList();
    public bool? IsVertexDragValid { get => _isVertexDragValid; private set => this.RaiseAndSetIfChanged(ref _isVertexDragValid, value); }
    public (MapPoint Start, MapPoint End)? SplitPreviewLine => _firstPoint.HasValue && _splitPreviewPoint.HasValue
        ? (_firstPoint.Value, _splitPreviewPoint.Value)
        : null;
    public bool ApplyBoundaryDistortion { get => _applyBoundaryDistortion; set { this.RaiseAndSetIfChanged(ref _applyBoundaryDistortion, value); RefreshPreview(); } }
    public bool ShowDistortionPreview { get => _showDistortionPreview; set { this.RaiseAndSetIfChanged(ref _showDistortionPreview, value); RefreshPreview(); } }
    public string SelectedRegionName { get => _selectedRegionName; set => RenameSelected(value); }
    public Bitmap? BackgroundImage { get => _backgroundImage; private set => this.RaiseAndSetIfChanged(ref _backgroundImage, value); }
    public string? BackgroundPath { get => _backgroundPath; private set => this.RaiseAndSetIfChanged(ref _backgroundPath, value); }
    public bool IsBackgroundVisible { get => _isBackgroundVisible; set => this.RaiseAndSetIfChanged(ref _isBackgroundVisible, value); }
    public bool IsBackgroundLocked { get => _isBackgroundLocked; set => this.RaiseAndSetIfChanged(ref _isBackgroundLocked, value); }
    public double BackgroundOpacity { get => _backgroundOpacity; set => this.RaiseAndSetIfChanged(ref _backgroundOpacity, value); }
    public double BackgroundScale { get => _backgroundScale; set => this.RaiseAndSetIfChanged(ref _backgroundScale, value); }
    public double BackgroundOffsetX { get => _backgroundOffsetX; set => this.RaiseAndSetIfChanged(ref _backgroundOffsetX, value); }
    public double BackgroundOffsetY { get => _backgroundOffsetY; set => this.RaiseAndSetIfChanged(ref _backgroundOffsetY, value); }
    public double BackgroundRotation { get => _backgroundRotation; set => this.RaiseAndSetIfChanged(ref _backgroundRotation, value); }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> MergeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteRegionCommand { get; }
    public ReactiveCommand<Unit, Unit> FitBackgroundCommand { get; }

    public RegionEditorResult CreateResult() => new(_draft, ApplyBoundaryDistortion);

    public void LoadBackground(string path)
    {
        using var stream = File.OpenRead(path);
        BackgroundImage = new Bitmap(stream);
        BackgroundPath = path;
        FitBackground();
    }

    public void SaveDraft(string path)
    {
        var document = RegionDraftCompatibility.CreateDocument(_mask, _options, _landmasses, _draft, ApplyBoundaryDistortion);
        MapRegionizer.GeoJson.RegionDraftGeoJson.WriteToFile(document, path);
        var backgroundPath = BackgroundPath is null ? null : Path.GetRelativePath(Path.GetDirectoryName(path)!, BackgroundPath);
        File.WriteAllText(path + ".editor.json", JsonSerializer.Serialize(new RegionEditorProjectState(
            backgroundPath, IsBackgroundVisible, IsBackgroundLocked, BackgroundOpacity, BackgroundScale, BackgroundOffsetX, BackgroundOffsetY, BackgroundRotation)));
    }

    public void LoadDraft(string path)
    {
        try
        {
            var document = MapRegionizer.GeoJson.RegionDraftGeoJson.ReadFromFile(path);
            RegionDraftCompatibility.EnsureCompatible(document, _mask, _options, _landmasses);
            var canonical = new RegionCoverageCanonicalizer().Canonicalize(_landmasses, document.Draft);
            if (!canonical.IsSuccessful)
            {
                Diagnostics = "Черновик не загружен: геометрия регионов невалидна." + Environment.NewLine +
                    string.Join(Environment.NewLine, canonical.Diagnostics.Select(diagnostic => diagnostic.Message));
                return;
            }

            CommitCanonicalized(document.Draft, canonical, recordUndo: true);
            ApplyBoundaryDistortion = document.ApplyBoundaryDistortion;
            LoadEditorState(path);
        }
        catch (Exception exception)
        {
            Diagnostics = "Черновик не загружен." + Environment.NewLine + exception.Message;
        }
    }

    private void LoadEditorState(string draftPath)
    {
        try
        {
            var statePath = draftPath + ".editor.json";
            if (!File.Exists(statePath))
                return;

            var state = JsonSerializer.Deserialize<RegionEditorProjectState>(File.ReadAllText(statePath));
            if (state is null)
                return;

            IsBackgroundVisible = state.IsBackgroundVisible; IsBackgroundLocked = state.IsBackgroundLocked;
            BackgroundOpacity = state.BackgroundOpacity; BackgroundScale = state.BackgroundScale;
            BackgroundOffsetX = state.BackgroundOffsetX; BackgroundOffsetY = state.BackgroundOffsetY; BackgroundRotation = state.BackgroundRotation;
            if (!string.IsNullOrWhiteSpace(state.BackgroundPath))
            {
                var background = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(draftPath)!, state.BackgroundPath));
                if (File.Exists(background))
                {
                    using var stream = File.OpenRead(background);
                    BackgroundImage = new Bitmap(stream);
                    BackgroundPath = background;
                }
            }
        }
        catch (Exception exception)
        {
            Diagnostics = "Черновик загружен, но параметры фона не удалось восстановить." + Environment.NewLine + exception.Message;
        }
    }

    public void HandlePointer(MapPoint point)
    {
        switch (SelectedTool)
        {
            case RegionEditorTool.Navigate:
                break;
            case RegionEditorTool.Select:
                SelectedRegionId = _canonicalRegions.LastOrDefault(region => region.Shape.Covers(region.Shape.Factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(point.X, point.Y))))?.Id;
                break;
            case RegionEditorTool.Split:
                HandleSplit(point);
                break;
            case RegionEditorTool.MoveVertex:
                HandleMoveVertex(point);
                break;
            case RegionEditorTool.AddVertex:
                HandleAddVertex(point);
                break;
            case RegionEditorTool.DeleteVertex:
                HandleDeleteVertex(point);
                break;
        }
    }

    private void HandleSplit(MapPoint point)
    {
        if (!SelectedRegionId.HasValue)
        {
            Diagnostics = "Сначала выберите регион для разделения.";
            return;
        }
        if (_firstPoint is null)
        {
            _firstPoint = point;
            _splitPreviewPoint = point;
            Diagnostics = "Укажите вторую точку линии разделения.";
            this.RaisePropertyChanged(nameof(SplitPreviewLine));
            return;
        }
        var factory = _canonicalRegions.Single(region => region.Id == SelectedRegionId.Value).Shape.Factory;
        var cut = factory.CreateLineString([new NetTopologySuite.Geometries.Coordinate(_firstPoint.Value.X, _firstPoint.Value.Y), new NetTopologySuite.Geometries.Coordinate(point.X, point.Y)]);
        _firstPoint = null;
        _splitPreviewPoint = null;
        this.RaisePropertyChanged(nameof(SplitPreviewLine));
        if (RegionDraftEditor.TrySplit(_draft, SelectedRegionId.Value, cut, Bounds.PixelSize * 8, out var draft, out var diagnostic)) CommitOrDiagnose(draft, diagnostic);
        else Diagnostics = diagnostic!.Message;
    }

    public void UpdatePointerPreview(MapPoint point)
    {
        if (SelectedTool != RegionEditorTool.Split || _firstPoint is null)
            return;
        _splitPreviewPoint = point;
        this.RaisePropertyChanged(nameof(SplitPreviewLine));
    }

    private void HandleMoveVertex(MapPoint point)
    {
        if (_topology is null) return;
        if (_vertexToMove is null)
        {
            var vertex = _topology.Vertices.Where(vertex => !vertex.IsCoastal).OrderBy(vertex => Distance(vertex.Position, point)).FirstOrDefault();
            if (vertex is null || Distance(vertex.Position, point) > Bounds.PixelSize * 8) { Diagnostics = "Выберите внутреннюю вершину."; return; }
            _vertexToMove = vertex.Id; Diagnostics = "Укажите новое положение вершины."; return;
        }
        if (_topology.TryMoveVertex(_vertexToMove.Value, point, out var draft, out var diagnostic)) CommitOrDiagnose(draft, diagnostic);
        else Diagnostics = diagnostic!.Message;
        _vertexToMove = null;
    }

    public bool BeginVertexDrag(MapPoint point, double hitTolerance)
    {
        if (SelectedTool != RegionEditorTool.MoveVertex || _topology is null || _isCompletingVertexDrag)
            return false;
        var vertex = _topology.Vertices.Where(vertex => !vertex.IsCoastal).OrderBy(vertex => Distance(vertex.Position, point)).FirstOrDefault();
        if (vertex is null || Distance(vertex.Position, point) > hitTolerance)
            return false;

        _draggedVertexId = vertex.Id;
        _draggedVertexPosition = vertex.Position;
        _dragDraft = null;
        CancelDragValidation();
        IsVertexDragValid = null;
        Diagnostics = "Проверка нового положения вершины…";
        this.RaisePropertyChanged(nameof(VertexMarkers));
        return true;
    }

    public void UpdateVertexDrag(MapPoint point)
    {
        if (_topology is null || _draggedVertexId is null)
            return;
        if (!_topology.TryMoveVertex(_draggedVertexId.Value, point, out var draft, out var diagnostic))
        {
            _dragDraft = null;
            _draggedVertexPosition = point;
            CancelDragValidation();
            IsVertexDragValid = false;
            Diagnostics = diagnostic?.Message ?? "Вершину нельзя переместить.";
            this.RaisePropertyChanged(nameof(VertexMarkers));
            return;
        }

        _dragDraft = draft;
        _draggedVertexPosition = point;
        _displayRegions = ToMapRegions(draft!);
        IsVertexDragValid = null;
        Diagnostics = "Проверка нового положения вершины…";
        this.RaisePropertyChanged(nameof(DisplayRegions));
        this.RaisePropertyChanged(nameof(VertexMarkers));
        ValidateDraggedDraftAsync(draft!);
    }

    public async Task EndVertexDragAsync()
    {
        if (_draggedVertexId is null || _isCompletingVertexDrag)
            return;
        var draft = _dragDraft;
        var validationTask = ReferenceEquals(_dragValidationDraft, draft) ? _dragValidationTask : null;
        _dragDraft = null;
        _isCompletingVertexDrag = true;
        Interlocked.Increment(ref _dragValidationRevision);
        this.RaisePropertyChanged(nameof(VertexMarkers));
        try
        {
            if (draft is null)
            {
                RefreshPreview();
                return;
            }

            Diagnostics = "Проверка нового положения вершины…";
            var result = validationTask is null
                ? await Task.Run(() => new RegionCoverageCanonicalizer().Canonicalize(_landmasses, draft))
                : (await validationTask) ?? await Task.Run(() => new RegionCoverageCanonicalizer().Canonicalize(_landmasses, draft));
            if (result.IsSuccessful)
            {
                CommitCanonicalized(draft, result, recordUndo: true);
                return;
            }

            IsVertexDragValid = false;
            Diagnostics = "Недопустимое положение вершины; изменение отменено." + Environment.NewLine +
                string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
            RefreshPreview();
        }
        catch (Exception exception)
        {
            IsVertexDragValid = false;
            Diagnostics = "Не удалось проверить новое положение вершины; изменение отменено." + Environment.NewLine + exception.Message;
            RefreshPreview();
        }
        finally
        {
            CancelDragValidation();
            _draggedVertexId = null;
            _draggedVertexPosition = null;
            this.RaisePropertyChanged(nameof(VertexMarkers));
            _isCompletingVertexDrag = false;
        }
    }

    private void ValidateDraggedDraftAsync(RegionDraft draft)
    {
        var revision = Interlocked.Increment(ref _dragValidationRevision);
        CancelDragValidation();
        var cancellation = _dragValidationCancellation = new CancellationTokenSource();
        _dragValidationDraft = draft;
        _dragValidationTask = ValidateDraggedDraftAsync(draft, revision, cancellation);
    }

    private async Task<RegionCanonicalizationResult?> ValidateDraggedDraftAsync(
        RegionDraft draft,
        int revision,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellation.Token);
            await _dragValidationGate.WaitAsync(cancellation.Token);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var result = await Task.Run(() => new RegionCoverageCanonicalizer().Canonicalize(_landmasses, draft), cancellation.Token);
                if (cancellation.IsCancellationRequested || revision != _dragValidationRevision || _draggedVertexId is null)
                    return result;

                IsVertexDragValid = result.IsSuccessful;
                Diagnostics = result.IsSuccessful
                    ? "Положение вершины допустимо."
                    : string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
                return result;
            }
            finally
            {
                _dragValidationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer pointer position superseded this preview validation.
            return null;
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested || revision != _dragValidationRevision || _draggedVertexId is null)
                return null;
            IsVertexDragValid = false;
            Diagnostics = "Не удалось проверить новое положение вершины." + Environment.NewLine + exception.Message;
            return null;
        }
    }

    private void CancelDragValidation()
    {
        _dragValidationCancellation?.Cancel();
        _dragValidationCancellation = null;
        _dragValidationDraft = null;
        _dragValidationTask = null;
    }

    private void HandleAddVertex(MapPoint point)
    {
        if (_topology is null) return;
        var edge = _topology.Edges.Where(edge => !edge.IsCoastal).OrderBy(edge => DistanceToEdge(edge, point)).FirstOrDefault();
        if (edge is null || DistanceToEdge(edge, point) > Bounds.PixelSize * 5) { Diagnostics = "Выберите общую внутреннюю границу."; return; }
        var start = _topology.Vertices.Single(vertex => vertex.Id == edge.StartVertexId).Position;
        var end = _topology.Vertices.Single(vertex => vertex.Id == edge.EndVertexId).Position;
        var projection = Project(point, start, end);
        if (_topology.TryInsertVertex(edge.Id, projection, out var draft, out var diagnostic)) CommitOrDiagnose(draft, diagnostic);
        else Diagnostics = diagnostic!.Message;
    }

    private void HandleDeleteVertex(MapPoint point)
    {
        if (_topology is null) return;
        var vertex = _topology.Vertices.Where(vertex => !vertex.IsCoastal).OrderBy(vertex => Distance(vertex.Position, point)).FirstOrDefault();
        if (vertex is null || Distance(vertex.Position, point) > Bounds.PixelSize * 8) { Diagnostics = "Выберите внутреннюю вершину для удаления."; return; }
        if (_topology.TryDeleteVertex(vertex.Id, out var draft, out var diagnostic)) CommitOrDiagnose(draft, diagnostic);
        else Diagnostics = diagnostic!.Message;
    }

    private void BeginMerge()
    {
        if (!SelectedRegionId.HasValue)
            return;
        _mergeSourceRegionId = SelectedRegionId;
        this.RaisePropertyChanged(nameof(IsAwaitingMergeTarget));
        SelectedTool = RegionEditorTool.Select;
        Diagnostics = $"Выберите регион, который нужно объединить с регионом {_mergeSourceRegionId.Value.Value}.";
    }

    private void CompleteMergeIfTargetSelected()
    {
        if (!_mergeSourceRegionId.HasValue || !SelectedRegionId.HasValue)
            return;
        if (_mergeSourceRegionId == SelectedRegionId)
        {
            Diagnostics = "Выберите другой регион для объединения.";
            return;
        }

        var retained = _mergeSourceRegionId.Value;
        var removed = SelectedRegionId.Value;
        _mergeSourceRegionId = null;
        this.RaisePropertyChanged(nameof(IsAwaitingMergeTarget));
        if (RegionDraftEditor.TryMerge(_draft, retained, removed, out var draft, out var diagnostic))
        {
            CommitOrDiagnose(draft, diagnostic);
            SelectedRegionId = retained;
        }
        else
        {
            Diagnostics = diagnostic!.Message;
        }
    }

    private void MergeSelectedWithFirstNeighbour()
    {
        if (!SelectedRegionId.HasValue || _topology is null) return;
        var neighbour = _topology.Edges.Where(edge => edge.FaceIds.Contains(SelectedRegionId.Value) && edge.FaceIds.Count == 2)
            .SelectMany(edge => edge.FaceIds).Where(id => id != SelectedRegionId.Value).OrderBy(id => id.Value).FirstOrDefault();
        if (neighbour.Value <= 0) { Diagnostics = "У выбранного региона нет соседей для удаления."; return; }
        if (RegionDraftEditor.TryMerge(_draft, neighbour, SelectedRegionId.Value, out var draft, out var diagnostic)) CommitOrDiagnose(draft, diagnostic);
        else Diagnostics = diagnostic!.Message;
    }

    private void RenameSelected(string name)
    {
        if (_selectedRegionName == name) return;
        this.RaiseAndSetIfChanged(ref _selectedRegionName, name);
        if (!SelectedRegionId.HasValue) return;
        _draft = new RegionDraft(_draft.Regions.Select(region => region.Id == SelectedRegionId ? region with { Name = name } : region).ToList());
        RefreshSelection();
    }

    private void CommitOrDiagnose(RegionDraft? draft, RegionDiagnostic? diagnostic)
    {
        if (draft is null) { Diagnostics = diagnostic?.Message ?? "Изменение не удалось."; return; }
        Commit(draft, recordUndo: true);
    }

    private void Commit(RegionDraft draft, bool recordUndo)
    {
        var result = new RegionCoverageCanonicalizer().Canonicalize(_landmasses, draft);
        Diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
        if (!result.IsSuccessful) return;
        CommitCanonicalized(draft, result, recordUndo);
    }

    private void CommitCanonicalized(RegionDraft draft, RegionCanonicalizationResult result, bool recordUndo)
    {
        if (recordUndo) { _undo.Push(_draft); _redo.Clear(); }
        var priorById = draft.Regions.Where(region => region.Id.HasValue).ToDictionary(region => region.Id!.Value);
        _draft = new RegionDraft(result.Regions.Select(region =>
        {
            if (priorById.TryGetValue(region.Id, out var prior))
                return prior with { Id = region.Id, LandmassId = region.LandmassId, Shape = region.Shape.Copy() };
            return new RegionDraftRegion(region.Id, region.LandmassId, region.Shape.Copy(), RegionDraftOrigin.GeneratedAndEdited);
        }).ToList());
        _canonicalRegions = result.Regions;
        _topology = RegionTopology.CreateFromVerifiedCoverage(result.Regions);
        RefreshPreview();
        RefreshSelection();
        this.RaisePropertyChanged(nameof(CanUndo)); this.RaisePropertyChanged(nameof(CanRedo));
    }

    private void RefreshFromDraft() => Commit(_draft, recordUndo: false);

    private void Undo() { if (_undo.TryPop(out var draft)) { _redo.Push(_draft); Commit(draft, recordUndo: false); } }
    private void Redo() { if (_redo.TryPop(out var draft)) { _undo.Push(_draft); Commit(draft, recordUndo: false); } }

    private void RefreshPreview()
    {
        _displayRegions = _canonicalRegions;
        if (ShowDistortionPreview && ApplyBoundaryDistortion)
        {
            try
            {
                var previewOptions = CloneWithDistortion(_options, true);
                var session = MapGenerationSession.Create(_mask, previewOptions);
                session.RunUntil(MapDataKeys.Landmasses);
                session.SetRegionDraft(_draft);
                session.RunUntil(MapDataKeys.Regions);
                _displayRegions = session.Regions;
            }
            catch (Exception exception) { Diagnostics = exception.Message; }
        }
        this.RaisePropertyChanged(nameof(DisplayRegions));
    }

    private void RefreshSelection()
    {
        Regions.Clear();
        foreach (var region in _canonicalRegions.OrderBy(region => region.Id.Value))
            Regions.Add(new RegionEditorRegionViewModel(region.Id, region.LandmassId, region.Id == SelectedRegionId));
        _selectedRegion = Regions.FirstOrDefault(region => region.Id == SelectedRegionId);
        this.RaisePropertyChanged(nameof(SelectedRegion));
        var selected = _draft.Regions.SingleOrDefault(region => region.Id == SelectedRegionId);
        this.RaiseAndSetIfChanged(ref _selectedRegionName, selected?.Name ?? string.Empty, nameof(SelectedRegionName));
        this.RaisePropertyChanged(nameof(HasSelection));
    }

    private static IReadOnlyList<MapRegion> ToMapRegions(RegionDraft draft) => draft.Regions
        .Where(region => region.Id.HasValue && region.LandmassId.HasValue && region.Shape is NetTopologySuite.Geometries.Polygon)
        .Select(region => new MapRegion(region.Id!.Value, region.LandmassId!.Value, (NetTopologySuite.Geometries.Polygon)region.Shape)).ToList();

    private void FitBackground() { BackgroundScale = 1; BackgroundOffsetX = 0; BackgroundOffsetY = 0; BackgroundRotation = 0; }
    private static double Distance(MapPoint a, MapPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private double DistanceToEdge(RegionTopologyEdge edge, MapPoint point)
    {
        var start = _topology!.Vertices.Single(vertex => vertex.Id == edge.StartVertexId).Position;
        var end = _topology.Vertices.Single(vertex => vertex.Id == edge.EndVertexId).Position;
        return Distance(Project(point, start, end), point);
    }
    private static MapPoint Project(MapPoint point, MapPoint start, MapPoint end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y; var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return new MapPoint(start.X + t * dx, start.Y + t * dy);
    }
    private static MapGenerationOptions CloneWithDistortion(MapGenerationOptions options, bool enabled) => new()
    {
        PixelSize = options.PixelSize,
        Seed = options.Seed,
        ProjectionMode = options.ProjectionMode,
        ShapeExtraction = options.ShapeExtraction,
        WaterBodies = options.WaterBodies,
        Regions = options.Regions,
        TectonicPlates = options.TectonicPlates,
        Elevation = options.Elevation,
        Hydrology = options.Hydrology,
        Climate = options.Climate,
        Boundaries = new BoundaryDistortionOptions { Enabled = enabled, Detail = options.Boundaries.Detail, MaxOffset = options.Boundaries.MaxOffset, MinLineLengthToCurve = options.Boundaries.MinLineLengthToCurve }
    };
}

public sealed record RegionEditorRegionViewModel(RegionId Id, LandmassId LandmassId, bool IsSelected)
{
    public string Label => $"Регион {Id.Value} · суша {LandmassId.Value}";
}

public sealed record RegionEditorProjectState(string? BackgroundPath, bool IsBackgroundVisible, bool IsBackgroundLocked,
    double BackgroundOpacity, double BackgroundScale, double BackgroundOffsetX, double BackgroundOffsetY, double BackgroundRotation);
