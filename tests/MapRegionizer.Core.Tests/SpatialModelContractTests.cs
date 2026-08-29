using System.Text.Json.Nodes;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class SpatialModelContractTests
{
    [Fact]
    public void DefaultSpatialOptionsPreserveLegacyConfiguration()
    {
        var options = new MapGenerationOptions { Seed = 7 };
        var spatial = options.EffectiveSpatial;

        Assert.Equal(GridMappingKind.Equirectangular, spatial.GridMapping);
        Assert.Equal(GridTopologyKind.CylindricalX, spatial.Topology);
        Assert.Equal(MapCoverageKind.Global, spatial.Coverage.Kind);
        Assert.Equal(360, spatial.Coverage.Longitude.SpanDegrees);
        Assert.Equal(1.0, spatial.UnitsPerCell);
        Assert.Equal(LegacyCompatibilityProfile.EquirectangularWorld, spatial.LegacyCompatibility);

        var session = MapGenerationSession.Create(new MapMask(3, 2, new HashSet<GridPoint>()), options);
        Assert.Equal(session.SpatialReference, session.CurrentMap.SpatialReference);
        Assert.Equal(session.SpatialReference.UnitsPerCell, session.CurrentMap.Bounds.UnitsPerCell);
    }

    [Fact]
    public void PixelSizeMapsToUnitsPerCell()
    {
#pragma warning disable CS0618
        var options = new MapGenerationOptions { PixelSize = 2.5 };
        var bounds = new MapBounds(10, 20, 2.5);
#pragma warning restore CS0618

        Assert.Equal(2.5, options.EffectiveSpatial.UnitsPerCell);
#pragma warning disable CS0618
        Assert.Equal(bounds.UnitsPerCell, bounds.PixelSize);
#pragma warning restore CS0618
    }

    [Fact]
    public void InvalidLatitudeRangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapCoverage.Regional(0, 10, -91, 20));
        Assert.Throws<ArgumentException>(() => MapCoverage.Regional(0, 10, 20, 10));
    }

    [Fact]
    public void InvalidLongitudeSpanIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LongitudeInterval(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LongitudeInterval(0, 361));
    }

    [Fact]
    public void SpatialReferenceValidationIsNullAndEnumSafe()
    {
        var reference = CreateReference();

        Assert.Throws<ArgumentNullException>(() => (reference with { WorldModel = null! }).Validate());
        Assert.Throws<ArgumentNullException>(() => (reference with { Coverage = null! }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (reference with { GridMapping = (GridMappingKind)999 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (reference with { Topology = (GridTopologyKind)999 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (reference with { CanonicalCoordinates = (CoordinateSpaceKind)999 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (reference with { LegacyCompatibility = (LegacyCompatibilityProfile)999 }).Validate());

        var webMercator = reference with
        {
            GridMapping = GridMappingKind.WebMercator,
            Coverage = MapCoverage.Global()
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => webMercator.Validate());
    }

    [Fact]
    public void SpatialReferenceIsStableForSameInput()
    {
        var options = new MapSpatialOptions
        {
            UnitsPerCell = 3,
            Coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), 30, 55),
            Topology = GridTopologyKind.OpenRectangular,
            LegacyCompatibility = LegacyCompatibilityProfile.None
        };

        var first = MapSpatialContext.Create(40, 25, options).SpatialReference;
        var second = MapSpatialContext.Create(40, 25, options).SpatialReference;

        Assert.Equal(first, second);
    }

    [Fact]
    public void CylindricalTopologyPreservesLegacyCardinalOrderAndSeam()
    {
        var topology = new CylindricalXTopology(4, 3);
        var neighbors = topology.GetNeighbors4(new GridPoint(0, 1)).ToArray();

        Assert.Equal(
            new[] { new GridPoint(3, 1), new GridPoint(1, 1), new GridPoint(0, 0), new GridPoint(0, 2) },
            neighbors);
        Assert.True(topology.TryResolve(new GridPoint(0, 1), -1, 0, out var left));
        Assert.Equal(new GridPoint(3, 1), left);
        Assert.True(topology.TryResolve(new GridPoint(3, 1), 1, 0, out var right));
        Assert.Equal(new GridPoint(0, 1), right);
        Assert.False(topology.TryResolve(new GridPoint(0, 0), 0, -1, out _));
        Assert.False(topology.TryResolve(new GridPoint(0, 2), 0, 1, out _));
    }

    [Fact]
    public void OpenTopologyRejectsHorizontalAndVerticalOutsideDomain()
    {
        var topology = new OpenRectangularTopology(4, 3);

        Assert.False(topology.TryResolve(new GridPoint(0, 1), -1, 0, out _));
        Assert.False(topology.TryResolve(new GridPoint(3, 1), 1, 0, out _));
        Assert.False(topology.TryResolve(new GridPoint(1, 0), 0, -1, out _));
        Assert.False(topology.TryResolve(new GridPoint(1, 2), 0, 1, out _));
    }

    [Fact]
    public void EquirectangularCellCenterMappingIsCorrect()
    {
        var context = MapSpatialContext.Create(360, 180, new MapSpatialOptions());

        AssertGeo(context.GridToGeographic(new GridCoordinate(.5, .5)), -179.5, 89.5);
        AssertGeo(context.GridToGeographic(new GridCoordinate(359.5, 179.5)), 179.5, -89.5);
    }

    [Fact]
    public void EquirectangularGridEdgesMapToCoverageEdges()
    {
        var context = MapSpatialContext.Create(360, 180, new MapSpatialOptions());

        AssertGeo(context.GridToGeographic(new GridCoordinate(0, 0)), -180, 90);
        AssertGeo(context.GridToGeographic(new GridCoordinate(360, 180)), 180, -90);
    }

    [Fact]
    public void EquirectangularGridGeoGridRoundTrip()
    {
        var context = MapSpatialContext.Create(360, 180, new MapSpatialOptions());
        var points = new[]
        {
            new GridCoordinate(0, 0),
            new GridCoordinate(360, 180),
            new GridCoordinate(180.25, 90.75),
            new GridCoordinate(.5, .5),
            new GridCoordinate(359.5, 179.5)
        };

        foreach (var point in points)
        {
            var geographic = context.GridToGeographic(point);
            var roundTrip = context.GeographicToGrid(geographic);
            Assert.Equal(point.X, roundTrip.X, 9);
            Assert.Equal(point.Y, roundTrip.Y, 9);
        }
    }

    [Fact]
    public void RegionalCoverageUsesGeographicLatitudeInsteadOfLocalRow()
    {
        var options = new MapSpatialOptions
        {
            Coverage = MapCoverage.Regional(new LongitudeInterval(10, 20), 30, 50),
            Topology = GridTopologyKind.OpenRectangular
        };
        var context = MapSpatialContext.Create(20, 10, options);
        var coordinate = context.GridToGeographic(new GridCoordinate(.5, .5));

        Assert.Equal(10.5, coordinate.LongitudeDegrees, 10);
        Assert.Equal(49, coordinate.LatitudeDegrees, 10);
        Assert.NotEqual(.95, Math.Abs(coordinate.LatitudeDegrees / 90.0), 3);
    }

    [Fact]
    public void ClimateUsesSpatialLatitudeInsteadOfLocalRasterRow()
    {
        const int width = 16;
        const int height = 10;
        var land = new HashSet<GridPoint>();
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
                land.Add(new GridPoint(x, y));
        }

        var options = new MapGenerationOptions
        {
            Seed = 87,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(20, 40), 30, 50),
                Topology = GridTopologyKind.OpenRectangular
            }
        };
        var session = MapGenerationSession.Create(new MapMask(width, height, land), options);
        session.RunUntil(MapDataKeys.Climate);

        var expected = Math.Abs(session.SpatialContext.GridToGeographic(new GridCoordinate(.5, .5)).LatitudeDegrees / 90.0);
        Assert.Equal(expected, session.Climate!.GetLatitudeNorm(0, 0), 10);
        Assert.NotEqual(Math.Abs(1.0 - 2.0 * (.5 / height)), session.Climate.GetLatitudeNorm(0, 0), 3);
        Assert.True(session.Climate.GetLatitudeNorm(0, 0) > session.Climate.GetLatitudeNorm(0, height - 1));
    }

    [Fact]
    public void GeographicOutputDoesNotMutateSourceAndHasCoverageBounds()
    {
        var options = new MapSpatialOptions
        {
            UnitsPerCell = 2,
            Coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), -10, 10),
            Topology = GridTopologyKind.OpenRectangular
        };
        var context = MapSpatialContext.Create(4, 2, options);
        var factory = new GeometryFactory();
        var source = factory.CreatePolygon([
            new Coordinate(0, 0), new Coordinate(8, 0), new Coordinate(8, 4),
            new Coordinate(0, 4), new Coordinate(0, 0)
        ]);
        var original = source.Coordinates.Select(c => new Coordinate(c)).ToArray();

        ICoordinateTransformer transformer = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        });
        var transformed = transformer.Transform(source);

        Assert.Equal(original, source.Coordinates);
        Assert.Equal(170, transformed.EnvelopeInternal.MinX, 10);
        Assert.Equal(190, transformed.EnvelopeInternal.MaxX, 10);
        Assert.Equal(-10, transformed.EnvelopeInternal.MinY, 10);
        Assert.Equal(10, transformed.EnvelopeInternal.MaxY, 10);
        Assert.NotSame(source, transformed);
    }

    [Fact]
    public void MapBoundsRetainsPixelSizeNamedArgumentAndCanonicalUnitsProperty()
    {
        var bounds = new MapBounds(10, 20, PixelSize: 2);

        Assert.Equal(2, bounds.UnitsPerCell);
#pragma warning disable CS0618
        Assert.Equal(2, bounds.PixelSize);
        var changed = bounds with { PixelSize = 3 };
        Assert.Equal(3, changed.PixelSize);
#pragma warning restore CS0618
        Assert.Equal(3, changed.UnitsPerCell);
    }

    [Fact]
    public void WideAndAntimeridianCoveragesPreserveDirectedLongitudeRoundTrips()
    {
        AssertDirectedLongitudeRoundTrip(GridMappingKind.Equirectangular, new LongitudeInterval(-170, 300), new GridCoordinate(270, 20), 100);
        AssertDirectedLongitudeRoundTrip(GridMappingKind.Equirectangular, new LongitudeInterval(170, 40), new GridCoordinate(225, 20), 200);
        AssertDirectedLongitudeRoundTrip(GridMappingKind.WebMercator, new LongitudeInterval(-170, 300), new GridCoordinate(270, 20), 100);
        AssertDirectedLongitudeRoundTrip(GridMappingKind.WebMercator, new LongitudeInterval(170, 40), new GridCoordinate(225, 20), 200);
    }

    [Fact]
    public void WebMercatorOutputIsExplicitlyRejectedUntilProjectionMilestone()
    {
        var transformer = new MapCoordinateTransformer(
            MapSpatialContext.Create(4, 2, new MapSpatialOptions()),
            new MapOutputOptions { CoordinateSystem = OutputCoordinateSystem.WebMercator });

        Assert.Throws<NotSupportedException>(() => transformer.Transform(new MapPoint(1, 1)));
    }

    [Fact]
    public void GeoJsonGeographicOutputIncludesSpatialReferenceMetadata()
    {
        var reference = CreateReference() with { GridWidth = 4, GridHeight = 2 };
        var factory = new GeometryFactory();
        var landmass = new Landmass(new LandmassId(1), factory.CreatePolygon([
            new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(4, 2),
            new Coordinate(0, 2), new Coordinate(0, 0)
        ]));
        var map = new GeneratedMap(new MapBounds(4, 2, 1), [landmass], [], [], SpatialReference: reference);

        var document = JsonNode.Parse(GeoJsonMapWriter.WriteLandmasses(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        }))!.AsObject();
        var spatial = document["spatialReference"]!.AsObject();

        Assert.Equal("GeographicLongitudeLatitude", spatial["outputCoordinates"]!.GetValue<string>());
        Assert.Equal("Global", spatial["coverage"]!["kind"]!.GetValue<string>());
        Assert.Equal(360, spatial["coverage"]!["longitudeSpan"]!.GetValue<double>());
        Assert.Equal("Equirectangular", spatial["gridMapping"]!.GetValue<string>());
        Assert.Equal("CylindricalX", spatial["topology"]!.GetValue<string>());
        Assert.Equal("None", spatial["legacyCompatibility"]!.GetValue<string>());
    }

    [Fact]
    public void OutputSerializationDoesNotDirtyGenerationStages()
    {
        var session = MapGenerationSession.Create(
            new MapMask(4, 3, new HashSet<GridPoint> { new(1, 1), new(2, 1), new(1, 2), new(2, 2) }),
            new MapGenerationOptions { Seed = 19 });
        session.RunUntil(MapDataKeys.Landmasses);
        var map = session.CurrentMap;

        _ = GeoJsonMapWriter.WriteLandmasses(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        });

        Assert.False(session.IsDirty(MapDataKeys.Landmasses));
        Assert.True(session.IsAvailable(MapDataKeys.SpatialContext));
    }

    [Fact]
    public void SpatialConfigurationUpdateDirtiesConsumersButKeepsInitialSpatialInputAvailable()
    {
        var session = MapGenerationSession.Create(
            new MapMask(4, 3, new HashSet<GridPoint> { new(1, 1), new(2, 1), new(1, 2), new(2, 2) }),
            new MapGenerationOptions { Seed = 19 });
        session.RunUntil(MapDataKeys.TectonicHistory);

        var changed = new MapGenerationOptions
        {
            Seed = 19,
            Spatial = session.Options.Spatial with
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(20, 30), 15, 45),
                Topology = GridTopologyKind.OpenRectangular,
                LegacyCompatibility = LegacyCompatibilityProfile.None
            }
        };
        session.UpdateOptions(changed, []);

        Assert.False(session.IsDirty(MapDataKeys.SpatialContext));
        Assert.True(session.IsDirty(MapDataKeys.Landmasses));
        Assert.True(session.IsDirty(MapDataKeys.TectonicHistory));
        Assert.True(session.IsAvailable(MapDataKeys.Mask));
    }

    private static MapSpatialReference CreateReference() => new()
    {
        GridWidth = 4,
        GridHeight = 2,
        UnitsPerCell = 1,
        WorldModel = WorldModelDescriptor.Spherical(),
        Coverage = MapCoverage.Global(),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.CylindricalX,
        CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits
    };

    private static void AssertGeo(GeoCoordinate actual, double longitude, double latitude)
    {
        Assert.Equal(longitude, actual.LongitudeDegrees, 10);
        Assert.Equal(latitude, actual.LatitudeDegrees, 10);
    }

    private static void AssertDirectedLongitudeRoundTrip(
        GridMappingKind mapping,
        LongitudeInterval longitude,
        GridCoordinate gridPoint,
        double expectedLongitude)
    {
        var options = new MapSpatialOptions
        {
            Coverage = MapCoverage.Regional(longitude, -40, 40),
            GridMapping = mapping,
            Topology = GridTopologyKind.OpenRectangular
        };
        var context = MapSpatialContext.Create(300, 80, options);
        var geographic = context.GridToGeographic(gridPoint);
        var roundTrip = context.GeographicToGrid(geographic);

        Assert.Equal(expectedLongitude, geographic.LongitudeDegrees, 10);
        Assert.Equal(gridPoint.X, roundTrip.X, 9);
        Assert.Equal(gridPoint.Y, roundTrip.Y, 9);
    }
}
