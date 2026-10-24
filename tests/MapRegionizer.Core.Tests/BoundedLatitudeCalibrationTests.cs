using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Spatial;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class BoundedLatitudeCalibrationTests
{
    [Fact]
    public void SuppliedPointsInBothHemispheresFitWithoutDiscardingAnyPoints()
    {
        var result = new ParametricGeoreferenceSolver().Solve(new(Reference(), SuppliedPoints()));

        Assert.True(result.IsSuccessful, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(16, result.Metrics!.CalibrationPointCount);
        Assert.Equal(1, result.Metrics.ValidationPointCount);
        Assert.Equal(17, result.Residuals.Count);
        Assert.Equal(90, result.Parameters!.SourceNorthLatitude, 9);
        Assert.Equal(-75.12633125074517, result.Parameters.SourceSouthLatitude, 9);
        Assert.Equal(12.228134529216012, result.Parameters.LongitudeOffsetDegrees, 9);
        Assert.Contains(result.Diagnostics, item => item.Code == "latitude-fit-constrained" && !item.IsError);
        Assert.All(result.Residuals, item => Assert.True(double.IsFinite(item.AngularErrorDegrees)));
        var transform = ParametricGeoreferenceSolver.CreateTransform(Reference(), result.Parameters);
        transform.CreateCalibratedReference().Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LimitingOnePoleRefitsTheOtherInsteadOfClampingTheUnboundedFit(bool reflect)
    {
        // Unconstrained line: north 120°, south -80°. Fixing north at 90°
        // makes the least-squares south -57.3 / .93 = -61.612903..., not -80°.
        var points = new[] { (.2, 80.0), (.5, 20.0), (.8, -40.0) }
            .Select((sample, index) => Point(index + 1, 1280,
                (reflect ? 1 - sample.Item1 : sample.Item1) * 1413,
                0, reflect ? -sample.Item2 : sample.Item2)).ToArray();
        var result = new ParametricGeoreferenceSolver().Solve(new(Reference(), points));

        Assert.True(result.IsSuccessful);
        Assert.Equal(reflect ? 61.61290322580645 : 90, result.Parameters!.SourceNorthLatitude, 9);
        Assert.Equal(reflect ? -90 : -61.61290322580645, result.Parameters.SourceSouthLatitude, 9);
        var error = result.Residuals.Sum(item => item.DeltaLatitudeDegrees * item.DeltaLatitudeDegrees);
        // Naively clamping the original fit gives errors -24°, -15°, -6°.
        Assert.True(error < 24 * 24 + 15 * 15 + 6 * 6);
    }

    [Theory]
    [InlineData(GridMappingKind.Equirectangular, 80)]
    [InlineData(GridMappingKind.WebMercator, 84)]
    public void BothProjectionLimitsCanBeActive(GridMappingKind mapping, double latitude)
    {
        var reference = Reference() with { GridMapping = mapping, Coverage = MapCoverage.Global(-80, 80) };
        var points = new[] { Point(1, 1280, 1413 * .25, 0, latitude), Point(2, 1280, 1413 * .75, 0, -latitude) };
        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.True(result.IsSuccessful);
        var limit = mapping == GridMappingKind.WebMercator ? WebMercatorGridMapping.WebMercatorLatitudeLimit : 90;
        Assert.Equal(limit, result.Parameters!.SourceNorthLatitude, 9);
        Assert.Equal(-limit, result.Parameters.SourceSouthLatitude, 9);
        ParametricGeoreferenceSolver.CreateTransform(reference, result.Parameters).CreateCalibratedReference().Validate();
    }

    [Fact]
    public void ChangingAnIndependentCheckPointCannotChangeTheConstrainedFit()
    {
        var points = SuppliedPoints();
        var solver = new ParametricGeoreferenceSolver();
        var before = solver.Solve(new(Reference(), points));
        var changed = points.Select(point => point.Role == GeographicControlPointRole.Validation
            ? point with { GeographicPosition = new(0, -70) } : point).ToArray();
        var after = solver.Solve(new(Reference(), changed));

        Assert.True(before.IsSuccessful);
        Assert.True(after.IsSuccessful);
        Assert.Equal(before.Parameters, after.Parameters);
        Assert.NotEqual(before.Metrics!.ValidationRmsDegrees, after.Metrics!.ValidationRmsDegrees);
    }

    [Fact]
    public void ValidMercatorPointsCanExtrapolateBeyondItsLimitWithoutInvalidatingTheFit()
    {
        var reference = Reference() with { GridMapping = GridMappingKind.WebMercator, Coverage = MapCoverage.Global(-80, 80) };
        var points = new[] { Point(1, 1280, 1413 * .4, 0, 85), Point(2, 1280, 1413 * .5, 0, 84) };
        var result = new ParametricGeoreferenceSolver().Solve(new(reference, points));

        Assert.True(result.IsSuccessful);
        Assert.Equal(WebMercatorGridMapping.WebMercatorLatitudeLimit, result.Parameters!.SourceNorthLatitude, 9);
        Assert.True(result.Parameters.SourceSouthLatitude < result.Parameters.SourceNorthLatitude);
        Assert.Contains(result.Diagnostics, item => item.Code == "latitude-fit-constrained");
        var repeated = new ParametricGeoreferenceSolver().Solve(new(reference, points));
        Assert.Equal(result.Parameters, repeated.Parameters);
        Assert.Equal(result.Metrics, repeated.Metrics);
        Assert.Equal(result.Residuals, repeated.Residuals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void CollapsedOrInvertedLatitudeModelsRemainInvalid(double latitude)
    {
        var points = new[] { Point(1, 1280, 1413 * .25, 0, -latitude), Point(2, 1280, 1413 * .75, 0, latitude) };
        var result = new ParametricGeoreferenceSolver().Solve(new(Reference(), points));

        Assert.False(result.IsSuccessful);
        Assert.Contains(result.Diagnostics, item => item.Code == "invalid-latitude-fit");
    }

    internal static MapSpatialReference Reference() => ManualMapSeamContractTests.Reference() with
    {
        GridWidth = 2560,
        GridHeight = 1413,
        Coverage = MapCoverage.Global(-90, 89.72368421052632)
    };

    // Only anchors from the supplied project, without its 2.9 MB geometry.
    internal static GeographicControlPoint[] SuppliedPoints() =>
    [
        Point(1, 1676.7198330638932, 79.97438635185581, 67, 77),
        Point(2, 854.5053214567326, 21.69263610371433, -38, 83, GeographicControlPointRole.Validation),
        Point(3, 1431.3886901499816, 236.98740642316244, 33, 69),
        Point(4, 1121.751008064453, 408.3856389325604, -10, 53),
        Point(5, 1400.398535331883, 520.5758099848603, 29, 41),
        Point(6, 618.3671225707334, 561.7632263492666, -80, 25),
        Point(7, 626.0171078958197, 682.7416301571302, -80, 9),
        Point(8, 945.4268686249138, 796.3341570540147, -35, -7),
        Point(9, 1069.2403701569106, 728.9853435765222, -18, -15),
        Point(10, 2291.8649950935564, 441.3060651964313, 157, 51),
        Point(11, 1745.717792771071, 780.1064426105441, 77, 8),
        Point(12, 1516.1179503787093, 1028.5593478555197, 45, -25),
        Point(13, 1335.172020669169, 1101.4090973712425, 20, -35),
        Point(14, 729.1570105087942, 1174.9570278147426, -65, -55),
        Point(15, 779.6921986061702, 1235.4616353247047, -58, -63),
        Point(16, 2236.4155632709617, 1175.6766705182754, 147, -44),
        Point(18, 1314.5912678415839, 89.20546861824579, 17, 77)
    ];

    private static GeographicControlPoint Point(int id, double x, double y, double longitude, double latitude,
        GeographicControlPointRole role = GeographicControlPointRole.Calibration) =>
        new(id, $"GCP {id}", GeographicControlPointAnchorKind.MapUnits, role, new(longitude, latitude), new(x, y));
}
