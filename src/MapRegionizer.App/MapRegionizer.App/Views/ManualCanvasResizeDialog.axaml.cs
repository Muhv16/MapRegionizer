using Avalonia.Interactivity;
using Avalonia.Controls;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.ManualAuthoring;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MapRegionizer.App.Views;

public partial class ManualCanvasResizeDialog : Window
{
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isClosed;

    public ManualCanvasResizeDialog() => InitializeComponent();

    public Func<ManualCanvasResizeResult, CancellationToken, Task>? ApplyResizeAsync { get; set; }

    private ManualCanvasResizeDialogViewModel ViewModel => (ManualCanvasResizeDialogViewModel)DataContext!;

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _lifetimeCancellation.Cancel();
        var dataContext = DataContext;
        DataContext = null;
        base.OnClosed(e);
        if (dataContext is IDisposable disposable)
            disposable.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void ApplyClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanApply || ViewModel.PreviewResult is not { } result)
            return;

        ViewModel.SetApplying(true);
        try
        {
            var applyResize = ApplyResizeAsync
                ?? throw new InvalidOperationException("Обработчик применения размера холста не задан.");
            await applyResize(result, _lifetimeCancellation.Token);
            if (!_isClosed)
                Close(true);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the dialog cancels background validation and index preparation.
        }
        catch (Exception exception)
        {
            if (!_isClosed)
                ViewModel.ShowError($"Размер холста не изменён: {exception.Message}");
        }
        finally
        {
            if (!_isClosed)
                ViewModel.SetApplying(false);
        }
    }
}
