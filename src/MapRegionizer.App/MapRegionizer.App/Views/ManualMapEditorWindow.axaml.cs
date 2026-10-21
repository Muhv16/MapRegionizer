using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.ManualAuthoring;

namespace MapRegionizer.App.Views;

public partial class ManualMapEditorWindow : Window
{
    private bool _isClosed;

    public ManualMapEditorWindow() => InitializeComponent();

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        base.OnClosed(e);
    }

    private ManualMapEditorViewModel ViewModel => (ManualMapEditorViewModel)DataContext!;

    private void ToolSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ManualMapEditorViewModel viewModel
            || e.AddedItems.Count == 0
            || e.AddedItems[0] is not ManualMapEditorTool tool)
            return;

        viewModel.SelectedTool = tool;
    }

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
        if (_isClosed || files.Count == 0)
            return;

        ViewModel.RequestLoadProject(files[0].Path.LocalPath);
    }

    private void CancelProjectLoadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel.CancelPendingProjectLoad();

    private void ConfirmLegacyProjectLoadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel.ConfirmPendingLegacyProjectLoad();

    private async void SaveProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "manual-map.json",
            SuggestedFileName = "map.manual-map.json"
        });
        if (!_isClosed && file is not null)
            ViewModel.SaveProject(file.Path.LocalPath);
    }

    private async void LoadBackgroundClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (!_isClosed && files.Count > 0)
            ViewModel.LoadBackground(files[0].Path.LocalPath);
    }

    private async void ResizeCanvasClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ManualCanvasResizeDialog
        {
            DataContext = ViewModel.CreateCanvasResizeDialogViewModel(),
            ApplyResizeAsync = ApplyCanvasResizeAsync
        };
        await dialog.ShowDialog<bool>(this);
    }

    private async Task ApplyCanvasResizeAsync(ManualCanvasResizeResult result, CancellationToken cancellationToken)
    {
        try
        {
            await ViewModel.ApplyCanvasResizeAsync(result, cancellationToken);
        }
        catch (ManualCanvasResizeException exception)
        {
            ViewModel.ShowDiagnostic($"Размер холста не изменён ({exception.Code}): {exception.Message}");
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ViewModel.ShowDiagnostic($"Размер холста не изменён: {exception.Message}");
            throw;
        }
    }

}
