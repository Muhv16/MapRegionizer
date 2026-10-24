using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapRegionizer.App.Services;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;

namespace MapRegionizer.App.Views;

public sealed record ManualMapReprojectionSelection(
    ManualMapReprojectionRequest Request,
    ManualMapReprojectionResult? Result,
    bool ApplyGeometry,
    bool ApplyBackground,
    byte[]? BackgroundPng,
    BackgroundReprojectionState? BackgroundState);

/// <summary>Explicit source/target setup and validated before/after preview for map reprojection.</summary>
public sealed class ManualMapReprojectionDialog : Window
{
    private readonly ManualMapEditorViewModel _editor;
    private readonly ManualMapDraft _draft;
    private readonly MapSpatialReference _currentReference;
    private readonly ComboBox _sourceMapping;
    private readonly ComboBox _sourceCoverageKind;
    private readonly ComboBox _targetMapping;
    private readonly ComboBox _targetCoverageKind;
    private readonly ComboBox _targetTopology;
    private readonly NumericUpDown _sourceUnits;
    private readonly NumericUpDown _sourceLongitudeStart;
    private readonly NumericUpDown _sourceLongitudeSpan;
    private readonly NumericUpDown _sourceSouth;
    private readonly NumericUpDown _sourceNorth;
    private readonly NumericUpDown _longitudeOffset;
    private readonly NumericUpDown _targetWidth;
    private readonly NumericUpDown _targetHeight;
    private readonly NumericUpDown _targetUnits;
    private readonly NumericUpDown _targetLongitudeStart;
    private readonly NumericUpDown _targetLongitudeSpan;
    private readonly NumericUpDown _targetSouth;
    private readonly NumericUpDown _targetNorth;
    private readonly MapTransformFeedback _status;
    private readonly TextBlock _applicationHint;
    private readonly TabControl _tabs;
    private readonly Button _apply;
    private readonly Button _relocateSeam;
    private readonly CheckBox _applyGeometry;
    private readonly CheckBox _applyBackground;
    private readonly ManualMapReprojectionPreview _preview;
    private readonly Bitmap? _sourceBackgroundImage;
    private readonly BackgroundImagePlacement? _sourceBackgroundPlacement;
    private readonly string? _backgroundSourceStamp;
    private CancellationTokenSource? _previewCancellation;
    private ManualMapReprojectionRequest? _previewRequest;
    private ManualMapReprojectionResult? _previewResult;
    private bool _previewApplyGeometry;
    private bool _previewApplyBackground;
    private byte[]? _previewBackgroundPng;
    private BackgroundReprojectionState? _previewBackgroundState;

    public ManualMapReprojectionDialog(ManualMapEditorViewModel editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _draft = editor.Draft;
        _currentReference = editor.SpatialReference;
        Title = "Перепроецирование карты";
        Width = 1080;
        Height = 760;
        MinWidth = 740;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _sourceMapping = Combo(Enum.GetValues<GridMappingKind>(), _currentReference.GridMapping);
        _sourceCoverageKind = Combo(Enum.GetValues<MapCoverageKind>(), _currentReference.Coverage.Kind);
        _targetMapping = Combo(Enum.GetValues<GridMappingKind>(), _currentReference.GridMapping);
        _targetCoverageKind = Combo(Enum.GetValues<MapCoverageKind>(), _currentReference.Coverage.Kind);
        _targetTopology = Combo(Enum.GetValues<GridTopologyKind>(), _currentReference.Topology);
        _sourceUnits = Number(_currentReference.UnitsPerCell, .1m, .01m, 1_000_000m);
        _sourceLongitudeStart = Number(_currentReference.Longitude.StartLongitudeDegrees, 10m, -180m, 180m);
        _sourceLongitudeSpan = Number(_currentReference.Longitude.SpanDegrees, 10m, .01m, 360m);
        _sourceSouth = Number(_currentReference.SouthLatitude, 5m, -90m, 89.99m);
        _sourceNorth = Number(_currentReference.NorthLatitude, 5m, -89.99m, 90m);
        _longitudeOffset = Number(0, 1m, -360m, 360m);
        _targetWidth = Number(_currentReference.GridWidth, 128m, 1m, 100_000m, 0);
        _targetHeight = Number(_currentReference.GridHeight, 128m, 1m, 100_000m, 0);
        _targetUnits = Number(_currentReference.UnitsPerCell, .1m, .01m, 1_000_000m);
        _targetLongitudeStart = Number(_currentReference.Longitude.StartLongitudeDegrees, 10m, -180m, 180m);
        _targetLongitudeSpan = Number(_currentReference.Longitude.SpanDegrees, 10m, .01m, 360m);
        _targetSouth = Number(_currentReference.SouthLatitude, 5m, -90m, 89.99m);
        _targetNorth = Number(_currentReference.NorthLatitude, 5m, -89.99m, 90m);
        _status = new MapTransformFeedback { Text = "Настройте исходную привязку и целевую сетку, затем проверьте результат." };
        _applicationHint = MapTransformDialogUi.Text("Применение станет доступно после проверки выбранных слоёв.");
        _apply = new Button { Content = "Применить", IsEnabled = false, MinWidth = 110 };
        _relocateSeam = new Button { Content = "Перенести разрез карты и проверить", IsVisible = false };
        _relocateSeam.Click += async (_, _) =>
        {
            if (_previewRequest is { } previous && ManualMapReprojectionService.SuggestContinuousTargetReference(previous) is { } suggested)
            {
                _targetLongitudeStart.Value = (decimal)suggested.Longitude.Start;
                // This field can be on an unopened tab. Keep its editable text
                // in sync before BuildRequest reads it for the new preview.
                _targetLongitudeStart.Text = ((decimal)suggested.Longitude.Start).ToString(CultureInfo.CurrentCulture);
                await PreviewAsync();
            }
        };
        _sourceBackgroundPlacement = editor.BackgroundWarpSourcePlacement ?? editor.BackgroundPlacement;
        _sourceBackgroundImage = MapTransformDialogUi.CloneBackground(editor.BackgroundImage);
        _backgroundSourceStamp = GetFileStamp(editor.BackgroundPath);
        _preview = new ManualMapReprojectionPreview
        {
            SourceDraft = _draft,
            SourceReference = _currentReference,
            SourceBackgroundImage = _sourceBackgroundImage,
            SourceBackgroundPlacement = editor.BackgroundPlacement
        };
        _applyGeometry = new CheckBox { Content = "Перепроецировать геометрию", IsChecked = true };
        _applyBackground = new CheckBox
        {
            Content = "Перепроецировать фоновое изображение",
            IsChecked = false,
            IsEnabled = _sourceBackgroundImage is not null && _sourceBackgroundPlacement is not null && !string.IsNullOrWhiteSpace(editor.BackgroundPath)
        };

        _sourceMapping.SelectionChanged += (_, _) => { LimitMercator(_sourceMapping, _sourceSouth, _sourceNorth); InvalidatePreview(); };
        _sourceCoverageKind.SelectionChanged += (_, _) => InvalidatePreview();
        _targetMapping.SelectionChanged += (_, _) => { LimitMercator(_targetMapping, _targetSouth, _targetNorth); InvalidatePreview(); };
        _targetCoverageKind.SelectionChanged += (_, _) => InvalidatePreview();
        _targetTopology.SelectionChanged += (_, _) => InvalidatePreview();
        _applyGeometry.PropertyChanged += (_, args) => { if (args.Property == CheckBox.IsCheckedProperty) InvalidatePreview(); };
        _applyBackground.PropertyChanged += (_, args) => { if (args.Property == CheckBox.IsCheckedProperty) InvalidatePreview(); };

        var sourcePanel = new StackPanel { Spacing = 14, Margin = new Thickness(0, 14, 12, 14) };
        sourcePanel.Children.Add(new TextBlock { Text = "Исходная привязка: как понимать координаты нынешней карты", FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Add(sourcePanel, "Исходная проекция", _sourceMapping);
        Add(sourcePanel, "Тип охвата", _sourceCoverageKind);
        Add(sourcePanel, "Единиц на ячейку", _sourceUnits);
        var sourceLongitude = Pair(MapTransformDialogUi.Field("Долгота левого края, °", _sourceLongitudeStart), MapTransformDialogUi.Field("Охват по долготе, °", _sourceLongitudeSpan));
        sourcePanel.Children.Add(sourceLongitude);
        sourcePanel.Children.Add(Pair(MapTransformDialogUi.Field("Южная широта, °", _sourceSouth), MapTransformDialogUi.Field("Северная широта, °", _sourceNorth)));
        Add(sourcePanel, "Сдвиг исходной долготы, °", _longitudeOffset);
        sourcePanel.Children.Add(new TextBlock { Text = $"Размер исходной сетки: {_draft.GridWidth} × {_draft.GridHeight}", Opacity = .72 });

        var targetPanel = new StackPanel { Spacing = 14, Margin = new Thickness(0, 14, 12, 14) };
        targetPanel.Children.Add(new TextBlock { Text = "Результат: куда записать преобразованную геометрию", FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Add(targetPanel, "Целевая проекция", _targetMapping);
        Add(targetPanel, "Тип охвата", _targetCoverageKind);
        targetPanel.Children.Add(Pair(MapTransformDialogUi.Field("Ширина сетки, ячеек", _targetWidth), MapTransformDialogUi.Field("Высота сетки, ячеек", _targetHeight)));
        Add(targetPanel, "Единиц на ячейку", _targetUnits);
        var targetLongitude = Pair(MapTransformDialogUi.Field("Долгота левого края, °", _targetLongitudeStart), MapTransformDialogUi.Field("Охват по долготе, °", _targetLongitudeSpan));
        targetPanel.Children.Add(targetLongitude);
        targetPanel.Children.Add(Pair(MapTransformDialogUi.Field("Южная широта, °", _targetSouth), MapTransformDialogUi.Field("Северная широта, °", _targetNorth)));
        Add(targetPanel, "Топология сетки", _targetTopology);
        targetPanel.Children.Add(new TextBlock
        {
            Text = "Если выбрана только подложка, она будет преобразована под текущую сетку карты. Настройки результата применяются к геометрии.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = .72
        });
        targetPanel.Children.Add(new TextBlock { Text = "Длинные нелинейные рёбра уплотняются с допуском 0,25 ячейки. Границы мира не обрезаются.", TextWrapping = TextWrapping.Wrap, Opacity = .72 });
        _sourceLongitudeSpan.IsEnabled = _sourceCoverageKind.SelectedItem is MapCoverageKind.Regional;
        _targetLongitudeSpan.IsEnabled = _targetCoverageKind.SelectedItem is MapCoverageKind.Regional;
        _sourceCoverageKind.SelectionChanged += (_, _) => _sourceLongitudeSpan.IsEnabled = _sourceCoverageKind.SelectedItem is MapCoverageKind.Regional;
        _targetCoverageKind.SelectionChanged += (_, _) => _targetLongitudeSpan.IsEnabled = _targetCoverageKind.SelectedItem is MapCoverageKind.Regional;
        _applyGeometry.IsCheckedChanged += (_, _) => targetPanel.IsEnabled = _applyGeometry.IsChecked == true;
        var selection = new StackPanel
        {
            Spacing = 10,
            Children =
        {
            MapTransformDialogUi.Text("Что преобразовать", true), _applyGeometry, _applyBackground,
            MapTransformDialogUi.Text(_applyBackground.IsEnabled ? "Сравните результат до применения. Изменения можно отменить в редакторе." : "Чтобы преобразовать фон, сначала загрузите исходное изображение в редакторе.")
        }
        };
        var previewButton = new Button { Content = "Проверить результат" };
        previewButton.Click += async (_, _) => await PreviewAsync();
        _apply.Click += (_, _) => ApplyPreview();
        var cancel = new Button { Content = "Отмена", MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { previewButton, _relocateSeam, _apply, cancel })
        {
            button.Margin = new Thickness(0, 6, 8, 0);
            actions.Children.Add(button);
        }
        _preview.MinHeight = 260;
        var resultPage = new Grid { RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 14, Margin = new Thickness(0, 14, 12, 14) };
        resultPage.Children.Add(selection);
        Grid.SetRow(_preview, 1);
        resultPage.Children.Add(_preview);
        var feedbackScroll = MapTransformDialogUi.Scroll(_status);
        feedbackScroll.MaxHeight = 170;
        Grid.SetRow(feedbackScroll, 2);
        resultPage.Children.Add(feedbackScroll);
        _tabs = new TabControl
        {
            ItemsSource = new[]
        {
            new TabItem { Header = "1. Исходная карта", Content = MapTransformDialogUi.Scroll(sourcePanel) },
            new TabItem { Header = "2. Целевая сетка", Content = MapTransformDialogUi.Scroll(targetPanel) },
            new TabItem { Header = "3. Проверка и результат", Content = MapTransformDialogUi.Scroll(resultPage) }
        }
        };
        var footer = new StackPanel { Spacing = 4, Children = { _applicationHint, actions } };
        Content = MapTransformDialogUi.Root("Перепроецирование карты", "Задайте исходную привязку и целевую сетку, затем сравните результат", _tabs, footer);
        foreach (var input in new[] { _sourceUnits, _sourceLongitudeStart, _sourceLongitudeSpan, _sourceSouth, _sourceNorth, _longitudeOffset,
            _targetWidth, _targetHeight, _targetUnits, _targetLongitudeStart, _targetLongitudeSpan, _targetSouth, _targetNorth })
            input.PropertyChanged += (_, args) => { if (args.Property == NumericUpDown.ValueProperty || args.Property == NumericUpDown.TextProperty) InvalidatePreview(); };
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _sourceBackgroundImage?.Dispose();
        if (_preview.TargetBackgroundImage is { } targetBackground)
            targetBackground.Dispose();
        base.OnClosed(e);
    }

    private async Task PreviewAsync()
    {
        InvalidatePreview();
        _tabs.SelectedIndex = 2;
        ManualMapReprojectionRequest request;
        var applyGeometry = _applyGeometry.IsChecked == true;
        var applyBackground = _applyBackground.IsChecked == true;
        try
        {
            if (!applyGeometry && !applyBackground)
                throw new ArgumentException("Выберите преобразование геометрии, фона или обеих частей.");
            request = BuildRequest();
        }
        catch (Exception exception)
        {
            _previewRequest = null;
            _previewResult = null;
            _apply.IsEnabled = false;
            _status.Show("Проверьте исходные и целевые параметры: границы широт, размер сетки и выбранные слои.", exception.ToString());
            _applicationHint.Text = "Параметры некорректны; применение недоступно.";
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        _previewRequest = null;
        _previewResult = null;
        _previewBackgroundPng = null;
        _previewBackgroundState = null;
        _apply.IsEnabled = false;
        _status.Text = "Выполняется преобразование и проверка геометрии…";
        _applicationHint.Text = "Выполняется расчёт. Изменение параметров отменит его.";
        try
        {
            var result = applyGeometry
                ? await Task.Run(() => new ManualMapReprojectionService().Reproject(request, token), token)
                : null;
            if (token.IsCancellationRequested)
                return;

            var backgroundTarget = applyGeometry ? request.TargetReference : _currentReference;
            byte[]? backgroundPng = null;
            BackgroundReprojectionState? backgroundState = null;
            if (applyBackground)
            {
                if (string.IsNullOrWhiteSpace(_editor.BackgroundPath) || _sourceBackgroundPlacement is null || _sourceBackgroundImage is null)
                    throw new BackgroundPreviewException("Загрузите доступное фоновое изображение перед его перепроецированием.");
                if (!string.Equals(_backgroundSourceStamp, GetFileStamp(_editor.BackgroundPath), StringComparison.Ordinal))
                    throw new BackgroundPreviewException("Исходное фоновое изображение изменилось. Загрузите его заново и повторите предпросмотр.");
                var savedBackground = _editor.BackgroundReprojectionState;
                if (savedBackground is not null &&
                    (request.SourceReference with { PreserveProjectedCellAspectRatio = _currentReference.PreserveProjectedCellAspectRatio } != _currentReference || request.SourceLongitudeOffsetDegrees != 0))
                    throw new BackgroundPreviewException("Фон уже привязан. Для изменения его исходной интерпретации сначала отмените прежнее преобразование; текущую привязку можно перенести в другую целевую сетку.");
                var backgroundSource = savedBackground?.SourceReference ?? request.SourceReference;
                var backgroundPlacement = savedBackground?.SourcePlacement ?? _sourceBackgroundPlacement;
                var backgroundOffset = savedBackground?.SourceLongitudeOffsetDegrees ?? request.SourceLongitudeOffsetDegrees;
                var backgroundTransform = savedBackground?.CalibrationParameters is { } calibration
                    ? ParametricGeoreferenceSolver.CreateTransform(backgroundSource, calibration) : null;
                backgroundPng = await Task.Run(() => ManualMapBackgroundReprojector.ReprojectToPng(
                    _editor.BackgroundPath,
                    backgroundPlacement,
                    backgroundSource,
                    backgroundTarget,
                    backgroundOffset,
                    georeferenceTransform: backgroundTransform,
                    cancellationToken: token), token);
                if (token.IsCancellationRequested)
                    return;
                backgroundState = new BackgroundReprojectionState(
                    backgroundSource,
                    backgroundTarget,
                    backgroundPlacement,
                    backgroundOffset,
                    savedBackground?.CalibrationParameters);
                using var imageStream = new MemoryStream(backgroundPng);
                _preview.TargetBackgroundImage?.Dispose();
                _preview.TargetBackgroundImage = new Bitmap(imageStream);
            }
            else if (_preview.TargetBackgroundImage is { } oldTargetImage)
            {
                _preview.TargetBackgroundImage = null;
                oldTargetImage.Dispose();
            }

            _previewRequest = request;
            _previewResult = result;
            _previewBackgroundPng = backgroundPng;
            _previewBackgroundState = backgroundState;
            _previewApplyGeometry = applyGeometry;
            _previewApplyBackground = applyBackground;
            _preview.SourceReference = request.SourceReference;
            _preview.TargetDraft = applyGeometry ? result?.Draft : _draft;
            _preview.TargetReference = result?.TargetReference ?? _currentReference;
            _preview.InvalidateVisual();
            var geometrySuccessful = !applyGeometry || result?.IsSuccessful == true;
            var backgroundSuccessful = !applyBackground || backgroundPng is not null;
            _apply.IsEnabled = geometrySuccessful && backgroundSuccessful;
            _relocateSeam.IsVisible = applyGeometry && result?.IsSuccessful == false && result.Diagnostics.Any(item => item.Code == "seam-crossing-unsupported") &&
                ManualMapReprojectionService.SuggestContinuousTargetReference(request) is not null;
            var statusLines = new List<string>();
            if (applyGeometry)
            {
                statusLines.Add(result!.IsSuccessful
                    ? $"Геометрия проверена. Регионов: {result.Draft!.Regions.Count}; вершин: {request.Draft.Vertices.Count} → {result.Draft.Vertices.Count}; новых общих точек: {result.AddedVertices}."
                    : "Преобразование геометрии заблокировано проверкой.");
                if (!result.IsSuccessful)
                    statusLines.Add(MapTransformDialogUi.Diagnostic(result.Diagnostics.Count > 0 ? result.Diagnostics[0].Code : ""));
                else
                    statusLines.Add($"Разрез результата: {MapTransformDialogUi.Degrees(request.TargetReference.Longitude.Start)}; охват {MapTransformDialogUi.Degrees(request.TargetReference.Longitude.SpanDegrees)}.");
            }
            if (_relocateSeam.IsVisible)
                statusLines.Add("Нажмите «Перенести разрез карты и проверить», чтобы сохранить целые регионы и полный охват мира.");
            if (applyBackground)
                statusLines.Add($"Фоновое изображение подготовлено для сетки {backgroundTarget.GridWidth} × {backgroundTarget.GridHeight}.");
            _status.Show(statusLines.Count == 0 ? "Результат готов." : string.Join(Environment.NewLine, statusLines),
                result?.Diagnostics.Count > 0 ? string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")) : null);
            _applicationHint.Text = _apply.IsEnabled ? "Результат проверен. Нажмите «Применить»; действие можно отменить в редакторе." : "Применение заблокировано. Причина указана на вкладке результата.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (BackgroundPreviewException exception)
        {
            if (token.IsCancellationRequested)
                return;
            _status.Text = exception.Message;
            _applicationHint.Text = "Фон не подготовлен; причина указана на вкладке результата.";
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested)
                return;
            _previewRequest = null;
            _previewResult = null;
            _status.Show("Предпросмотр не выполнен. Проверьте параметры охвата и доступность исходного фона.", exception.ToString());
            _applicationHint.Text = "Предпросмотр не завершён; применение недоступно.";
        }
    }

    private void ApplyPreview()
    {
        try
        {
            var currentRequest = BuildRequest();
            var currentApplyGeometry = _applyGeometry.IsChecked == true;
            var currentApplyBackground = _applyBackground.IsChecked == true;
            if (_previewRequest is null || _previewRequest != currentRequest ||
                currentApplyGeometry != _previewApplyGeometry || currentApplyBackground != _previewApplyBackground ||
                (currentApplyGeometry && _previewResult?.IsSuccessful != true) ||
                (currentApplyBackground && (_previewBackgroundPng is null || _previewBackgroundState is null)) ||
                (currentApplyBackground && !string.Equals(_backgroundSourceStamp, GetFileStamp(_editor.BackgroundPath), StringComparison.Ordinal)))
            {
                _apply.IsEnabled = false;
                _status.Text = "Параметры изменились после проверки. Постройте предпросмотр заново.";
                return;
            }
            Close(new ManualMapReprojectionSelection(
                _previewRequest,
                _previewResult,
                _previewApplyGeometry,
                _previewApplyBackground,
                _previewBackgroundPng,
                _previewBackgroundState));
        }
        catch (Exception exception)
        {
            _status.Show("Применение недоступно. Повторите проверку текущих параметров.", exception.ToString());
        }
    }

    private ManualMapReprojectionRequest BuildRequest()
    {
        var sourceCoverage = BuildCoverage(
            (MapCoverageKind)_sourceCoverageKind.SelectedItem!,
            Value(_sourceLongitudeStart), Value(_sourceLongitudeSpan), Value(_sourceSouth), Value(_sourceNorth));
        var targetCoverage = _applyGeometry.IsChecked == true
            ? BuildCoverage(
                (MapCoverageKind)_targetCoverageKind.SelectedItem!,
                Value(_targetLongitudeStart), Value(_targetLongitudeSpan), Value(_targetSouth), Value(_targetNorth))
            : _currentReference.Coverage;
        var source = _currentReference with
        {
            GridWidth = _draft.GridWidth,
            GridHeight = _draft.GridHeight,
            UnitsPerCell = Value(_sourceUnits),
            Coverage = sourceCoverage,
            GridMapping = (GridMappingKind)_sourceMapping.SelectedItem!,
            PreserveProjectedCellAspectRatio = false
        };
        var target = _applyGeometry.IsChecked == true
            ? _currentReference with
            {
                GridWidth = Dimension(_targetWidth),
                GridHeight = Dimension(_targetHeight),
                UnitsPerCell = Value(_targetUnits),
                Coverage = targetCoverage,
                GridMapping = (GridMappingKind)_targetMapping.SelectedItem!,
                Topology = (GridTopologyKind)_targetTopology.SelectedItem!,
                PreserveProjectedCellAspectRatio = false
            }
            : _currentReference;
        source.Validate();
        target.Validate();
        return new ManualMapReprojectionRequest(_draft, source, target, Value(_longitudeOffset));
    }

    private static MapCoverage BuildCoverage(MapCoverageKind kind, double longitudeStart, double longitudeSpan, double south, double north) =>
        kind == MapCoverageKind.Global
            ? MapCoverage.Create(MapCoverageKind.Global, new LongitudeInterval(longitudeStart, 360), south, north)
            : MapCoverage.Regional(new LongitudeInterval(longitudeStart, longitudeSpan), south, north);

    private void InvalidatePreview()
    {
        _previewCancellation?.Cancel();
        _previewRequest = null;
        _previewResult = null;
        _previewBackgroundPng = null;
        _previewBackgroundState = null;
        _apply.IsEnabled = false;
        _relocateSeam.IsVisible = false;
        _applicationHint.Text = "Параметры изменены. Нажмите «Проверить результат».";
        _status.Text = "Для текущих параметров ещё нет результата проверки.";
        _preview.TargetDraft = null;
        _preview.TargetReference = null;
        if (_preview.TargetBackgroundImage is { } image)
        {
            _preview.TargetBackgroundImage = null;
            image.Dispose();
        }
        _preview.InvalidateVisual();
    }

    private static string? GetFileStamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var info = new FileInfo(Path.GetFullPath(path));
            return info.Exists ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ComboBox Combo<T>(IEnumerable<T> values, T selected) => MapTransformDialogUi.Combo(values, selected);

    private static NumericUpDown Number(double value, decimal increment, decimal minimum, decimal maximum, int decimalPlaces = 4) =>
        MapTransformDialogUi.Number(value, minimum, maximum, increment, decimalPlaces);

    private static double Value(NumericUpDown input) => MapTransformDialogUi.Read(input);

    private static int Dimension(NumericUpDown input)
    {
        var value = Value(input);
        if (value != Math.Truncate(value))
            throw new ArgumentException("Размер сетки должен быть целым числом ячеек.");
        return (int)value;
    }

    private static Control Pair(Control left, Control right)
    {
        return MapTransformDialogUi.Pair(left, right);
    }

    private static void Add(Panel parent, string label, Control control)
    {
        parent.Children.Add(MapTransformDialogUi.Field(label, control));
    }

    private static void LimitMercator(ComboBox mapping, NumericUpDown south, NumericUpDown north)
    {
        if (mapping.SelectedItem is not GridMappingKind.WebMercator)
            return;
        var limit = (decimal)MapRegionizer.Core.Spatial.WebMercatorGridMapping.WebMercatorLatitudeLimit;
        south.Value = Math.Max(south.Value ?? -limit, -limit);
        north.Value = Math.Min(north.Value ?? limit, limit);
    }

    private sealed class BackgroundPreviewException(string message) : Exception(message);
}
