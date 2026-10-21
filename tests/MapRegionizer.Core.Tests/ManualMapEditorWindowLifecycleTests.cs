using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MapRegionizer.App.Services;
using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Views;
using ReactiveUI.Avalonia;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualMapEditorWindowLifecycleTests
{
    [Fact]
    public async Task ClosingEmptyEditorReturnsControlAndDoesNotLeaveManualMapWarning()
    {
        AppBuilder.Configure<HeadlessTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseReactiveUI(_ => { })
            .SetupWithoutStarting();

        var settingsDirectory = Path.Combine(Path.GetTempPath(), $"MapRegionizer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(settingsDirectory);
        Window? owner = null;
        try
        {
            using var viewModel = new MainViewModel(
                new UserSettingsService(Path.Combine(settingsDirectory, "settings.json")));
            viewModel.OutputDirectory = settingsDirectory;
            viewModel.IsManualSource = true;

            const string manualMapRequiredMessage = "Сначала откройте manual editor и сформируйте корректную карту.";
            Assert.DoesNotContain(manualMapRequiredMessage, viewModel.ValidationMessage, StringComparison.Ordinal);

            owner = new Window();
            owner.Show();
            Dispatcher.UIThread.RunJobs();

            var resources = new TrackingDisposable();
            var editor = new ManualMapEditorWindow { DataContext = resources };
            var dialogTask = editor.ShowDialog<object?>(owner);

            editor.Close(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(dialogTask.IsCompleted);
            Assert.True(owner.IsEnabled);
            Assert.False(resources.IsDisposed);
            Assert.DoesNotContain(manualMapRequiredMessage, viewModel.ValidationMessage, StringComparison.Ordinal);

            editor.DataContext = null;
            resources.Dispose();

            var generationAttempt = viewModel.RunFullCommand.Execute().ToTask();
            Assert.True(generationAttempt.IsCompleted);
            await generationAttempt;
            Assert.Equal(manualMapRequiredMessage, viewModel.ValidationMessage);

            await viewModel.OpenManualMapEditorCommand.Execute().ToTask();
            Assert.DoesNotContain(manualMapRequiredMessage, viewModel.ValidationMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (owner?.IsVisible == true)
                owner.Close();
            Directory.Delete(settingsDirectory, recursive: true);
        }

    }

    private sealed class HeadlessTestApplication : Application;

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
