using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MapRegionizer.App.ViewModels;

namespace MapRegionizer.App.Views;

public partial class RegionEditorWindow : Window
{
    public RegionEditorWindow() => InitializeComponent();
    private RegionEditorViewModel ViewModel => (RegionEditorViewModel)DataContext!;
    private void ApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(ViewModel.CreateResult());
    private void CancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);
    private async void LoadDraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Region draft") { Patterns = ["*.geojson", "*.json"] }] });
        var file = files.Count == 0 ? null : files[0];
        if (file is not null) ViewModel.LoadDraft(file.Path.LocalPath);
    }
    private async void SaveDraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { DefaultExtension = "geojson", SuggestedFileName = "region-draft.geojson" });
        if (file is not null) ViewModel.SaveDraft(file.Path.LocalPath);
    }
    private async void LoadBackgroundClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        var file = files.Count == 0 ? null : files[0];
        if (file is not null) ViewModel.LoadBackground(file.Path.LocalPath);
    }
}
