using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;

namespace MapRegionizer.App.Views;

/// <summary>Shared presentation and numeric-entry conventions for map transformations.</summary>
internal static class MapTransformDialogUi
{
    public static Bitmap? CloneBackground(Bitmap? current)
    {
        if (current is null)
            return null;
        using var stream = new MemoryStream();
        current.Save(stream);
        stream.Position = 0;
        return Bitmap.DecodeToWidth(stream, Math.Min(2048, current.PixelSize.Width), BitmapInterpolationMode.HighQuality);
    }

    public static TextBlock Text(string text, bool heading = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = heading ? 17 : 14,
        FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal
    };

    public static Control Field(string label, Control input)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(Text(label));
        panel.Children.Add(input);
        return panel;
    }

    public static Grid Pair(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    public static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public static ComboBox Combo<T>(IEnumerable<T> values, T selected) => new()
    {
        ItemsSource = values.Distinct().ToArray(),
        SelectedItem = selected,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new FuncDataTemplate<object>((value, _) => Text(Label(value)))
    };

    public static string Label(object? value) => value switch
    {
        GridMappingKind.Equirectangular => "Равнопромежуточная (Equirectangular)",
        GridMappingKind.WebMercator => "Меркатор (Web Mercator)",
        MapCoverageKind.Global => "Весь мир · 360° долготы",
        MapCoverageKind.Regional => "Часть мира",
        GridTopologyKind.CylindricalX => "Стыковка слева и справа",
        GridTopologyKind.OpenRectangular => "Без стыковки краёв",
        GeographicControlPointRole.Calibration => "Для подбора",
        GeographicControlPointRole.Validation => "Для проверки",
        _ => value?.ToString() ?? ""
    };

    public static NumericUpDown Number(double value, decimal minimum, decimal maximum, decimal increment = .1m, int decimals = 4) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = (decimal)value,
        Increment = increment,
        FormatString = decimals == 0 ? "0" : "0.####",
        ShowButtonSpinner = false,
        MinWidth = 140,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        TextConverter = new MapNumberConverter(decimals),
        Text = ((decimal)value).ToString(CultureInfo.CurrentCulture)
    };

    public static decimal ParseNumber(string? text, decimal minimum, decimal maximum, string label)
    {
        if (!decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"{label}: введите число от {minimum} до {maximum}. Можно использовать запятую или точку.");
        return value;
    }

    public static double Read(NumericUpDown input, string label = "Поле") =>
        (double)ParseNumber(input.Text ?? input.Value?.ToString(CultureInfo.CurrentCulture), input.Minimum, input.Maximum, label);

    public static string Degrees(double value) => double.IsFinite(value) ? $"{value:F3}°" : "нет данных";

    public static string Diagnostic(string code) => code switch
    {
        "coordinate-out-of-range" => "Часть карты выходит за целевой охват или предел широт Меркатора. Расширьте целевой охват либо проверьте исходную проекцию и точки.",
        "seam-crossing-unsupported" => "Граница региона пересекает край холста. Нужно изменить положение разреза карты или целевой охват.",
        "vertex-budget-exceeded" or "subdivision-depth-exceeded" => "Преобразование требует слишком сложных границ. Уменьшите искажение исходной и целевой сеток.",
        "invalid-grid-size" => "Проверьте размер сетки: допускается не более 100 миллионов ячеек.",
        "invalid-reference" => "Проверьте границы охвата и проекцию. Для Меркатора широты должны быть в пределах ±85,0511°.",
        "insufficient-calibration-points" => "Нужны хотя бы две включённые точки «Для подбора» на разных строках карты.",
        "underdetermined-latitude-fit" => "Точки для подбора находятся на одной строке. Добавьте точку севернее или южнее.",
        "invalid-latitude-fit" => "Точки задают перевёрнутую карту или нулевой диапазон широт. Для этой модели север должен быть сверху. Проверьте знаки широт и положение точек.",
        "latitude-fit-constrained" => "Границы подобраны в пределах допустимых широт проекции. Все включённые точки учтены; проверьте их ошибки ниже.",
        "latitude-outside-source-projection" => "Широта одной из точек выходит за предел Меркатора ±85,0511°. Проверьте эту широту либо выберите Equirectangular.",
        "conflicting-longitudes" or "high-calibration-error" => "Точки плохо согласуются с выбранной моделью. Проверьте координаты, проекцию и ошибки отдельных точек.",
        "invalid-control-point-coordinate" or "invalid-background-control-point" or "unresolved-control-point" => "Проверьте координаты и положение опорных точек; точки фона должны находиться внутри исходного изображения.",
        _ => "Проверка не пройдена. Проверьте исходные данные; причина и номера объектов доступны в технических подробностях."
    };

    public static Grid Root(string title, string subtitle, Control body, Control footer)
    {
        var header = new StackPanel { Spacing = 5, Children = { Text(title, true), Text(subtitle) } };
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 16 };
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private sealed class MapNumberConverter(int decimals) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            ParseNumber(value as string, decimal.MinValue, decimal.MaxValue, "Число");

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is decimal number ? number.ToString(decimals == 0 ? "0" : "G29", CultureInfo.CurrentCulture) : "";
    }
}

/// <summary>Keeps actionable feedback separate from copyable technical diagnostics.</summary>
internal sealed class MapTransformFeedback : StackPanel
{
    public string Text { get => _summary.Text ?? ""; set => Show(value); }
    private readonly TextBlock _summary = MapTransformDialogUi.Text("");
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        MaxHeight = 200
    };
    private readonly Expander _expander;

    public MapTransformFeedback()
    {
        Spacing = 12;
        _expander = new Expander { Header = "Технические подробности", Content = _details, HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
        Children.Add(_summary);
        Children.Add(_expander);
    }

    public void Show(string summary, string? details = null)
    {
        _summary.Text = summary;
        _details.Text = details ?? "";
        _expander.IsVisible = !string.IsNullOrWhiteSpace(details);
    }
}
