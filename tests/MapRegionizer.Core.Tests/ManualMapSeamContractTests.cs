using System.Text.Json;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using MapRegionizer.Core.Spatial;
using ReactiveUI.Builder;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualMapSeamContractTests
{
    [Theory]
    [InlineData(GridMappingKind.Equirectangular)]
    [InlineData(GridMappingKind.WebMercator)]
    public void CalibratedReferencePreservesContoursInTheFittedProjection(GridMappingKind mapping)
    {
        var source = Reference();
        var transform = ParametricGeoreferenceSolver.CreateTransform(source, new(1, mapping, 12, -60, 70, SourceReference: source));
        var target = transform.CreateCalibratedReference();
        var result = new ManualMapReprojectionService().Reproject(new(Draft(), source, target, GeoreferenceTransform: transform));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(mapping, target.GridMapping);
        Assert.Equal(-60, target.SouthLatitude);
        Assert.Equal(70, target.NorthLatitude);
        foreach (var (before, after) in Draft().Vertices.Zip(result.Draft!.Vertices))
        {
            Assert.Equal(before.Position.X, after.Position.X, 8);
            Assert.Equal(before.Position.Y, after.Position.Y, 8);
        }
    }

    [Fact]
    public void KeepingTheCalibratedGridAvoidsRoundingACloseBoundaryIntoSelfIntersection()
    {
        // Four vertices reduced from region 1360 in the supplied map. The
        // near-touch is valid on the original 1e-6 lattice, but an affine Y
        // compression into the old latitude extent rounds it across the edge.
        var source = Reference() with { GridWidth = 2560, GridHeight = 1413, Coverage = MapCoverage.Global(-90, 89.72368421052632) };
        var draft = new ManualMapDraft(2560, 1413,
        [
            new(2619, new(1219.758388, 428.746567)), new(11813, new(1218.823381, 427.395557)),
            new(11814, new(1217.937958, 429.477217)), new(11793, new(1216.837042, 429.919082))
        ], [new(1360, [2619, 11813, 11814, 11793], "close boundary")]);
        var transform = ParametricGeoreferenceSolver.CreateTransform(source, new(1, GridMappingKind.Equirectangular, 12.0219, -28.8145, 83.7987, SourceReference: source));
        var request = new ManualMapReprojectionRequest(draft, source, source, GeoreferenceTransform: transform);
        var cutOnly = ManualMapReprojectionService.SuggestContinuousTargetReference(request)!;
        var service = new ManualMapReprojectionService();
        var compressed = service.Reproject(request with { TargetReference = cutOnly });
        Assert.False(compressed.IsSuccessful);
        Assert.Contains(compressed.Diagnostics, item => item.Code == "self-intersection");

        var result = service.Reproject(request with { TargetReference = transform.CreateCalibratedReference() });

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(draft.Regions[0].VertexIds, result.Draft!.Regions[0].VertexIds);
        var verticesById = result.Draft.Vertices.ToDictionary(vertex => vertex.Id);
        foreach (var before in draft.Vertices)
        {
            var after = verticesById[before.Id];
            Assert.Equal(before.Position.X, after.Position.X, 8);
            Assert.Equal(before.Position.Y, after.Position.Y, 8);
        }
    }

    [Theory]
    [InlineData(12)]
    [InlineData(-12)]
    public void RelocatingFullWorldCutPreservesRegionsAndCalibratedGeography(double offset)
    {
        var source = Reference();
        var original = Draft();
        if (offset < 0)
            original = original with { Vertices = original.Vertices.Select(vertex => vertex with { Position = vertex.Position with { X = vertex.Position.X - 94 } }).ToArray() };
        var parameters = new GeographicCalibrationParameters(1, GridMappingKind.Equirectangular, offset, -70, 75, SourceReference: source);
        var transform = ParametricGeoreferenceSolver.CreateTransform(source, parameters);
        var request = new ManualMapReprojectionRequest(original, source, source, GeoreferenceTransform: transform);
        var service = new ManualMapReprojectionService();
        var blocked = service.Reproject(request);
        Assert.False(blocked.IsSuccessful);
        Assert.Contains(blocked.Diagnostics, item => item.Code == "seam-crossing-unsupported");

        var suggested = ManualMapReprojectionService.SuggestContinuousTargetReference(request);
        Assert.NotNull(suggested);
        Assert.Equal(LongitudeInterval.Normalize(-180 + offset), suggested.Longitude.Start, 10);
        Assert.Equal(360, suggested.Longitude.SpanDegrees);
        var result = service.Reproject(request with { TargetReference = suggested });

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(original.Vertices.Select(vertex => vertex.Id), result.Draft!.Vertices.Select(vertex => vertex.Id));
        Assert.Equal(original.Regions.Select(region => region.Id), result.Draft.Regions.Select(region => region.Id));
        Assert.Equal(original.Regions.SelectMany(region => region.VertexIds), result.Draft.Regions.SelectMany(region => region.VertexIds));
        Assert.Equal(original.Regions.Select(region => region.Name), result.Draft.Regions.Select(region => region.Name));
        foreach (var (oldVertex, newVertex) in original.Vertices.Zip(result.Draft.Vertices))
        {
            Assert.Equal(oldVertex.Position.X, newVertex.Position.X, 8);
            var actual = MapSpatialContext.Create(suggested).GridToGeographic(new(newVertex.Position.X, newVertex.Position.Y));
            Assert.InRange(Math.Abs(LongitudeInterval.Normalize(actual.LongitudeDegrees - (-180 + oldVertex.Position.X * 3.6 + offset))), 0, 1e-8);
            Assert.Equal(75 - oldVertex.Position.Y * 1.45, actual.LatitudeDegrees, 8);
        }
        var repeated = service.Reproject(request with { TargetReference = suggested });
        Assert.Equal(result.Draft.Vertices, repeated.Draft!.Vertices);
        Assert.NotNull(result.Finalization);
        Assert.True(result.Finalization.IsSuccessful);
    }

    [Fact]
    public void CalibratedFullWidthFaceKeepsBothSidesOfItsRelocatedCut()
    {
        var source = Reference(170);
        var original = Draft() with { Vertices = Draft().Vertices.Select(vertex => vertex with { Position = vertex.Position with { X = vertex.Id is 1 or 4 ? 0 : 100 } }).ToArray() };
        var transform = ParametricGeoreferenceSolver.CreateTransform(source, new(1, GridMappingKind.Equirectangular, 12, -80, 80, SourceReference: source));
        var request = new ManualMapReprojectionRequest(original, source, Reference(), GeoreferenceTransform: transform);
        var target = ManualMapReprojectionService.SuggestContinuousTargetReference(request)!;
        var result = new ManualMapReprojectionService().Reproject(request with { TargetReference = target });

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(0, result.Draft!.Vertices.Single(vertex => vertex.Id == 1).Position.X, 8);
        Assert.Equal(100, result.Draft.Vertices.Single(vertex => vertex.Id == 2).Position.X, 8);
    }

    [Fact]
    public void RegionalExtentDoesNotOfferAFullWorldCutChange()
    {
        var source = Reference() with { Coverage = MapCoverage.Regional(170, -170, -80, 80), Topology = GridTopologyKind.OpenRectangular };
        Assert.Null(ManualMapReprojectionService.SuggestContinuousTargetReference(new(Draft(), source, Reference())));
    }

    [Fact]
    public void ShiftedGlobalOriginSurvivesProjectAndNestedReferenceSerialization()
    {
        var reference = Reference(-167.9781);
        var draft = Draft();
        var parameters = new GeographicCalibrationParameters(1, GridMappingKind.Equirectangular, 12, -80, 80, SourceReference: reference);

        var project = ManualMapJson.ReadProject(ManualMapJson.Write(draft, reference, [], parameters));

        Assert.Equal(reference, project.SpatialReference);
        Assert.Equal(reference, project.CalibrationParameters!.SourceReference);
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MapSpatialReferenceJsonConverter());
        var recipe = new BackgroundReprojectionState(reference, Reference(170), new(0, 0, 100, 100), CalibrationParameters: parameters);
        Assert.Equal(recipe, JsonSerializer.Deserialize<BackgroundReprojectionState>(JsonSerializer.Serialize(recipe, options), options));
    }

    [Fact]
    public void CalibrationPreservesShiftedSourceOrigin()
    {
        var source = Reference(170);
        var parameters = new GeographicCalibrationParameters(1, GridMappingKind.Equirectangular, 12, -80, 80, SourceReference: source);
        var transform = ParametricGeoreferenceSolver.CreateTransform(source, parameters);

        Assert.Equal(2, transform.ToGeographic(new(50, 50)).LongitudeDegrees, 9);
    }

    [Fact]
    public void ShiftedGlobalOriginSurvivesSpatialControls()
    {
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
        var reference = Reference(-167.9781);
        var configuration = new SpatialConfigurationViewModel();
        configuration.LoadSpatialOptions(new MapSpatialOptions { Coverage = reference.Coverage, PreserveProjectedCellAspectRatio = false });

        Assert.Equal(reference.Longitude, configuration.BuildSpatialOptions().Coverage.Longitude);
    }

    [Fact]
    public void ShiftedGlobalOriginSurvivesCanvasResize()
    {
        var reference = Reference(-167.9781);
        var result = new ManualMapCanvasResizer().Resize(Draft(), reference, new(1, 0, 0, 0));

        Assert.Equal(reference.Longitude, result.SpatialReference.Longitude);
    }

    internal static MapSpatialReference Reference(double west = -180) => new()
    {
        GridWidth = 100,
        GridHeight = 100,
        UnitsPerCell = 1,
        WorldModel = WorldModelDescriptor.Spherical(),
        Coverage = MapCoverage.Create(MapCoverageKind.Global, new(west, 360), -80, 80),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.CylindricalX,
        PreserveProjectedCellAspectRatio = false
    };

    internal static ManualMapDraft Draft() => new(100, 100,
        [new(1, new(95, 20)), new(2, new(99, 20)), new(3, new(99, 40)), new(4, new(95, 40))],
        [new(1, [1, 2, 4], "north"), new(2, [2, 3, 4], "south")]);
}
