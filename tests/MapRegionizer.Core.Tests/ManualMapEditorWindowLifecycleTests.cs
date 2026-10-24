using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    public async Task ManualMapDialogsReturnControlAfterCancellationAndFinalization()
    {
        AppBuilder.Configure<HeadlessTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseReactiveUI(_ => { })
            .SetupWithLifetime(new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown });

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
            MapTransformDialogContractChecks.Run(owner);
            ManualMapFinalizationDialogChecks.Run(settingsDirectory);

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

    private sealed class HeadlessTestApplication : Application
    {
        public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
