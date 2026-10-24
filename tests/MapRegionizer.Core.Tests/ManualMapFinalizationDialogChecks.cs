using System.Reflection;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using MapRegionizer.App.Services;
using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Views;
using MapRegionizer.GeoJson;
using Xunit;
using static MapRegionizer.Core.Tests.MapTransformDialogContractChecks;

namespace MapRegionizer.Core.Tests;

internal static class ManualMapFinalizationDialogChecks
{
    public static void Run(string directory)
    {
        var previousContext = SynchronizationContext.Current;
        var lifetime = Assert.IsType<ClassicDesktopStyleApplicationLifetime>(Application.Current!.ApplicationLifetime);
        var previousMainWindow = lifetime.MainWindow;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        // Observe native modal enablement: Window.IsEnabled does not track it.
        var platform = DispatchProxy.Create<IWindowImpl, TrackingWindowPlatform>();
        var tracking = (TrackingWindowPlatform)platform;
        tracking.Inner = new Window().PlatformImpl!;
        var owner = new Window(platform);
        try
        {
            lifetime.MainWindow = owner;
            owner.Show();
            using var main = new MainViewModel(new UserSettingsService(Path.Combine(directory, "finalization-settings.json")));
            main.OutputDirectory = directory;
            main.IsManualSource = true;
            var path = Path.Combine(directory, "shifted.manual-map.json");
            ManualMapJson.Save(path, ManualMapSeamContractTests.Draft(), ManualMapSeamContractTests.Reference(-167.9781));
            VerifyFinalization(main, owner, tracking, path);
            var suppliedPath = Environment.GetEnvironmentVariable("MAPREGIONIZER_FINALIZE_SOURCE");
            if (!string.IsNullOrWhiteSpace(suppliedPath))
                VerifyFinalization(main, owner, tracking, suppliedPath);
            VerifyRemainingModalWindow(owner, tracking);
            VerifyDisabledOwner(owner);
            VerifyFailedOpening(owner, tracking);
            VerifyClosedOwner();
        }
        finally
        {
            owner.Close();
            lifetime.MainWindow = previousMainWindow;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyRemainingModalWindow(Window owner, TrackingWindowPlatform tracking)
    {
        var first = new Window();
        var second = new Window();
        var firstModal = OwnedWindowDialog.ShowAsync<object?>(first, owner);
        var secondModal = OwnedWindowDialog.ShowAsync<object?>(second, owner);
        first.Close();
        PumpUntil(() => firstModal.IsCompleted);
        firstModal.GetAwaiter().GetResult();
        Assert.False(tracking.Enabled);
        Assert.True(second.IsVisible);
        second.Close();
        PumpUntil(() => secondModal.IsCompleted);
        secondModal.GetAwaiter().GetResult();
        Assert.True(tracking.Enabled);
    }

    private static void VerifyDisabledOwner(Window owner)
    {
        owner.IsEnabled = false;
        var dialog = new Window();
        var modal = OwnedWindowDialog.ShowAsync<object?>(dialog, owner);
        dialog.Close();
        PumpUntil(() => modal.IsCompleted);
        modal.GetAwaiter().GetResult();
        Assert.False(owner.IsEnabled);
        owner.IsEnabled = true;
    }

    private static void VerifyClosedOwner()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new Window();
        var modal = OwnedWindowDialog.ShowAsync<object?>(dialog, owner);
        owner.Close();
        PumpUntil(() => modal.IsCompleted);
        modal.GetAwaiter().GetResult();
        Assert.False(owner.IsVisible);
        Assert.False(dialog.IsVisible);
    }

    private static void VerifyFailedOpening(Window owner, TrackingWindowPlatform tracking)
    {
        var dialog = new Window();
        dialog.Close();
        var modal = OwnedWindowDialog.ShowAsync<object?>(dialog, owner);
        PumpUntil(() => modal.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => modal.GetAwaiter().GetResult());
        Assert.True(tracking.Enabled);
        Assert.True(owner.IsEnabled);
    }

    private static void VerifyFinalization(MainViewModel main, Window owner, TrackingWindowPlatform tracking, string path)
    {
        var edit = main.OpenManualMapEditorCommand.Execute().ToTask();
        PumpUntil(() => owner.OwnedWindows.OfType<ManualMapEditorWindow>().Any());
        var window = owner.OwnedWindows.OfType<ManualMapEditorWindow>().Single();
        Assert.False(tracking.Enabled);
        var editor = Assert.IsType<ManualMapEditorViewModel>(window.DataContext);
        editor.RequestLoadProject(path);
        var validation = editor.ValidateCommand.Execute().ToTask();
        PumpUntil(() => validation.IsCompleted, () => editor.Diagnostics, TimeSpan.FromSeconds(60));
        validation.GetAwaiter().GetResult();
        Assert.True(editor.CanFinalize, editor.Diagnostics);
        var expected = editor.CreateResult();
        bool? enabledWhenAccepted = null;
        bool? detachedWhenAccepted = null;
        void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.HasManualMapDraft))
            {
                enabledWhenAccepted = tracking.Enabled;
                detachedWhenAccepted = window.Owner is null && window.DataContext is null;
            }
        }
        main.PropertyChanged += OnMainChanged;
        try
        {
            FindButton(window, "Сформировать карту").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => edit.IsCompleted, () => main.StatusMessage);
            edit.GetAwaiter().GetResult();
            Assert.True(enabledWhenAccepted, "Main state was updated inside Closed while its native window was still disabled.");
            Assert.True(detachedWhenAccepted, "The editor must finish closing and release its data context before the result is accepted.");
            Assert.True(tracking.Enabled);
            Assert.True(tracking.EnabledOnLastActivation);
            Assert.True(owner.IsEnabled);
            Assert.True(main.HasManualMapDraft);
            Assert.Null(window.DataContext);
            Assert.Equal(expected.Draft.GridWidth, main.ManualGridWidth);
            Assert.Equal(expected.SpatialOptions.Coverage, main.SpatialConfiguration.BuildSpatialOptions().Coverage);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            main.PropertyChanged -= OnMainChanged;
        }

        var reopen = main.OpenManualMapEditorCommand.Execute().ToTask();
        PumpUntil(() => owner.OwnedWindows.OfType<ManualMapEditorWindow>().Any());
        var reopened = owner.OwnedWindows.OfType<ManualMapEditorWindow>().Single();
        Assert.Equal(expected.Draft, Assert.IsType<ManualMapEditorViewModel>(reopened.DataContext).Draft);
        reopened.Close(null);
        PumpUntil(() => reopen.IsCompleted);
        reopen.GetAwaiter().GetResult();
        Assert.True(tracking.Enabled);
        Assert.True(tracking.EnabledOnLastActivation);
        Assert.True(owner.IsEnabled);
    }
}

// A transparent platform spy, forwarding the real headless implementation.
public class TrackingWindowPlatform : DispatchProxy
{
    public IWindowImpl Inner { get; set; } = null!;
    public bool Enabled { get; private set; } = true;
    public bool? EnabledOnLastActivation { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "SetEnabled")
            Enabled = (bool)args![0]!;
        if (targetMethod.Name == "Activate")
            EnabledOnLastActivation = Enabled;
        return targetMethod.Invoke(Inner, args);
    }
}
