using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MapRegionizer.App.Services;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Spatial;
using ReactiveUI;

namespace MapRegionizer.App.Views;

/// <summary>Editable GCP list, independent accuracy report, and validated geometry preview.</summary>
public sealed class GeographicCalibrationDialog : Window
{
    private readonly ManualMapEditorViewModel _editor;
    private readonly MapSpatialReference _currentReference;
    private readonly MapSpatialReference _sourceBaseReference;
    private readonly bool _modelAlreadyApplied;
    private readonly bool _backgroundAlreadyApplied;
    private readonly ComboBox _mapping;
    private readonly NumericUpDown _sourceSouth;
    private readonly NumericUpDown _sourceNorth;
    private readonly ListBox _pointsList;
    private readonly ObservableCollection<GeographicControlPointRowViewModel> _rows;
    private readonly MapTransformFeedback _report;
    private readonly TextBlock _pointCount;
    private readonly TextBlock _applicationHint;
    private readonly TextBox _pointErrors;
    private readonly TabControl _tabs;
    private readonly Button _fitButton;
    private readonly Button _relocateSeam;
    private bool _useContinuousSeam;
    private bool _isApplying;
    private readonly ManualMapReprojectionPreview _preview;
    private readonly Button _apply;
    private readonly Button _applyBackground;
    private readonly Button _applyBoth;
    private readonly Button _savePoints;
    private GeographicCalibrationResult? _fit;
    private ManualMapReprojectionRequest? _previewRequest;
    private ManualMapReprojectionResult? _previewResult;
    private IReadOnlyList<GeographicControlPoint>? _fitPoints;
    private CancellationTokenSource? _previewCancellation;
    private byte[]? _reprojectedBackgroundPng;
    private readonly Bitmap? _sourceBackgroundPreview;

    public GeographicCalibrationDialog(ManualMapEditorViewModel editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _currentReference = editor.SpatialReference;
        _modelAlreadyApplied = editor.CalibrationParameters?.IsApplied == true;
        _backgroundAlreadyApplied = editor.CalibrationParameters?.IsAppliedToBackground == true || editor.BackgroundReprojectionState is not null;
        _sourceBaseReference = _modelAlreadyApplied
            ? _currentReference
            : editor.CalibrationParameters?.IsAppliedToBackground == true
            ? editor.CalibrationParameters.SourceReference ?? _currentReference
            : editor.CalibrationParameters?.SourceReference ?? editor.BackgroundReprojectionState?.SourceReference ?? _currentReference;
        Title = "Географическая привязка и опорные точки";
        Width = 1080;
        Height = 760;
        MinWidth = 740;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _rows = new ObservableCollection<GeographicControlPointRowViewModel>(
            editor.ControlPoints.Select(point => new GeographicControlPointRowViewModel(point)));
        foreach (var row in _rows)
            ObserveRow(row);
        _rows.CollectionChanged += OnRowsChanged;
        _mapping = MapTransformDialogUi.Combo(Enum.GetValues<GridMappingKind>(), _sourceBaseReference.GridMapping);
        var defaultSouth = _sourceBaseReference.GridMapping == GridMappingKind.WebMercator
            ? Math.Max(-WebMercatorGridMapping.WebMercatorLatitudeLimit, _sourceBaseReference.SouthLatitude)
            : _sourceBaseReference.SouthLatitude;
        var defaultNorth = _sourceBaseReference.GridMapping == GridMappingKind.WebMercator
            ? Math.Min(WebMercatorGridMapping.WebMercatorLatitudeLimit, _sourceBaseReference.NorthLatitude)
            : _sourceBaseReference.NorthLatitude;
        _sourceSouth = Number(defaultSouth, -90m, 89.99m);
        _sourceNorth = Number(defaultNorth, -89.99m, 90m);
        _pointsList = new ListBox { ItemsSource = _rows, ItemTemplate = CreatePointTemplate() };
        ScrollViewer.SetHorizontalScrollBarVisibility(_pointsList, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _pointsList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        _report = new MapTransformFeedback();
        _pointCount = MapTransformDialogUi.Text("");
        _applicationHint = MapTransformDialogUi.Text("");
        _pointErrors = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 260 };
        _preview = new ManualMapReprojectionPreview { SourceDraft = editor.Draft, SourceReference = _currentReference };
        _sourceBackgroundPreview = MapTransformDialogUi.CloneBackground(editor.BackgroundImage);
        _preview.SourceBackgroundImage = _sourceBackgroundPreview;
        _preview.SourceBackgroundPlacement = editor.BackgroundPlacement;
        _apply = new Button { Content = "К геометрии…", IsEnabled = false };
        _applyBackground = new Button { Content = "К фону…", IsEnabled = false };
        _applyBoth = new Button { Content = "К обоим слоям…", IsEnabled = false };
        _savePoints = new Button { Content = "Сохранить точки", IsEnabled = false };

        _mapping.SelectionChanged += (_, _) =>
        {
            if (_mapping.SelectedItem is GridMappingKind.WebMercator)
            {
                var limit = (decimal)WebMercatorGridMapping.WebMercatorLatitudeLimit;
                if ((_sourceSouth.Value ?? 0) < -limit)
                    _sourceSouth.Value = -limit;
                if ((_sourceNorth.Value ?? 0) > limit)
                    _sourceNorth.Value = limit;
            }
            InvalidateFit();
        };
        _sourceSouth.PropertyChanged += (_, args) => { if (args.Property == NumericUpDown.ValueProperty || args.Property == NumericUpDown.TextProperty) InvalidateFit(); };
        _sourceNorth.PropertyChanged += (_, args) => { if (args.Property == NumericUpDown.ValueProperty || args.Property == NumericUpDown.TextProperty) InvalidateFit(); };
        _apply.Click += async (_, _) => await ApplyToGeometryAsync();
        _applyBackground.Click += async (_, _) => await ApplyToBackgroundAsync(includeGeometry: false);
        _applyBoth.Click += async (_, _) => await ApplyToBackgroundAsync(includeGeometry: true);
        _savePoints.Click += (_, _) => SavePointsAndClose();
        _fitButton = new Button { Content = "Подобрать и проверить" };
        _fitButton.Click += async (_, _) => await FitAndPreviewAsync();
        _relocateSeam = new Button { Content = "Сохранить контуры и проверить привязку", IsVisible = false };
        _relocateSeam.Click += async (_, _) =>
        {
            _useContinuousSeam = true;
            await FitAndPreviewAsync();
        };
        var removeButton = new Button { Content = "Удалить выбранную точку" };
        removeButton.Click += (_, _) =>
        {
            if (_pointsList.SelectedItem is GeographicControlPointRowViewModel row)
                _rows.Remove(row);
            InvalidateFit();
        };
        var cancel = new Button { Content = "Отмена", MinWidth = 90 };
        cancel.Click += (_, _) => Close();

        var pointsHeader = new StackPanel
        {
            Spacing = 8,
            Children =
        {
            MapTransformDialogUi.Text("Все известные точки можно использовать для подбора. Роль «Для проверки» — для дополнительного сравнения без участия в подборе."),
            _pointCount
        }
        };
        var pointsPage = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        pointsPage.Children.Add(pointsHeader);
        Grid.SetRow(_pointsList, 1);
        pointsPage.Children.Add(_pointsList);
        Grid.SetRow(removeButton, 2);
        pointsPage.Children.Add(removeButton);

        var settings = new StackPanel { Spacing = 16, Margin = new Thickness(0, 14, 12, 14) };
        settings.Children.Add(MapTransformDialogUi.Text("Как интерпретировать исходную карту", true));
        settings.Children.Add(MapTransformDialogUi.Field("Исходная проекция", _mapping));
        settings.Children.Add(MapTransformDialogUi.Pair(
            MapTransformDialogUi.Field("Южная широта, °", _sourceSouth),
            MapTransformDialogUi.Field("Северная широта, °", _sourceNorth)));
        settings.Children.Add(MapTransformDialogUi.Text("Модель подбирает сдвиг долготы и границы широт. Она не исправляет локальные искажения изображения."));
        settings.Children.Add(_fitButton);
        settings.Children.Add(_report);
        settings.Children.Add(new Expander { Header = "Ошибки отдельных точек · широта, долгота", Content = _pointErrors, HorizontalAlignment = HorizontalAlignment.Stretch });

        _tabs = new TabControl
        {
            ItemsSource = new[]
        {
            new TabItem { Header = "1. Опорные точки", Content = pointsPage },
            new TabItem { Header = "2. Подбор и проверка", Content = MapTransformDialogUi.Scroll(settings) },
            new TabItem { Header = "3. До и после", Content = _preview }
        }
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _savePoints, _relocateSeam, _apply, _applyBackground, _applyBoth, cancel })
        {
            button.Margin = new Thickness(0, 6, 8, 0);
            actions.Children.Add(button);
        }
        var footer = new StackPanel { Spacing = 4, Children = { _applicationHint, actions } };
        Content = MapTransformDialogUi.Root("Географическая привязка", "Укажите ориентиры → проверьте модель → сравните и примените результат", _tabs, footer);
        _report.Text = "Укажите известные координаты точек, затем нажмите «Подобрать и проверить».";
        if (_editor.CalibrationReport is { } savedReport)
        {
            _fit = savedReport;
            _report.Show("Сохранённый результат. " + (savedReport.Metrics is { } savedMetrics
                ? FitErrorSummary(savedMetrics) + "\n" + ValidationSummary(savedMetrics) : "Нет данных об ошибках точек."));
            ShowPointErrors(savedReport);
        }
        RefreshButtons();
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _sourceBackgroundPreview?.Dispose();
        if (_preview.TargetBackgroundImage is { } targetImage)
            targetImage.Dispose();
        base.OnClosed(e);
    }

    private async Task FitAndPreviewAsync()
    {
        if (_modelAlreadyApplied || _backgroundAlreadyApplied || _isApplying)
            return;
        InvalidateFit();
        IReadOnlyList<GeographicControlPoint> points;
        MapSpatialReference sourceReference;
        try
        {
            points = _rows.Select(row => row.ToPoint()).ToArray();
            sourceReference = BuildSourceReference();
            if (points.Select(point => point.Id).Distinct().Count() != points.Count)
                throw new ArgumentException("Идентификаторы точек должны быть уникальными.");
        }
        catch (Exception exception)
        {
            _report.Show(exception is ArgumentException && exception.Message.Contains("Можно использовать", StringComparison.Ordinal)
                ? exception.Message : "Проверьте координаты точек и исходные широты: южная должна быть меньше северной, а для Меркатора обе — в пределах ±85,0511°.", exception.ToString());
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        _fit = null;
        _previewResult = null;
        _previewRequest = null;
        ClearWarpPreview();
        _apply.IsEnabled = false;
        _applyBackground.IsEnabled = false;
        _applyBoth.IsEnabled = false;
        _savePoints.IsEnabled = points.Count > 0;
        _report.Text = "Подбирается модель и проверяется геометрия…";
        _applicationHint.Text = "Выполняется расчёт. Изменение точек отменит его.";

        try
        {
            var fit = await Task.Run(() => new ParametricGeoreferenceSolver().Solve(new GeographicCalibrationRequest(
                sourceReference,
                points,
                _editor.BackgroundPlacement)), token);
            if (token.IsCancellationRequested)
                return;
            _fit = fit;
            _fitPoints = points;
            if (!fit.IsSuccessful || fit.Parameters is null)
            {
                _report.Show(MapTransformDialogUi.Diagnostic(fit.Diagnostics.Count > 0 ? fit.Diagnostics[0].Code : ""),
                    string.Join(Environment.NewLine, fit.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
                _applicationHint.Text = "Модель не подобрана. Исправьте данные на вкладке «Подбор и проверка».";
                return;
            }

            var transform = ParametricGeoreferenceSolver.CreateTransform(sourceReference, fit.Parameters);
            var request = new ManualMapReprojectionRequest(
                _editor.Draft,
                sourceReference,
                _currentReference,
                GeoreferenceTransform: transform);
            if (_useContinuousSeam)
                request = request with { TargetReference = transform.CreateCalibratedReference() };
            var reprojection = await Task.Run(() => new ManualMapReprojectionService().Reproject(request, token), token);
            if (token.IsCancellationRequested)
                return;
            var backgroundPath = _editor.BackgroundPath;
            var sourcePlacement = _editor.BackgroundWarpSourcePlacement ?? _editor.BackgroundPlacement;
            string? backgroundError = null;
            if (!string.IsNullOrWhiteSpace(backgroundPath) && sourcePlacement is not null)
            {
                try
                {
                    _reprojectedBackgroundPng = await Task.Run(() => ManualMapBackgroundReprojector.ReprojectToPng(
                        backgroundPath,
                        sourcePlacement,
                        fit.Parameters.SourceReference ?? sourceReference,
                        request.TargetReference,
                        georeferenceTransform: transform,
                        cancellationToken: token), token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    backgroundError = exception.ToString();
                }
                if (token.IsCancellationRequested)
                    return;
                if (_reprojectedBackgroundPng is not null)
                {
                    using var imageStream = new MemoryStream(_reprojectedBackgroundPng);
                    _preview.TargetBackgroundImage?.Dispose();
                    _preview.TargetBackgroundImage = new Bitmap(imageStream);
                }
                _preview.SourceBackgroundPlacement = sourcePlacement;
            }
            _previewRequest = request;
            _previewResult = reprojection;
            _preview.SourceReference = sourceReference;
            _preview.TargetDraft = reprojection.Draft ?? (_reprojectedBackgroundPng is not null ? _editor.Draft : null);
            _preview.TargetLabel = reprojection.IsSuccessful ? "После" : "После · только фон";
            _preview.TargetReference = reprojection.TargetReference;
            UpdatePreviewMarkers(points, fit);
            _preview.InvalidateVisual();
            _savePoints.IsEnabled = true;
            _apply.IsEnabled = reprojection.IsSuccessful && !_modelAlreadyApplied;
            var relocated = request.TargetReference.Longitude != _currentReference.Longitude;
            var referenceChanged = request.TargetReference != _currentReference;
            _applyBackground.IsEnabled = _reprojectedBackgroundPng is not null && !_backgroundAlreadyApplied && !_modelAlreadyApplied && !referenceChanged;
            _applyBoth.IsEnabled = reprojection.IsSuccessful && _reprojectedBackgroundPng is not null && !_backgroundAlreadyApplied && !_modelAlreadyApplied;
            _relocateSeam.IsVisible = !reprojection.IsSuccessful &&
                reprojection.Diagnostics.Any(item => item.Code is "seam-crossing-unsupported" or "self-intersection" or "coordinate-out-of-range") &&
                transform.CreateCalibratedReference() != request.TargetReference;
            var metrics = fit.Metrics!;
            var summary = $"Модель подобрана. Сдвиг долготы {MapTransformDialogUi.Degrees(fit.Parameters.LongitudeOffsetDegrees)}; широты {MapTransformDialogUi.Degrees(fit.Parameters.SourceSouthLatitude)}…{MapTransformDialogUi.Degrees(fit.Parameters.SourceNorthLatitude)}.\n" +
                FitErrorSummary(metrics) + "\n" + ValidationSummary(metrics) + "\n" +
                (reprojection.IsSuccessful ? "Геометрия проверена. Сравните результат на вкладке «До и после»." : MapTransformDialogUi.Diagnostic(reprojection.Diagnostics.Count > 0 ? reprojection.Diagnostics[0].Code : ""));
            if (fit.Diagnostics.Any(item => item.Code is "conflicting-longitudes" or "high-calibration-error"))
                summary += "\n" + MapTransformDialogUi.Diagnostic("high-calibration-error");
            if (backgroundError is not null)
                summary += "\nФон не подготовлен. Проверьте доступность исходного изображения.";
            if (_relocateSeam.IsVisible)
                summary += "\nНажмите «Сохранить контуры и проверить привязку»: результат будет использовать рассчитанные проекцию и границы широт, сохранив форму регионов и полный охват мира.";
            if (relocated)
                summary += $"\nНовый разрез карты: {MapTransformDialogUi.Degrees(request.TargetReference.Longitude.Start)}. Проекция и границы широт результата соответствуют рассчитанной модели.";
            else if (referenceChanged)
                summary += "\nПривязка результата соответствует рассчитанным проекции и границам широт; форма регионов сохраняется.";
            _report.Show(summary, FormatReport(fit, reprojection) + (backgroundError is null ? "" : Environment.NewLine + backgroundError));
            ShowPointErrors(fit);
            _applicationHint.Text = (reprojection.IsSuccessful ? "Геометрия готова к применению. " : "Геометрия заблокирована: причина на вкладке проверки. ") +
                (_reprojectedBackgroundPng is not null ? "Фон подготовлен." : "Для применения к фону нужно доступное изображение.");
            if (referenceChanged && _reprojectedBackgroundPng is not null)
                _applicationHint.Text += " Новая привязка с подложкой применяется действием «К обоим слоям».";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested)
                return;
            _report.Show("Расчёт не выполнен. Проверьте исходную проекцию и координаты точек.", exception.ToString());
            _applicationHint.Text = "Расчёт не завершён; применение недоступно.";
        }
    }

    private async Task ApplyToGeometryAsync()
    {
        if (_modelAlreadyApplied)
            return;
        if (_fit?.Parameters is null || _fitPoints is null || _previewRequest is null || _previewResult is null || !_previewResult.IsSuccessful)
            return;
        IReadOnlyList<GeographicControlPoint> currentPoints;
        try
        {
            currentPoints = _rows.Select(row => row.ToPoint()).ToArray();
        }
        catch (Exception exception)
        {
            _report.Text = $"Данные точек некорректны: {exception.Message}";
            return;
        }
        if (!_fitPoints.SequenceEqual(currentPoints))
        {
            _report.Text = "Точки изменились после расчёта. Повторно подберите модель и проверьте геометрию.";
            _apply.IsEnabled = false;
            _applyBoth.IsEnabled = false;
            return;
        }

        var confirmed = await ConfirmApplicationAsync(
            "Применить коррекцию к геометрии?",
            "Будут преобразованы общие вершины всех регионов. " + AccuracyNotice() + " Операцию можно отменить через Undo.");
        if (!confirmed)
            return;
        try
        {
            _isApplying = true;
            IsEnabled = false;
            await _editor.ApplyReprojectionAsync(
                _previewRequest,
                _previewResult,
                controlPointsToSave: currentPoints,
                calibrationParameters: _fit.Parameters,
                calibrationReport: _fit);
            _isApplying = false;
            Close();
        }
        catch (Exception exception)
        {
            _tabs.SelectedIndex = 1;
            _report.Show("Поправка не применена. Проект остался в исходном состоянии; повторите проверку.", exception.ToString());
        }
        finally
        {
            _isApplying = false;
            IsEnabled = true;
        }
    }

    private async Task ApplyToBackgroundAsync(bool includeGeometry)
    {
        if (!includeGeometry && _previewRequest?.TargetReference != _currentReference)
            return;
        if (_modelAlreadyApplied || _backgroundAlreadyApplied)
            return;
        if (_fit?.Parameters is null || _fitPoints is null || _reprojectedBackgroundPng is null ||
            _editor.BackgroundPlacement is not { } sourcePlacement || string.IsNullOrWhiteSpace(_editor.BackgroundPath))
            return;
        if (includeGeometry && (_previewRequest is null || _previewResult is null || !_previewResult.IsSuccessful))
            return;

        IReadOnlyList<GeographicControlPoint> currentPoints;
        try
        {
            currentPoints = _rows.Select(row => row.ToPoint()).ToArray();
        }
        catch (Exception exception)
        {
            _report.Text = $"Данные точек некорректны: {exception.Message}";
            return;
        }
        if (!_fitPoints.SequenceEqual(currentPoints))
        {
            _report.Text = "Точки изменились после расчёта. Повторно подберите модель и проверьте геометрию.";
            _applyBackground.IsEnabled = false;
            _applyBoth.IsEnabled = false;
            return;
        }

        var actionTitle = includeGeometry ? "Применить поправку к геометрии и фону?" : "Применить поправку только к фону?";
        var actionText = includeGeometry
            ? "Будут преобразованы геометрия регионов и фоновое изображение одной проверенной операцией. Undo восстановит оба состояния."
            : "Будет преобразовано только фоновое изображение. Координаты регионов и spatial reference останутся прежними.";
        if (!await ConfirmApplicationAsync(actionTitle, actionText + " " + AccuracyNotice()))
            return;

        try
        {
            _isApplying = true;
            IsEnabled = false;
            var appliedParameters = _fit.Parameters with
            {
                IsApplied = includeGeometry,
                IsAppliedToBackground = true
            };
            var appliedReport = _fit with { Parameters = appliedParameters };
            if (includeGeometry)
            {
                await _editor.ApplyReprojectionAsync(
                    _previewRequest!,
                    _previewResult!,
                    controlPointsToSave: currentPoints,
                    calibrationParameters: appliedParameters,
                    reprojectedBackgroundPng: _reprojectedBackgroundPng,
                    sourceBackgroundPlacement: sourcePlacement,
                    calibrationReport: appliedReport);
            }
            else
            {
                _editor.ApplyBackgroundReprojection(
                    _reprojectedBackgroundPng,
                    sourcePlacement,
                    appliedParameters,
                    currentPoints,
                    appliedReport);
            }
            _isApplying = false;
            Close();
        }
        catch (Exception exception)
        {
            _tabs.SelectedIndex = 1;
            _report.Show("Поправка не применена. Повторите проверку и убедитесь, что исходный фон доступен.", exception.ToString());
        }
        finally
        {
            _isApplying = false;
            IsEnabled = true;
        }
    }

    private void SavePointsAndClose()
    {
        try
        {
            var points = _rows.Select(row => row.ToPoint()).ToArray();
            if (_modelAlreadyApplied || _backgroundAlreadyApplied)
            {
                var unchanged = points.SequenceEqual(_editor.ControlPoints);
                _editor.SaveGeoreferencingData(
                    points,
                    _editor.CalibrationParameters,
                    unchanged ? _editor.CalibrationReport : null);
            }
            else
            {
                _editor.SaveGeoreferencingData(points, _fit?.Parameters, _fit);
            }
            Close();
        }
        catch (Exception exception)
        {
            _report.Text = $"Точки не сохранены: {exception.Message}";
        }
    }

    private MapSpatialReference BuildSourceReference()
    {
        var mapping = (GridMappingKind)_mapping.SelectedItem!;
        var south = MapTransformDialogUi.Read(_sourceSouth, "Южная широта");
        var north = MapTransformDialogUi.Read(_sourceNorth, "Северная широта");
        var coverage = MapCoverage.Create(_sourceBaseReference.Coverage.Kind, _sourceBaseReference.Longitude, south, north);
        var reference = _sourceBaseReference with
        {
            GridMapping = mapping,
            Coverage = coverage,
            PreserveProjectedCellAspectRatio = false
        };
        reference.Validate();
        return reference;
    }

    private async Task<bool> ConfirmApplicationAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            MinHeight = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Отмена", MinWidth = 90 },
                            new Button { Content = "Применить", MinWidth = 110 }
                        }
                    }
                }
            }
        };
        var panel = (StackPanel)dialog.Content!;
        var actions = (StackPanel)panel.Children[1]!;
        ((Button)actions.Children[0]!).Click += (_, _) => dialog.Close(false);
        ((Button)actions.Children[1]!).Click += (_, _) => dialog.Close(true);
        return await OwnedWindowDialog.ShowAsync<bool>(dialog, this);
    }

    private static string FormatReport(GeographicCalibrationResult fit, ManualMapReprojectionResult reprojection)
    {
        var model = fit.Parameters!;
        var metrics = fit.Metrics!;
        var lines = new List<string>
        {
            $"Решение: {model.SourceMapping}; сдвиг долготы {model.LongitudeOffsetDegrees:F4}°; границы широт {model.SourceSouthLatitude:F4}°…{model.SourceNorthLatitude:F4}°.",
            $"Подгонка: {metrics.CalibrationPointCount} точек; RMS {metrics.CalibrationRmsDegrees:F3}°, максимум {metrics.CalibrationMaximumDegrees:F3}°.",
            $"Средняя абсолютная ошибка подгонки: долгота {metrics.CalibrationMeanAbsoluteLongitudeErrorDegrees:F3}°, широта {metrics.CalibrationMeanAbsoluteLatitudeErrorDegrees:F3}°.",
            ValidationSummary(metrics),
            $"Средняя абсолютная ошибка контроля: долгота {MapTransformDialogUi.Degrees(metrics.ValidationMeanAbsoluteLongitudeErrorDegrees)}, широта {MapTransformDialogUi.Degrees(metrics.ValidationMeanAbsoluteLatitudeErrorDegrees)}.",
            $"Геометрия: {(reprojection.IsSuccessful ? "проверка пройдена" : "применение заблокировано")}; добавлено общих вершин: {reprojection.AddedVertices}."
        };
        lines.Add($"Разрез результата: {MapTransformDialogUi.Degrees(reprojection.TargetReference.Longitude.Start)}; охват {MapTransformDialogUi.Degrees(reprojection.TargetReference.Longitude.SpanDegrees)}.");
        lines.AddRange(fit.Residuals.Select(residual =>
            $"{residual.Name} ({residual.Role}): ожидалось {FormatCoordinate(residual.Expected)}, до {FormatCoordinate(residual.Before)}, после {FormatCoordinate(residual.After)}; Δlon {residual.DeltaLongitudeDegrees:F3}°, Δlat {residual.DeltaLatitudeDegrees:F3}°, ошибка {residual.AngularErrorDegrees:F3}°."));
        lines.AddRange(fit.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        lines.AddRange(reprojection.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatCoordinate(GeoCoordinate coordinate) =>
        $"{coordinate.LatitudeDegrees:F3}°, {coordinate.LongitudeDegrees:F3}°";

    private void InvalidateFit()
    {
        _previewCancellation?.Cancel();
        _fit = null;
        _previewRequest = null;
        _previewResult = null;
        _fitPoints = null;
        _pointErrors.Text = "После расчёта здесь появятся известные координаты, значения до и после и ошибки каждой включённой точки.";
        _report.Text = "Данные изменены. Нажмите «Подобрать и проверить» для нового расчёта.";
        ClearWarpPreview();
        RefreshButtons();
    }

    private void ClearWarpPreview()
    {
        _reprojectedBackgroundPng = null;
        _preview.TargetDraft = null;
        _preview.TargetReference = null;
        _preview.SourceMarkers = [];
        _preview.TargetMarkers = [];
        if (_preview.TargetBackgroundImage is { } image)
        {
            _preview.TargetBackgroundImage = null;
            image.Dispose();
        }
        _preview.InvalidateVisual();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (GeographicControlPointRowViewModel row in e.NewItems)
                ObserveRow(row);
        }
        InvalidateFit();
        RefreshButtons();
    }

    private void ObserveRow(GeographicControlPointRowViewModel row) =>
        row.PropertyChanged += (_, _) => InvalidateFit();

    private void RefreshButtons()
    {
        _savePoints.IsEnabled = true;
        _apply.IsEnabled = false;
        _applyBackground.IsEnabled = false;
        _applyBoth.IsEnabled = false;
        _relocateSeam.IsVisible = false;
        var calibrationCount = _rows.Count(row => row.IsActive && row.Role == GeographicControlPointRole.Calibration);
        var validationCount = _rows.Count(row => row.IsActive && row.Role == GeographicControlPointRole.Validation);
        _pointCount.Text = _rows.Count == 0
            ? "Пока нет точек. Закройте окно и добавьте ориентиры инструментом «Добавить опорную точку» в редакторе."
            : $"Для подбора: {calibrationCount} · для проверки: {validationCount} · выключено: {_rows.Count(row => !row.IsActive)}. Координаты — в градусах; допустимы запятая и точка.";
        _fitButton.IsEnabled = calibrationCount >= 2 && !_modelAlreadyApplied && !_backgroundAlreadyApplied;
        _applicationHint.Text = _modelAlreadyApplied || _backgroundAlreadyApplied
            ? "Преобразование уже применено. Точки можно сохранить; для нового подбора сначала отмените применение в редакторе."
            : calibrationCount < 2 ? "Добавьте минимум две точки для подбора, затем откройте вкладку «Подбор и проверка»."
            : "После проверки станут доступны действия применения. Сохранение точек не меняет карту.";
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isApplying)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private string AccuracyNotice()
    {
        var message = _fit?.Metrics is { } metrics ? FitErrorSummary(metrics) : "";
        if (_useContinuousSeam && _previewRequest?.TargetReference is { } target)
            message += $" Результат: {MapTransformDialogUi.Label(target.GridMapping)}, широты {MapTransformDialogUi.Degrees(target.SouthLatitude)}…{MapTransformDialogUi.Degrees(target.NorthLatitude)}, разрез {MapTransformDialogUi.Degrees(target.Longitude.Start)}; форма регионов сохранится.";
        return message;
    }

    private static string FitErrorSummary(GeographicCalibrationMetrics metrics) =>
        $"Ошибка на точках подбора: RMS {MapTransformDialogUi.Degrees(metrics.CalibrationRmsDegrees)}, максимум {MapTransformDialogUi.Degrees(metrics.CalibrationMaximumDegrees)}.";

    private static string ValidationSummary(GeographicCalibrationMetrics metrics) => metrics.ValidationPointCount == 0
        ? "Независимая проверка не выполнялась."
        : $"Проверочные точки: {metrics.ValidationPointCount}; RMS {MapTransformDialogUi.Degrees(metrics.ValidationRmsDegrees)}, P95 {MapTransformDialogUi.Degrees(metrics.ValidationP95Degrees)}, максимум {MapTransformDialogUi.Degrees(metrics.ValidationMaximumDegrees)}.";

    private void ShowPointErrors(GeographicCalibrationResult fit) => _pointErrors.Text = string.Join(Environment.NewLine + Environment.NewLine,
        fit.Residuals.Select(residual => $"#{residual.ControlPointId} · {residual.Name} · {MapTransformDialogUi.Label(residual.Role)}\n" +
            $"Известно: {FormatCoordinate(residual.Expected)} · До: {FormatCoordinate(residual.Before)} · После: {FormatCoordinate(residual.After)}\n" +
            $"Ошибка: {MapTransformDialogUi.Degrees(residual.AngularErrorDegrees)} · Δ широты {MapTransformDialogUi.Degrees(residual.DeltaLatitudeDegrees)} · Δ долготы {MapTransformDialogUi.Degrees(residual.DeltaLongitudeDegrees)}"));

    private void UpdatePreviewMarkers(IReadOnlyList<GeographicControlPoint> points, GeographicCalibrationResult fit)
    {
        var targetReference = _preview.TargetReference ?? _currentReference;
        var targetContext = MapSpatialContext.Create(targetReference);
        var sourceMarkers = new List<MapTransformPreviewMarker>();
        var targetMarkers = new List<MapTransformPreviewMarker>();
        foreach (var residual in fit.Residuals)
        {
            var point = points.Single(point => point.Id == residual.ControlPointId);
            var position = point.AnchorKind == GeographicControlPointAnchorKind.MapUnits ? point.MapPosition
                : (point.BackgroundPlacementAtCapture ?? _editor.BackgroundPlacement)?.ToMapPoint(point.BackgroundU!.Value, point.BackgroundV!.Value);
            if (position is { } sourcePosition)
                sourceMarkers.Add(new(point.Id, sourcePosition));
            try
            {
                var actualGrid = targetContext.GeographicToGrid(residual.After);
                var expectedGrid = targetContext.GeographicToGrid(residual.Expected);
                targetMarkers.Add(new(point.Id,
                    new(actualGrid.X * targetReference.UnitsPerCell, actualGrid.Y * targetReference.UnitsPerCell),
                    new(expectedGrid.X * targetReference.UnitsPerCell, expectedGrid.Y * targetReference.UnitsPerCell)));
            }
            catch (ArgumentException)
            {
                // Outside target coverage: the calculation report retains the
                // residual, while the map cannot display a meaningful marker.
            }
        }
        _preview.SourceMarkers = sourceMarkers;
        _preview.TargetMarkers = targetMarkers;
    }

    private static FuncDataTemplate<GeographicControlPointRowViewModel> CreatePointTemplate() => new((row, _) =>
    {
        if (row is null)
            return null;
        var line = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(new TextBlock { Text = $"#{row.Id} · {row.AnchorLabel}", MinWidth = 90, VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBox { PlaceholderText = "Название ориентира", MinWidth = 120 };
        name.Bind(TextBox.TextProperty, new Binding(nameof(row.Name)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var active = new CheckBox { Content = "Включена", VerticalAlignment = VerticalAlignment.Center };
        active.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(row.IsActive)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(active, 2);
        header.Children.Add(active);
        line.Children.Add(header);
        var fields = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 16 };
        fields.Bind(IsEnabledProperty, new Binding(nameof(row.IsActive)));
        var latitude = new TextBox { PlaceholderText = "−90…90", MinWidth = 130 };
        latitude.Bind(TextBox.TextProperty, new Binding(nameof(row.LatitudeText)) { Mode = BindingMode.TwoWay });
        var longitude = new TextBox { PlaceholderText = "−180…180", MinWidth = 130 };
        longitude.Bind(TextBox.TextProperty, new Binding(nameof(row.LongitudeText)) { Mode = BindingMode.TwoWay });
        var role = MapTransformDialogUi.Combo(Enum.GetValues<GeographicControlPointRole>(), row.Role);
        role.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(row.Role)) { Mode = BindingMode.TwoWay });
        fields.Children.Add(MapTransformDialogUi.Field("Широта, °", latitude));
        var longitudeField = MapTransformDialogUi.Field("Долгота, °", longitude);
        Grid.SetColumn(longitudeField, 1);
        fields.Children.Add(longitudeField);
        var roleField = MapTransformDialogUi.Field("Назначение", role);
        Grid.SetColumn(roleField, 2);
        fields.Children.Add(roleField);
        line.Children.Add(fields);
        return line;
    });

    private static NumericUpDown Number(double value, decimal minimum, decimal maximum) =>
        MapTransformDialogUi.Number(value, minimum, maximum);
}

public sealed class GeographicControlPointRowViewModel : ReactiveObject
{
    private string _name;
    private string _longitudeText;
    private string _latitudeText;
    private GeographicControlPointRole _role;
    private bool _isActive;

    public GeographicControlPointRowViewModel(GeographicControlPoint point)
    {
        _originalGeographicPosition = point.GeographicPosition;
        Id = point.Id;
        _name = point.Name;
        _longitudeText = point.GeographicPosition.LongitudeDegrees.ToString("G", CultureInfo.CurrentCulture);
        _latitudeText = point.GeographicPosition.LatitudeDegrees.ToString("G", CultureInfo.CurrentCulture);
        _role = point.Role;
        _isActive = point.IsActive;
        AnchorKind = point.AnchorKind;
        MapPosition = point.MapPosition;
        BackgroundU = point.BackgroundU;
        BackgroundV = point.BackgroundV;
        BackgroundPlacementAtCapture = point.BackgroundPlacementAtCapture;
        BackgroundImageId = point.BackgroundImageId;
        SourceReferenceAtCapture = point.SourceReferenceAtCapture;
    }

    public int Id { get; }
    public GeographicControlPointAnchorKind AnchorKind { get; }
    public MapPoint? MapPosition { get; }
    public double? BackgroundU { get; }
    public double? BackgroundV { get; }
    public BackgroundImagePlacement? BackgroundPlacementAtCapture { get; }
    public string? BackgroundImageId { get; }
    public MapSpatialReference? SourceReferenceAtCapture { get; }
    public string AnchorLabel => AnchorKind == GeographicControlPointAnchorKind.MapUnits ? "карта" : "фон";
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string LongitudeText { get => _longitudeText; set => this.RaiseAndSetIfChanged(ref _longitudeText, value); }
    public string LatitudeText { get => _latitudeText; set => this.RaiseAndSetIfChanged(ref _latitudeText, value); }
    public GeographicControlPointRole Role { get => _role; set => this.RaiseAndSetIfChanged(ref _role, value); }
    public bool IsActive { get => _isActive; set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    public GeographicControlPoint ToPoint() => new(
        Id,
        Name,
        AnchorKind,
        Role,
        CoordinatesForSave(),
        MapPosition,
        BackgroundU,
        BackgroundV,
        IsActive,
        BackgroundPlacementAtCapture,
        BackgroundImageId,
        SourceReferenceAtCapture);

    private readonly GeoCoordinate _originalGeographicPosition;

    private GeoCoordinate CoordinatesForSave()
    {
        try
        {
            return new(
                (double)MapTransformDialogUi.ParseNumber(LongitudeText, -180, 180, $"Точка #{Id}, долгота"),
                (double)MapTransformDialogUi.ParseNumber(LatitudeText, -90, 90, $"Точка #{Id}, широта"));
        }
        catch (ArgumentException) when (!IsActive)
        {
            return _originalGeographicPosition;
        }
    }
}
