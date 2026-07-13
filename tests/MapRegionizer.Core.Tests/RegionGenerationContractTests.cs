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
    public void DisabledDistortionPreservesRawRegionGeometry()
    {
        var session = Generate(CreateMask(), CreateOptions(distortionEnabled: false));

        Assert.Equal(Signature(session.RawRegions), Signature(session.Regions));
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));
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

    private static MapGenerationOptions CreateOptions(bool distortionEnabled = true) => new()
    {
        Seed = 124_578,
        Regions = new RegionGenerationOptions
        {
            TargetArea = 150,
            PointsMultiplier = 2.5,
            MinAreaRatio = 0.6,
            MaxAreaRatio = 2
        },
        Boundaries = new BoundaryDistortionOptions
        {
            Enabled = distortionEnabled,
            Detail = 0.2,
            MaxOffset = 1.5,
            MinLineLengthToCurve = 5
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
