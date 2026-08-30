using System.Text.Json.Nodes;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

#pragma warning disable CA1707

namespace MapRegionizer.Core.Tests;

public sealed class ProjectionContractTests
{
    private const double WebLimit = WebMercator3857.LatitudeLimitDegrees;

    [Fact]
    public void WebMercator_OriginMapsToOrigin()
    {
        var projected = WebMercator3857.Forward(new GeoCoordinate(0, 0));

        Assert.Equal(0, projected.X, 12);
        Assert.Equal(0, projected.Y, 12);
    }

    [Fact]
    public void WebMercator_ForwardInverse_RoundTrip()
    {
        var geographic = new GeoCoordinate(37.25, 48.125);
        var roundTrip = WebMercator3857.Inverse(WebMercator3857.Forward(geographic));

        Assert.Equal(geographic.LongitudeDegrees, roundTrip.LongitudeDegrees, 10);
        Assert.Equal(geographic.LatitudeDegrees, roundTrip.LatitudeDegrees, 10);
    }

    [Fact]
    public void WebMercator_Longitude180_HasExpectedX()
    {
        var projected = WebMercator3857.Forward(new GeoCoordinate(180, 0));

        Assert.Equal(WebMercator3857.HalfWorldMeters, projected.X, 6);
    }

    [Fact]
    public void WebMercator_MaxTileLatitude_HasExpectedY()
    {
        var projected = WebMercator3857.Forward(new GeoCoordinate(0, WebLimit));

        Assert.Equal(WebMercator3857.HalfWorldMeters, projected.Y, 6);
    }

    [Fact]
    public void WebMercator_InvalidPole_IsRejectedOrClippedAccordingToPolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebMercator3857.Forward(new GeoCoordinate(0, 90), LatitudeOverflowPolicy.Reject));

        var clipped = WebMercator3857.Forward(new GeoCoordinate(0, 90), LatitudeOverflowPolicy.Clip);
        Assert.Equal(WebLimit, WebMercator3857.Inverse(clipped).LatitudeDegrees, 10);
    }

    [Fact]
    public void WebMercator_InverseOutsideTileDomain_IsRejectedOrClippedAccordingToPolicy()
    {
        var outside = new MapPoint(0, WebMercator3857.HalfWorldMeters + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => WebMercator3857.Inverse(outside));

        var clipped = WebMercator3857.Inverse(outside, LatitudeOverflowPolicy.Clip);
        Assert.Equal(WebLimit, clipped.LatitudeDegrees, 10);
    }

    [Fact]
    public void WebMercator_OutputContainsNoNaNOrInfinity()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(0, 0), new Coordinate(180, 80), new Coordinate(360, 160)]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = 1_000
        }).Transform(source);

        Assert.All(output.Coordinates, coordinate =>
        {
            Assert.True(double.IsFinite(coordinate.X));
            Assert.True(double.IsFinite(coordinate.Y));
        });
    }

    [Fact]
    public void WebMercatorOutput_DoesNotChangeGeneratedWorld()
    {
        var session = MapGenerationSession.Create(
            new MapMask(6, 4, new HashSet<GridPoint>
            {
                new(1, 1), new(2, 1), new(3, 1),
                new(1, 2), new(2, 2), new(3, 2)
            }),
            new MapGenerationOptions { Seed = 20260829 });
        session.RunUntil(MapDataKeys.Landmasses);
        var map = session.CurrentMap;
        var canonical = map.Landmasses
            .SelectMany(landmass => landmass.Shape.Coordinates)
            .Select(coordinate => new Coordinate(coordinate))
            .ToArray();

        _ = GeoJsonMapWriter.WriteLandmasses(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Clip,
            ProjectionErrorTolerance = 10_000
        });

        var after = map.Landmasses.SelectMany(landmass => landmass.Shape.Coordinates).ToArray();
        Assert.Equal(canonical, after);
        Assert.False(session.IsDirty(MapDataKeys.Landmasses));
    }

    [Fact]
    public void ProjectedLongSegment_IsDensifiedToTolerance()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(45, 5), new Coordinate(135, 120)]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = 100,
            MaxDensificationDepth = 12,
            MinDensificationSegmentLength = 0
        }).Transform(source);

        Assert.IsType<LineString>(output);
        Assert.True(output.NumPoints > 2);
    }

    [Fact]
    public void WebMercatorGenerationGrid_StraightSegmentStaysStraightInWebMercatorOutput()
    {
        var context = CreateWebMercatorContext(360, 180, -WebLimit, WebLimit, preserveAspect: false);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(40, 20), new Coordinate(140, 150)
        ]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = .001,
            MinDensificationSegmentLength = 0
        }).Transform(source);

        Assert.IsType<LineString>(output);
        Assert.Equal(2, output.NumPoints);
    }

    [Fact]
    public void ProjectedLongSegment_EachOutputSubsegmentMeetsMidpointTolerance()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var firstCanonical = new Coordinate(45, 5);
        var lastCanonical = new Coordinate(135, 120);
        var source = new GeometryFactory().CreateLineString([firstCanonical, lastCanonical]);
        const double tolerance = 100;
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = tolerance,
            MaxDensificationDepth = 12,
            MinDensificationSegmentLength = 0
        }).Transform(source);

        Assert.True(output.NumPoints > 2);
        var outputCoordinates = output.Coordinates;
        var firstProjected = outputCoordinates[0];
        var lastProjected = outputCoordinates[^1];
        for (var index = 0; index < outputCoordinates.Length - 1; index++)
        {
            var start = outputCoordinates[index];
            var end = outputCoordinates[index + 1];
            var startT = (start.X - firstProjected.X) / (lastProjected.X - firstProjected.X);
            var endT = (end.X - firstProjected.X) / (lastProjected.X - firstProjected.X);
            var midpointT = (startT + endT) * .5;
            var canonicalMidpoint = new GridCoordinate(
                firstCanonical.X + (lastCanonical.X - firstCanonical.X) * midpointT,
                firstCanonical.Y + (lastCanonical.Y - firstCanonical.Y) * midpointT);
            var expected = WebMercator3857.Forward(context.GridToGeographic(canonicalMidpoint));
            var actual = new MapPoint((start.X + end.X) * .5, (start.Y + end.Y) * .5);

            Assert.InRange(Distance(expected, actual), 0, tolerance + 1e-6);
        }
    }

    [Fact]
    public void ProjectedSymmetricMercatorCurve_UsesQuarterPointError()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(180, 0), new Coordinate(180, 160)
        ]);
        const double tolerance = 1_000;
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = tolerance,
            MaxDensificationDepth = 12,
            MinDensificationSegmentLength = 0
        }).Transform(source);

        Assert.True(output.NumPoints > 2);
        var outputCoordinates = output.Coordinates;
        var first = WebMercator3857.Inverse(new MapPoint(outputCoordinates[0].X, outputCoordinates[0].Y));
        var last = WebMercator3857.Inverse(new MapPoint(outputCoordinates[^1].X, outputCoordinates[^1].Y));
        var firstGrid = context.GeographicToGrid(first);
        var lastGrid = context.GeographicToGrid(last);
        for (var index = 0; index < outputCoordinates.Length - 1; index++)
        {
            var start = outputCoordinates[index];
            var end = outputCoordinates[index + 1];
            var startGrid = context.GeographicToGrid(WebMercator3857.Inverse(new MapPoint(start.X, start.Y)));
            var endGrid = context.GeographicToGrid(WebMercator3857.Inverse(new MapPoint(end.X, end.Y)));
            var startT = (startGrid.Y - firstGrid.Y) / (lastGrid.Y - firstGrid.Y);
            var endT = (endGrid.Y - firstGrid.Y) / (lastGrid.Y - firstGrid.Y);

            foreach (var parameter in new[] { .25, .5, .75 })
            {
                var t = startT + (endT - startT) * parameter;
                var canonical = new GridCoordinate(
                    firstGrid.X + (lastGrid.X - firstGrid.X) * t,
                    firstGrid.Y + (lastGrid.Y - firstGrid.Y) * t);
                var expected = WebMercator3857.Forward(context.GridToGeographic(canonical));
                var actual = new MapPoint(
                    start.X + (end.X - start.X) * parameter,
                    start.Y + (end.Y - start.Y) * parameter);

                Assert.InRange(Distance(expected, actual), 0, tolerance + 1e-6);
            }
        }
    }

    [Fact]
    public void ProjectedDensification_RejectsUnmetToleranceAtMaximumDepth()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(180, 0), new Coordinate(180, 160)
        ]);
        var transformer = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            ProjectionErrorTolerance = 1,
            MaxDensificationDepth = 0,
            MinDensificationSegmentLength = 0
        });

        var exception = Assert.Throws<InvalidOperationException>(() => transformer.Transform(source));
        Assert.Contains("maximum densification depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectedPolygon_RemainsValid()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreatePolygon([
            new Coordinate(80, 30), new Coordinate(140, 30),
            new Coordinate(140, 100), new Coordinate(80, 100),
            new Coordinate(80, 30)
        ]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            ProjectionErrorTolerance = 100,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject
        }).Transform(source);

        Assert.True(output.IsValid);
        Assert.False(output.IsEmpty);
    }

    [Fact]
    public void ProjectedRiver_RemainsContinuous()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(358, 80), new Coordinate(362, 80), new Coordinate(365, 82)]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            ProjectionErrorTolerance = 1_000
        }).Transform(source);

        var parts = output is MultiLineString multi
            ? multi.Geometries.Cast<LineString>().ToArray()
            : [Assert.IsType<LineString>(output)];
        Assert.NotEmpty(parts);
        Assert.All(parts, part =>
        {
            Assert.True(part.NumPoints >= 2);
            Assert.All(part.Coordinates, coordinate =>
            {
                Assert.True(double.IsFinite(coordinate.X));
                Assert.True(double.IsFinite(coordinate.Y));
            });
        });
    }

    [Fact]
    public void AntimeridianPolygon_IsSplitWithoutWorldSpanningEdge()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var source = new GeometryFactory().CreatePolygon([
            new Coordinate(359, 70), new Coordinate(361, 70),
            new Coordinate(361, 90), new Coordinate(359, 90),
            new Coordinate(359, 70)
        ]);
        var output = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            ProjectionErrorTolerance = 1_000
        }).Transform(source);

        var polygons = output is MultiPolygon multi
            ? multi.Geometries.Cast<Polygon>().ToArray()
            : [Assert.IsType<Polygon>(output)];
        Assert.True(polygons.Length >= 2);
        Assert.All(polygons, polygon =>
        {
            Assert.True(polygon.IsValid);
            Assert.True(polygon.EnvelopeInternal.Width < WebMercator3857.WorldWidthMeters * .25);
        });
    }

    [Fact]
    public void WideRegionalOutput_PreservesDirectedLongitudeBeforeSeamSplit()
    {
        var context = MapSpatialContext.Create(100, 10, new MapSpatialOptions
        {
            Coverage = MapCoverage.Regional(new LongitudeInterval(-170, 300), -20, 20),
            Topology = GridTopologyKind.OpenRectangular
        });
        var source = new GeometryFactory().CreateLineString([
            new Coordinate(0, 5), new Coordinate(90, 5)
        ]);

        var geographic = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        }).Transform(source);
        var projected = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            ProjectionErrorTolerance = 10_000
        }).Transform(source);

        Assert.Equal(100, geographic.Coordinates[^1].X, 10);
        Assert.IsType<LineString>(projected);
        Assert.All(projected.Coordinates, coordinate => Assert.InRange(coordinate.X, -WebMercator3857.HalfWorldMeters, WebMercator3857.HalfWorldMeters));
    }

    [Fact]
    public void WideRegionalPolygon_IsSplitAtOutputSeamWithoutShorteningCoverage()
    {
        var reference = new MapSpatialReference
        {
            GridWidth = 100,
            GridHeight = 10,
            UnitsPerCell = 1,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Regional(new LongitudeInterval(100, 300), -20, 20),
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.OpenRectangular,
            CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits,
            PreserveProjectedCellAspectRatio = false
        };
        var source = new GeometryFactory().CreatePolygon([
            new Coordinate(0, 2), new Coordinate(100, 2),
            new Coordinate(100, 8), new Coordinate(0, 8),
            new Coordinate(0, 2)
        ]);

        var output = new MapCoordinateTransformer(reference, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Split,
            ProjectionErrorTolerance = 100_000
        }).Transform(source);

        var polygons = output is MultiPolygon multi
            ? multi.Geometries.Cast<Polygon>().ToArray()
            : [Assert.IsType<Polygon>(output)];
        Assert.True(polygons.Length >= 2);
        Assert.All(polygons, polygon =>
        {
            Assert.True(polygon.IsValid);
            Assert.True(polygon.EnvelopeInternal.Width < WebMercator3857.WorldWidthMeters * .75);
        });
    }

    [Fact]
    public void WebMercatorOutput_CylindricalSeamInterpolationUsesMapUnitsPerCell()
    {
        const int width = 4;
        const int height = 4;
        const double unitsPerCell = 2;
        var reference = new MapSpatialReference
        {
            GridWidth = width,
            GridHeight = height,
            UnitsPerCell = unitsPerCell,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Global(-WebLimit, WebLimit),
            GridMapping = GridMappingKind.WebMercator,
            Topology = GridTopologyKind.CylindricalX,
            CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits,
            PreserveProjectedCellAspectRatio = false
        };
        var source = new GeometryFactory().CreateLineString([
            // 3.5 -> 0.5 grid cells is a one-cell cylindrical seam edge.
            new Coordinate(3.5 * unitsPerCell, 2 * unitsPerCell),
            new Coordinate(.5 * unitsPerCell, 2 * unitsPerCell)
        ]);

        var output = new MapCoordinateTransformer(reference, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = .001,
            MinDensificationSegmentLength = 0
        }).Transform(source);

        var line = Assert.IsType<LineString>(output);
        // A map-unit/cell-unit mismatch would put the midpoint at a false
        // world-scale longitude and trigger needless densification.
        Assert.Equal(2, line.NumPoints);
        Assert.Equal(180, WebMercator3857.Inverse(new MapPoint(
            (line.GetCoordinateN(0).X + line.GetCoordinateN(1).X) * .5,
            line.GetCoordinateN(0).Y)).LongitudeDegrees, 10);
    }

    [Fact]
    public void Projection_DoesNotMutateCanonicalGeometry()
    {
        var context = CreateEquirectangularContext(360, 160, -80, 80);
        var factory = new GeometryFactory();
        var source = factory.CreateGeometryCollection([
            factory.CreatePoint(new Coordinate(20, 20)),
            factory.CreateLineString([new Coordinate(20, 20), new Coordinate(40, 40)])
        ]);
        var snapshot = source.Coordinates.Select(coordinate => new Coordinate(coordinate)).ToArray();
        ICoordinateTransformer transformer = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            ProjectionErrorTolerance = 1_000
        });

        var output = transformer.Transform(source);

        Assert.NotSame(source, output);
        Assert.Equal(snapshot, source.Coordinates);
    }

    [Theory]
    [InlineData(AntimeridianOutputPolicy.Auto)]
    [InlineData(AntimeridianOutputPolicy.Split)]
    public void WebMercator_PointOutputNormalizesToOneWorldCopy(AntimeridianOutputPolicy policy)
    {
        var transformer = new MapCoordinateTransformer(
            CreateEquirectangularContext(360, 180, -80, 80),
            new MapOutputOptions
            {
                CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
                AntimeridianPolicy = policy
            });
        var point = new GeometryFactory().CreatePoint(new Coordinate(370, 90));

        var output = Assert.IsType<Point>(transformer.Transform(point));
        var expected = WebMercator3857.Forward(new GeoCoordinate(-170, 0));

        Assert.Equal(expected.X, output.X, 6);
        Assert.Equal(expected.Y, output.Y, 10);
    }

    [Theory]
    [InlineData(AntimeridianOutputPolicy.Auto)]
    [InlineData(AntimeridianOutputPolicy.Split)]
    public void WebMercator_MultiPointOutputNormalizesEachPoint(AntimeridianOutputPolicy policy)
    {
        var transformer = new MapCoordinateTransformer(
            CreateEquirectangularContext(360, 180, -80, 80),
            new MapOutputOptions
            {
                CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
                AntimeridianPolicy = policy
            });
        var factory = new GeometryFactory();
        var source = factory.CreateMultiPoint([
            factory.CreatePoint(new Coordinate(370, 90)),
            factory.CreatePoint(new Coordinate(10, 90))
        ]);

        var output = Assert.IsType<MultiPoint>(transformer.Transform(source));
        var expected = WebMercator3857.Forward(new GeoCoordinate(-170, 0));

        Assert.Equal(2, output.NumGeometries);
        Assert.All(output.Geometries.Cast<Point>(), point =>
        {
            Assert.Equal(expected.X, point.X, 6);
            Assert.Equal(expected.Y, point.Y, 10);
        });
    }

    [Theory]
    [InlineData(AntimeridianOutputPolicy.Auto)]
    [InlineData(AntimeridianOutputPolicy.Split)]
    public void WebMercator_DirectMapPointOutputNormalizesToOneWorldCopy(AntimeridianOutputPolicy policy)
    {
        var transformer = new MapCoordinateTransformer(
            CreateEquirectangularContext(360, 180, -80, 80),
            new MapOutputOptions
            {
                CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
                AntimeridianPolicy = policy
            });
        var output = transformer.Transform(new MapPoint(370, 90));
        var expected = WebMercator3857.Forward(new GeoCoordinate(-170, 0));

        Assert.Equal(expected.X, output.X, 6);
        Assert.Equal(expected.Y, output.Y, 10);
    }

    [Fact]
    public void GeographicPoint_SplitNormalizesWhileAutoAndUnwrapPreserveLongitude()
    {
        var context = CreateEquirectangularContext(360, 180, -80, 80);
        var canonicalPoint = new MapPoint(370, 90);

        var auto = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude,
            AntimeridianPolicy = AntimeridianOutputPolicy.Auto
        }).Transform(canonicalPoint);
        var unwrap = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap
        }).Transform(canonicalPoint);
        var split = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude,
            AntimeridianPolicy = AntimeridianOutputPolicy.Split
        }).Transform(canonicalPoint);

        Assert.Equal(190, auto.X, 10);
        Assert.Equal(190, unwrap.X, 10);
        Assert.Equal(-170, split.X, 10);
        Assert.Equal(auto.Y, split.Y, 10);
    }

    [Fact]
    public void WebMercatorGrid_EquatorialRowMapsToZeroLatitude()
    {
        var context = CreateWebMercatorContext(360, 180, -WebLimit, WebLimit, preserveAspect: false);

        var geographic = context.GridToGeographic(new GridCoordinate(180.5, 90));

        Assert.Equal(0, geographic.LatitudeDegrees, 10);
    }

    [Fact]
    public void WebMercatorGrid_LatitudeSpacingIsNonLinear()
    {
        var context = CreateWebMercatorContext(360, 180, -WebLimit, WebLimit, preserveAspect: false);
        var top = context.GridToGeographic(new GridCoordinate(0.5, 0.5)).LatitudeDegrees;
        var next = context.GridToGeographic(new GridCoordinate(0.5, 1.5)).LatitudeDegrees;
        var equator = context.GridToGeographic(new GridCoordinate(0.5, 89.5)).LatitudeDegrees;
        var nextEquator = context.GridToGeographic(new GridCoordinate(0.5, 90.5)).LatitudeDegrees;

        Assert.True(Math.Abs(top - next) < Math.Abs(equator - nextEquator));
    }

    [Fact]
    public void WebMercatorGrid_TopAndBottomEdgesMatchConfiguredLatitudeBounds()
    {
        const double south = -72;
        const double north = 81;
        var context = CreateWebMercatorContext(360, 180, south, north, preserveAspect: false);

        var topEdge = context.GridToGeographic(new GridCoordinate(.5, 0));
        var bottomEdge = context.GridToGeographic(new GridCoordinate(.5, 180));

        Assert.Equal(north, topEdge.LatitudeDegrees, 10);
        Assert.Equal(south, bottomEdge.LatitudeDegrees, 10);
    }

    [Fact]
    public void WebMercatorGrid_ForwardInverseRoundTrip()
    {
        var context = CreateWebMercatorContext(360, 180, -WebLimit, WebLimit, preserveAspect: false);
        var grid = new GridCoordinate(212.25, 37.75);
        var geographic = context.GridToGeographic(grid);

        var roundTrip = context.GeographicToGrid(geographic);

        Assert.Equal(grid.X, roundTrip.X, 9);
        Assert.Equal(grid.Y, roundTrip.Y, 9);
    }

    [Fact]
    public void WebMercatorGrid_ProjectedCellsAreSquareWhenRequired()
    {
        var options = new MapSpatialOptions
        {
            GridMapping = GridMappingKind.WebMercator,
            Coverage = MapCoverage.Global(-WebLimit, WebLimit),
            Topology = GridTopologyKind.CylindricalX,
            PreserveProjectedCellAspectRatio = true
        };
        var reference = MapSpatialContext.Create(360, 360, options).SpatialReference;

        Assert.Equal(1, WebMercatorGridMapping.ProjectedCellAspectRatio(reference), 6);
    }

    [Fact]
    public void WebMercatorGrid_InvalidAspectRatioIsRejectedWhenLocked()
    {
        var options = new MapSpatialOptions
        {
            GridMapping = GridMappingKind.WebMercator,
            Coverage = MapCoverage.Global(-WebLimit, WebLimit),
            Topology = GridTopologyKind.CylindricalX,
            PreserveProjectedCellAspectRatio = true
        };

        Assert.Throws<ArgumentException>(() => MapSpatialContext.Create(360, 180, options));
    }

    [Fact]
    public void Climate_WebMercatorGridUsesInverseProjectedLatitude()
    {
        const int width = 8;
        const int height = 6;
        var land = new HashSet<GridPoint>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                land.Add(new GridPoint(x, y));
        }

        var session = MapGenerationSession.Create(new MapMask(width, height, land), new MapGenerationOptions
        {
            Seed = 20260829,
            Spatial = new MapSpatialOptions
            {
                GridMapping = GridMappingKind.WebMercator,
                Coverage = MapCoverage.Global(-70, 70),
                Topology = GridTopologyKind.CylindricalX,
                PreserveProjectedCellAspectRatio = false
            }
        });
        session.RunUntil(MapDataKeys.Climate);

        var expected = Math.Abs(session.SpatialContext.GridToGeographic(new GridCoordinate(.5, .5)).LatitudeDegrees / 90);

        Assert.Equal(expected, session.Climate!.GetLatitudeNorm(0, 0), 10);
        Assert.NotEqual(expected, Math.Abs((1 - 2 * (.5 / height))), 3);
    }

    [Fact]
    public void ProjectedCirclePreservesAspectInSquareWebMercatorGrid()
    {
        const double radius = 80;
        var context = CreateWebMercatorContext(360, 360, -WebLimit, WebLimit, preserveAspect: true);
        var factory = new GeometryFactory();
        var center = new Coordinate(180, 180);
        var points = Enumerable.Range(0, 33)
            .Select(index =>
            {
                var angle = index * Math.PI * 2 / 32;
                return new Coordinate(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            })
            .ToArray();
        points[^1] = new Coordinate(points[0]);
        var source = factory.CreatePolygon(points);

        var projected = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            AntimeridianPolicy = AntimeridianOutputPolicy.Unwrap,
            ProjectionErrorTolerance = 10_000
        }).Transform(source);
        var envelope = projected.EnvelopeInternal;

        Assert.InRange(envelope.Width / envelope.Height, .97, 1.03);
    }

    [Fact]
    public void RiverJsonWriter_WritesWebMercatorPolylineAndOutputMetadata()
    {
        var reference = new MapSpatialReference
        {
            GridWidth = 3,
            GridHeight = 2,
            UnitsPerCell = 2,
            WorldModel = WorldModelDescriptor.Spherical(6371),
            Coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), -10, 10),
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.OpenRectangular,
            CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits
        };
        var hydrology = CreateHydrology();
        var map = new GeneratedMap(new MapBounds(6, 4, 2), [], [], [], WorldContextMode.Isolated, Hydrology: hydrology, SpatialReference: reference);

        var json = RiverJsonWriter.Write(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            ProjectionErrorTolerance = 100
        }, new RiverJsonExportOptions { WriteIndented = false });
        var document = JsonNode.Parse(json)!.AsObject();
        var spatial = document["spatialReference"]!.AsObject();
        var coverage = spatial["coverage"]!.AsObject();
        var polyline = document["Rivers"]![0]!["Polyline"]!.AsArray();
        var polylineParts = document["Rivers"]![0]!["PolylineParts"];

        Assert.Equal("WebMercator3857", spatial["projection"]!.GetValue<string>());
        Assert.Equal("WebMercator3857", spatial["outputCoordinates"]!.GetValue<string>());
        Assert.Equal("Regional", coverage["kind"]!.GetValue<string>());
        Assert.Equal(170, coverage["longitudeStart"]!.GetValue<double>());
        Assert.Equal(20, coverage["longitudeSpan"]!.GetValue<double>());
        Assert.True(polyline[0]!["X"]!.GetValue<double>() > 1_000_000);
        Assert.NotNull(polylineParts);
        Assert.True(polylineParts!.AsArray().Count >= 2);
    }

    [Fact]
    public void GeoJsonMapWriter_WritesWebMercatorGeometryAndOutputMetadata()
    {
        var reference = new MapSpatialReference
        {
            GridWidth = 4,
            GridHeight = 4,
            UnitsPerCell = 1,
            WorldModel = WorldModelDescriptor.Spherical(),
            Coverage = MapCoverage.Regional(new LongitudeInterval(10, 20), -10, 10),
            GridMapping = GridMappingKind.Equirectangular,
            Topology = GridTopologyKind.OpenRectangular
        };
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), factory.CreatePolygon([
            new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(4, 4),
            new Coordinate(0, 4), new Coordinate(0, 0)
        ]));
        var map = new GeneratedMap(new MapBounds(4, 4, 1), [landmass], [], [], WorldContextMode.Isolated, SpatialReference: reference);

        var document = JsonNode.Parse(GeoJsonMapWriter.WriteLandmasses(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.WebMercator3857,
            LatitudeOverflowPolicy = LatitudeOverflowPolicy.Reject,
            ProjectionErrorTolerance = 10
        }))!.AsObject();
        var spatial = document["spatialReference"]!.AsObject();
        var coordinates = document["features"]![0]!["geometry"]!["coordinates"]!
            .AsArray()[0]![0]!.AsArray();

        Assert.Equal("WebMercator3857", spatial["projection"]!.GetValue<string>());
        Assert.Equal("Reject", spatial["latitudeOverflowPolicy"]!.GetValue<string>());
        Assert.Equal("Split", spatial["effectiveAntimeridianPolicy"]!.GetValue<string>());
        Assert.True(Math.Abs(coordinates[0]!.GetValue<double>()) > 1_000_000);
        Assert.True(double.IsFinite(coordinates[0]!.GetValue<double>()));
    }

    private static MapSpatialContext CreateEquirectangularContext(int width, int height, double south, double north) =>
        MapSpatialContext.Create(width, height, new MapSpatialOptions
        {
            Coverage = MapCoverage.Global(south, north),
            Topology = GridTopologyKind.CylindricalX
        });

    private static MapSpatialContext CreateWebMercatorContext(int width, int height, double south, double north, bool preserveAspect) =>
        MapSpatialContext.Create(width, height, new MapSpatialOptions
        {
            GridMapping = GridMappingKind.WebMercator,
            Coverage = MapCoverage.Global(south, north),
            Topology = GridTopologyKind.CylindricalX,
            PreserveProjectedCellAspectRatio = preserveAspect
        });

    private static double Distance(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static HydrologyMap CreateHydrology()
    {
        var river = new RiverSegment(
            1,
            [new GridPoint(0, 0), new GridPoint(1, 0)],
            [new MapPoint(.5, .5), new MapPoint(2.5, .5)],
            new GridPoint(0, 0),
            new GridPoint(1, 0),
            new GridPoint(1, 0),
            null,
            DrainageTargetKind.Ocean,
            null,
            10,
            2,
            1,
            RiverKind.Plain);
        return new HydrologyMap(
            3,
            2,
            new double[6],
            [-1, -1, -1, -1, -1, -1],
            new double[6],
            new int[6],
            new byte[6],
            [river],
            [new RiverMouth(1, new GridPoint(1, 0), DrainageTargetKind.Ocean, null, RiverMouthKind.SimpleMouth, 10)],
            [],
            []);
    }
}

#pragma warning restore CA1707
