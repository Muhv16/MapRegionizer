using Avalonia;
using Avalonia.Controls;

namespace MapRegionizer.App.Views;

public partial class SettingsRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SettingsRow, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> TipProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Tip));

    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<SettingsRow, object?>(nameof(Value));

    public SettingsRow()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
