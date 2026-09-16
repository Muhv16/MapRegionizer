using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MapRegionizer.App.ViewModels;

namespace MapRegionizer.App.Views;

public partial class ManualMapEditorWindow : Window
{
    public ManualMapEditorWindow() => InitializeComponent();

    private ManualMapEditorViewModel ViewModel => (ManualMapEditorViewModel)DataContext!;

    private void CancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);

    private void FinalizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Close(ViewModel.CreateResult());
        }
        catch (Exception exception)
        {
            // The validation command normally prevents this path; keep the
            // window open if an input changed between validation and click.
            ViewModel.ShowDiagnostic($"Карта не сформирована: {exception.Message}");
        }
    }

    private async void LoadProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Manual map") { Patterns = ["*.manual-map.json", "*.json"] }]
        });
        if (files.Count > 0)
            ViewModel.LoadProject(files[0].Path.LocalPath);
    }

    private async void SaveProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "manual-map.json",
            SuggestedFileName = "map.manual-map.json"
        });
        if (file is not null)
            ViewModel.SaveProject(file.Path.LocalPath);
    }

    private async void LoadBackgroundClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count > 0)
            ViewModel.LoadBackground(files[0].Path.LocalPath);
    }
}
