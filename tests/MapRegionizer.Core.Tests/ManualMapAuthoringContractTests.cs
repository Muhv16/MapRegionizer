using System.Globalization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualMapAuthoringContractTests
{
    private readonly GeometryFactory _factory = new();
    private readonly ManualMapDraftFinalizer _finalizer;

    public ManualMapAuthoringContractTests()
    {
        _finalizer = new ManualMapDraftFinalizer(_factory);
    }

    [Fact]
    public void OneRegionProducesOneLandmassAndCenterSampledMask()
    {
        var result = Finalize(SquareDraft(10, 10, 1, 1, 5, 5));

        Assert.True(result.IsSuccessful, Diagnostics(result));
        Assert.Single(result.RegionDraft.Regions);
        Assert.Single(result.Landmasses);
        Assert.Equal(25, result.DerivedMask.LandPoints.Count);
        Assert.Equal(1, result.DerivedMask.LandPoints.Count(point => point.X == 1 && point.Y == 1));
    }

    [Fact]
    public void SharedEdgeRegionsRemainOneLandmassAndPassCanonicalizer()
    {
        var draft = new ManualMapDraft(
            12,
            10,
            [
                Vertex(1, 1, 1), Vertex(2, 5, 1), Vertex(3, 5, 5), Vertex(4, 1, 5),
                Vertex(5, 9, 1), Vertex(6, 9, 5)
            ],
            [
                new ManualRegionFace(1, [1, 2, 3, 4], "West"),
                new ManualRegionFace(2, [2, 5, 6, 3], "East")
            ]);

        var result = Finalize(draft);

        Assert.True(result.IsSuccessful, Diagnostics(result));
        Assert.Single(result.Landmasses);
        Assert.Equal(2, result.RegionDraft.Regions.Count);
        var canonical = new RegionCoverageCanonicalizer().Canonicalize(result.Landmasses, result.RegionDraft);
        Assert.True(canonical.IsSuccessful, string.Join(Environment.NewLine, canonical.Diagnostics.Select(d => d.Message)));
        Assert.Empty(RegionGeometryContract.Validate(result.Landmasses, canonical.Regions));
        Assert.True(RegionGeometryContract.ShareBoundary(canonical.Regions[0].Shape, canonical.Regions[1].Shape));
    }

    [Fact]
    public void SeparateIslandsProduceSeparateLandmasses()
    {
        var draft = new ManualMapDraft(
            20,
            12,
            [
                Vertex(1, 1, 1), Vertex(2, 4, 1), Vertex(3, 4, 4), Vertex(4, 1, 4),
                Vertex(5, 12, 7), Vertex(6, 16, 7), Vertex(7, 16, 10), Vertex(8, 12, 10)
            ],
            [
                new ManualRegionFace(1, [1, 2, 3, 4]),
                new ManualRegionFace(2, [5, 6, 7, 8])
            ]);

        var result = Finalize(draft);

        Assert.True(result.IsSuccessful, Diagnostics(result));
        Assert.Equal(2, result.Landmasses.Count);
        Assert.Equal([1, 2], result.RegionDraft.Regions.Select(region => region.LandmassId!.Value.Value).Order().ToArray());
    }

    [Fact]
    public void PointOnlyTouchDoesNotJoinLandmassesOrCountAsAdjacency()
    {
        var draft = new ManualMapDraft(
            12,
            12,
            [
                Vertex(1, 1, 1), Vertex(2, 4, 1), Vertex(3, 4, 4), Vertex(4, 1, 4),
                Vertex(5, 7, 4), Vertex(6, 10, 4), Vertex(7, 10, 7), Vertex(8, 7, 7)
            ],
            [
                new ManualRegionFace(1, [1, 2, 3, 4]),
                new ManualRegionFace(2, [3, 5, 6, 7, 8])
            ]);

        var result = Finalize(draft);

        Assert.True(result.IsSuccessful, Diagnostics(result));
        Assert.Equal(2, result.Landmasses.Count);
        var canonical = new RegionCoverageCanonicalizer().Canonicalize(result.Landmasses, result.RegionDraft);
        Assert.False(RegionGeometryContract.ShareBoundary(canonical.Regions[0].Shape, canonical.Regions[1].Shape));
    }

    [Fact]
    public void RingOfRegionsPreservesHoleAndRasterizesItAsWater()
    {
        var draft = new ManualMapDraft(
            12,
            12,
            [
                Vertex(1, 1, 1), Vertex(2, 9, 1), Vertex(3, 9, 4), Vertex(4, 1, 4),
                Vertex(5, 1, 6), Vertex(6, 9, 6), Vertex(7, 9, 9), Vertex(8, 1, 9),
                Vertex(9, 4, 4), Vertex(10, 6, 4), Vertex(11, 6, 6), Vertex(12, 4, 6)
            ],
            [
                new ManualRegionFace(1, [1, 2, 3, 4]),
                new ManualRegionFace(2, [10, 3, 6, 11]),
                new ManualRegionFace(3, [5, 6, 7, 8]),
                new ManualRegionFace(4, [4, 9, 12, 5])
            ]);

        var result = Finalize(draft);

        Assert.True(result.IsSuccessful, Diagnostics(result));
        var landmass = Assert.Single(result.Landmasses);
        Assert.Equal(1, landmass.Shape.NumInteriorRings);
        Assert.False(result.DerivedMask.IsLand(new GridPoint(4, 4)));
        Assert.True(result.DerivedMask.IsLand(new GridPoint(2, 2)));

        var spatial = CreateSpatialReference(12, 12);
        var options = new MapGenerationOptions
        {
            Seed = 42,
            Spatial = new MapSpatialOptions
            {
                WorldModel = WorldModelDescriptor.Planar(),
                Coverage = spatial.Coverage,
                GridMapping = spatial.GridMapping,
                Topology = spatial.Topology,
                UnitsPerCell = spatial.UnitsPerCell,
                LegacyCompatibility = LegacyCompatibilityProfile.None
            },
            ShapeExtraction = new ShapeExtractionOptions { SimplifyTolerance = 0 },
            Boundaries = new BoundaryDistortionOptions { Enabled = false }
        };
        var session = MapGenerationSession.Create(
            MapGenerationRequest.Isolated(new RequestedDomain(result.DerivedMask.Window), result.DerivedMask, options),
            new MapGeometrySeed(result.Landmasses, result.RegionDraft));
        session.RunUntil(MapDataKeys.WaterBodies);

        Assert.Contains(session.WaterBodies, water => water.Shape.Covers(
            _factory.CreatePoint(new Coordinate(4.5, 4.5))));
    }

    [Fact]
    public void OverlapIsBlockingDiagnostic()
    {
        var draft = new ManualMapDraft(
            10,
            10,
            [
                Vertex(1, 1, 1), Vertex(2, 6, 1), Vertex(3, 6, 6), Vertex(4, 1, 6),
                Vertex(5, 4, 4), Vertex(6, 8, 4), Vertex(7, 8, 8), Vertex(8, 4, 8)
            ],
            [new ManualRegionFace(1, [1, 2, 3, 4]), new ManualRegionFace(2, [5, 6, 7, 8])]);

        var result = Finalize(draft);

        Assert.False(result.IsSuccessful);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "material-overlap");
    }

    [Fact]
    public void SelfIntersectionAndOutsideBoundsAreRejected()
    {
        var bowTie = new ManualMapDraft(
            10,
            10,
            [Vertex(1, 1, 1), Vertex(2, 8, 8), Vertex(3, 1, 8), Vertex(4, 8, 1)],
            [new ManualRegionFace(1, [1, 2, 3, 4])]);
        var outside = new ManualMapDraft(
            10,
            10,
            [Vertex(1, -1, 1), Vertex(2, 8, 1), Vertex(3, 8, 8), Vertex(4, -1, 8)],
            [new ManualRegionFace(1, [1, 2, 3, 4])]);

        var bowTieResult = Finalize(bowTie);
        var outsideResult = Finalize(outside);

        Assert.Contains(bowTieResult.Diagnostics, diagnostic => diagnostic.Code == "self-intersection");
        Assert.Contains(outsideResult.Diagnostics, diagnostic => diagnostic.Code == "outside-map-bounds");
    }

    [Fact]
    public void SharedVertexCoordinatesRemainCanonicalAndEdgeSplitUpdatesEveryIncidentFace()
    {
        var draft = new ManualMapDraft(
            12,
            10,
            [
                Vertex(1, 1, 1), Vertex(2, 5, 1), Vertex(3, 5, 5), Vertex(4, 1, 5),
                Vertex(5, 9, 1), Vertex(6, 9, 5)
            ],
            [new ManualRegionFace(1, [1, 2, 3, 4]), new ManualRegionFace(2, [2, 5, 6, 3])]);

        Assert.True(ManualMapDraftTopology.TrySplitEdge(
            draft,
            2,
            3,
            7,
            new MapPoint(5, 3),
            out var split,
            out var diagnostic), diagnostic?.Message);
        Assert.NotNull(split);
        Assert.All(split!.Regions, region => Assert.Contains(7, region.VertexIds));
        Assert.Equal(5, split.Vertices.Single(vertex => vertex.Id == 7).Position.X);
        Assert.Equal(3, split.Vertices.Single(vertex => vertex.Id == 7).Position.Y);

        var result = Finalize(split);
        Assert.True(result.IsSuccessful, Diagnostics(result));
    }

    [Fact]
    public void DraftSpatialIndexFindsNearestVertexAndSharedEdgeDeterministically()
    {
        var draft = new ManualMapDraft(
            12,
            10,
            [
                Vertex(1, 1, 1), Vertex(2, 5, 1), Vertex(3, 5, 5), Vertex(4, 1, 5),
                Vertex(5, 9, 1), Vertex(6, 9, 5)
            ],
            [new ManualRegionFace(1, [1, 2, 3, 4]), new ManualRegionFace(2, [2, 5, 6, 3])]);
        var index = new ManualMapDraftSpatialIndex(draft, 2);

        var vertex = index.FindNearestVertex(new MapPoint(5.04, 5.01), .1);
        Assert.True(vertex.HasValue);
        Assert.Equal(3, vertex.Value.VertexId);

        var edge = index.FindNearestEdge(new MapPoint(5.1, 3), .2);
        Assert.True(edge.HasValue);
        Assert.Equal(2, edge.Value.StartVertexId);
        Assert.Equal(3, edge.Value.EndVertexId);
        Assert.Equal(new MapPoint(5, 3), edge.Value.Position);

        index.AddVertex(Vertex(99, 10, 8));
        var addedVertex = index.FindNearestVertex(new MapPoint(10.02, 8.01), .1);
        Assert.True(addedVertex.HasValue);
        Assert.Equal(99, addedVertex.Value.VertexId);
    }

    [Fact]
    public void FinalizationIsDeterministic()
    {
        var draft = new ManualMapDraft(
            20,
            12,
            [
                Vertex(10, 12, 7), Vertex(11, 16, 7), Vertex(12, 16, 10), Vertex(13, 12, 10),
                Vertex(1, 1, 1), Vertex(2, 4, 1), Vertex(3, 4, 4), Vertex(4, 1, 4)
            ],
            [new ManualRegionFace(2, [10, 11, 12, 13]), new ManualRegionFace(1, [1, 2, 3, 4])]);

        var first = Finalize(draft);
        var second = Finalize(draft);

        Assert.Equal(Signature(first.Landmasses), Signature(second.Landmasses));
        Assert.Equal(first.RegionDraft.Regions.Select(region => region.LandmassId), second.RegionDraft.Regions.Select(region => region.LandmassId));
        Assert.Equal(first.DerivedMask.LandPoints.OrderBy(point => point.Y).ThenBy(point => point.X), second.DerivedMask.LandPoints.OrderBy(point => point.Y).ThenBy(point => point.X));
    }

    [Fact]
    public void ManualSeedUsesVectorLandmassesAndStandardPipeline()
    {
        var spatial = CreateSpatialReference(10, 10);
        var finalization = _finalizer.FinalizeDraft(SquareDraft(10, 10, 1, 1, 5, 5), spatial);
        Assert.True(finalization.IsSuccessful, Diagnostics(finalization));

        var options = new MapGenerationOptions
        {
            Seed = 42,
            Spatial = new MapSpatialOptions
            {
                WorldModel = WorldModelDescriptor.Planar(),
                Coverage = spatial.Coverage,
                GridMapping = spatial.GridMapping,
                Topology = spatial.Topology,
                UnitsPerCell = spatial.UnitsPerCell,
                LegacyCompatibility = LegacyCompatibilityProfile.None
            },
            Boundaries = new BoundaryDistortionOptions { Enabled = false }
        };
        var request = MapGenerationRequest.Isolated(
            new RequestedDomain(finalization.DerivedMask.Window),
            finalization.DerivedMask,
            options);
        var session = MapGenerationSession.Create(
            request,
            new MapGeometrySeed(finalization.Landmasses, finalization.RegionDraft));

        session.RunUntil(MapDataKeys.Regions);
        session.RunUntil(MapDataKeys.WaterBodies);

        Assert.Equal(finalization.Landmasses.Select(landmass => landmass.Shape.Area), session.Landmasses.Select(landmass => landmass.Shape.Area));
        Assert.Single(session.Regions);
        Assert.NotEmpty(session.WaterBodies);
        Assert.False(session.IsDirty(MapDataKeys.Landmasses));

        session.Regenerate(MapDataKeys.Landmasses);
        session.Regenerate(MapDataKeys.RegionDraft);

        Assert.Equal(finalization.Landmasses.Select(landmass => landmass.Shape.Area), session.Landmasses.Select(landmass => landmass.Shape.Area));
        Assert.Equal(finalization.RegionDraft.Regions.Select(region => region.Id), session.RegionDraft!.Regions.Select(region => region.Id));
    }

    [Fact]
    public void ManualMapJsonRoundTripsDraft()
    {
        var draft = SquareDraft(10, 10, 1, 1, 5, 5) with
        {
            Regions = [new ManualRegionFace(7, [1, 2, 3, 4], "Capital")]
        };

        var roundTripped = ManualMapJson.Read(ManualMapJson.Write(draft));

        Assert.Equal(draft.GridWidth, roundTripped.GridWidth);
        Assert.Equal(draft.GridHeight, roundTripped.GridHeight);
        Assert.Equal(draft.Vertices, roundTripped.Vertices);
        Assert.Equal(draft.Regions.Select(region => region.Id), roundTripped.Regions.Select(region => region.Id));
        Assert.Equal(draft.Regions.Select(region => region.Name), roundTripped.Regions.Select(region => region.Name));
        Assert.Equal(draft.Regions.SelectMany(region => region.VertexIds), roundTripped.Regions.SelectMany(region => region.VertexIds));
    }

    private ManualMapFinalizationResult Finalize(ManualMapDraft draft) => _finalizer.FinalizeDraft(draft, CreateSpatialReference(draft.GridWidth, draft.GridHeight));

    private static ManualMapDraft SquareDraft(int width, int height, double x, double y, double squareWidth, double squareHeight) => new(
        width,
        height,
        [Vertex(1, x, y), Vertex(2, x + squareWidth, y), Vertex(3, x + squareWidth, y + squareHeight), Vertex(4, x, y + squareHeight)],
        [new ManualRegionFace(1, [1, 2, 3, 4])]);

    private static ManualMapVertex Vertex(int id, double x, double y) => new(id, new MapPoint(x, y));

    private static MapSpatialReference CreateSpatialReference(int width, int height) => new()
    {
        GridWidth = width,
        GridHeight = height,
        UnitsPerCell = 1,
        WorldModel = WorldModelDescriptor.Planar(),
        Coverage = MapCoverage.Regional(new LongitudeInterval(0, Math.Min(width, 360)), 0, height),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.OpenRectangular,
        CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits,
        LegacyCompatibility = LegacyCompatibilityProfile.None
    };

    private static string Diagnostics(ManualMapFinalizationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static IReadOnlyList<string> Signature(IEnumerable<Landmass> landmasses) => landmasses.Select(landmass => string.Join(
        "|",
        landmass.Id.Value.ToString(CultureInfo.InvariantCulture),
        landmass.Shape.Area.ToString("R", CultureInfo.InvariantCulture),
        string.Join(';', landmass.Shape.Coordinates.Select(coordinate => $"{coordinate.X:R},{coordinate.Y:R}")))).ToList();
}
