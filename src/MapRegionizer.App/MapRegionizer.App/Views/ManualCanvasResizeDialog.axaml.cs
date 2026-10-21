using Avalonia.Controls;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.ManualAuthoring;

namespace MapRegionizer.App.Views;

public partial class ManualCanvasResizeDialog : Window
{
    public ManualCanvasResizeDialog() => InitializeComponent();

    private ManualCanvasResizeDialogViewModel ViewModel => (ManualCanvasResizeDialogViewModel)DataContext!;

    private void CancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);

    private void ApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.CanApply)
            Close(ViewModel.Request);
    }
}
