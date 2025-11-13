
namespace AvaloniaRegionizer.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapRegionizer;
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
    private readonly MapOptions _options = new();
    private string _statusMessage = "Готов к работе";
    private bool _isGenerating;
    private Bitmap? _resultImage;
    private readonly Dictionary<string, string> _validationErrors = new();
    private MapManager? _currentMapManager;

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

    // Свойства MapOptions
    public double PixelSize { get => _options.PixelSize; set => _options.PixelSize = value; }
    public double SimplifyTolerance { get => _options.SimplifyTolerance; set => _options.SimplifyTolerance = value; }
    public uint TargetSize { get => _options.TargetSize; set => _options.TargetSize = value; }
    public double PointsMultiplier { get => _options.PointsMultiplier; set { _options.PointsMultiplier = value; this.RaisePropertyChanged(); } }
    public double MaxDownward { get => _options.MaxDownward; set { _options.MaxDownward = value; this.RaisePropertyChanged(); } }
    public double MaxUpward { get => _options.MaxUpward; set { _options.MaxUpward = value; this.RaisePropertyChanged(); } }
    public double DistortionDetail { get => _options.DistortionDetail; set { _options.DistortionDetail = value; this.RaisePropertyChanged(); } }
    public double MaxOffset { get => _options.MaxOffst; set => _options.MaxOffst = value; }
    public double MinLineLenghtToCurve { get => _options.MinLineLenghtToCurve; set => _options.MinLineLenghtToCurve = value; }

    // Статус и результат
    public string StatusMessage { get => _statusMessage; set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
    public bool IsGenerating { get => _isGenerating; set => this.RaiseAndSetIfChanged(ref _isGenerating, value); }
    public Bitmap ResultImage { get => _resultImage; set => this.RaiseAndSetIfChanged(ref _resultImage, value); }

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

        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение маски карты",
            Filters = new()
            {
                new() { Name = "Изображения", Extensions = { "png", "jpg", "jpeg", "bmp", "gif" } },
                new() { Name = "Все файлы", Extensions = { "*" } }
            }
        };

        var result = await dialog.ShowAsync(window);
        if (result?.Length > 0)
        {
            MaskPath = result[0];
            StatusMessage = $"Маска: {Path.GetFileName(MaskPath)}";
        }
    }

    private async Task GenerateAsync()
    {
        if (HasErrors)
        {
            StatusMessage = "Исправьте ошибки в настройках";
        }

        IsGenerating = true;
        StatusMessage = "Генерация...";

        try
        {
            _currentMapManager = new MapManager(_options);
            StatusMessage = "Парсинг маски...";
            await Task.Run(() => _currentMapManager.CreateMapFromImage(_maskPath));
            StatusMessage = "Создание регионов...";
            await Task.Run(() => _currentMapManager.CreateRegions());
            StatusMessage = "Обработка границ регионов...";
            await Task.Run(() => _currentMapManager.Distort());
            StatusMessage = "Сохранение...";
            var resultPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/result.png";
            await Task.Run(() => { _currentMapManager.SaveMapToPng(resultPath); _currentMapManager.SaveMapToJson(); });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResultImage = new Bitmap(resultPath);
            });
            StatusMessage = "Генерация завершена успешно!";
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
}