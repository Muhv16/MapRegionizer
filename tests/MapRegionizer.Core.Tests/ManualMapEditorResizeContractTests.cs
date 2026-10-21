using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualMapEditorResizeContractTests
{
    static ManualMapEditorResizeContractTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public void ResizeUndoAndRedoRestoreGeometryCoverageAndBackgroundRectangle()
    {
        var options = CreateSpatialOptions();
        var draft = SquareDraft(10, 10);
        var viewModel = new ManualMapEditorViewModel(
            draft,
            options.CreateReference(10, 10),
            new MapGenerationOptions { Spatial = options },
            Configuration(options));
        viewModel.BackgroundOffsetX = 3;
        viewModel.BackgroundOffsetY = 4;
        viewModel.BackgroundRotation = 17;
        var backgroundWidth = viewModel.BackgroundMapWidth;
        var backgroundHeight = viewModel.BackgroundMapHeight;

        viewModel.ApplyCanvasResize(new ManualCanvasResizeRequest(2, 0, 1, 0));

        Assert.Equal(11, viewModel.GridWidth);
        Assert.Equal(12, viewModel.GridHeight);
        Assert.Equal(5, viewModel.BackgroundMapX);
        Assert.Equal(8, viewModel.BackgroundMapY);
        Assert.Equal(backgroundWidth, viewModel.BackgroundMapWidth);
        Assert.Equal(backgroundHeight, viewModel.BackgroundMapHeight);
        Assert.Equal(17, viewModel.BackgroundRotation);
        Assert.Equal(-12, viewModel.SpatialConfiguration.WestLongitude, 10);
        Assert.True(viewModel.CanUndo);

        viewModel.UndoCommand.Execute().Subscribe();

        Assert.Equal(10, viewModel.GridWidth);
        Assert.Equal(10, viewModel.GridHeight);
        Assert.Equal(draft.Vertices, viewModel.Draft.Vertices);
        Assert.Equal(3, viewModel.BackgroundMapX);
        Assert.Equal(4, viewModel.BackgroundMapY);
        Assert.Equal(backgroundWidth, viewModel.BackgroundMapWidth);
        Assert.Equal(backgroundHeight, viewModel.BackgroundMapHeight);
        Assert.Equal(17, viewModel.BackgroundRotation);
        Assert.Equal(-10, viewModel.SpatialConfiguration.WestLongitude);
        Assert.True(viewModel.CanRedo);

        viewModel.RedoCommand.Execute().Subscribe();

        Assert.Equal(11, viewModel.GridWidth);
        Assert.Equal(12, viewModel.GridHeight);
        Assert.Equal(5, viewModel.BackgroundMapX);
        Assert.Equal(8, viewModel.BackgroundMapY);
        Assert.Equal(-12, viewModel.SpatialConfiguration.WestLongitude);
        viewModel.Dispose();
    }

    [Fact]
    public void SavedProjectRestoresItsOwnGridAndSpatialReference()
    {
        var options = CreateSpatialOptions();
        var source = new ManualMapEditorViewModel(
            SquareDraft(10, 10),
            options.CreateReference(10, 10),
            new MapGenerationOptions { Spatial = options },
            Configuration(options));
        source.ApplyCanvasResize(new ManualCanvasResizeRequest(3, 0, 0, 0));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.manual-map.json");

        try
        {
            source.SaveProject(path);
            var otherOptions = CreateSpatialOptions() with
            {
                Coverage = MapCoverage.Global(-60, 60)
            };
            var destination = new ManualMapEditorViewModel(
                ManualMapDraft.Empty(3, 4),
                otherOptions.CreateReference(3, 4),
                new MapGenerationOptions { Spatial = otherOptions },
                Configuration(otherOptions));

            destination.LoadProject(path);

            Assert.Equal(10, destination.GridWidth);
            Assert.Equal(13, destination.GridHeight);
            Assert.Equal(16, destination.SpatialConfiguration.NorthLatitude, 10);
            Assert.Equal(6, destination.BackgroundMapY);
            Assert.Equal(20, destination.BackgroundMapHeight);
            Assert.Equal(10, destination.Draft.Regions.Single().Id);
            Assert.Equal(destination.Draft.Vertices.Select(vertex => vertex.Id),
                source.Draft.Vertices.Select(vertex => vertex.Id));
            Assert.Contains("загружен", destination.Diagnostics, StringComparison.OrdinalIgnoreCase);
            destination.Dispose();
        }
        finally
        {
            source.Dispose();
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".editor.json"))
                File.Delete(path + ".editor.json");
        }
    }

    [Fact]
    public void LegacyProjectRequiresConfirmationAndThenUsesCurrentSpatialSettings()
    {
        var options = CreateSpatialOptions();
        var editor = new ManualMapEditorViewModel(
            ManualMapDraft.Empty(4, 4),
            options.CreateReference(4, 4),
            new MapGenerationOptions { Spatial = options },
            Configuration(options));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.manual-map.json");

        try
        {
            MapRegionizer.GeoJson.ManualMapJson.Save(path, SquareDraft(9, 7));

            Assert.True(ManualMapEditorViewModel.ProjectNeedsSpatialReferenceConfirmation(path));
            editor.LoadProject(path);
            Assert.Equal(4, editor.GridWidth);
            Assert.Equal(4, editor.GridHeight);
            Assert.Contains("Подтвердите", editor.Diagnostics, StringComparison.OrdinalIgnoreCase);

            editor.LoadProject(path, useCurrentSpatialReferenceForLegacy: true);

            Assert.Equal(9, editor.GridWidth);
            Assert.Equal(7, editor.GridHeight);
            Assert.Equal(-10, editor.SpatialConfiguration.SouthLatitude);
            Assert.Contains("географическая привязка", editor.Diagnostics, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            editor.Dispose();
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".editor.json"))
                File.Delete(path + ".editor.json");
        }
    }

    [Fact]
    public void CancellingLegacyProjectImportClosesConfirmationWithoutChangingEditorState()
    {
        var options = CreateSpatialOptions();
        var draft = ManualMapDraft.Empty(4, 4);
        var editor = new ManualMapEditorViewModel(
            draft,
            options.CreateReference(4, 4),
            new MapGenerationOptions { Spatial = options },
            Configuration(options));
        var originalSpatialOptions = editor.SpatialConfiguration.BuildSpatialOptions();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.manual-map.json");

        try
        {
            MapRegionizer.GeoJson.ManualMapJson.Save(path, SquareDraft(9, 7));

            editor.RequestLoadProject(path);

            Assert.True(editor.IsLegacyProjectConfirmationVisible);
            Assert.Same(draft, editor.Draft);
            Assert.Equal(4, editor.GridWidth);
            Assert.Equal(4, editor.GridHeight);
            Assert.Equal(originalSpatialOptions, editor.SpatialConfiguration.BuildSpatialOptions());

            editor.CancelPendingProjectLoad();

            Assert.False(editor.IsLegacyProjectConfirmationVisible);
            Assert.Same(draft, editor.Draft);
            Assert.Equal(4, editor.GridWidth);
            Assert.Equal(4, editor.GridHeight);
            Assert.Equal(originalSpatialOptions, editor.SpatialConfiguration.BuildSpatialOptions());
            Assert.Contains("отменена", editor.Diagnostics, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            editor.Dispose();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static SpatialConfigurationViewModel Configuration(MapSpatialOptions options)
    {
        var configuration = new SpatialConfigurationViewModel();
        configuration.LoadSpatialOptions(options);
        return configuration;
    }

    private static MapSpatialOptions CreateSpatialOptions() => new()
    {
        WorldModel = WorldModelDescriptor.Spherical(),
        Coverage = MapCoverage.Regional(-10, 10, -10, 10),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.OpenRectangular,
        UnitsPerCell = 2,
        PreserveProjectedCellAspectRatio = false
    };

    private static ManualMapDraft SquareDraft(int width, int height) => new(
        width,
        height,
        [
            new ManualMapVertex(1, new MapPoint(2, 2)),
            new ManualMapVertex(2, new MapPoint(8, 2)),
            new ManualMapVertex(3, new MapPoint(8, 8)),
            new ManualMapVertex(4, new MapPoint(2, 8))
        ],
        [new ManualRegionFace(10, [1, 2, 3, 4], "Region")]);
}
