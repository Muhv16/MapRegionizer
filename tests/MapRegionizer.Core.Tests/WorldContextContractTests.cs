using System.Text.Json;
using MapRegionizer.App.Services;
using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.Core.Terrain;
using MapRegionizer.Runner;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class WorldContextContractTests
{
    [Fact]
    public void GlobalIsolatedKeepsCylindricalTopology()
    {
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var options = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX);

        var request = MapGenerationRequest.Isolated(new RequestedDomain(mask.Window), mask, options);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(WorldContextMode.Isolated, request.WorldContextMode);
        Assert.Equal(GridTopologyKind.CylindricalX, request.Options.EffectiveSpatial.Topology);
        Assert.IsType<CylindricalXTopology>(session.SpatialContext.GridTopology);
    }

    [Fact]
    public void GlobalAutomaticAcceptsAnExplicitWorldSource()
    {
        var requested = new RequestedDomain(0, 0, 4, 3);
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var options = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX);

        var request = MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(WorldContextMode.Automatic, request.WorldContextMode);
        Assert.Equal(WorldContextMode.Automatic, session.WorldContextMode);
        Assert.IsType<CylindricalXTopology>(session.SpatialContext.GridTopology);
    }

    [Fact]
    public void RegionalIsolatedNormalizesCylindricalTopologyToOpen()
    {
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var options = CanonicalOptions(
            MapCoverage.Regional(new LongitudeInterval(20, 30), -20, 20),
            GridTopologyKind.CylindricalX);

        var request = MapGenerationRequest.Isolated(new RequestedDomain(mask.Window), mask, options);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(WorldContextMode.Isolated, request.WorldContextMode);
        Assert.Equal(GridTopologyKind.OpenRectangular, request.Options.EffectiveSpatial.Topology);
        Assert.IsType<OpenRectangularTopology>(session.SpatialContext.GridTopology);
    }

    [Fact]
    public void ExplicitLegacySpatialProfilePreservesRegionalCylindricalTopology()
    {
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var options = new MapGenerationOptions
        {
            Seed = 31,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(20, 30), -20, 20),
                Topology = GridTopologyKind.CylindricalX,
                LegacyCompatibility = LegacyCompatibilityProfile.Regional
            }
        };

        var request = MapGenerationRequest.Isolated(new RequestedDomain(mask.Window), mask, options);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(GridTopologyKind.CylindricalX, request.Options.EffectiveSpatial.Topology);
        Assert.IsType<CylindricalXTopology>(session.SpatialContext.GridTopology);
    }

    [Fact]
    public void GlobalCustomPreservesCallerBoundaryContexts()
    {
        var requested = new RequestedDomain(0, 0, 4, 3);
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var climate = new FixedClimateBoundary();
        var hydrology = new HydrologyBoundaryContext(treatUnspecifiedBoundaryAsExternal: false);
        var options = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX);

        var request = MapGenerationRequest.Custom(
            requested,
            new WorkingDomain(requested.Window),
            source,
            climate,
            hydrology,
            options);
        var session = MapGenerationSession.Create(request);

        Assert.Equal(WorldContextMode.Custom, request.WorldContextMode);
        Assert.Same(climate, session.ClimateBoundary);
        Assert.Same(hydrology, session.HydrologyBoundary);
    }

    [Fact]
    public void ExplicitBoundaryContextsSurviveOptionUpdates()
    {
        var requested = new RequestedDomain(0, 0, 4, 3);
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var climate = new FixedClimateBoundary();
        var hydrology = new HydrologyBoundaryContext(treatUnspecifiedBoundaryAsExternal: false);
        var options = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX, seed: 41);
        var request = MapGenerationRequest.Custom(
            requested,
            new WorkingDomain(requested.Window),
            source,
            climate,
            hydrology,
            options);
        var session = MapGenerationSession.Create(request);

        session.UpdateOptions(new MapGenerationOptions
        {
            Seed = 42,
            WorldSeed = 42,
            Spatial = options.Spatial
        }, []);

        Assert.Same(climate, session.ClimateBoundary);
        Assert.Same(hydrology, session.HydrologyBoundary);
    }

    [Fact]
    public void LegacyFactoryUsesIsolatedCanonicalContextAndCompatibilityProfile()
    {
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var request = MapGenerationRequest.Legacy(mask, new MapGenerationOptions { Seed = 13 });

        Assert.Equal(WorldContextMode.Isolated, request.WorldContextMode);
        Assert.Equal(LegacyCompatibilityProfile.EquirectangularWorld, request.Options.EffectiveSpatial.LegacyCompatibility);
#pragma warning disable CS0618
        Assert.Equal(RegionalGenerationMode.Legacy, request.Mode);
        Assert.Equal(GenerationMode.Legacy, request.GenerationMode);
#pragma warning restore CS0618
    }

    [Fact]
    public void GeneratedMapUsesCanonicalStateAndKeepsLegacyConstructionShape()
    {
        var canonical = new GeneratedMap(
            new MapBounds(4, 3, 1),
            [],
            [],
            [],
            WorldContextMode.Automatic);
        var copy = canonical with { };

        Assert.Equal(WorldContextMode.Automatic, canonical.WorldContextMode);
        Assert.Equal(canonical, copy);

#pragma warning disable CS0618
        var legacy = new GeneratedMap(
            new MapBounds(4, 3, 1),
            [],
            [],
            [],
            GenerationMode: RegionalGenerationMode.Legacy);
        legacy.Deconstruct(
            out _, out _, out _, out _, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out var oldMode);
#pragma warning restore CS0618
#pragma warning disable CS0618
        Assert.Equal(RegionalGenerationMode.Legacy, oldMode);
#pragma warning restore CS0618
    }

#pragma warning disable CS0618
    [Fact]
    public void ObsoleteClimateContextFactoryKeepsPermissiveCustomFallback()
    {
        var context = ClimateWorldContext.Create(17, RegionalGenerationMode.Custom);

        Assert.IsType<AnalyticalClimateBoundaryContext>(context.Boundary);
    }
#pragma warning restore CS0618

#pragma warning disable CS0618
    [Theory]
    [InlineData(RegionalGenerationMode.Legacy, WorldContextMode.Isolated)]
    [InlineData(RegionalGenerationMode.Isolated, WorldContextMode.Isolated)]
    [InlineData(RegionalGenerationMode.Automatic, WorldContextMode.Automatic)]
    [InlineData(RegionalGenerationMode.Custom, WorldContextMode.Custom)]
    public void ObsoleteRegionalModeConstructorMapsToCanonicalContext(
        RegionalGenerationMode oldMode,
        WorldContextMode expectedContext)
    {
        var requested = new RequestedDomain(0, 0, 4, 3);
        var working = new WorkingDomain(requested.Window);
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var options = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX);
        var climate = new FixedClimateBoundary();
        var hydrology = new HydrologyBoundaryContext(treatUnspecifiedBoundaryAsExternal: false);
        var request = new MapGenerationRequest(
            requested,
            working,
            source,
            options,
            oldMode,
            climate,
            hydrology);

        Assert.Equal(expectedContext, request.WorldContextMode);
        Assert.Equal(LegacyCompatibilityProfile.None, request.Options.EffectiveSpatial.LegacyCompatibility);
        Assert.Equal(oldMode, request.Mode);
        Assert.Equal((GenerationMode)oldMode, request.GenerationMode);
    }
#pragma warning restore CS0618

    [Fact]
    public void ModernRunnerOptionsDoNotFallBackToLegacyForGlobalAutomaticContext()
    {
        var runOptions = new MapGenerationRunOptions
        {
            SpatialConfigurationEnabled = true,
            WorldContextMode = WorldContextMode.Automatic,
            CoverageKind = MapCoverageKind.Global
        };
        var requestOptions = runOptions.ToRequestOptions();
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var source = new DelegateMapMaskSource(window => FullMask(window));

        var request = requestOptions.ToGenerationRequest(mask, runOptions.ToGenerationOptions(), source);

        Assert.False(requestOptions.UsesLegacyCompatibility);
        Assert.Equal(WorldContextMode.Automatic, request.WorldContextMode);
    }

    [Fact]
    public void LegacyRunnerAliasStillSelectsTheLegacyAdapter()
    {
        var runOptions = new MapGenerationRunOptions { SpatialConfigurationEnabled = true };
#pragma warning disable CS0618
        runOptions.GenerationMode = RegionalGenerationMode.Legacy;
#pragma warning restore CS0618
        var mask = FullMask(GridWindow.FromSize(4, 3));

        var request = runOptions.ToRequestOptions().ToGenerationRequest(mask, runOptions.ToGenerationOptions());

        Assert.True(runOptions.UsesLegacyCompatibility);
        Assert.Equal(WorldContextMode.Isolated, request.WorldContextMode);
        Assert.Equal(LegacyCompatibilityProfile.EquirectangularWorld, request.Options.EffectiveSpatial.LegacyCompatibility);
    }

    [Fact]
    public void ModernRequestOptionsAcceptGlobalAutomaticContext()
    {
        var generationOptions = CanonicalOptions(MapCoverage.Global(), GridTopologyKind.CylindricalX, seed: 9);
        var requestOptions = new MapGenerationRequestOptions
        {
            GenerationOptions = generationOptions,
            SpatialConfigurationEnabled = true,
            WorldContextMode = WorldContextMode.Automatic
        };
        var mask = FullMask(GridWindow.FromSize(4, 3));
        var source = new DelegateMapMaskSource(window => FullMask(window));

        var request = requestOptions.ToGenerationRequest(mask, generationOptions, source);

        Assert.False(requestOptions.UsesLegacyCompatibility);
        Assert.Equal(WorldContextMode.Automatic, request.WorldContextMode);
    }

    [Fact]
    public void SettingsMigrationUsesCanonicalContextAndCanonicalSaveShape()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapRegionizerTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"WorldContextMode\":\"Automatic\",\"GenerationMode\":\"Legacy\"}");
            var service = new UserSettingsService(path);

            var loaded = service.Load();

            Assert.Equal(WorldContextMode.Automatic, loaded.WorldContextMode);
#pragma warning disable CS0618
            loaded.GenerationMode = RegionalGenerationMode.Isolated;
#pragma warning restore CS0618
            service.Save(loaded);
            var saved = File.ReadAllText(path);

            Assert.Contains("\"WorldContextMode\": \"Isolated\"", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("\"GenerationMode\"", saved, StringComparison.Ordinal);

            File.WriteAllText(path, "{\"GenerationMode\":\"Custom\"}");
            var custom = service.Load();
            Assert.Equal(WorldContextMode.Isolated, custom.WorldContextMode);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static MapGenerationOptions CanonicalOptions(MapCoverage coverage, GridTopologyKind topology, int? seed = null) => new()
    {
        Seed = seed,
        Spatial = new MapSpatialOptions
        {
            Coverage = coverage,
            Topology = topology,
            LegacyCompatibility = LegacyCompatibilityProfile.None
        }
    };

    private static MapMask FullMask(GridWindow window) => new(
        window,
        Enumerable.Range(0, window.Height)
            .SelectMany(y => Enumerable.Range(0, window.Width).Select(x => new GridPoint(x, y)))
            .ToHashSet());

    private sealed class FixedClimateBoundary : IClimateBoundaryContext
    {
        public double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees) => 0.25;
    }
}
