using System.Globalization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class RegionGenerationContractTests
{
    [Fact]
    public void RawAndDistortedRegionsSatisfyGeometryContractForLandmassWithHoleAndIsland()
    {
        var session = MapGenerationSession.Create(CreateMask(), CreateOptions());

        session.RunUntil(MapDataKeys.RawRegions);
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.RawRegions));

        session.RunUntil(MapDataKeys.Regions);
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));

        Assert.Contains(session.Landmasses, landmass => landmass.Shape.NumInteriorRings == 1);
        Assert.Equal(session.RawRegions.Select(region => region.Id), session.Regions.Select(region => region.Id));
    }

    [Fact]
    public void FixedSeedProducesStableRegionIdentifiersAndGeometry()
    {
        var first = Generate(CreateMask(), CreateOptions());
        var second = Generate(CreateMask(), CreateOptions());

        Assert.Equal(Signature(first.RawRegions), Signature(second.RawRegions));
        Assert.Equal(Signature(first.Regions), Signature(second.Regions));
    }

    [Fact]
    public void FixedSeedProducesTheSameRegionsForFullAndRegionOnlyPipelinePaths()
    {
        var direct = Generate(CreateMask(), CreateOptions());
        var full = MapGenerationSession.Create(CreateMask(), CreateOptions());
        full.RunFull();

        Assert.Equal(Signature(direct.RawRegions), Signature(full.RawRegions));
        Assert.Equal(Signature(direct.Regions), Signature(full.Regions));
    }

    [Fact]
    public void ChangingRegionOptionsAtTheDraftRootRegeneratesRegions()
    {
        var session = MapGenerationSession.Create(CreateMask(), CreateOptions(targetArea: 150));
        session.RunUntil(MapDataKeys.RawRegions);
        var original = Signature(session.RawRegions);

        session.UpdateOptions(CreateOptions(targetArea: 900), [MapDataKeys.RegionDraft]);

        Assert.True(session.IsDirty(MapDataKeys.RegionDraft));
        Assert.True(session.IsDirty(MapDataKeys.RawRegions));

        session.RunUntil(MapDataKeys.RawRegions);

        Assert.NotEqual(original, Signature(session.RawRegions));
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.RawRegions));
    }

    [Fact]
    public void ChangingSeedRegeneratesRegionsLikeANewSession()
    {
        var mask = CreateMask();
        var session = MapGenerationSession.Create(mask, CreateOptions(seed: 124_578));
        session.RunUntil(MapDataKeys.RawRegions);
        var original = Signature(session.RawRegions);

        session.UpdateOptions(CreateOptions(seed: 876_543), [MapDataKeys.RegionDraft]);
        session.RunUntil(MapDataKeys.RawRegions);

        var expected = Generate(mask, CreateOptions(seed: 876_543));
        Assert.NotEqual(original, Signature(session.RawRegions));
        Assert.Equal(Signature(expected.RawRegions), Signature(session.RawRegions));
    }

    [Fact]
    public void DisabledDistortionPreservesRawRegionGeometry()
    {
        var session = Generate(CreateMask(), CreateOptions(distortionEnabled: false));

        Assert.Equal(Signature(session.RawRegions), Signature(session.Regions));
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));
    }

    [Fact]
    public void ZeroMaxOffsetPreservesRawRegionGeometry()
    {
        var session = Generate(CreateMask(), CreateOptions(maxOffset: 0));

        Assert.Equal(Signature(session.RawRegions), Signature(session.Regions));
        Assert.DoesNotContain(session.RegionDiagnostics, diagnostic => diagnostic.Code is "distortion-reverted" or "distortion-reduced");
    }

    [Fact]
    public void MaxOffsetChangesGeometryButNeverExceedsTheConfiguredDistance()
    {
        const double maxOffset = .75;
        var session = Generate(CreateMask(), CreateOptions(maxOffset: maxOffset));
        var rawBoundaries = session.RawRegions.Select(region => region.Shape.Boundary).ToArray();

        Assert.NotEqual(Signature(session.RawRegions), Signature(session.Regions));
        foreach (var coordinate in session.Regions.SelectMany(region => region.Shape.Coordinates))
        {
            var point = session.Regions[0].Shape.Factory.CreatePoint(coordinate);
            var distanceToRawBoundary = rawBoundaries.Min(boundary => boundary.Distance(point));
            Assert.InRange(distanceToRawBoundary, 0, maxOffset + 1e-6);
        }
    }

    [Fact]
    public void ExcessiveDistortionIsReducedBeforeItIsReverted()
    {
        var session = Generate(CreateMask(), CreateOptions(boundaryDetail: 1, maxOffset: 20, minLineLengthToCurve: 0));

        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));
        Assert.NotEqual(Signature(session.RawRegions), Signature(session.Regions));
        Assert.Contains(session.RegionDiagnostics, diagnostic => diagnostic.Code == "distortion-reduced");
        Assert.DoesNotContain(session.RegionDiagnostics, diagnostic => diagnostic.Code == "distortion-reverted");
    }

    [Fact]
    public void RegeneratingRegionsReplacesPreviousDistortionDiagnostics()
    {
        var session = Generate(CreateMask(), CreateOptions(boundaryDetail: 1, maxOffset: 100_000, minLineLengthToCurve: 0));
        Assert.Contains(session.RegionDiagnostics, diagnostic => diagnostic.Code == "distortion-reverted");

        session.UpdateOptions(CreateOptions(maxOffset: 0), [MapDataKeys.Regions]);
        session.RunUntil(MapDataKeys.Regions);

        Assert.Equal(Signature(session.RawRegions), Signature(session.Regions));
        Assert.DoesNotContain(session.RegionDiagnostics, diagnostic => diagnostic.Code is "distortion-reverted" or "distortion-reduced");
    }

    [Fact]
    public void AdjacencyRequiresSharedBoundaryWithNonZeroLength()
    {
        var factory = new GeometryFactory();
        var first = Square(factory, 0, 0);
        var edgeNeighbor = Square(factory, 1, 0);
        var vertexNeighbor = Square(factory, 1, 1);

        Assert.True(RegionGeometryContract.ShareBoundary(first, edgeNeighbor));
        Assert.False(RegionGeometryContract.ShareBoundary(first, vertexNeighbor));
    }

    [Fact]
    public void ManualDraftIsCanonicalizedAndInvalidatesOnlyTheRegionBranch()
    {
        var pipeline = MapGenerationPipelineBuilder.CreateDefault().AddRegionRasterization().Build();
        var session = MapGenerationSession.Create(CreateMask(), CreateOptions(distortionEnabled: false), pipeline);
        session.RunUntil(MapDataKeys.RegionRaster);
        var originalDraft = RegionDraft.FromRegions(session.RawRegions);

        session.SetRegionDraft(originalDraft);

        Assert.True(session.IsDirty(MapDataKeys.RawRegions));
        Assert.True(session.IsDirty(MapDataKeys.Regions));
        Assert.True(session.IsDirty(MapDataKeys.RegionRaster));
        Assert.False(session.IsDirty(MapDataKeys.Climate));

        session.RunUntil(MapDataKeys.Regions);
        Assert.Equal(originalDraft.Regions.Select(region => region.Id), session.RawRegions.Select(region => (RegionId?)region.Id));
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.RawRegions));
        Assert.DoesNotContain(session.RegionDiagnostics, diagnostic => diagnostic.Severity == RegionDiagnosticSeverity.Error);
    }

    [Fact]
    public void ClearingManualDraftRestoresDeterministicAutomaticRegions()
    {
        var session = MapGenerationSession.Create(CreateMask(), CreateOptions(distortionEnabled: false));
        session.RunUntil(MapDataKeys.RawRegions);
        var automatic = Signature(session.RawRegions);

        session.SetRegionDraft(RegionDraft.FromRegions(session.RawRegions));
        Assert.True(session.UsesExternalRegionDraft);

        session.SetRegionDraft(null);
        session.RunUntil(MapDataKeys.RawRegions);

        Assert.False(session.UsesExternalRegionDraft);
        Assert.Equal(automatic, Signature(session.RawRegions));
    }

    [Fact]
    public void CanonicalizerAssignsStableIdsAndRejectsSubstantialGaps()
    {
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 1));
        var validDraft = new RegionDraft([
            new RegionDraftRegion(null, landmass.Id, Square(factory, 0, 0, 1, 1)),
            new RegionDraftRegion(null, landmass.Id, Square(factory, 1, 0, 1, 1))
        ]);

        var canonicalizer = new RegionCoverageCanonicalizer();
        var valid = canonicalizer.Canonicalize([landmass], validDraft);
        Assert.True(valid.IsSuccessful);
        Assert.Equal([1, 2], valid.Regions.Select(region => region.Id.Value));

        var gapDraft = validDraft with { Regions = [validDraft.Regions[0]] };
        var invalid = canonicalizer.Canonicalize([landmass], gapDraft);
        Assert.False(invalid.IsSuccessful);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "coverage-topology" && diagnostic.Message.Contains("uncovered area"));
    }

    [Fact]
    public void CanonicalizerNodesASharedBoundaryWithDifferentVertexCounts()
    {
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 1));
        var draft = new RegionDraft([
            new RegionDraftRegion(new RegionId(1), landmass.Id, Square(factory, 0, 0, 1, 1)),
            new RegionDraftRegion(new RegionId(2), landmass.Id, factory.CreatePolygon([
                new Coordinate(1, 0), new Coordinate(2, 0), new Coordinate(2, 1),
                new Coordinate(1, 1), new Coordinate(1, .5), new Coordinate(1, 0)
            ]))
        ]);

        var result = new RegionCoverageCanonicalizer().Canonicalize([landmass], draft);

        Assert.True(result.IsSuccessful);
        Assert.Empty(RegionGeometryContract.Validate([landmass], result.Regions));
        Assert.Contains(result.Regions.Single(region => region.Id.Value == 1).Shape.Coordinates,
            coordinate => RegionGeometryPrecision.IsEquivalent(coordinate, new Coordinate(1, .5)));
    }

    [Fact]
    public void PortableDraftGeoJsonRoundTripsAndRejectsAnIncompatibleMask()
    {
        var factory = new GeometryFactory();
        var mask = new MapMask(2, 1, new HashSet<GridPoint> { new(0, 0), new(1, 0) });
        var options = new MapGenerationOptions { ProjectionMode = MapProjectionMode.Flat };
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 1));
        var draft = new RegionDraft([
            new RegionDraftRegion(new RegionId(7), landmass.Id, Square(factory, 0, 0, 1, 1), RegionDraftOrigin.Manual, "West", new Dictionary<string, string> { ["culture"] = "A" }),
            new RegionDraftRegion(new RegionId(8), landmass.Id, Square(factory, 1, 0, 1, 1), RegionDraftOrigin.GeneratedAndEdited)
        ]);
        var document = RegionDraftCompatibility.CreateDocument(mask, options, [landmass], draft, applyBoundaryDistortion: false);

        var reloaded = RegionDraftGeoJson.Read(RegionDraftGeoJson.Write(document));

        Assert.Equal(RegionDraftDocument.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.False(reloaded.ApplyBoundaryDistortion);
        Assert.Equal("West", reloaded.Draft.Regions[0].Name);
        Assert.Equal("A", reloaded.Draft.Regions[0].Metadata!["culture"]);
        Assert.Equal(RegionDraftOrigin.GeneratedAndEdited, reloaded.Draft.Regions[1].Origin);
        RegionDraftCompatibility.EnsureCompatible(reloaded, mask, options, [landmass]);

        var incompatibleMask = mask with { LandPoints = new HashSet<GridPoint> { new(0, 0) } };
        Assert.Throws<InvalidOperationException>(() => RegionDraftCompatibility.EnsureCompatible(reloaded, incompatibleMask, options, [landmass]));
    }

    [Fact]
    public void DraftEditorSplitAndMergePreserveDeterministicIdsAndCoverage()
    {
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 2));
        var draft = new RegionDraft([new RegionDraftRegion(new RegionId(10), landmass.Id, landmass.Shape)]);
        var cut = factory.CreateLineString([new Coordinate(1, 0), new Coordinate(1, 2)]);

        Assert.True(RegionDraftEditor.TrySplit(draft, new RegionId(10), cut, out var split, out _));
        var canonical = new RegionCoverageCanonicalizer().Canonicalize([landmass], split!);
        Assert.True(canonical.IsSuccessful);
        Assert.Equal([10, 11], canonical.Regions.Select(region => region.Id.Value).Order());

        Assert.True(RegionDraftEditor.TryMerge(split!, new RegionId(10), new RegionId(11), out var merged, out _));
        var mergedCanonical = new RegionCoverageCanonicalizer().Canonicalize([landmass], merged!);
        Assert.True(mergedCanonical.IsSuccessful);
        Assert.Single(mergedCanonical.Regions);
    }

    [Fact]
    public void DraftEditorSplitUsesBoundaryIntersectionsForArbitraryUserPoints()
    {
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 2));
        var draft = new RegionDraft([new RegionDraftRegion(new RegionId(10), landmass.Id, landmass.Shape)]);
        var cut = factory.CreateLineString([new Coordinate(1.05, .04), new Coordinate(.96, 1.97)]);

        Assert.True(RegionDraftEditor.TrySplit(draft, new RegionId(10), cut, .1, out var split, out _));
        Assert.True(new RegionCoverageCanonicalizer().Canonicalize([landmass], split!).IsSuccessful);
    }

    [Fact]
    public void TopologyMovesAnInteriorVertexForEveryIncidentFaceAndProtectsCoast()
    {
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), Square(factory, 0, 0, 2, 2));
        var regions = new[]
        {
            new MapRegion(new RegionId(1), landmass.Id, Square(factory, 0, 0, 1, 1)),
            new MapRegion(new RegionId(2), landmass.Id, Square(factory, 1, 0, 1, 1)),
            new MapRegion(new RegionId(3), landmass.Id, Square(factory, 0, 1, 1, 1)),
            new MapRegion(new RegionId(4), landmass.Id, Square(factory, 1, 1, 1, 1))
        };
        var topology = RegionTopology.Create([landmass], regions);
        var interior = Assert.Single(topology.Vertices, vertex => !vertex.IsCoastal);
        var coast = topology.Vertices.First(vertex => vertex.IsCoastal);

        Assert.True(topology.TryMoveVertex(interior.Id, new MapPoint(1.1, 1.1), out var movedDraft, out _));
        Assert.NotNull(movedDraft);
        Assert.True(new RegionCoverageCanonicalizer().Canonicalize([landmass], movedDraft!).IsSuccessful);
        Assert.False(topology.TryMoveVertex(coast.Id, new MapPoint(0.2, 0.2), out _, out var diagnostic));
        Assert.Equal("protected-coast", diagnostic!.Code);
    }

    private static MapGenerationSession Generate(MapMask mask, MapGenerationOptions options)
    {
        var session = MapGenerationSession.Create(mask, options);
        session.RunUntil(MapDataKeys.Regions);
        return session;
    }

    private static MapGenerationOptions CreateOptions(
        bool distortionEnabled = true,
        int seed = 124_578,
        uint targetArea = 150,
        double boundaryDetail = .2,
        double maxOffset = 1.5,
        double minLineLengthToCurve = 5) => new()
        {
            Seed = seed,
            Regions = new RegionGenerationOptions
            {
                TargetArea = targetArea,
                PointsMultiplier = 2.5,
                MinAreaRatio = 0.6,
                MaxAreaRatio = 2
            },
            Boundaries = new BoundaryDistortionOptions
            {
                Enabled = distortionEnabled,
                Detail = boundaryDetail,
                MaxOffset = maxOffset,
                MinLineLengthToCurve = minLineLengthToCurve
            }
        };

    private static MapMask CreateMask()
    {
        var land = new HashSet<GridPoint>();

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 50; x++)
            {
                if (x is < 17 or > 32 || y is < 12 or > 27)
                    land.Add(new GridPoint(x, y));
            }
        }

        for (var y = 4; y < 10; y++)
        {
            for (var x = 58; x < 64; x++)
                land.Add(new GridPoint(x, y));
        }

        return new MapMask(70, 50, land);
    }

    private static Polygon Square(GeometryFactory factory, double x, double y) => Square(factory, x, y, 1, 1);

    private static Polygon Square(GeometryFactory factory, double x, double y, double width, double height)
    {
        return factory.CreatePolygon([
            new Coordinate(x, y),
            new Coordinate(x + width, y),
            new Coordinate(x + width, y + height),
            new Coordinate(x, y + height),
            new Coordinate(x, y)
        ]);
    }

    private static IReadOnlyList<string> Signature(IEnumerable<MapRegion> regions)
    {
        return regions.Select(region => string.Join(
            '|',
            region.Id.Value.ToString(CultureInfo.InvariantCulture),
            region.LandmassId.Value.ToString(CultureInfo.InvariantCulture),
            string.Join(';', region.Shape.Coordinates.Select(coordinate => $"{coordinate.X:R},{coordinate.Y:R}")))).ToList();
    }
}
