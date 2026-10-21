using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Spatial;
using ReactiveUI;
using System.Threading;
using System.Threading.Tasks;

namespace MapRegionizer.App.ViewModels;

public sealed class ManualCanvasResizeDialogViewModel : ReactiveObject, IDisposable
{
    private const int PreviewDebounceMilliseconds = 150;

    private readonly ManualMapDraft _draft;
    private readonly MapSpatialReference _spatialReference;
    private readonly ManualMapCanvasResizer _resizer;
    private CancellationTokenSource? _previewCancellation;
    private int _previewVersion;
    private int _topCells;
    private int _bottomCells;
    private int _leftCells;
    private int _rightCells;
    private ManualCanvasResizeResult? _previewResult;
    private string _errorMessage = string.Empty;
    private bool _isCalculating;
    private bool _isApplying;
    private bool _disposed;

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

    public ManualMapDraft Draft => _draft;
    public int CurrentWidth => _draft.GridWidth;
    public int CurrentHeight => _draft.GridHeight;
    public double UnitsPerCell => _spatialReference.UnitsPerCell;
    public double OldWidthMapUnits => _spatialReference.WidthInMapUnits;
    public double OldHeightMapUnits => _spatialReference.HeightInMapUnits;
    public bool IsGlobal => _spatialReference.Coverage.IsGlobal;
    public bool IsHorizontalResizeEnabled => !IsGlobal;

    public int TopCells
    {
        get => _topCells;
        set
        {
            if (_topCells == value)
                return;
            this.RaiseAndSetIfChanged(ref _topCells, value);
            UpdatePreview();
        }
    }

    public int BottomCells
    {
        get => _bottomCells;
        set
        {
            if (_bottomCells == value)
                return;
            this.RaiseAndSetIfChanged(ref _bottomCells, value);
            UpdatePreview();
        }
    }

    public int LeftCells
    {
        get => _leftCells;
        set
        {
            if (_leftCells == value)
                return;
            this.RaiseAndSetIfChanged(ref _leftCells, value);
            UpdatePreview();
        }
    }

    public int RightCells
    {
        get => _rightCells;
        set
        {
            if (_rightCells == value)
                return;
            this.RaiseAndSetIfChanged(ref _rightCells, value);
            UpdatePreview();
        }
    }

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

    public bool IsCalculating
    {
        get => _isCalculating;
        private set => this.RaiseAndSetIfChanged(ref _isCalculating, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        private set => this.RaiseAndSetIfChanged(ref _isApplying, value);
    }

    public bool IsBusy => IsCalculating || IsApplying;
    public string BusyMessage => IsApplying ? "Проверка геометрии и применение…" : "Пересчёт preview…";

    public bool CanApply => !IsBusy && PreviewResult is not null && string.IsNullOrEmpty(ErrorMessage) &&
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
    public ManualCanvasResizeRequest Request => new(TopCells, BottomCells, LeftCells, RightCells);

    public void SetApplying(bool isApplying)
    {
        if (_disposed || IsApplying == isApplying)
            return;

        IsApplying = isApplying;
        RaiseBusyProperties();
    }

    public void ShowError(string message)
    {
        if (_disposed)
            return;

        ErrorMessage = message;
        RaisePreviewProperties();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _previewVersion++;
        _previewCancellation?.Cancel();
        _previewCancellation = null;
    }

    private void UpdatePreview()
    {
        if (_disposed)
            return;

        _previewCancellation?.Cancel();
        _previewCancellation = null;
        var version = ++_previewVersion;
        PreviewResult = null;

        var request = Request;
        if (request.TopCells == 0 && request.BottomCells == 0 && request.LeftCells == 0 && request.RightCells == 0)
        {
            IsCalculating = false;
            ErrorMessage = "Укажите изменение хотя бы одного края.";
            RaisePreviewProperties();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        ErrorMessage = string.Empty;
        IsCalculating = true;
        RaisePreviewProperties();
        _ = CalculatePreviewAsync(request, version, cancellation);
    }

    private async Task CalculatePreviewAsync(
        ManualCanvasResizeRequest request,
        int version,
        CancellationTokenSource cancellation)
    {
        ManualCanvasResizeResult? result = null;
        var errorMessage = string.Empty;
        var cancellationToken = cancellation.Token;

        try
        {
            await Task.Delay(PreviewDebounceMilliseconds, cancellationToken);
            result = await Task.Run(
                () => _resizer.Resize(_draft, _spatialReference, request, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ManualCanvasResizeException exception)
        {
            errorMessage = exception.Message;
        }
        catch (Exception exception)
        {
            errorMessage = $"Невозможно изменить холст: {exception.Message}";
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                cancellation.Dispose();
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || version != _previewVersion || cancellationToken.IsCancellationRequested)
                return;

            if (ReferenceEquals(_previewCancellation, cancellation))
                _previewCancellation = null;
            PreviewResult = result;
            ErrorMessage = errorMessage;
            IsCalculating = false;
            RaisePreviewProperties();
        });
        cancellation.Dispose();
    }

    private void RaiseBusyProperties()
    {
        this.RaisePropertyChanged(nameof(IsBusy));
        this.RaisePropertyChanged(nameof(BusyMessage));
        this.RaisePropertyChanged(nameof(CanApply));
    }

    private void RaisePreviewProperties()
    {
        RaiseBusyProperties();
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
