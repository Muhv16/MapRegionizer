using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Views;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Options;
using MapRegionizer.App.Services;
using System.Reactive.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Size = Avalonia.Size;
using Xunit;

namespace MapRegionizer.Core.Tests;

// Invoked on the existing headless application's UI thread, avoiding a second
// Avalonia application/dispatcher with a different thread affinity in the suite.
internal static class MapTransformDialogContractChecks
{
    public static void Run(Window owner)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try
        {
            using var editor = CreateEditor();
            foreach (var scaling in new[] { 1.0, 1.5, 2.0 })
            {
                VerifyLayout(new GeographicCalibrationDialog(editor), owner, scaling);
                VerifyLayout(new ManualMapReprojectionDialog(editor), owner, scaling);
            }
            VerifyCalibrationWorkflow(editor, owner);
            VerifyCalibrationApplicationClosesDialog(owner);
            VerifyReprojectionWorkflow(editor, owner);
            VerifyAtomicApplicationAndPersistence(owner);
            ManualMapSeamDialogChecks.Run(owner);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyLayout(Window dialog, Window owner, double scaling)
    {
        if (scaling == 2)
            dialog.RequestedThemeVariant = ThemeVariant.Dark;
        var modal = dialog.ShowDialog(owner);
        dialog.SetRenderScaling(scaling);
        try
        {
            foreach (var size in new[] { new Size(740, 480), new Size(1080, 760), new Size(1460, 900) })
            {
                dialog.Width = size.Width;
                dialog.Height = size.Height;
                var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
                for (var page = 0; page < 3; page++)
                {
                    tabs.SelectedIndex = page;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(size.Width, dialog.ClientSize.Width);
                    Assert.Equal(size.Height, dialog.ClientSize.Height);
                    foreach (var input in dialog.GetVisualDescendants().OfType<TextBox>().Where(input => input.IsEffectivelyVisible && !input.IsReadOnly))
                    {
                        Assert.True(input.Bounds.Width >= 120, $"{dialog.Title}, scale {scaling}, page {page}: input width {input.Bounds.Width}");
                        var origin = input.TranslatePoint(default, dialog)!.Value;
                        Assert.InRange(origin.X, 0, dialog.ClientSize.Width - input.Bounds.Width + 1);
                    }
                    // Footer actions must remain on screen even if a page needs scrolling.
                    var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Отмена"));
                    var point = cancel.TranslatePoint(default, dialog)!.Value;
                    Assert.InRange(point.Y, 0, dialog.ClientSize.Height - cancel.Bounds.Height + 1);
                    Assert.InRange(point.X, 0, dialog.ClientSize.Width - cancel.Bounds.Width + 1);
                    if (page == 2 && dialog is ManualMapReprojectionDialog)
                        AssertPreviewBackground(dialog);
                    SaveSnapshot(dialog, $"{dialog.GetType().Name}-{scaling.ToString(CultureInfo.InvariantCulture)}-{size.Width}-{page}");
                }
            }
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(modal.IsCompleted);
        Assert.True(owner.IsEnabled);
    }

    private static void VerifyCalibrationWorkflow(ManualMapEditorViewModel editor, Window owner)
    {
        var original = editor.Draft;
        var dialog = new GeographicCalibrationDialog(editor);
        dialog.ShowDialog(owner);
        var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var fit = FindButton(dialog, "Подобрать и проверить");
        fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => FindButton(dialog, "К геометрии…").IsEnabled);
        var report = VisibleText(dialog);
        Assert.Contains("Модель подобрана", report);
        Assert.DoesNotContain("NaN", report);
        Assert.DoesNotContain("не число", report);
        Assert.Contains("Ошибка на точках подбора:", report);
        Assert.Contains("Независимая проверка не выполнялась.", report);
        Assert.DoesNotContain("не подтверждена", report);
        Assert.DoesNotContain("не менее трёх", report);
        SaveSnapshot(dialog, "calibration-result");
        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        SaveSnapshot(dialog, "calibration-preview");
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, editor.Draft);

        // Cancel via both the action and the native window close path.
        foreach (var closeWithButton in new[] { true, false })
        {
            FindButton(dialog, "К геометрии…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var confirmation = dialog.OwnedWindows.Single();
            Assert.Contains("Ошибка на точках подбора:", VisibleText(confirmation));
            Assert.DoesNotContain("не подтверждена", VisibleText(confirmation));
            if (closeWithButton)
                FindButton(confirmation, "Отмена").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else
                confirmation.Close();
            PumpUntil(() => dialog.IsEnabled);
            Assert.Same(original, editor.Draft);
        }

        // Editing during a pending calculation must cancel the old result.
        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var rows = dialog.GetVisualDescendants().OfType<ListBox>().Single().ItemsSource!.Cast<GeographicControlPointRowViewModel>().ToArray();
        rows[0].LatitudeText = "48,125";
        Assert.False(FindButton(dialog, "К геометрии…").IsEnabled);
        tabs.SelectedIndex = 1;
        fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        rows[0].LatitudeText = "47.5";
        PumpFor(TimeSpan.FromMilliseconds(150));
        Assert.False(FindButton(dialog, "К геометрии…").IsEnabled);
        Assert.Same(original, editor.Draft);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(owner.IsEnabled);
    }

    private static void VerifyReprojectionWorkflow(ManualMapEditorViewModel editor, Window owner)
    {
        var dialog = new ManualMapReprojectionDialog(editor);
        dialog.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        FindButton(dialog, "Проверить результат").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => FindButton(dialog, "Применить").IsEnabled, () => VisibleText(dialog));
        var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var number = dialog.GetVisualDescendants().OfType<NumericUpDown>().First();
        number.Text = "2,5";
        Assert.Equal(2.5m, number.Value);
        Assert.False(FindButton(dialog, "Применить").IsEnabled);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(owner.IsEnabled);
    }

    private static void VerifyCalibrationApplicationClosesDialog(Window owner)
    {
        using var editor = CreateEditor();
        var dialog = new GeographicCalibrationDialog(editor);
        var modal = dialog.ShowDialog(owner);
        var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        FindButton(dialog, "Подобрать и проверить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => FindButton(dialog, "К геометрии…").IsEnabled);
        FindButton(dialog, "К геометрии…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var confirmation = dialog.OwnedWindows.Single();
        FindButton(confirmation, "Применить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => modal.IsCompleted);
        Assert.False(dialog.IsVisible);
        Assert.True(owner.IsEnabled);
        Assert.True(editor.CalibrationParameters!.IsApplied);
        Assert.NotNull(editor.CalibrationReport);
    }

    private static void VerifyAtomicApplicationAndPersistence(Window owner)
    {
        using var editor = CreateEditor();
        var imagePath = Path.Combine(Path.GetTempPath(), $"map-transform-{Guid.NewGuid():N}.png");
        var projectPath = imagePath + ".manual-map.json";
        try
        {
            using (var image = new Image<Rgba32>(4, 4, new Rgba32(100, 150, 200, 255)))
                image.SaveAsPng(imagePath);
            editor.LoadBackground(imagePath);
            var oldImage = editor.BackgroundImage;
            var oldPlacement = editor.BackgroundPlacement!;
            var oldDraft = editor.Draft;
            var oldReference = editor.SpatialReference;
            var oldPoints = editor.ControlPoints.ToArray();
            var fit = new ParametricGeoreferenceSolver().Solve(new(oldReference, oldPoints));
            var parameters = fit.Parameters!;
            var transform = ParametricGeoreferenceSolver.CreateTransform(oldReference, parameters);
            var target = oldReference with { GridWidth = 200, UnitsPerCell = .5 };
            var request = new ManualMapReprojectionRequest(oldDraft, oldReference, target, GeoreferenceTransform: transform);
            var result = new ManualMapReprojectionService().Reproject(request);
            Assert.True(result.IsSuccessful);
            var png = ManualMapBackgroundReprojector.ReprojectToPng(imagePath, oldPlacement, oldReference, target, georeferenceTransform: transform);

            var rejected = editor.ApplyReprojectionAsync(request, result with { IsSuccessful = false });
            Assert.True(rejected.IsFaulted);
            Assert.Same(oldDraft, editor.Draft);
            Assert.Same(oldImage, editor.BackgroundImage);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var cancelled = editor.ApplyReprojectionAsync(request, result, cancellationToken: cancellation.Token);
                PumpUntil(() => cancelled.IsCompleted);
                Assert.True(cancelled.IsCanceled);
                Assert.Same(oldDraft, editor.Draft);
                Assert.Equal(oldReference, editor.SpatialReference);
            }

            var apply = editor.ApplyReprojectionAsync(request, result, oldPoints, parameters, png, oldPlacement, fit);
            PumpUntil(() => apply.IsCompleted);
            apply.GetAwaiter().GetResult();
            Assert.Equal(target, editor.SpatialReference);
            Assert.NotSame(oldImage, editor.BackgroundImage);
            Assert.True(editor.CalibrationParameters!.IsApplied);
            Assert.True(editor.CalibrationParameters.IsAppliedToBackground);
            var newDraft = editor.Draft;
            var newImage = editor.BackgroundImage;
            var recipe = editor.BackgroundReprojectionState;

            editor.UndoCommand.Execute().Subscribe();
            Assert.Same(oldDraft, editor.Draft);
            Assert.Equal(oldReference, editor.SpatialReference);
            Assert.Same(oldImage, editor.BackgroundImage);
            Assert.Equal(oldPoints, editor.ControlPoints);
            Assert.Null(editor.CalibrationParameters);
            Assert.Null(editor.BackgroundReprojectionState);
            editor.RedoCommand.Execute().Subscribe();
            Assert.Same(newDraft, editor.Draft);
            Assert.Same(newImage, editor.BackgroundImage);
            Assert.Equal(recipe, editor.BackgroundReprojectionState);
            Assert.True(editor.CalibrationParameters!.IsApplied);

            editor.SaveProject(projectPath);
            using var loaded = CreateEditor();
            loaded.LoadProject(projectPath);
            Assert.Equal(newDraft.Vertices, loaded.Draft.Vertices);
            Assert.Equal(target, loaded.SpatialReference);
            Assert.Equal(editor.CalibrationParameters, loaded.CalibrationParameters);
            Assert.Equal(recipe, loaded.BackgroundReprojectionState);
            Assert.NotNull(loaded.CalibrationReport);
            VerifySavedBackgroundReprojection(loaded, owner, oldReference);
            loaded.AddGeographicControlPoint(new(25, 25));
            Assert.True(loaded.CalibrationParameters!.IsApplied);
            loaded.UpdateControlPoints(loaded.ControlPoints);
            Assert.True(loaded.CalibrationParameters!.IsAppliedToBackground);

            editor.UndoCommand.Execute().Subscribe();
            var backgroundParameters = parameters with { IsAppliedToBackground = true };
            var backgroundPng = ManualMapBackgroundReprojector.ReprojectToPng(imagePath, oldPlacement, oldReference, oldReference, georeferenceTransform: transform);
            editor.ApplyBackgroundReprojection(backgroundPng, oldPlacement, backgroundParameters, oldPoints, fit);
            Assert.Same(oldDraft, editor.Draft);
            Assert.Equal(oldReference, editor.SpatialReference);
            Assert.True(editor.CalibrationParameters!.IsAppliedToBackground);
            Assert.False(editor.CalibrationParameters.IsApplied);
            editor.UndoCommand.Execute().Subscribe();
            Assert.Same(oldImage, editor.BackgroundImage);
        }
        finally
        {
            foreach (var path in new[] { imagePath, projectPath, projectPath + ".editor.json" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    private static void VerifySavedBackgroundReprojection(ManualMapEditorViewModel editor, Window owner, MapSpatialReference originalReference)
    {
        var dialog = new ManualMapReprojectionDialog(editor);
        var selection = dialog.ShowDialog<ManualMapReprojectionSelection?>(owner);
        var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        var background = dialog.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Перепроецировать фоновое изображение"));
        Assert.True(background.IsEnabled);
        background.IsChecked = true;
        FindButton(dialog, "Проверить результат").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => FindButton(dialog, "Применить").IsEnabled, () => VisibleText(dialog));
        FindButton(dialog, "Применить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => selection.IsCompleted);
        var result = selection.GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal(originalReference, result.BackgroundState!.SourceReference);
        Assert.NotNull(result.BackgroundState.CalibrationParameters);
        Assert.NotNull(result.BackgroundPng);
        Assert.True(owner.IsEnabled);
        var apply = editor.ApplyReprojectionAsync(result.Request, result.Result!,
            reprojectedBackgroundPng: result.BackgroundPng,
            sourceBackgroundPlacement: result.BackgroundState.SourcePlacement,
            backgroundReprojectionState: result.BackgroundState);
        PumpUntil(() => apply.IsCompleted);
        apply.GetAwaiter().GetResult();
        Assert.NotNull(editor.BackgroundReprojectionState!.CalibrationParameters);
        Assert.True(editor.CalibrationParameters!.IsAppliedToBackground);
    }

    internal static Button FindButton(Window dialog, string content) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));

    internal static string VisibleText(Window dialog) => string.Join("\n", dialog.GetVisualDescendants()
        .OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text));

    internal static void SaveSnapshot(Window dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MAPREGIONIZER_UI_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        using var frame = RenderCurrentLayout(dialog);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private static void AssertPreviewBackground(Window dialog)
    {
        var preview = dialog.GetVisualDescendants().OfType<ManualMapReprojectionPreview>().Single();
        var origin = preview.TranslatePoint(new Avalonia.Point(3, 3), dialog)!.Value;
        using var frame = RenderCurrentLayout(dialog);
        using var stream = new MemoryStream();
        frame.Save(stream);
        using var pixels = SixLabors.ImageSharp.Image.Load<Rgba32>(stream.ToArray());
        var pixel = pixels[(int)(origin.X * dialog.RenderScaling), (int)(origin.Y * dialog.RenderScaling)];
        Assert.Equal(new Rgba32(11, 18, 32, 255), pixel);
    }

    private static RenderTargetBitmap RenderCurrentLayout(Window dialog)
    {
        // Read the current visual tree rather than a compositor frame that may
        // still belong to the previous window size after a simulated DPI change.
        Dispatcher.UIThread.RunJobs();
        var scale = dialog.RenderScaling;
        var bitmap = new RenderTargetBitmap(new PixelSize(
            (int)Math.Ceiling(dialog.ClientSize.Width * scale),
            (int)Math.Ceiling(dialog.ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(dialog);
        return bitmap;
    }

    internal static void PumpUntil(Func<bool> condition, Func<string>? diagnostic = null, TimeSpan? timeout = null)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < (timeout ?? TimeSpan.FromSeconds(10)))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(condition(), "The dialog did not finish its calculation. " + diagnostic?.Invoke());
    }

    private static void PumpFor(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static ManualMapEditorViewModel CreateEditor()
    {
        var spatial = new MapSpatialOptions { Coverage = MapCoverage.Global(-80, 80), PreserveProjectedCellAspectRatio = false };
        var draft = new ManualMapDraft(100, 100,
            [new(1, new(20, 20)), new(2, new(70, 20)), new(3, new(40, 70))],
            [new(1, [1, 2, 3], "test")]);
        var configuration = new SpatialConfigurationViewModel();
        configuration.LoadSpatialOptions(spatial);
        var editor = new ManualMapEditorViewModel(draft, spatial.CreateReference(100, 100), new MapGenerationOptions { Spatial = spatial }, configuration);
        editor.UpdateControlPoints([
            new(1, new string('Ж', 100), GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-108, 48), new(20, 20)),
            new(2, "Южный ориентир", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(72, -32), new(70, 70))
        ]);
        return editor;
    }
}
