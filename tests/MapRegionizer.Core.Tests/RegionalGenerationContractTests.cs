using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.Core.Terrain;
using MapRegionizer.Core.Tectonics;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class RegionalGenerationContractTests
{
    [Fact]
    public void IsolatedRegionalModeUsesOpenTopologyAndDeclaredLatitudeRange()
    {
        const int width = 12;
        const int height = 8;
        var coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), 40, 55);
        var options = new MapGenerationOptions
        {
            Seed = 19,
            Spatial = new MapSpatialOptions { Coverage = coverage }
        };
        var mask = FullMask(new GridWindow(0, 0, width, height));
        var request = MapGenerationRequest.Isolated(new RequestedDomain(mask.Window), mask, options);
        var session = MapGenerationSession.Create(request);

        Assert.IsType<OpenRectangularTopology>(session.SpatialContext.GridTopology);
        Assert.False(session.SpatialContext.GridTopology.TryResolve(new GridPoint(0, 2), -1, 0, out _));
        Assert.False(session.SpatialContext.GridTopology.TryResolve(new GridPoint(width - 1, 2), 1, 0, out _));
        session.RunUntil(MapDataKeys.Climate);

        for (var y = 0; y < height; y++)
        {
            var latitude = session.SpatialContext.GridToGeographic(new GridCoordinate(.5, y + .5)).LatitudeDegrees;
            Assert.InRange(latitude, 40, 55);
            Assert.InRange(session.Climate!.GetLatitudeNorm(0, y), 40.0 / 90.0, 55.0 / 90.0);
        }
    }

    [Fact]
    public void AutomaticRegionalAntimeridianDefaultsToOpenTopology()
    {
        var requested = new RequestedDomain(0, 0, 10, 4);
        var options = new MapGenerationOptions
        {
            Seed = 23,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), 40, 55)
            }
        };
        var request = MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            new DelegateMapMaskSource(window => FullMask(window)),
            options);
        var session = MapGenerationSession.Create(request);

        Assert.IsType<OpenRectangularTopology>(session.SpatialContext.GridTopology);
        Assert.False(session.SpatialContext.GridTopology.TryResolve(new GridPoint(0, 1), -1, 0, out _));
        Assert.False(session.SpatialContext.GridTopology.TryResolve(new GridPoint(requested.Width - 1, 1), 1, 0, out _));
    }

    [Fact]
    public void AntimeridianRegionalCoverageKeepsDirectedExtentOpen()
    {
        var coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), 40, 55);
        var reference = new MapSpatialOptions
        {
            Coverage = coverage,
            Topology = GridTopologyKind.OpenRectangular
        };
        var context = MapSpatialContext.Create(10, 4, reference);

        Assert.Equal(170, context.GridToGeographic(new GridCoordinate(0, 0)).LongitudeDegrees, 10);
        Assert.Equal(190, context.GridToGeographic(new GridCoordinate(10, 0)).LongitudeDegrees, 10);
        Assert.Equal(10, context.GeographicToGrid(new GeoCoordinate(190, 47.5)).X, 10);
        Assert.Equal(0, context.GeographicToGrid(new GeoCoordinate(170, 47.5)).X, 10);
    }

    [Fact]
    public void WorkingDomainRunsOnHaloAndCropsEveryPublicRaster()
    {
        var requested = new RequestedDomain(4, 3, 6, 4);
        var working = WorkingDomain.ForRequested(requested, 2);
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var options = new MapGenerationOptions
        {
            Seed = 231,
            WorldSeed = 9001,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(20, 30), 20, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var map = new MapGenerator().Generate(MapGenerationRequest.Automatic(requested, working, source, options));

        Assert.Equal(requested.Width, map.Bounds.Width / map.Bounds.UnitsPerCell, 10);
        Assert.Equal(requested.Height, map.Bounds.Height / map.Bounds.UnitsPerCell, 10);
        Assert.Equal(requested.Width, map.SpatialReference!.GridWidth);
        Assert.Equal(requested.Height, map.SpatialReference.GridHeight);
        Assert.Equal(requested, map.RequestedDomain);
        Assert.Equal(working, map.WorkingDomain);
        Assert.Equal(requested.Width, map.Elevation!.Width);
        Assert.Equal(requested.Height, map.Elevation.Height);
        Assert.Equal(requested.Width, map.Climate!.Width);
        Assert.Equal(requested.Height, map.Hydrology!.Height);
        Assert.Equal(requested.Width, map.TectonicPlates!.Raster.Width);
        Assert.Equal(requested.Height, map.TectonicPlates.Raster.Height);
        Assert.All(map.Landmasses.SelectMany(landmass => landmass.Shape.Coordinates), point =>
        {
            Assert.InRange(point.X, 0, map.Bounds.Width);
            Assert.InRange(point.Y, 0, map.Bounds.Height);
        });
    }

    [Fact]
    public void AutomaticContextRequiresAWorkingMaskProvider()
    {
        var requested = new RequestedDomain(0, 0, 4, 4);
        var working = WorkingDomain.ForRequested(requested, 1);
        Assert.Throws<ArgumentException>(() => new MapGenerationRequest(
            requested,
            working,
            null,
            new MapGenerationOptions { Seed = 1 },
            WorldContextMode.Automatic));
    }

    [Fact]
    public void TectonicWorldIdentityIsIndependentOfRasterWindow()
    {
        var options = new TectonicPlateGenerationOptions { PlateCount = 11, HotspotCount = 5 };
        var first = TectonicWorldContext.Create(77, options);
        var second = TectonicWorldContext.Create(77, options);

        Assert.Equal(first.Plates, second.Plates);
        Assert.Equal(first.Hotspots, second.Hotspots);
        Assert.Equal(LineamentSignature(first.MacroLineaments), LineamentSignature(second.MacroLineaments));
        Assert.Equal(LineamentSignature(first.RiftSystems), LineamentSignature(second.RiftSystems));
        Assert.Equal(first.Plates.Select(plate => plate.Id), first.Plates.Select(plate => plate.Id).Distinct());

        var full = Enumerable.Range(0, 20).SelectMany(y => Enumerable.Range(0, 20).Select(x => first.SamplePlateId(x, y))).ToArray();
        var regional = Enumerable.Range(5, 8).SelectMany(y => Enumerable.Range(7, 8).Select(x => first.SamplePlateId(x, y))).ToArray();
        var expected = Enumerable.Range(5, 8).SelectMany(y => Enumerable.Range(7, 8).Select(x => full[y * 20 + x])).ToArray();
        Assert.Equal(expected, regional);
    }

    [Fact]
    public void AutomaticRegionalTectonicsMatchesTheSameWorldOnOverlap()
    {
        var options = new MapGenerationOptions
        {
            Seed = 73,
            WorldSeed = 7300,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Global(),
                Topology = GridTopologyKind.CylindricalX
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var fullDomain = new RequestedDomain(0, 0, 32, 16);
        var regionalDomain = new RequestedDomain(8, 4, 10, 6);
        var generator = new MapGenerator();
        var full = generator.Generate(MapGenerationRequest.Automatic(
            fullDomain,
            new WorkingDomain(fullDomain.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            IsolatedHydrologyBoundaryContext.Instance));
        var regional = generator.Generate(MapGenerationRequest.Automatic(
            regionalDomain,
            new WorkingDomain(regionalDomain.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            IsolatedHydrologyBoundaryContext.Instance));

        var fullRaster = full.TectonicPlates!.Raster;
        var regionalRaster = regional.TectonicPlates!.Raster;
        for (var y = 0; y < regionalDomain.Height; y++)
            for (var x = 0; x < regionalDomain.Width; x++)
                Assert.Equal(fullRaster.GetPlate(regionalDomain.X + x, regionalDomain.Y + y), regionalRaster.GetPlate(x, y));
    }

    [Fact]
    public void BoundaryContextsExposeDeterministicIncomingAndExternalState()
    {
        var climate = new AnalyticalClimateBoundaryContext(123);
        Assert.Equal(climate.GetIncomingMoisture(10, 20, 45), climate.GetIncomingMoisture(10, 20, 45), 12);
        Assert.InRange(climate.GetIncomingMoisture(10, 20, 45), 0, 1);

        var hydrology = new HydrologyBoundaryContext(
            new Dictionary<(int X, int Y), double> { [(0, 1)] = 7.5 },
            new Dictionary<(int X, int Y), HydrologyExternalTarget> { [(3, 1)] = new(DrainageTargetKind.Ocean) },
            new Dictionary<(int X, int Y), double> { [(3, 1)] = -120 },
            new HashSet<(int X, int Y)> { (3, 1) });
        Assert.Equal(7.5, hydrology.GetIncomingFlow(0, 1));
        Assert.Equal(DrainageTargetKind.Ocean, hydrology.GetExternalDownstreamTarget(3, 1)!.Kind);
        Assert.Equal(-120, hydrology.GetExternalElevation(3, 1));
        Assert.True(hydrology.GetExternalWaterInfluence(3, 1));
        Assert.Equal(0, IsolatedHydrologyBoundaryContext.Instance.GetIncomingFlow(0, 1));
    }

    [Fact]
    public void ClimateBoundaryChangesIncomingMoistureAtTheUpwindEdge()
    {
        var requested = new RequestedDomain(0, 0, 14, 8);
        var options = new MapGenerationOptions
        {
            Seed = 31,
            WorldSeed = 31,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var dry = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.0),
            IsolatedHydrologyBoundaryContext.Instance));
        var wet = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.9),
            IsolatedHydrologyBoundaryContext.Instance));

        Assert.NotEqual(dry.Climate!.AtmosphericMoistureSpan.ToArray(), wet.Climate!.AtmosphericMoistureSpan.ToArray());
        Assert.True(wet.Climate.AtmosphericMoistureSpan.ToArray().Max() > dry.Climate.AtmosphericMoistureSpan.ToArray().Max());
    }

    [Fact]
    public void HydrologyBoundaryIncomingFlowPropagatesToDownstreamCells()
    {
        var requested = new RequestedDomain(0, 0, 14, 8);
        var options = new MapGenerationOptions
        {
            Seed = 41,
            WorldSeed = 41,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var baseline = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            new HydrologyBoundaryContext()));
        var incoming = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            new HydrologyBoundaryContext(
                new Dictionary<(int X, int Y), double> { [(0, 3)] = 250.0 },
                treatUnspecifiedBoundaryAsExternal: false)));

        Assert.True(incoming.Hydrology!.FlowAccumulationSpan.ToArray().Sum() > baseline.Hydrology!.FlowAccumulationSpan.ToArray().Sum());
        Assert.Contains(incoming.Hydrology.FlowAccumulationSpan.ToArray(), value => value >= 250.0);
    }

    [Fact]
    public void ClimateBoundaryIsDeterministicAndReachesTheRequestedUpwindEdge()
    {
        var requested = new RequestedDomain(3, 1, 8, 5);
        var working = WorkingDomain.ForRequested(requested, 2);
        var options = new MapGenerationOptions
        {
            Seed = 43,
            WorldSeed = 43,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var boundary = new ConstantClimateBoundary(0.85);
        var first = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested, working, source, options, boundary, IsolatedHydrologyBoundaryContext.Instance));
        var second = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested, working, source, options, boundary, IsolatedHydrologyBoundaryContext.Instance));

        Assert.Equal(first.Climate!.AtmosphericMoistureSpan.ToArray(), second.Climate!.AtmosphericMoistureSpan.ToArray());
        Assert.True(first.Climate.GetAtmosphericMoisture(0, 2) > 0.0);
        Assert.True(first.Climate.GetAtmosphericMoisture(first.Climate.Width - 1, 2) > 0.0);
    }

    [Fact]
    public void ClimateBoundaryExternalWaterAndElevationAffectTheBoundarySolve()
    {
        var requested = new RequestedDomain(0, 0, 12, 6);
        var options = new MapGenerationOptions
        {
            Seed = 79,
            WorldSeed = 79,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var neutral = new ExternalClimateBoundary(0.2, false, 0);
        var external = new ExternalClimateBoundary(0.2, true, -750);
        var baseline = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested, new WorkingDomain(requested.Window), source, options, neutral,
            IsolatedHydrologyBoundaryContext.Instance));
        var influenced = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested, new WorkingDomain(requested.Window), source, options, external,
            IsolatedHydrologyBoundaryContext.Instance));

        Assert.NotEqual(
            baseline.Climate!.AtmosphericMoistureSpan.ToArray(),
            influenced.Climate!.AtmosphericMoistureSpan.ToArray());
    }

    [Fact]
    public void HydrologyBoundaryIncomingFlowChangesAnActualDownstreamCell()
    {
        var requested = new RequestedDomain(0, 0, 14, 8);
        var options = new MapGenerationOptions
        {
            Seed = 47,
            WorldSeed = 47,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var source = new DelegateMapMaskSource(window => FullMask(window));
        var baseline = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            new HydrologyBoundaryContext(treatUnspecifiedBoundaryAsExternal: false)));
        var baselineHydrology = baseline.Hydrology!;
        var topology = MapSpatialContext.Create(baseline.SpatialReference!);
        var edge = Enumerable.Range(0, requested.Height)
            .SelectMany(y => new[] { new GridPoint(0, y), new GridPoint(requested.Width - 1, y) })
            .Concat(Enumerable.Range(1, requested.Width - 2).SelectMany(x => new[] { new GridPoint(x, 0), new GridPoint(x, requested.Height - 1) }))
            .First(point => TryDownstream(point, baselineHydrology.GetFlowDirection(point), topology.GridTopology, requested.Width, out _));
        var incoming = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            source,
            options,
            new ConstantClimateBoundary(0.2),
            new HydrologyBoundaryContext(
                new Dictionary<(int X, int Y), double> { [(edge.X, edge.Y)] = 250.0 },
                treatUnspecifiedBoundaryAsExternal: false)));

        Assert.True(TryDownstream(edge, baselineHydrology.GetFlowDirection(edge), topology.GridTopology, requested.Width, out var downstream));
        Assert.True(incoming.Hydrology!.GetFlowAccumulation(downstream) > baselineHydrology.GetFlowAccumulation(downstream));
    }

    [Fact]
    public void LegacyMapMaskApiRetainsDeconstructionWithAndGenerationEntryPoint()
    {
        var original = FullMask(GridWindow.FromSize(8, 6));
        original.Deconstruct(out var width, out var height, out var points);
        var compatible = original with { Width = width, Height = height, LandPoints = points };

        var map = new MapGenerator().Generate(compatible, new MapGenerationOptions { Seed = 53 });

        Assert.Equal(original.Window, compatible.Window);
        Assert.Equal(original.LandPoints, points);
        Assert.Equal(width, map.SpatialReference!.GridWidth);
        Assert.Equal(height, map.SpatialReference.GridHeight);
        Assert.Equal(WorldContextMode.Isolated, map.WorldContextMode);
#pragma warning disable CS0618
        Assert.Equal(RegionalGenerationMode.Legacy, map.GenerationMode);
#pragma warning restore CS0618
    }

    [Fact]
    public void WorkingDomainCropPreservesRequestedSpatialAlignment()
    {
        var requested = new RequestedDomain(4, 3, 6, 4);
        var working = WorkingDomain.ForRequested(requested, 2);
        var coverage = MapCoverage.Regional(new LongitudeInterval(-35, 70), 22, 58);
        var options = new MapGenerationOptions
        {
            Seed = 59,
            WorldSeed = 59,
            Spatial = new MapSpatialOptions { Coverage = coverage, Topology = GridTopologyKind.OpenRectangular }
        };
        var map = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested, working, new DelegateMapMaskSource(window => FullMask(window)), options,
            new ConstantClimateBoundary(0.3), IsolatedHydrologyBoundaryContext.Instance));
        var reference = map.SpatialReference!;
        var spatial = MapSpatialContext.Create(reference);
        var northwest = spatial.GridToGeographic(new GridCoordinate(0, 0));
        var southeast = spatial.GridToGeographic(new GridCoordinate(requested.Width, requested.Height));

        Assert.Equal(requested, map.RequestedDomain);
        Assert.Equal(working, map.WorkingDomain);
        Assert.Equal(coverage, reference.Coverage);
        Assert.Equal(coverage.Longitude.StartLongitudeDegrees, northwest.LongitudeDegrees, 10);
        Assert.Equal(coverage.NorthLatitude, northwest.LatitudeDegrees, 10);
        Assert.Equal(coverage.Longitude.EndLongitudeDegrees, southeast.LongitudeDegrees, 10);
        Assert.Equal(coverage.SouthLatitude, southeast.LatitudeDegrees, 10);
        Assert.Equal(requested.Width * reference.UnitsPerCell, map.Bounds.Width, 10);
        Assert.Equal(requested.Height * reference.UnitsPerCell, map.Bounds.Height, 10);
    }

    [Fact]
    public void WorkingDomainCropRemovesExternalNestedMetadataAndTranslatesRiftCenters()
    {
        var working = new WorkingDomain(0, 0, 6, 6);
        var requested = new RequestedDomain(2, 2, 2, 2);
        var bodyIds = new int[working.Width * working.Height];
        var bodyKinds = new byte[bodyIds.Length];
        bodyIds[0] = 1;
        bodyKinds[0] = (byte)WaterBodyKind.InlandLake;
        foreach (var point in new[] { new GridPoint(2, 2), new GridPoint(3, 2), new GridPoint(3, 3) })
        {
            bodyIds[point.Y * working.Width + point.X] = 2;
            bodyKinds[point.Y * working.Width + point.X] = (byte)WaterBodyKind.InlandLake;
        }

        var topology = new WaterBodyTopology(
            working.Width,
            working.Height,
            bodyIds,
            bodyKinds,
            [
                new WaterBodyClassification(new WaterBodyId(1), WaterBodyKind.InlandLake, 1, false, 1 / 36.0),
                new WaterBodyClassification(new WaterBodyId(2), WaterBodyKind.InlandLake, 3, false, 3 / 36.0)
            ]);
        var workingSurfaces = new WaterSurfaceMap(
            working.Width,
            working.Height,
            Enumerable.Repeat(double.NaN, bodyIds.Length).ToArray(),
            [
                new WaterBodySurface(new WaterBodyId(1), WaterBodyKind.InlandLake, 10, 10, 1, 5, 1, 1, new GridPoint(0, 0)),
                new WaterBodySurface(new WaterBodyId(2), WaterBodyKind.InlandLake, 20, 20, 1, 5, 2, 3, new GridPoint(3, 2))
            ]);
        var zeros = new double[bodyIds.Length];
        var elevation = new ElevationMap(
            working.Width,
            working.Height,
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            new byte[bodyIds.Length],
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            waterSurfaces: workingSurfaces);
        var riftProvince = new RiftProvince(
            4,
            RiftProvinceKind.ContinentalRift,
            [new GridPoint(2, 2)],
            [
                new RiftProvinceSegment(new GridPoint(2, 2), new GridVector(1, 0), 2, 1, 1, false),
                new RiftProvinceSegment(new GridPoint(1, 2), new GridVector(1, 0), 2, 1, 1, false),
                new RiftProvinceSegment(new GridPoint(4, 4), new GridVector(1, 0), 2, 1, 1, false)
            ],
            1,
            1,
            1,
            1);
        var riftMap = new RiftProvinceMap(
            working.Width,
            working.Height,
            [riftProvince],
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray(),
            zeros.ToArray());
        var plates = Enumerable.Repeat((short)1, bodyIds.Length).ToArray();
        var tectonic = new TectonicPlateMap(
            working.Width,
            working.Height,
            [new TectonicPlate(new TectonicPlateId(1), TectonicPlateKind.Mixed, bodyIds.Length, new GridPoint(2, 2), new GridVector(0, 0), 1, 1, 1, null)],
            [],
            new TectonicPlateRaster(working.Width, working.Height, plates, new byte[bodyIds.Length]),
            RiftProvinces: riftMap);

        var request = MapGenerationRequest.Automatic(
            requested,
            working,
            new DelegateMapMaskSource(window => FullMask(window)),
            new MapGenerationOptions { Seed = 83 });
        var session = MapGenerationSession.Create(
            request,
            new MapGenerationPipeline([new NestedMetadataFixtureStage(topology, workingSurfaces, elevation, tectonic)]));
        session.RunFull();
        var map = session.CurrentMap;

        Assert.Same(map.WaterSurfaces, map.Elevation!.WaterSurfaces);
        Assert.Equal(requested.Width, map.WaterSurfaces!.Width);
        Assert.Equal(requested.Height, map.WaterSurfaces.Height);
        var surface = Assert.Single(map.WaterSurfaces.Bodies);
        Assert.Equal(new WaterBodyId(2), surface.Id);
        Assert.Equal(3, surface.CellCount);
        Assert.InRange(surface.Centroid!.Value.X, 0, requested.Width - 1);
        Assert.InRange(surface.Centroid.Value.Y, 0, requested.Height - 1);

        var croppedRift = Assert.Single(map.TectonicPlates!.RiftProvinces!.Provinces);
        var croppedSegment = Assert.Single(croppedRift.Segments);
        Assert.Equal(new GridPoint(0, 0), croppedSegment.Center);
    }

    [Fact]
    public void HaloBelowRequiredRadiusIsRejected()
    {
        var requested = new RequestedDomain(0, 0, 4, 4);
        var request = MapGenerationRequest.Automatic(
            requested,
            WorkingDomain.ForRequested(requested, 1),
            new DelegateMapMaskSource(window => FullMask(window)),
            new MapGenerationOptions { Seed = 61 });
        var pipeline = new MapGenerationPipeline([new FiniteBoundaryFixtureStage(2)]);

        Assert.Throws<ArgumentException>(() => request.ValidateFiniteHalo(pipeline));
    }

    [Fact]
    public void HaloAtRequiredRadiusIsAccepted()
    {
        var requested = new RequestedDomain(0, 0, 4, 4);
        var request = MapGenerationRequest.Automatic(
            requested,
            WorkingDomain.ForRequested(requested, 2),
            new DelegateMapMaskSource(window => FullMask(window)),
            new MapGenerationOptions { Seed = 67 });
        var pipeline = new MapGenerationPipeline([new FiniteBoundaryFixtureStage(2)]);

        request.ValidateFiniteHalo(pipeline);
        Assert.Equal(2, StageBoundaryContracts.RequiredFiniteHalo(pipeline.Stages));
    }

    [Fact]
    public void AsymmetricWorkingDomainReportsTheSmallestUsableHalo()
    {
        var requested = new RequestedDomain(2, 2, 4, 4);
        var working = new WorkingDomain(0, 1, 8, 7);

        Assert.Equal(1, working.HaloCells(requested));
    }

    [Fact]
    public void RegionalHydrologyRiverCanEnterAndLeaveRequestedDomain()
    {
        var working = new WorkingDomain(0, 0, 6, 3);
        var requested = new RequestedDomain(2, 0, 2, 3);
        var river = new RiverSegment(
            7,
            [new GridPoint(0, 1), new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1), new GridPoint(4, 1), new GridPoint(5, 1)],
            [new MapPoint(.5, 1.5), new MapPoint(1.5, 1.5), new MapPoint(2.5, 1.5), new MapPoint(3.5, 1.5), new MapPoint(4.5, 1.5), new MapPoint(5.5, 1.5)],
            new GridPoint(0, 1),
            new GridPoint(5, 1),
            new GridPoint(5, 1),
            null,
            DrainageTargetKind.Ocean,
            null,
            10,
            5,
            0.1,
            RiverKind.Plain);
        var stage = new HydrologyFixtureStage(working.Width, working.Height, river);
        var request = MapGenerationRequest.Automatic(
            requested,
            working,
            new DelegateMapMaskSource(window => FullMask(window)),
            new MapGenerationOptions { Seed = 71, Spatial = new MapSpatialOptions { Topology = GridTopologyKind.OpenRectangular } });
        var session = MapGenerationSession.Create(request, new MapGenerationPipeline([stage]));
        session.RunFull();

        var cropped = session.CurrentMap.Hydrology!;
        var croppedRiver = Assert.Single(cropped.Rivers);
        Assert.Equal(new[] { new GridPoint(0, 1), new GridPoint(1, 1) }, croppedRiver.Cells);
        Assert.Equal(0.0, croppedRiver.Polyline[0].X, 10);
        Assert.Equal(2.0, croppedRiver.Polyline[^1].X, 10);
        Assert.DoesNotContain(cropped.Mouths, mouth => mouth.Cell.X == 1);
    }

    [Fact]
    public void ExternalDownstreamTargetDoesNotBecomeAnInteriorMouth()
    {
        var requested = new RequestedDomain(0, 0, 12, 8);
        var options = new MapGenerationOptions
        {
            Seed = 51,
            WorldSeed = 51,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 35), 25, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var targets = Enumerable.Range(0, requested.Height)
            .ToDictionary(y => (requested.Width - 1, y), _ => new HydrologyExternalTarget(DrainageTargetKind.Ocean));
        var map = new MapGenerator().Generate(MapGenerationRequest.Automatic(
            requested,
            new WorkingDomain(requested.Window),
            new DelegateMapMaskSource(window => FullMask(window)),
            options,
            new ConstantClimateBoundary(0.2),
            new HydrologyBoundaryContext(downstreamTargets: targets, treatUnspecifiedBoundaryAsExternal: false)));

        Assert.DoesNotContain(map.Hydrology!.Mouths, mouth => mouth.Cell.X == requested.Width - 1);
    }

    [Fact]
    public void SpatialStagesDeclareBoundaryClassification()
    {
        var pipeline = MapGenerationPipelineBuilder.CreateDefault().Build();
        var climate = pipeline.Stages.Single(stage => stage.Id == MapStageIds.GenerateClimate);
        var hydrology = pipeline.Stages.Single(stage => stage.Id == MapStageIds.GenerateHydrology);
        var tectonics = pipeline.Stages.Single(stage => stage.Id == MapStageIds.GenerateTectonicWorldContext);

        Assert.Equal(SpatialBoundaryDependency.Propagating, StageBoundaryContracts.Get(climate).Dependency);
        Assert.Equal(SpatialBoundaryDependency.Propagating, StageBoundaryContracts.Get(hydrology).Dependency);
        Assert.Equal(SpatialBoundaryDependency.GlobalContextDependent, StageBoundaryContracts.Get(tectonics).Dependency);
        Assert.Equal(0, StageBoundaryContracts.RequiredFiniteHalo(pipeline.Stages));
    }

    private static MapMask FullMask(GridWindow window)
    {
        var points = Enumerable.Range(0, window.Height)
            .SelectMany(y => Enumerable.Range(0, window.Width).Select(x => new GridPoint(x, y)))
            .ToHashSet();
        return new MapMask(window, points);
    }

    private static IReadOnlyList<string> LineamentSignature(IEnumerable<TectonicWorldLineament> lineaments) =>
        lineaments.Select(lineament => $"{lineament.Id}:{lineament.Kind}:{string.Join(';', lineament.WorldPoints.Select(point => $"{point.X},{point.Y}"))}").ToArray();

    private static bool TryDownstream(GridPoint point, int direction, IGridTopology topology, int width, out GridPoint downstream)
    {
        downstream = default;
        if (direction < 0 || direction >= 8)
            return false;
        var moves = new[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };
        return topology.TryResolve(point, moves[direction].Item1, moves[direction].Item2, out downstream);
    }

    private sealed class ConstantClimateBoundary(double moisture) : IClimateBoundaryContext
    {
        public double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees) => moisture;
    }

    private sealed class ExternalClimateBoundary(double moisture, bool water, double elevation) : IClimateBoundaryContext
    {
        public double GetIncomingMoisture(int worldX, int worldY, double latitudeDegrees) => moisture;
        public double GetExternalElevation(int worldX, int worldY) => elevation;
        public bool GetExternalWaterInfluence(int worldX, int worldY) => water;
    }

    private sealed class FiniteBoundaryFixtureStage(int radius) : IMapGenerationStage, ISpatialBoundaryAwareStage
    {
        public string Id => "finiteBoundaryFixture";
        public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>();
        public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { new("finiteBoundaryFixtureOutput") };
        public StageBoundaryMetadata BoundaryMetadata => StageBoundaryMetadata.Finite(radius);
        public void Execute(MapGenerationContext context)
        {
        }
    }

    private sealed class HydrologyFixtureStage(int width, int height, RiverSegment river) : IMapGenerationStage
    {
        public string Id => "hydrologyFixture";
        public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>();
        public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Hydrology };

        public void Execute(MapGenerationContext context)
        {
            var length = width * height;
            context.Hydrology = new HydrologyMap(
                width,
                height,
                new double[length],
                Enumerable.Repeat(-1, length).ToArray(),
                new double[length],
                new int[length],
                new byte[length],
                [river],
                [],
                [],
                []);
        }
    }

    private sealed class NestedMetadataFixtureStage(
        WaterBodyTopology topology,
        WaterSurfaceMap waterSurfaces,
        ElevationMap elevation,
        TectonicPlateMap tectonic) : IMapGenerationStage
    {
        public string Id => "nestedMetadataFixture";
        public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>();
        public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey>
        {
            MapDataKeys.WaterBodyTopology,
            MapDataKeys.WaterSurfaces,
            MapDataKeys.Elevation,
            MapDataKeys.TectonicPlates
        };

        public void Execute(MapGenerationContext context)
        {
            context.WaterBodyTopology = topology;
            context.WaterSurfaces = waterSurfaces;
            context.Elevation = elevation;
            context.TectonicPlates = tectonic;
        }
    }
}
