using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Services;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class AppSpatialConfigurationContractTests
{
    [Fact]
    public void GlobalConfigurationBuildsIndependentSpatialOptions()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            WorldModel = WorldModelKind.Spherical,
            CoverageKind = MapCoverageKind.Global,
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.CylindricalX,
            UnitsPerCell = 1.5
        };

        var spatial = viewModel.BuildSpatialOptions();

        Assert.Equal(WorldModelKind.Spherical, spatial.WorldModel.Kind);
        Assert.Equal(MapCoverageKind.Global, spatial.Coverage.Kind);
        Assert.Equal(GridTopologyKind.CylindricalX, spatial.Topology);
        Assert.Equal(1.5, spatial.UnitsPerCell);
    }

    [Fact]
    public void UnitsPerCellConfigurationFlowsIntoGenerationRequest()
    {
        var viewModel = new SpatialConfigurationViewModel { UnitsPerCell = 2.25 };
#pragma warning disable CS0618
        var legacyOptions = new MapGenerationOptions { PixelSize = 0.5 };
#pragma warning restore CS0618

        var request = viewModel.BuildRequest(FullMask(GridWindow.FromSize(2, 2)), legacyOptions);

        Assert.Equal(2.25, request.Options.EffectiveSpatial.UnitsPerCell);
    }

    [Fact]
    public void RegionalConfigurationBuildsShortAntimeridianInterval()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            CoverageKind = MapCoverageKind.Regional,
            WestLongitude = 170,
            EastLongitude = -170,
            SouthLatitude = -20,
            NorthLatitude = 30
        };

        var spatial = viewModel.BuildSpatialOptions();

        Assert.True(spatial.Coverage.Longitude.CrossesAntimeridian);
        Assert.Equal(20, spatial.Coverage.Longitude.SpanDegrees);
        Assert.Equal(GridTopologyKind.OpenRectangular, spatial.Topology);
    }

    [Fact]
    public void ChoosingRegionMakesOpenHorizontalEdgesExplicit()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            Topology = GridTopologyKind.CylindricalX
        };

        viewModel.CoverageKind = MapCoverageKind.Regional;

        Assert.True(viewModel.IsOpenRectangular);
        Assert.False(viewModel.IsHorizontalWrapping);
        Assert.Equal(GridTopologyKind.OpenRectangular, viewModel.Topology);
    }

    [Fact]
    public void LoadingRegionalSpatialSettingsKeepsOpenHorizontalEdges()
    {
        var viewModel = new SpatialConfigurationViewModel();
        var persisted = new MapSpatialOptions
        {
            Coverage = MapCoverage.Regional(-30, 30, -20, 20),
            Topology = GridTopologyKind.CylindricalX,
            LegacyCompatibility = LegacyCompatibilityProfile.Regional
        };

        viewModel.LoadSpatialOptions(persisted);

        Assert.Equal(MapCoverageKind.Regional, viewModel.CoverageKind);
        Assert.Equal(GridTopologyKind.OpenRectangular, viewModel.Topology);
        Assert.True(viewModel.IsOpenRectangular);
    }

    [Fact]
    public void WholeWorldConfigurationKeepsWorldContextIndependent()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            CoverageKind = MapCoverageKind.Regional,
            WorldContextMode = WorldContextMode.Automatic
        };

        viewModel.CoverageKind = MapCoverageKind.Global;

        Assert.Equal(WorldContextMode.Automatic, viewModel.WorldContextMode);
        Assert.True(viewModel.ShowWorldContext);
        Assert.True(viewModel.ShowWorldMask);
        Assert.True(viewModel.ShowWorkingHalo);
        Assert.True(viewModel.ShowRequestedOrigin);
        Assert.False(viewModel.ShowRegionBounds);
    }

    [Fact]
    public void WholeWorldSurroundingContextBuildsWithAnExplicitWorldSource()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            CoverageKind = MapCoverageKind.Global,
            WorldContextMode = WorldContextMode.Automatic
        };
        var requested = GridWindow.FromSize(3, 2);
        var mask = FullMask(requested);
        var source = new RecordingMaskSource();

        var request = viewModel.BuildRequest(
            mask,
            new MapGenerationOptions { Seed = 7 },
            source);
        _ = MapGenerationSession.Create(request);

        Assert.Equal(WorldContextMode.Automatic, request.WorldContextMode);
        Assert.Equal(request.WorkingDomain.Window, source.LastWindow);
    }

    [Fact]
    public void UnsupportedCustomWorldContextIsNormalizedAtAppBoundary()
    {
        var viewModel = new SpatialConfigurationViewModel();

        viewModel.WorldContextMode = WorldContextMode.Custom;

        Assert.Equal(WorldContextMode.Isolated, viewModel.WorldContextMode);
        Assert.Equal([WorldContextMode.Isolated, WorldContextMode.Automatic], viewModel.WorldContextModes);
    }

    [Fact]
    public void SurroundingContextRequiresAnExplicitWorldSource()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            CoverageKind = MapCoverageKind.Regional,
            WorldContextMode = WorldContextMode.Automatic
        };
        var mask = FullMask(GridWindow.FromSize(3, 3));

        var error = Assert.Throws<InvalidOperationException>(() =>
            viewModel.BuildRequest(mask, new MapGenerationOptions { Seed = 4 }));

        Assert.Contains("world", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SurroundingContextUsesWorldAlignedOriginAndWorkingHalo()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            CoverageKind = MapCoverageKind.Regional,
            WorldContextMode = WorldContextMode.Automatic,
            WestLongitude = -30,
            EastLongitude = 30,
            SouthLatitude = -20,
            NorthLatitude = 20,
            RequestedOriginX = 10,
            RequestedOriginY = 20,
            WorkingHaloCells = 2
        };
        var requestedWindow = new GridWindow(10, 20, 3, 2);
        var mask = FullMask(requestedWindow);
        var source = new RecordingMaskSource();

        var request = viewModel.BuildRequest(mask, new MapGenerationOptions { Seed = 4 }, source);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(new GridWindow(8, 18, 7, 6), request.WorkingDomain.Window);
        Assert.Equal(request.WorkingDomain.Window, source.LastWindow);
        Assert.Equal(request.WorkingDomain.Window, session.WorkingDomain.Window);
    }

    [Fact]
    public void ImageMaskAdapterKeepsPointsLocalWhenAssigningWorldOrigin()
    {
        using var image = new Image<Rgba32>(3, 2);
        image[0, 0] = new Rgba32(255, 255, 255, 255);
        image[2, 1] = new Rgba32(255, 255, 255, 255);

        var mask = ImageMaskReader.ReadAt(image, 10, 20);

        Assert.Equal(new GridWindow(10, 20, 3, 2), mask.Window);
        Assert.Equal(new HashSet<GridPoint> { new(0, 0), new(2, 1) }, mask.LandPoints);
        Assert.All(mask.LandPoints, point => Assert.True(point.X < mask.Width && point.Y < mask.Height));
    }

    [Fact]
    public void SpatialModeAndCoverageRaiseDerivedPropertyNotifications()
    {
        var viewModel = new SpatialConfigurationViewModel();
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changed.Add(args.PropertyName);
        };

        viewModel.CoverageKind = MapCoverageKind.Regional;
        viewModel.WorldContextMode = WorldContextMode.Automatic;

        Assert.True(viewModel.IsRegional);
        Assert.True(viewModel.RequiresMaskSource);
        Assert.Contains(nameof(SpatialConfigurationViewModel.IsRegional), changed);
        Assert.Contains(nameof(SpatialConfigurationViewModel.RequiresMaskSource), changed);
        Assert.Contains(nameof(SpatialConfigurationViewModel.WorldContextSelection), changed);
    }

    [Fact]
    public void AppExposesOnlyIsolatedAndAutomaticWorldContextChoices()
    {
        var viewModel = new SpatialConfigurationViewModel();

        Assert.Equal(WorldContextMode.Isolated, viewModel.WorldContextMode);
        Assert.Equal([WorldContextMode.Isolated, WorldContextMode.Automatic], viewModel.WorldContextModes);
        Assert.True(viewModel.IsIsolated);
        Assert.False(viewModel.IsAutomatic);
        Assert.False(viewModel.RequiresMaskSource);

        viewModel.CoverageKind = MapCoverageKind.Regional;
        viewModel.WorldContextMode = WorldContextMode.Automatic;

        Assert.True(viewModel.IsAutomatic);
        Assert.False(viewModel.IsIsolated);
        Assert.True(viewModel.RequiresMaskSource);
        Assert.True(viewModel.ShowWorldMask);
        Assert.True(viewModel.ShowRequestedOrigin);
        Assert.True(viewModel.ShowWorkingHalo);

        viewModel.WorldContextMode = WorldContextMode.Isolated;
        Assert.Equal(WorldContextMode.Isolated, viewModel.WorldContextMode);
    }

    [Fact]
    public void WebMercatorSelectionClampsOnlyTheFullLatitudeDefaults()
    {
        var viewModel = new SpatialConfigurationViewModel();

        viewModel.GridMapping = GridMappingKind.WebMercator;

        Assert.Equal(-WebMercatorGridMapping.WebMercatorLatitudeLimit, viewModel.SouthLatitude);
        Assert.Equal(WebMercatorGridMapping.WebMercatorLatitudeLimit, viewModel.NorthLatitude);

        viewModel.GridMapping = GridMappingKind.Equirectangular;

        Assert.Equal(-90, viewModel.SouthLatitude);
        Assert.Equal(90, viewModel.NorthLatitude);
    }

    [Fact]
    public void WebMercatorAppLayoutAcceptsTheSelectedRasterAspect()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            GridMapping = GridMappingKind.WebMercator
        };

        var spatial = viewModel.BuildSpatialOptions();

        Assert.False(spatial.PreserveProjectedCellAspectRatio);
    }

    [Fact]
    public void LoadingFullLatitudeMercatorSettingsUsesTheAppLimit()
    {
        var viewModel = new SpatialConfigurationViewModel();
        var persisted = new MapSpatialOptions
        {
            GridMapping = GridMappingKind.WebMercator,
            Coverage = MapCoverage.Global(-90, 90),
            PreserveProjectedCellAspectRatio = false
        };

        viewModel.LoadSpatialOptions(persisted);

        Assert.Equal(-WebMercatorGridMapping.WebMercatorLatitudeLimit, viewModel.SouthLatitude);
        Assert.Equal(WebMercatorGridMapping.WebMercatorLatitudeLimit, viewModel.NorthLatitude);
    }

    [Fact]
    public void WebMercatorManualLatitudeRangeIsNotOverwrittenWhenSwitchingBack()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            GridMapping = GridMappingKind.WebMercator,
            SouthLatitude = -80,
            NorthLatitude = 80
        };

        viewModel.GridMapping = GridMappingKind.Equirectangular;

        Assert.Equal(-80, viewModel.SouthLatitude);
        Assert.Equal(80, viewModel.NorthLatitude);
    }

    [Fact]
    public void InvalidMercatorLatitudesUseAnAppFacingValidationMessage()
    {
        var viewModel = new SpatialConfigurationViewModel
        {
            GridMapping = GridMappingKind.WebMercator,
            SouthLatitude = -90,
            NorthLatitude = 90
        };

        var error = Assert.Throws<ArgumentException>(() => viewModel.BuildSpatialOptions());

        Assert.Contains("Web Mercator", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WebMercatorGridMapping.WebMercatorLatitudeLimit.ToString("0.###############", System.Globalization.CultureInfo.CurrentCulture), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mathematical poles", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMercatorLatitudesUseTheSelectedAppLanguage()
    {
        var localization = new LocalizationService { Language = "ru-RU" };
        var viewModel = new SpatialConfigurationViewModel(localization)
        {
            GridMapping = GridMappingKind.WebMercator,
            SouthLatitude = -90,
            NorthLatitude = 90
        };

        var error = Assert.Throws<ArgumentException>(() => viewModel.BuildSpatialOptions());

        Assert.Contains("раскладки", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("математичес", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutputChoicesExposeOnlyRelevantPolicyControls()
    {
        var viewModel = new SpatialConfigurationViewModel();

        Assert.True(viewModel.IsGridMapUnitsOutput);
        Assert.False(viewModel.ShowOutputLatitudeOverflow);
        Assert.False(viewModel.ShowOutputAntimeridian);

        viewModel.IsWebMercatorOutput = true;
        Assert.True(viewModel.ShowOutputLatitudeOverflow);
        Assert.True(viewModel.ShowOutputAntimeridian);

        viewModel.IsGeographicOutput = true;
        Assert.False(viewModel.ShowOutputLatitudeOverflow);
        Assert.True(viewModel.ShowOutputAntimeridian);
    }

    [Fact]
    public void OutputSelectionDoesNotChangeGenerationOptions()
    {
        var viewModel = new SpatialConfigurationViewModel();
        var mask = FullMask(GridWindow.FromSize(3, 3));
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var options = new MapGenerationOptions { Seed = 4 };

        var first = viewModel.BuildRequest(mask, options, source).Options;
        viewModel.OutputCoordinates = OutputCoordinateSystem.WebMercator3857;
        var second = viewModel.BuildRequest(mask, options, source).Options;

        Assert.Equal(first.EffectiveSpatial, second.EffectiveSpatial);
        Assert.Equal(WorldContextMode.Isolated, viewModel.WorldContextMode);
    }

    private static MapMask FullMask(GridWindow window) => new(
        window,
        Enumerable.Range(0, window.Height)
            .SelectMany(y => Enumerable.Range(0, window.Width).Select(x => new GridPoint(x, y)))
            .ToHashSet());

    private sealed class RecordingMaskSource : IMapMaskSource
    {
        public GridWindow LastWindow { get; private set; }

        public MapMask GetMask(GridWindow window)
        {
            LastWindow = window;
            return FullMask(window);
        }
    }

}
