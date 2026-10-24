using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Spatial;
using MapRegionizer.App.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class ManualMapReprojectionAndGeoreferencingTests
{
    [Fact]
    public void MercatorCalibrationMatchesIndependentKnownLatitudesWithScaledMapUnits()
    {
        // Analytic Mercator values: asinh(tan(60°))/2 maps back to
        // atan(sinh(...)) = 35.26438968275465°. No production mapping
        // is used to manufacture the expected coordinates.
        var reference = Reference(100, 100, 2, GridMappingKind.WebMercator, MapCoverage.Global(-80, 80));
        GeographicControlPoint[] points =
        [
            new(1, "north fit", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-96, 35.26438968275465), new(40, 50)),
            new(2, "south fit", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(120, -35.26438968275465), new(160, 150)),
            new(3, "north check", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(12, 60), new(100, 0)),
            new(4, "equator check", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(-78, 0), new(50, 100)),
            new(5, "south check", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(102, -60), new(150, 200))
        ];

        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.True(result.IsSuccessful);
        Assert.Equal(12, result.Parameters!.LongitudeOffsetDegrees, 8);
        Assert.Equal(-60, result.Parameters.SourceSouthLatitude, 8);
        Assert.Equal(60, result.Parameters.SourceNorthLatitude, 8);
        Assert.True(result.Metrics!.ValidationTargetReached);
        Assert.InRange(result.Metrics.ValidationMaximumDegrees, 0, 1e-8);
        var changedHoldout = points.Select(point => point.Role == GeographicControlPointRole.Validation
            ? point with { GeographicPosition = new(0, 0) } : point).ToArray();
        var second = new ParametricGeoreferenceSolver().Solve(new(reference, changedHoldout));
        Assert.Equal(result.Parameters, second.Parameters);
        Assert.False(second.Metrics!.ValidationTargetReached);
    }

    [Theory]
    [InlineData("-179,999999", "-89.999999")]
    [InlineData("-179.999999", "-89,999999")]
    public void CoordinateEntryAcceptsBothDecimalSeparatorsWithoutSwappingAxes(string longitude, string latitude)
    {
        var row = new MapRegionizer.App.Views.GeographicControlPointRowViewModel(new(1, "test",
            GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(0, 0), new(10, 10)))
        {
            LongitudeText = longitude,
            LatitudeText = latitude
        };
        var point = row.ToPoint();
        Assert.Equal(-179.999999, point.GeographicPosition.LongitudeDegrees, 6);
        Assert.Equal(-89.999999, point.GeographicPosition.LatitudeDegrees, 6);
        row.IsActive = false;
        Assert.Equal(point.GeographicPosition, row.ToPoint().GeographicPosition);
        row.IsActive = true;
        row.LatitudeText = "";
        Assert.Throws<ArgumentException>(() => row.ToPoint());
        row.LatitudeText = "91";
        Assert.Throws<ArgumentException>(() => row.ToPoint());
    }

    [Fact]
    public void SymmetricMercatorEdgeStillNeedsDensificationAwayFromItsMidpoint()
    {
        var source = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        var target = source with { GridMapping = GridMappingKind.WebMercator };
        var draft = TwoRegionDraft(100, 100, new(10, 5), new(90, 5), new(90, 95), new(10, 95));

        var result = new ManualMapReprojectionService().Reproject(new(draft, source, target));

        Assert.True(result.IsSuccessful);
        Assert.True(result.AddedVertices > 0, "The equator midpoint hides the curvature on either side.");
        var sharedIds = result.Draft!.Regions[0].VertexIds.Intersect(result.Draft.Regions[1].VertexIds).ToHashSet();
        var edge = result.Draft.Vertices.Where(vertex => sharedIds.Contains(vertex.Id)).OrderBy(vertex => vertex.Position.X).ToArray();
        const double mercator80 = 2.4362460537158777;
        for (var index = 0; index < edge.Length - 1; index++)
            for (var sample = 1; sample < 32; sample++)
            {
                var fraction = sample / 32.0;
                var x = edge[index].Position.X + (edge[index + 1].Position.X - edge[index].Position.X) * fraction;
                var sourceY = 95 - 90 * (x - 10) / 80;
                var latitude = (80 - 1.6 * sourceY) * Math.PI / 180;
                var expectedY = (mercator80 - Math.Asinh(Math.Tan(latitude))) / (2 * mercator80) * 100;
                var chordY = edge[index].Position.Y + (edge[index + 1].Position.Y - edge[index].Position.Y) * fraction;
                Assert.InRange(Math.Abs(expectedY - chordY), 0, .25);
            }
        var repeated = new ManualMapReprojectionService().Reproject(new(draft, source, target));
        Assert.Equal(result.Draft.Vertices, repeated.Draft!.Vertices);
    }

    [Fact]
    public void MercatorControlPointOutsideItsLatitudeLimitReturnsDiagnosticInsteadOfThrowing()
    {
        var reference = Reference(100, 100, 1, GridMappingKind.WebMercator, MapCoverage.Global(-80, 80));
        GeographicControlPoint[] points =
        [
            new(1, "north", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-144, 86), new(10, 40)),
            new(2, "south", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(108, 84), new(80, 50))
        ];

        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.False(result.IsSuccessful);
        Assert.Contains(result.Diagnostics, item => item.Code == "latitude-outside-source-projection");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ValidationMetricsDoNotRequireMinimumCountOrSpread(int validationCount)
    {
        var reference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        GeographicControlPoint[] points =
        [
            new(1, "fit north", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-144, 48), new(10, 20)),
            new(2, "fit south", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(108, -48), new(80, 80)),
            new(3, "check 1", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(0, 0), new(50, 50)),
            new(4, "check 2", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(0, 0), new(50, 50)),
            new(5, "check 3", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Validation, new(0, 0), new(50, 50))
        ];

        var selectedPoints = points.Take(2 + validationCount).ToArray();
        var result = new ParametricGeoreferenceSolver().Solve(new(reference, selectedPoints));

        Assert.True(result.IsSuccessful);
        Assert.True(result.Metrics!.ValidationTargetReached);
        Assert.Equal(validationCount, result.Metrics.ValidationPointCount);
        Assert.InRange(result.Metrics.ValidationMaximumDegrees, 0, 1e-8);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "poor-validation-distribution" or "insufficient-validation-points");
    }

    [Fact]
    public void CalibrationWithoutValidationReportsFitWithoutRequestingMorePoints()
    {
        var reference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        GeographicControlPoint[] points =
        [
            new(1, "north", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(-144, 48), new(10, 20)),
            new(2, "south", GeographicControlPointAnchorKind.MapUnits, GeographicControlPointRole.Calibration, new(108, -48), new(80, 80))
        ];

        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.True(result.IsSuccessful);
        Assert.InRange(result.Metrics!.CalibrationRmsDegrees, 0, 1e-8);
        Assert.Equal(0, result.Metrics.ValidationPointCount);
        Assert.False(result.Metrics.ValidationTargetReached);
        Assert.Equal("Независимая проверка не выполнялась.", result.Metrics.ValidationStatus);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "poor-validation-distribution" or "insufficient-validation-points");
    }

    [Fact]
    public void ReprojectionPreservesGeographicPositionsAndUnitsPerCell()
    {
        var sourceReference = Reference(100, 80, 2, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        var targetReference = Reference(200, 100, .5, GridMappingKind.WebMercator, MapCoverage.Global(-80, 80));
        var source = TwoRegionDraft(100, 80, new MapPoint(20, 20), new MapPoint(160, 20), new MapPoint(160, 140), new MapPoint(20, 140));

        var result = new ManualMapReprojectionService().Reproject(new(source, sourceReference, targetReference));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(result.Draft);
        var sourceContext = MapSpatialContext.Create(sourceReference);
        var targetContext = MapSpatialContext.Create(targetReference);
        var newVertices = result.Draft.Vertices.ToDictionary(vertex => vertex.Id);
        foreach (var oldVertex in source.Vertices)
        {
            var oldGeo = sourceContext.GridToGeographic(new GridCoordinate(oldVertex.Position.X / sourceReference.UnitsPerCell, oldVertex.Position.Y / sourceReference.UnitsPerCell));
            var newGeo = targetContext.GridToGeographic(new GridCoordinate(newVertices[oldVertex.Id].Position.X / targetReference.UnitsPerCell, newVertices[oldVertex.Id].Position.Y / targetReference.UnitsPerCell));
            Assert.InRange(Math.Abs(LongitudeInterval.Normalize(newGeo.LongitudeDegrees - oldGeo.LongitudeDegrees)), 0, 1e-8);
            Assert.InRange(Math.Abs(newGeo.LatitudeDegrees - oldGeo.LatitudeDegrees), 0, 1e-8);
        }
    }

    [Fact]
    public void ReprojectionDensifiesSharedNonlinearEdgeOnceAndKeepsItShared()
    {
        var sourceReference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        var targetReference = Reference(100, 100, 1, GridMappingKind.WebMercator, MapCoverage.Global(-80, 80));
        var draft = TwoRegionDraft(100, 100, new MapPoint(10, 5), new MapPoint(90, 5), new MapPoint(90, 45), new MapPoint(10, 45));

        var result = new ManualMapReprojectionService().Reproject(new(draft, sourceReference, targetReference));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.AddedVertices > 0);
        Assert.NotNull(result.Draft);
        var first = result.Draft.Regions[0].VertexIds.ToHashSet();
        var second = result.Draft.Regions[1].VertexIds.ToHashSet();
        var sharedInserted = first.Intersect(second).Where(id => id > 4).ToArray();
        Assert.NotEmpty(sharedInserted);
        Assert.Equal(sharedInserted.Length, result.Draft.Vertices.Count(vertex => sharedInserted.Contains(vertex.Id)));

        var overBudget = new ManualMapReprojectionService().Reproject(new(draft, sourceReference, targetReference, MaximumAddedVertices: 0));
        Assert.False(overBudget.IsSuccessful);
        Assert.Null(overBudget.Draft);
        Assert.Contains(overBudget.Diagnostics, diagnostic => diagnostic.Code == "vertex-budget-exceeded");
    }

    [Fact]
    public void ReprojectionRejectsMercatorLatitudeOverflowWithoutReturningPartialDraft()
    {
        var sourceReference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-90, 90));
        var targetReference = Reference(100, 100, 1, GridMappingKind.WebMercator, MapCoverage.Global(-80, 80));
        var draft = OneTriangleDraft(100, 100, new MapPoint(10, 2), new MapPoint(40, 5), new MapPoint(20, 20));

        var result = new ManualMapReprojectionService().Reproject(new(draft, sourceReference, targetReference));

        Assert.False(result.IsSuccessful);
        Assert.Null(result.Draft);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "coordinate-out-of-range" or "coordinate-transform-failed");
    }

    [Fact]
    public void ReprojectionKeepsTheExactEasternBoundaryOnTheRightEdge()
    {
        var reference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        var draft = OneTriangleDraft(100, 100, new MapPoint(99, 20), new MapPoint(100, 20), new MapPoint(99, 30));

        var result = new ManualMapReprojectionService().Reproject(new(draft, reference, reference));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(100, result.Draft!.Vertices.Single(vertex => vertex.Id == 2).Position.X, 8);
    }

    [Fact]
    public void CalibrationRecoversLongitudeOffsetAndLatitudeBoundsAndKeepsValidationHoldout()
    {
        var wrongReference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-90, 90));
        var trueReference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-50, 70));
        var trueContext = MapSpatialContext.Create(trueReference);
        var points = new List<GeographicControlPoint>();
        AddPoint(1, 10, 10, GeographicControlPointRole.Calibration);
        AddPoint(2, 80, 80, GeographicControlPointRole.Calibration);
        AddPoint(3, 30, 40, GeographicControlPointRole.Validation);
        AddPoint(4, 55, 65, GeographicControlPointRole.Validation);
        AddPoint(5, 70, 20, GeographicControlPointRole.Validation);

        var result = new ParametricGeoreferenceSolver().Solve(new(wrongReference, points));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(result.Parameters);
        Assert.Equal(12, result.Parameters.LongitudeOffsetDegrees, 8);
        Assert.Equal(-50, result.Parameters.SourceSouthLatitude, 8);
        Assert.Equal(70, result.Parameters.SourceNorthLatitude, 8);
        Assert.Equal(2, result.Metrics!.CalibrationPointCount);
        Assert.Equal(3, result.Metrics.ValidationPointCount);
        Assert.True(result.Metrics.ValidationTargetReached);
        Assert.InRange(result.Metrics.CalibrationMeanAbsoluteLongitudeErrorDegrees, 0, 1e-8);
        Assert.InRange(result.Metrics.CalibrationMeanAbsoluteLatitudeErrorDegrees, 0, 1e-8);
        Assert.InRange(result.Metrics.ValidationMeanAbsoluteLongitudeErrorDegrees, 0, 1e-8);
        Assert.InRange(result.Metrics.ValidationMeanAbsoluteLatitudeErrorDegrees, 0, 1e-8);
        Assert.All(result.Residuals.Where(residual => residual.Role == GeographicControlPointRole.Validation), residual =>
        {
            Assert.InRange(Math.Abs(residual.DeltaLongitudeDegrees), 0, 1e-8);
            Assert.InRange(Math.Abs(residual.DeltaLatitudeDegrees), 0, 1e-8);
        });

        void AddPoint(int id, double y, double x, GeographicControlPointRole role)
        {
            var mapPosition = new MapPoint(x, y);
            var geographic = trueContext.GridToGeographic(new GridCoordinate(x, y));
            points.Add(new GeographicControlPoint(
                id,
                $"point-{id}",
                GeographicControlPointAnchorKind.MapUnits,
                role,
                geographic with { LongitudeDegrees = LongitudeInterval.Normalize(geographic.LongitudeDegrees + 12) },
                MapPosition: mapPosition));
        }
    }

    [Fact]
    public void CalibrationFindsLongitudeOffsetAcrossTheAntimeridian()
    {
        var reference = Reference(100, 100, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
        var context = MapSpatialContext.Create(reference);
        var points = new[]
        {
            Create(1, new MapPoint(99.5, 20)),
            Create(2, new MapPoint(.5, 80))
        };

        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(2, result.Parameters!.LongitudeOffsetDegrees, 8);

        GeographicControlPoint Create(int id, MapPoint position)
        {
            var geographic = context.GridToGeographic(new GridCoordinate(position.X, position.Y));
            return new GeographicControlPoint(
                id,
                $"seam-{id}",
                GeographicControlPointAnchorKind.MapUnits,
                GeographicControlPointRole.Calibration,
                geographic with { LongitudeDegrees = LongitudeInterval.Normalize(geographic.LongitudeDegrees + 2) },
                position);
        }
    }

    [Fact]
    public void BackgroundImagePlacementUvRoundTripsWithRotation()
    {
        var placement = new BackgroundImagePlacement(-15, 8, 120, 80, 23);
        var map = placement.ToMapPoint(.27, .83);
        var uv = placement.ToImageUv(map);

        Assert.Equal(.27, uv.U, 10);
        Assert.Equal(.83, uv.V, 10);
    }

    [Fact]
    public void BackgroundReprojectorInverseMapsAndLeavesOutsidePixelsTransparent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"map-regionizer-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = new Image<Rgba32>(3, 3))
            {
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        source[x, y] = new Rgba32((byte)(x * 100), (byte)(y * 100), 50, 255);
                source.SaveAsPng(path);
            }

            var reference = Reference(10, 10, 1, GridMappingKind.Equirectangular, MapCoverage.Global(-80, 80));
            var png = ManualMapBackgroundReprojector.ReprojectToPng(
                path,
                new BackgroundImagePlacement(2, 2, 4, 4),
                reference,
                reference);
            using var result = Image.Load<Rgba32>(png);

            Assert.Equal(10, result.Width);
            Assert.Equal(10, result.Height);
            Assert.Equal((byte)0, result[0, 0].A);
            Assert.Equal((byte)255, result[4, 4].A);
            Assert.InRange(result[4, 4].R, (byte)110, (byte)140);
            Assert.InRange(result[4, 4].G, (byte)110, (byte)140);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static ManualMapDraft TwoRegionDraft(
        int width,
        int height,
        MapPoint topLeft,
        MapPoint topRight,
        MapPoint bottomRight,
        MapPoint bottomLeft) => new(width, height,
    [
        new ManualMapVertex(1, topLeft),
        new ManualMapVertex(2, topRight),
        new ManualMapVertex(3, bottomRight),
        new ManualMapVertex(4, bottomLeft)
    ],
    [
        new ManualRegionFace(1, [1, 2, 4], "north-west"),
        new ManualRegionFace(2, [2, 3, 4], "south-east")
    ]);

    private static ManualMapDraft OneTriangleDraft(int width, int height, MapPoint a, MapPoint b, MapPoint c) => new(width, height,
    [new ManualMapVertex(1, a), new ManualMapVertex(2, b), new ManualMapVertex(3, c)],
    [new ManualRegionFace(1, [1, 2, 3])]);

    private static MapSpatialReference Reference(
        int width,
        int height,
        double unitsPerCell,
        GridMappingKind mapping,
        MapCoverage coverage) => new()
        {
            GridWidth = width,
            GridHeight = height,
            UnitsPerCell = unitsPerCell,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = coverage,
            GridMapping = mapping,
            Topology = GridTopologyKind.CylindricalX,
            PreserveProjectedCellAspectRatio = false,
            LegacyCompatibility = LegacyCompatibilityProfile.None
        };
}
