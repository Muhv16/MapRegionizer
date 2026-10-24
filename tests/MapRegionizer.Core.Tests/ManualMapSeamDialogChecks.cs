using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Views;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.GeoJson;
using System.Reactive.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static MapRegionizer.Core.Tests.MapTransformDialogContractChecks;

namespace MapRegionizer.Core.Tests;

internal static class ManualMapSeamDialogChecks
{
    private static readonly JsonSerializerOptions SummaryOptions = new() { WriteIndented = true };
    public static void Run(Window owner)
    {
        VerifySuppliedMap();
        VerifyCalibrationAction(owner);
        VerifyDirectReprojectionAction(owner);
    }

    private static ManualMapEditorViewModel Editor(ManualMapDraft draft, MapSpatialReference reference)
    {
        var spatial = new MapSpatialOptions
        {
            WorldModel = reference.WorldModel,
            Coverage = reference.Coverage,
            GridMapping = reference.GridMapping,
            Topology = reference.Topology,
            UnitsPerCell = reference.UnitsPerCell,
            PreserveProjectedCellAspectRatio = reference.PreserveProjectedCellAspectRatio
        };
        var configuration = new SpatialConfigurationViewModel();
        configuration.LoadSpatialOptions(spatial);
        return new(draft, reference, new MapGenerationOptions { Spatial = spatial }, configuration);
    }

    private static void VerifyCalibrationAction(Window owner)
    {
        using var editor = Editor(ManualMapSeamContractTests.Draft(), ManualMapSeamContractTests.Reference());
        editor.UpdateControlPoints([
            new(1, "north", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-96, 48), new(20, 20)),
            new(2, "south", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(84, -32), new(70, 70))
        ]);
        var imagePath = Path.Combine(Path.GetTempPath(), $"map-seam-{Guid.NewGuid():N}.png");
        var projectPath = imagePath + ".manual-map.json";
        try
        {
            using (var image = new Image<Rgba32>(4, 4, new Rgba32(50, 100, 150, 255)))
                image.SaveAsPng(imagePath);
            editor.LoadBackground(imagePath);
            var original = editor.Draft;
            var dialog = new GeographicCalibrationDialog(editor);
            var modal = dialog.ShowDialog(owner);
            dialog.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            FindButton(dialog, "Подобрать и проверить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var relocate = FindButton(dialog, "Сохранить контуры и проверить привязку");
            PumpUntil(() => relocate.IsVisible, () => VisibleText(dialog));
            Assert.False(FindButton(dialog, "К геометрии…").IsEnabled);
            Assert.Same(original, editor.Draft);
            SaveSnapshot(dialog, "calibration-seam-blocked");
            relocate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => FindButton(dialog, "К обоим слоям…").IsEnabled, () => VisibleText(dialog));
            Assert.False(FindButton(dialog, "К фону…").IsEnabled);
            Assert.False(relocate.IsVisible);
            Assert.Same(original, editor.Draft);
            Assert.Contains("Новый разрез карты", VisibleText(dialog));
            SaveSnapshot(dialog, "calibration-seam-resolved");
            FindButton(dialog, "К обоим слоям…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var confirmation = dialog.OwnedWindows.Single();
            FindButton(confirmation, "Применить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => modal.IsCompleted);
            Assert.True(owner.IsEnabled);
            Assert.Equal(-168, editor.SpatialReference.Longitude.Start, 8);
            Assert.Equal(editor.SpatialReference, editor.BackgroundReprojectionState!.TargetReference);
            Assert.Equal(-180, editor.BackgroundReprojectionState.SourceReference.Longitude.Start);
            Assert.True(editor.CalibrationParameters!.IsApplied);
            Assert.True(editor.CalibrationParameters.IsAppliedToBackground);
            editor.SaveProject(projectPath);
            using var loaded = Editor(ManualMapDraft.Empty(100, 100), ManualMapSeamContractTests.Reference());
            loaded.LoadProject(projectPath);
            Assert.Equal(editor.SpatialReference, loaded.SpatialReference);
            Assert.Equal(-168, loaded.SpatialConfiguration.BuildSpatialOptions().Coverage.West, 8);
            Assert.Equal(editor.BackgroundReprojectionState, loaded.BackgroundReprojectionState);
            loaded.AddGeographicControlPoint(new(99, 50));
            Assert.Equal(-171.6, loaded.ControlPoints[^1].GeographicPosition.LongitudeDegrees, 8);
            editor.UndoCommand.Execute().Subscribe();
            Assert.Same(original, editor.Draft);
            Assert.Equal(-180, editor.SpatialReference.Longitude.Start);
            editor.RedoCommand.Execute().Subscribe();
            Assert.Equal(-168, editor.SpatialReference.Longitude.Start, 8);
            Assert.Equal(editor.SpatialReference, editor.BackgroundReprojectionState!.TargetReference);
        }
        finally
        {
            foreach (var path in new[] { imagePath, projectPath, projectPath + ".editor.json" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void VerifyDirectReprojectionAction(Window owner)
    {
        using var editor = Editor(ManualMapSeamContractTests.Draft(), ManualMapSeamContractTests.Reference());
        var dialog = new ManualMapReprojectionDialog(editor);
        var selection = dialog.ShowDialog<ManualMapReprojectionSelection?>(owner);
        Dispatcher.UIThread.RunJobs();
        // Source tab fields are labelled individually; the longitude correction
        // is the last number on this page.
        dialog.GetVisualDescendants().OfType<NumericUpDown>().Last().Text = "12";
        FindButton(dialog, "Проверить результат").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var relocate = FindButton(dialog, "Перенести разрез карты и проверить");
        PumpUntil(() => relocate.IsVisible, () => VisibleText(dialog));
        relocate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => FindButton(dialog, "Применить").IsEnabled, () => VisibleText(dialog));
        FindButton(dialog, "Применить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => selection.IsCompleted);
        var result = selection.GetAwaiter().GetResult()!;
        Assert.Equal(-168, result.Request.TargetReference.Longitude.Start, 8);
        Assert.True(result.Result!.IsSuccessful);
    }

    private static void VerifySuppliedMap()
    {
        var path = Environment.GetEnvironmentVariable("MAPREGIONIZER_SEAM_SOURCE");
        if (string.IsNullOrWhiteSpace(path)) return;
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var project = ManualMapJson.LoadProject(path);
        using var editor = Editor(project.Draft, project.SpatialReference!);
        // Parameters are supplied by the user's calculation report; the v2
        // attachment itself has no stored GCPs. Do not invent or refit anchors.
        var parameters = new GeographicCalibrationParameters(1, GridMappingKind.Equirectangular, 12.0219, -28.8145, 83.7987, SourceReference: editor.SpatialReference);
        var transform = ParametricGeoreferenceSolver.CreateTransform(editor.SpatialReference, parameters);
        var request = new ManualMapReprojectionRequest(editor.Draft, editor.SpatialReference, editor.SpatialReference, GeoreferenceTransform: transform);
        var service = new ManualMapReprojectionService();
        var blocked = service.Reproject(request);
        Assert.Contains(blocked.Diagnostics, item => item.Code == "seam-crossing-unsupported");
        var target = transform.CreateCalibratedReference();
        request = request with { TargetReference = target };
        var result = service.Reproject(request);
        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => $"{item.Code} (region {item.RegionId}, vertex {item.VertexId}): {item.Message}")));
        Assert.Equal(project.Draft.Regions.Select(region => region.Id), result.Draft!.Regions.Select(region => region.Id));
        Assert.Equal(project.Draft.Regions.SelectMany(region => region.VertexIds), result.Draft.Regions.SelectMany(region => region.VertexIds));
        Assert.Equal(project.Draft.Vertices.Select(vertex => vertex.Id), result.Draft.Vertices.Select(vertex => vertex.Id));
        foreach (var (before, after) in project.Draft.Vertices.Zip(result.Draft.Vertices))
        {
            var expected = transform.ToGeographic(before.Position);
            var actual = MapSpatialContext.Create(target).GridToGeographic(new(after.Position.X / target.UnitsPerCell, after.Position.Y / target.UnitsPerCell));
            Assert.InRange(Math.Abs(LongitudeInterval.Normalize(expected.LongitudeDegrees - actual.LongitudeDegrees)), 0, 1e-8);
            Assert.Equal(expected.LatitudeDegrees, actual.LatitudeDegrees, 8);
            Assert.Equal(before.Position.X, after.Position.X, 8);
            Assert.Equal(before.Position.Y, after.Position.Y, 8);
        }
        var apply = editor.ApplyReprojectionAsync(request, result, calibrationParameters: parameters);
        PumpUntil(() => apply.IsCompleted, timeout: TimeSpan.FromMinutes(3));
        apply.GetAwaiter().GetResult();
        Assert.Equal(-167.9781, editor.SpatialReference.Longitude.Start, 8);
        var output = Environment.GetEnvironmentVariable("MAPREGIONIZER_SEAM_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            var copy = Path.Combine(output, "map.calibrated.manual-map.json");
            editor.SaveProject(copy);
            var restored = ManualMapJson.LoadProject(copy);
            Assert.Equal(target, restored.SpatialReference);
            Assert.Equal(result.Draft.Vertices, restored.Draft.Vertices);
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
            {
                source = path,
                sourceSha256 = Convert.ToHexString(hash),
                regions = result.Draft.Regions.Count,
                vertices = result.Draft.Vertices.Count,
                targetCut = target.Longitude.Start,
                result.IsSuccessful,
                result.AddedVertices,
                parameters
            }, SummaryOptions));
        }
        editor.UndoCommand.Execute().Subscribe();
        Assert.Same(project.Draft, editor.Draft);
        Assert.Equal(project.SpatialReference, editor.SpatialReference);
        editor.RedoCommand.Execute().Subscribe();
        Assert.Equal(target, editor.SpatialReference);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }
}
