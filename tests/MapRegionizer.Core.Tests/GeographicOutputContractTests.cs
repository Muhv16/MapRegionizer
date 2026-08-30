using System.Text.Json.Nodes;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class GeographicOutputContractTests
{
    [Fact]
    public void GeographicGeometryRoundTripsToCanonicalGridMapUnitsAndPreservesYOrientation()
    {
        var options = new MapSpatialOptions
        {
            UnitsPerCell = 2,
            Coverage = MapCoverage.Regional(new LongitudeInterval(170, 20), -10, 10),
            Topology = GridTopologyKind.OpenRectangular
        };
        var context = MapSpatialContext.Create(4, 2, options);
        var source = new GeometryFactory().CreatePolygon([
            new Coordinate(0, 0), new Coordinate(8, 0), new Coordinate(8, 4),
            new Coordinate(0, 4), new Coordinate(0, 0)
        ]);
        var transformed = new MapCoordinateTransformer(context, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        }).Transform(source);

        foreach (var coordinate in transformed.Coordinates)
        {
            var grid = context.GeographicToGrid(new GeoCoordinate(coordinate.X, coordinate.Y));
            var canonical = new MapPoint(
                grid.X * context.SpatialReference.UnitsPerCell,
                grid.Y * context.SpatialReference.UnitsPerCell);
            var original = source.Coordinates[Array.IndexOf(transformed.Coordinates, coordinate)];
            Assert.Equal(original.X, canonical.X, 9);
            Assert.Equal(original.Y, canonical.Y, 9);
        }

        Assert.True(transformed.Coordinates[0].Y > transformed.Coordinates[2].Y);
    }

    [Fact]
    public void RiverJsonWritesGeographicPolylineAndCompleteCoverageMetadataWithoutMutation()
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
        var sourcePolyline = hydrology.Rivers[0].Polyline.ToArray();
        var sourceSurface = hydrology.HydroSurfaceMetersSpan.ToArray();
        var map = new GeneratedMap(new MapBounds(6, 4, 2), [], [], [], WorldContextMode.Isolated, Hydrology: hydrology, SpatialReference: reference);

        var json = RiverJsonWriter.Write(map, new MapOutputOptions
        {
            CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude
        }, new RiverJsonExportOptions { WriteIndented = false });
        var document = JsonNode.Parse(json)!.AsObject();
        var riverPoint = document["Rivers"]![0]!["Polyline"]![1]!.AsObject();
        var expected = MapSpatialContext.Create(reference).GridToGeographic(new GridCoordinate(1.12345, .5));

        Assert.Equal(Math.Round(expected.LongitudeDegrees, 3), riverPoint["X"]!.GetValue<double>(), 3);
        Assert.Equal(Math.Round(expected.LatitudeDegrees, 3), riverPoint["Y"]!.GetValue<double>(), 3);
        Assert.InRange(Math.Abs(expected.LongitudeDegrees - riverPoint["X"]!.GetValue<double>()), 0, .0005001);
        Assert.InRange(Math.Abs(expected.LatitudeDegrees - riverPoint["Y"]!.GetValue<double>()), 0, .0005001);

        var spatial = document["spatialReference"]!.AsObject();
        var coverage = spatial["coverage"]!.AsObject();
        Assert.Equal("Spherical", spatial["worldModel"]!["kind"]!.GetValue<string>());
        Assert.Equal(6371, spatial["worldModel"]!["planetRadius"]!.GetValue<double>());
        Assert.Equal("Regional", coverage["kind"]!.GetValue<string>());
        Assert.Equal(-10, coverage["southLatitude"]!.GetValue<double>());
        Assert.Equal(10, coverage["northLatitude"]!.GetValue<double>());
        Assert.Equal(170, coverage["longitudeStart"]!.GetValue<double>());
        Assert.Equal(20, coverage["longitudeSpan"]!.GetValue<double>());
        Assert.Equal(190, coverage["longitudeEnd"]!.GetValue<double>());
        Assert.True(coverage["crossesAntimeridian"]!.GetValue<bool>());
        Assert.Equal("None", spatial["legacyCompatibility"]!.GetValue<string>());
        Assert.Equal("GeographicLongitudeLatitude", spatial["outputCoordinates"]!.GetValue<string>());

        Assert.Equal(sourcePolyline, hydrology.Rivers[0].Polyline);
        Assert.Equal(sourceSurface, hydrology.HydroSurfaceMetersSpan.ToArray());
    }

    private static HydrologyMap CreateHydrology()
    {
        var river = new RiverSegment(
            1,
            [new GridPoint(0, 0), new GridPoint(1, 0)],
            [new MapPoint(.5, .5), new MapPoint(1.12345, .5)],
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
