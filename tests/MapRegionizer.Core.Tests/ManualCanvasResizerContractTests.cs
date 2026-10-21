using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Spatial;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualCanvasResizerContractTests
{
    [Fact]
    public void ExpandingTopAndLeftPreservesEveryVertexGeographyAndSharedIds()
    {
        var draft = SharedEdgeDraft(20, 10);
        var reference = RegionalReference(20, 10, unitsPerCell: 2);
        var resized = new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(TopCells: 3, BottomCells: 0, LeftCells: 4, RightCells: 0));
        var oldMapping = new EquirectangularGridMapping(reference);
        var newMapping = new EquirectangularGridMapping(resized.SpatialReference);

        Assert.Equal(24, resized.Draft.GridWidth);
        Assert.Equal(13, resized.Draft.GridHeight);
        Assert.Equal(draft.Vertices.Select(vertex => vertex.Id), resized.Draft.Vertices.Select(vertex => vertex.Id));
        Assert.Equal(draft.Regions.Select(region => region.Id), resized.Draft.Regions.Select(region => region.Id));
        Assert.Equal(draft.Regions.SelectMany(region => region.VertexIds), resized.Draft.Regions.SelectMany(region => region.VertexIds));
        Assert.Equal(8, resized.Draft.Vertices[0].Position.X - draft.Vertices[0].Position.X, precision: 10);
        Assert.Equal(6, resized.Draft.Vertices[0].Position.Y - draft.Vertices[0].Position.Y, precision: 10);

        foreach (var (oldVertex, newVertex) in draft.Vertices.Zip(resized.Draft.Vertices))
        {
            var before = oldMapping.GridToGeographic(new GridCoordinate(
                oldVertex.Position.X / reference.UnitsPerCell,
                oldVertex.Position.Y / reference.UnitsPerCell));
            var after = newMapping.GridToGeographic(new GridCoordinate(
                newVertex.Position.X / resized.SpatialReference.UnitsPerCell,
                newVertex.Position.Y / resized.SpatialReference.UnitsPerCell));
            Assert.Equal(before.LongitudeDegrees, after.LongitudeDegrees, 10);
            Assert.Equal(before.LatitudeDegrees, after.LatitudeDegrees, 10);
        }

        Assert.Contains(2, resized.Draft.Regions[0].VertexIds.Intersect(resized.Draft.Regions[1].VertexIds));
        Assert.Contains(3, resized.Draft.Regions[0].VertexIds.Intersect(resized.Draft.Regions[1].VertexIds));
    }

    [Fact]
    public void ShrinkingEmptyBottomEdgeSucceedsButDoesNotClipVertices()
    {
        var draft = new ManualMapDraft(
            12,
            12,
            [Vertex(1, 2, 2), Vertex(2, 6, 2), Vertex(3, 6, 6), Vertex(4, 2, 6)],
            [new ManualRegionFace(5, [1, 2, 3, 4], "Keep")]);
        var reference = RegionalReference(12, 12) with
        {
            Coverage = MapCoverage.Regional(-100, 100, 0, 12)
        };

        var resized = new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(0, -4, 0, 0));

        Assert.Equal(8, resized.Draft.GridHeight);
        Assert.Equal(draft.Vertices, resized.Draft.Vertices);
        Assert.Equal(4, resized.SpatialReference.SouthLatitude, 10);
        Assert.Equal(draft.Regions, resized.Draft.Regions);
    }

    [Fact]
    public void ShrinkingThroughExistingGeometryIsRejectedWithoutMutatingSource()
    {
        var draft = new ManualMapDraft(
            10,
            10,
            [Vertex(1, 2, 2), Vertex(2, 8, 2), Vertex(3, 8, 9), Vertex(4, 2, 9)],
            [new ManualRegionFace(1, [1, 2, 3, 4])]);

        var exception = Assert.Throws<ManualCanvasResizeException>(() => new ManualMapCanvasResizer().Resize(
            draft,
            RegionalReference(10, 10),
            new ManualCanvasResizeRequest(0, -2, 0, 0)));

        Assert.Equal(ManualCanvasResizeErrorCode.VertexOutsideNewCanvas, exception.Code);
        Assert.Equal(10, draft.GridHeight);
        Assert.Equal(9, draft.Vertices[2].Position.Y);
    }

    [Fact]
    public void WebMercatorUsesProjectedLatitudeStepsAndRejectsPoleOverflow()
    {
        var reference = new MapSpatialReference
        {
            GridWidth = 20,
            GridHeight = 20,
            UnitsPerCell = 1,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Regional(-60, 60, -60, 60),
            GridMapping = GridMappingKind.WebMercator,
            Topology = GridTopologyKind.OpenRectangular,
            PreserveProjectedCellAspectRatio = false
        };
        var draft = Square(20, 20, 2, 2, 5);
        var resized = new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(TopCells: 2, BottomCells: 0, LeftCells: 0, RightCells: 0));
        var oldMapping = new WebMercatorGridMapping(reference);
        var newMapping = new WebMercatorGridMapping(resized.SpatialReference);
        foreach (var (oldVertex, newVertex) in draft.Vertices.Zip(resized.Draft.Vertices))
        {
            var before = oldMapping.GridToGeographic(new GridCoordinate(oldVertex.Position.X, oldVertex.Position.Y));
            var after = newMapping.GridToGeographic(new GridCoordinate(newVertex.Position.X, newVertex.Position.Y));
            Assert.Equal(before.LatitudeDegrees, after.LatitudeDegrees, 10);
            Assert.Equal(before.LongitudeDegrees, after.LongitudeDegrees, 10);
        }

        var polarReference = reference with { Coverage = MapCoverage.Global(-80, 80) };
        var polarDraft = Square(20, 20, 2, 2, 5);
        var error = Assert.Throws<ManualCanvasResizeException>(() => new ManualMapCanvasResizer().Resize(
            polarDraft,
            polarReference,
            new ManualCanvasResizeRequest(TopCells: 100, BottomCells: 0, LeftCells: 0, RightCells: 0)));
        Assert.Equal(ManualCanvasResizeErrorCode.LatitudeCoverageOutOfRange, error.Code);
    }

    [Fact]
    public void GlobalMapRejectsAnyHorizontalResize()
    {
        var draft = Square(12, 12, 2, 2, 5);
        var reference = new MapSpatialReference
        {
            GridWidth = 12,
            GridHeight = 12,
            UnitsPerCell = 1,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Global(-45, 45),
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.CylindricalX
        };

        var error = Assert.Throws<ManualCanvasResizeException>(() => new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(0, 0, 1, 0)));

        Assert.Equal(ManualCanvasResizeErrorCode.GlobalLongitudeResizeUnsupported, error.Code);
    }

    [Fact]
    public void EquirectangularLatitudeOverflowIsRejected()
    {
        var draft = Square(10, 10, 2, 2, 4);
        var reference = RegionalReference(10, 10) with
        {
            Coverage = MapCoverage.Regional(-10, 10, -80, 80)
        };

        var error = Assert.Throws<ManualCanvasResizeException>(() => new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(1, 0, 0, 0)));

        Assert.Equal(ManualCanvasResizeErrorCode.LatitudeCoverageOutOfRange, error.Code);
        Assert.Equal(10, draft.GridHeight);
    }

    [Fact]
    public void RegionalHorizontalResizePreservesAllVertexLongitudes()
    {
        var draft = Square(20, 10, 3, 2, 3);
        var reference = RegionalReference(20, 10);
        var resized = new ManualMapCanvasResizer().Resize(
            draft,
            reference,
            new ManualCanvasResizeRequest(0, 0, 5, 7));
        var oldMapping = new EquirectangularGridMapping(reference);
        var newMapping = new EquirectangularGridMapping(resized.SpatialReference);

        foreach (var (oldVertex, newVertex) in draft.Vertices.Zip(resized.Draft.Vertices))
        {
            var before = oldMapping.GridToGeographic(new GridCoordinate(oldVertex.Position.X, oldVertex.Position.Y));
            var after = newMapping.GridToGeographic(new GridCoordinate(newVertex.Position.X, newVertex.Position.Y));
            Assert.Equal(before.LongitudeDegrees, after.LongitudeDegrees, 10);
        }
    }

    [Fact]
    public void ResizeAndInverseResizeRestoreDraftAndCoverage()
    {
        var draft = SharedEdgeDraft(20, 10);
        var reference = RegionalReference(20, 10, unitsPerCell: 2);
        var resizer = new ManualMapCanvasResizer();
        var expanded = resizer.Resize(draft, reference, new ManualCanvasResizeRequest(4, 2, 3, 5));
        var restored = resizer.Resize(
            expanded.Draft,
            expanded.SpatialReference,
            new ManualCanvasResizeRequest(-4, -2, -3, -5));

        Assert.Equal(draft.GridWidth, restored.Draft.GridWidth);
        Assert.Equal(draft.GridHeight, restored.Draft.GridHeight);
        Assert.Equal(draft.Vertices, restored.Draft.Vertices);
        Assert.Equal(draft.Regions.Select(region => region.Id), restored.Draft.Regions.Select(region => region.Id));
        Assert.Equal(reference.Longitude.StartLongitudeDegrees, restored.SpatialReference.Longitude.StartLongitudeDegrees, 10);
        Assert.Equal(reference.Longitude.SpanDegrees, restored.SpatialReference.Longitude.SpanDegrees, 10);
        Assert.Equal(reference.SouthLatitude, restored.SpatialReference.SouthLatitude, 10);
        Assert.Equal(reference.NorthLatitude, restored.SpatialReference.NorthLatitude, 10);
    }

    [Fact]
    public void ResizedDraftStillFinalizesAgainstItsUpdatedSpatialReference()
    {
        var draft = SharedEdgeDraft(20, 10);
        var resized = new ManualMapCanvasResizer().Resize(
            draft,
            RegionalReference(20, 10),
            new ManualCanvasResizeRequest(TopCells: 2, BottomCells: 0, LeftCells: 0, RightCells: 0));

        var finalization = new ManualMapDraftFinalizer().FinalizeDraft(resized.Draft, resized.SpatialReference);

        Assert.True(finalization.IsSuccessful, string.Join(Environment.NewLine, finalization.Diagnostics.Select(item => item.Message)));
        Assert.Equal(2, finalization.RegionCount);
        Assert.Equal(resized.Draft.GridWidth, finalization.DerivedMask.Width);
        Assert.Equal(resized.Draft.GridHeight, finalization.DerivedMask.Height);
    }

    private static ManualMapDraft SharedEdgeDraft(int width, int height) => new(
        width,
        height,
        [Vertex(1, 2, 2), Vertex(2, 10, 2), Vertex(3, 10, 8), Vertex(4, 2, 8), Vertex(5, 18, 2), Vertex(6, 18, 8)],
        [new ManualRegionFace(10, [1, 2, 3, 4], "West"), new ManualRegionFace(11, [2, 5, 6, 3], "East")]);

    private static ManualMapDraft Square(int width, int height, double x, double y, double size) => new(
        width,
        height,
        [Vertex(1, x, y), Vertex(2, x + size, y), Vertex(3, x + size, y + size), Vertex(4, x, y + size)],
        [new ManualRegionFace(1, [1, 2, 3, 4])]);

    private static ManualMapVertex Vertex(int id, double x, double y) => new(id, new MapPoint(x, y));

    private static MapSpatialReference RegionalReference(int width, int height, double unitsPerCell = 1) => new()
    {
        GridWidth = width,
        GridHeight = height,
        UnitsPerCell = unitsPerCell,
        WorldModel = WorldModelDescriptor.Spherical(),
        Coverage = MapCoverage.Regional(-100, 100, -40, 40),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.OpenRectangular
    };
}
