
namespace AvaloniaRegionizer.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using MapRegionizer.ImageSharp;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;

public class MainViewModel : ReactiveObject
{
    private GeneratedMap? _currentMap;
    private string _statusMessage = "Готов к работе";
    private bool _isGenerating;
    private Bitmap? _resultImage;
    private readonly Dictionary<string, string> _validationErrors = new();

    private double _pixelSize = 1;
    private double _simplifyTolerance = 1;
    private uint _targetSize = 400;
    private double _pointsMultiplier = 4;
    private double _maxDownward = 0.75;
    private double _maxUpward = 1.75;
    private double _distortionDetail = 0.25;
    private double _maxOffset = 3.25;
    private double _minLineLengthToCurve = 7;

    private string _maskPath = string.Empty;
    public string MaskPath
    {
        get => _maskPath;
        set { _maskPath = value; this.RaisePropertyChanged(); }
    }

    public MainViewModel()
    {
        BrowseMaskCommand = ReactiveCommand.CreateFromTask(BrowseMaskAsync);
        GenerateCommand = ReactiveCommand.CreateFromTask(GenerateAsync,
            this.WhenAnyValue(vm => vm.HasErrors, vm => vm.IsGenerating,
                (hasErrors, generating) => !hasErrors && !generating));

        this.WhenAnyValue(
            x => x.MaxDownward, x => x.MaxUpward, x => x.DistortionDetail,
            x => x.PointsMultiplier, x => x.MaskPath)
            .Subscribe(_ => ValidateAll());

        ValidateAll();
    }

    public double PixelSize { get => _pixelSize; set => this.RaiseAndSetIfChanged(ref _pixelSize, value); }
    public double SimplifyTolerance { get => _simplifyTolerance; set => this.RaiseAndSetIfChanged(ref _simplifyTolerance, value); }
    public uint TargetSize { get => _targetSize; set => this.RaiseAndSetIfChanged(ref _targetSize, value); }
    public double PointsMultiplier { get => _pointsMultiplier; set => this.RaiseAndSetIfChanged(ref _pointsMultiplier, value); }
    public double MaxDownward { get => _maxDownward; set => this.RaiseAndSetIfChanged(ref _maxDownward, value); }
    public double MaxUpward { get => _maxUpward; set => this.RaiseAndSetIfChanged(ref _maxUpward, value); }
    public double DistortionDetail { get => _distortionDetail; set => this.RaiseAndSetIfChanged(ref _distortionDetail, value); }
    public double MaxOffset { get => _maxOffset; set => this.RaiseAndSetIfChanged(ref _maxOffset, value); }
    public double MinLineLenghtToCurve { get => _minLineLengthToCurve; set => this.RaiseAndSetIfChanged(ref _minLineLengthToCurve, value); }

    // Статус и результат
    public string StatusMessage { get => _statusMessage; set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
    public bool IsGenerating { get => _isGenerating; set => this.RaiseAndSetIfChanged(ref _isGenerating, value); }
    public Bitmap? ResultImage { get => _resultImage; set => this.RaiseAndSetIfChanged(ref _resultImage, value); }

    // Ошибки (для отображения в UI)
    public string MaxDownwardError => GetError(nameof(MaxDownward));
    public string MaxUpwardError => GetError(nameof(MaxUpward));
    public string DistortionDetailError => GetError(nameof(DistortionDetail));
    public string PointsMultiplierError => GetError(nameof(PointsMultiplier));
    public string MaskPathError => GetError(nameof(MaskPath));
    public bool HasErrors => _validationErrors.Any();
    private string GetError(string name) => _validationErrors.TryGetValue(name, out var error) ? error : string.Empty;

    private void ValidateAll()
    {
        _validationErrors.Clear();

        if (MaxDownward <= 0 || MaxDownward > 1)
            _validationErrors[nameof(MaxDownward)] = "Должно быть > 0 и ≤ 1";

        if (MaxUpward < 1)
            _validationErrors[nameof(MaxUpward)] = "Должно быть ≥ 1";

        if (PointsMultiplier <= 0)
            _validationErrors[nameof(PointsMultiplier)] = "Должно быть > 0";

        if (string.IsNullOrWhiteSpace(MaskPath))
            _validationErrors[nameof(MaskPath)] = "Выберите файл маски карты";
        else if (!File.Exists(MaskPath))
            _validationErrors[nameof(MaskPath)] = "Файл не найден";

        // Обновляем все свойства ошибок
        foreach (var prop in new[] { nameof(MaxDownwardError), nameof(MaxUpwardError),
                                      nameof(DistortionDetailError), nameof(PointsMultiplierError),
                                      nameof(MaskPathError), nameof(HasErrors) })
            this.RaisePropertyChanged(prop);
    }

    public ReactiveCommand<Unit, Unit> BrowseMaskCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateCommand { get; }

    private async Task BrowseMaskAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null) return;

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите изображение маски карты",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] },
                FilePickerFileTypes.All
            ]
        });

        var file = result.FirstOrDefault();
        if (file?.Path.LocalPath is { Length: > 0 } path)
        {
            MaskPath = path;
            StatusMessage = $"Маска: {Path.GetFileName(MaskPath)}";
        }
    }

    private async Task GenerateAsync()
    {
        if (HasErrors)
        {
            StatusMessage = "Исправьте ошибки в настройках";
            return;
        }

        IsGenerating = true;
        StatusMessage = "Генерация...";

        try
        {
            StatusMessage = "Парсинг маски...";
            var mask = await Task.Run(() => ImageMaskReader.Read(_maskPath));

            StatusMessage = "Генерация карты...";
            var generator = new MapGenerator();
            var options = BuildGenerationOptions();
            _currentMap = await Task.Run(() => generator.Generate(mask, options));

            StatusMessage = "Сохранение...";
            var resultPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/result.png";
            var outputDirectory = Path.GetDirectoryName(resultPath) ?? AppContext.BaseDirectory;
            var tectonicImagePath = Path.Combine(outputDirectory, "tectonic-plates.png");
            var crustImagePath = Path.Combine(outputDirectory, "tectonic-crust.png");
            var featuresImagePath = Path.Combine(outputDirectory, "tectonic-features.png");
            var elevationImagePath = Path.Combine(outputDirectory, "elevation.png");
            await Task.Run(() =>
            {
                MapImageRenderer.RenderToFile(_currentMap, resultPath);
                MapImageRenderer.RenderTectonicPlatesToFile(_currentMap, tectonicImagePath);
                MapImageRenderer.RenderCrustToFile(_currentMap, crustImagePath);
                MapImageRenderer.RenderTectonicFeaturesToFile(_currentMap, featuresImagePath);
                MapImageRenderer.RenderElevationToFile(_currentMap, elevationImagePath);
                GeoJsonMapWriter.WriteRegionsToFile(_currentMap, Path.Combine(outputDirectory, "regions.geojson"));
                GeoJsonMapWriter.WriteLandmassesToFile(_currentMap, Path.Combine(outputDirectory, "landmasses.geojson"));
                GeoJsonMapWriter.WriteWaterBodiesToFile(_currentMap, Path.Combine(outputDirectory, "water-bodies.geojson"));
                TectonicPlateJsonWriter.WriteToFile(_currentMap, Path.Combine(outputDirectory, "tectonic-plates.json"));
                ElevationJsonWriter.WriteToFile(_currentMap, Path.Combine(outputDirectory, "elevation.json"));
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResultImage = new Bitmap(tectonicImagePath);
            });
            StatusMessage = $"Генерация завершена успешно! Файлы сохранены в {outputDirectory}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка генерации: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private MapGenerationOptions BuildGenerationOptions()
    {
        return new MapGenerationOptions
        {
            PixelSize = PixelSize,
            ProjectionMode = MapProjectionMode.EquirectangularWorld,
            ShapeExtraction = new ShapeExtractionOptions
            {
                SimplifyTolerance = SimplifyTolerance
            },
            Regions = new RegionGenerationOptions
            {
                TargetArea = TargetSize,
                PointsMultiplier = PointsMultiplier,
                MinAreaRatio = MaxDownward,
                MaxAreaRatio = MaxUpward
            },
            Boundaries = new BoundaryDistortionOptions
            {
                Detail = DistortionDetail,
                MaxOffset = MaxOffset,
                MinLineLengthToCurve = MinLineLenghtToCurve
            }
        };
    }
}
