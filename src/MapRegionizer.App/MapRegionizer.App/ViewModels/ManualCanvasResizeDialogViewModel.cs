using Avalonia.Media.Imaging;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Spatial;
using ReactiveUI;

namespace MapRegionizer.App.ViewModels;

public sealed class ManualCanvasResizeDialogViewModel : ReactiveObject
{
    private readonly ManualMapDraft _draft;
    private readonly MapSpatialReference _spatialReference;
    private readonly ManualMapCanvasResizer _resizer;
    private int _topCells;
    private int _bottomCells;
    private int _leftCells;
    private int _rightCells;
    private ManualCanvasResizeResult? _previewResult;
    private string _errorMessage = string.Empty;

    public ManualCanvasResizeDialogViewModel(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        ManualMapCanvasResizer resizer,
        Bitmap? backgroundImage,
        bool isBackgroundVisible,
        double backgroundOpacity,
        double backgroundMapX,
        double backgroundMapY,
        double backgroundMapWidth,
        double backgroundMapHeight,
        double backgroundRotation)
    {
        _draft = draft;
        _spatialReference = spatialReference;
        _resizer = resizer;
        BackgroundImage = backgroundImage;
        IsBackgroundVisible = isBackgroundVisible;
        BackgroundOpacity = backgroundOpacity;
        BackgroundMapX = backgroundMapX;
        BackgroundMapY = backgroundMapY;
        BackgroundMapWidth = backgroundMapWidth;
        BackgroundMapHeight = backgroundMapHeight;
        BackgroundRotation = backgroundRotation;
        UpdatePreview();
    }

    public int CurrentWidth => _draft.GridWidth;
    public int CurrentHeight => _draft.GridHeight;
    public double UnitsPerCell => _spatialReference.UnitsPerCell;
    public double OldWidthMapUnits => _spatialReference.WidthInMapUnits;
    public double OldHeightMapUnits => _spatialReference.HeightInMapUnits;
    public bool IsGlobal => _spatialReference.Coverage.IsGlobal;
    public bool IsHorizontalResizeEnabled => !IsGlobal;
    public int TopCells { get => _topCells; set { if (_topCells != value) { this.RaiseAndSetIfChanged(ref _topCells, value); UpdatePreview(); } } }
    public int BottomCells { get => _bottomCells; set { if (_bottomCells != value) { this.RaiseAndSetIfChanged(ref _bottomCells, value); UpdatePreview(); } } }
    public int LeftCells { get => _leftCells; set { if (_leftCells != value) { this.RaiseAndSetIfChanged(ref _leftCells, value); UpdatePreview(); } } }
    public int RightCells { get => _rightCells; set { if (_rightCells != value) { this.RaiseAndSetIfChanged(ref _rightCells, value); UpdatePreview(); } } }

    public ManualCanvasResizeResult? PreviewResult
    {
        get => _previewResult;
        private set => this.RaiseAndSetIfChanged(ref _previewResult, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool CanApply => PreviewResult is not null && string.IsNullOrEmpty(ErrorMessage) &&
                            (TopCells != 0 || BottomCells != 0 || LeftCells != 0 || RightCells != 0);
    public string CurrentSizeLabel => $"{CurrentWidth} × {CurrentHeight} ячеек";
    public string NewSizeLabel => PreviewResult is null
        ? $"{(long)CurrentWidth + LeftCells + RightCells} × {(long)CurrentHeight + TopCells + BottomCells} ячеек"
        : $"{PreviewResult.Draft.GridWidth} × {PreviewResult.Draft.GridHeight} ячеек";
    public string CoverageLabel => PreviewResult is null
        ? $"Сейчас: {FormatCoverage(_spatialReference)}"
        : $"После изменения: {FormatCoverage(PreviewResult.SpatialReference)}";
    public string GeometryImpactLabel => _draft.Vertices.Count == 0
        ? "Существующих вершин нет."
        : LeftCells != 0 || TopCells != 0
            ? $"Существующие вершины будут перенесены на {LeftCells * UnitsPerCell:0.###} × {TopCells * UnitsPerCell:0.###} map units; их IDs и границы регионов сохранятся."
            : "Положение существующих вершин не изменится.";

    public Bitmap? BackgroundImage { get; }
    public bool IsBackgroundVisible { get; }
    public double BackgroundOpacity { get; }
    public double BackgroundMapX { get; }
    public double BackgroundMapY { get; }
    public double BackgroundMapWidth { get; }
    public double BackgroundMapHeight { get; }
    public double BackgroundRotation { get; }
    public double PreviewWidthMapUnits => PreviewResult?.SpatialReference.WidthInMapUnits ?? OldWidthMapUnits;
    public double PreviewHeightMapUnits => PreviewResult?.SpatialReference.HeightInMapUnits ?? OldHeightMapUnits;
    public double PreviewOldX => LeftCells * UnitsPerCell;
    public double PreviewOldY => TopCells * UnitsPerCell;
    public double PreviewOldWidth => OldWidthMapUnits;
    public double PreviewOldHeight => OldHeightMapUnits;
    public double PreviewBackgroundX => BackgroundMapX + PreviewOldX;
    public double PreviewBackgroundY => BackgroundMapY + PreviewOldY;
    public IReadOnlyList<IReadOnlyList<MapPoint>> PreviewRegionPoints
    {
        get
        {
            var positions = _draft.Vertices.ToDictionary(vertex => vertex.Id, vertex => vertex.Position);
            return _draft.Regions
                .Select(region => (IReadOnlyList<MapPoint>)region.VertexIds
                    .Where(positions.ContainsKey)
                    .Select(id => positions[id] with { X = positions[id].X + PreviewOldX, Y = positions[id].Y + PreviewOldY })
                    .ToArray())
                .ToArray();
        }
    }

    public ManualCanvasResizeRequest Request => new(TopCells, BottomCells, LeftCells, RightCells);

    private void UpdatePreview()
    {
        PreviewResult = null;
        if (TopCells == 0 && BottomCells == 0 && LeftCells == 0 && RightCells == 0)
        {
            ErrorMessage = "Укажите изменение хотя бы одного края.";
        }
        else
        {
            try
            {
                PreviewResult = _resizer.Resize(_draft, _spatialReference, Request);
                ErrorMessage = string.Empty;
            }
            catch (ManualCanvasResizeException exception)
            {
                ErrorMessage = exception.Message;
            }
            catch (Exception exception)
            {
                ErrorMessage = $"Невозможно изменить холст: {exception.Message}";
            }
        }

        this.RaisePropertyChanged(nameof(CanApply));
        this.RaisePropertyChanged(nameof(NewSizeLabel));
        this.RaisePropertyChanged(nameof(CoverageLabel));
        this.RaisePropertyChanged(nameof(GeometryImpactLabel));
        this.RaisePropertyChanged(nameof(PreviewWidthMapUnits));
        this.RaisePropertyChanged(nameof(PreviewHeightMapUnits));
        this.RaisePropertyChanged(nameof(PreviewOldX));
        this.RaisePropertyChanged(nameof(PreviewOldY));
        this.RaisePropertyChanged(nameof(PreviewOldWidth));
        this.RaisePropertyChanged(nameof(PreviewOldHeight));
        this.RaisePropertyChanged(nameof(PreviewBackgroundX));
        this.RaisePropertyChanged(nameof(PreviewBackgroundY));
    }

    private static string Format(double value) => value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    private static string FormatCoverage(MapSpatialReference reference) => reference.Coverage.IsGlobal
        ? $"360° по долготе; {Format(reference.SouthLatitude)}…{Format(reference.NorthLatitude)}° по широте"
        : $"{Format(reference.Coverage.West)}…{Format(reference.Coverage.East)}° по долготе; {Format(reference.SouthLatitude)}…{Format(reference.NorthLatitude)}° по широте";
}
