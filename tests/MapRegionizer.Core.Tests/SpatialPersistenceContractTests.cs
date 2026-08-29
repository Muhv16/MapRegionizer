using System.Text.Json.Nodes;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

#pragma warning disable CS0618

namespace MapRegionizer.Core.Tests;

public sealed class SpatialPersistenceContractTests
{
    [Fact]
    public void VersionTwoDraftStoresACompleteCanonicalSpatialDescriptor()
    {
        var mask = FullMask(new GridWindow(0, 0, 4, 3));
        var landmass = new Landmass(new LandmassId(1), Square(0, 0, 4, 3));
        var options = new MapGenerationOptions
        {
            Seed = 11,
            Spatial = new MapSpatialOptions
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(170, 25), 20, 55),
                Topology = GridTopologyKind.OpenRectangular,
                UnitsPerCell = 2
            }
        };
        var document = RegionDraftCompatibility.CreateDocument(
            mask,
            options,
            [landmass],
            new RegionDraft([new RegionDraftRegion(new RegionId(1), landmass.Id, landmass.Shape)]),
            applyBoundaryDistortion: false);

        var json = JsonNode.Parse(RegionDraftGeoJson.Write(document))!.AsObject();
        var spatial = json["spatialReference"]!.AsObject();
        Assert.Equal(RegionDraftDocument.CurrentSchemaVersion, json["schemaVersion"]!.GetValue<string>());
        Assert.NotNull(spatial["worldModel"]);
        Assert.NotNull(spatial["gridMapping"]);
        Assert.NotNull(spatial["topology"]);
        Assert.NotNull(spatial["coverage"]);
        Assert.Equal(4, spatial["gridWidth"]!.GetValue<int>());
        Assert.Equal(3, spatial["gridHeight"]!.GetValue<int>());
        Assert.Equal(2, spatial["unitsPerCell"]!.GetValue<double>());
        Assert.Equal(nameof(CoordinateSpaceKind.GridMapUnits), spatial["canonicalCoordinates"]!.GetValue<string>());

        var reloaded = RegionDraftGeoJson.Read(json.ToJsonString());
        Assert.Equal(document.SpatialReference, reloaded.SpatialReference);
        RegionDraftCompatibility.EnsureCompatible(reloaded, mask, options, [landmass]);
    }

    [Fact]
    public void DescriptorOnlyVersionTwoDraftDoesNotRequireTheLegacyProjectionHint()
    {
        var document = CreateDocument(MapProjectionMode.EquirectangularWorld);
        var json = JsonNode.Parse(RegionDraftGeoJson.Write(document))!.AsObject();
        json.Remove("projectionMode");

        var reloaded = RegionDraftGeoJson.Read(json.ToJsonString());

        Assert.Equal(RegionDraftDocument.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.Equal(document.SpatialReference, reloaded.SpatialReference);
    }

    [Theory]
    [InlineData(MapProjectionMode.EquirectangularWorld, LegacyCompatibilityProfile.EquirectangularWorld, WorldModelKind.Spherical)]
    [InlineData(MapProjectionMode.Flat, LegacyCompatibilityProfile.Flat, WorldModelKind.Planar)]
    [InlineData(MapProjectionMode.Regional, LegacyCompatibilityProfile.Regional, WorldModelKind.Planar)]
    public void VersionOneDraftMigrationPreservesHistoricalSpatialSemantics(
        MapProjectionMode projection,
        LegacyCompatibilityProfile expectedProfile,
        WorldModelKind expectedWorldModel)
    {
        var document = CreateDocument(projection);
        var json = JsonNode.Parse(RegionDraftGeoJson.Write(document))!.AsObject();
        json["schemaVersion"] = "1.0";
        json.Remove("spatialReference");
        json["projectionMode"] = projection.ToString();

        var legacy = RegionDraftGeoJson.Read(json.ToJsonString());
        Assert.Equal("1.0", legacy.SchemaVersion);
        Assert.True(legacy.IsMigratedFromV1);
        Assert.Equal(expectedProfile, legacy.SpatialReference!.LegacyCompatibility);
        Assert.Equal(expectedWorldModel, legacy.SpatialReference.WorldModel.Kind);
        if (projection is MapProjectionMode.Flat or MapProjectionMode.Regional)
            Assert.Equal(GridTopologyKind.CylindricalX, legacy.SpatialReference.Topology);

        var rewritten = JsonNode.Parse(RegionDraftGeoJson.Write(legacy))!.AsObject();
        Assert.Equal(RegionDraftDocument.CurrentSchemaVersion, rewritten["schemaVersion"]!.GetValue<string>());
        Assert.Equal(expectedProfile.ToString(), rewritten["spatialReference"]!["legacyCompatibility"]!.GetValue<string>());
    }

    [Fact]
    public void ChangingOutputCoordinateSystemDoesNotChangeDraftCompatibility()
    {
        var document = CreateDocument(MapProjectionMode.EquirectangularWorld);
        var mask = FullMask(new GridWindow(0, 0, 2, 2));
        var landmass = new Landmass(new LandmassId(1), Square(0, 0, 2, 2));
        var options = new MapGenerationOptions
        {
            Spatial = document.SpatialReference is null
                ? new MapSpatialOptions()
                : new MapSpatialOptions
                {
                    WorldModel = document.SpatialReference.WorldModel,
                    Coverage = document.SpatialReference.Coverage,
                    GridMapping = document.SpatialReference.GridMapping,
                    Topology = document.SpatialReference.Topology,
                    UnitsPerCell = document.SpatialReference.UnitsPerCell,
                    LegacyCompatibility = document.SpatialReference.LegacyCompatibility
                }
        };

        // Output options are intentionally not accepted by EnsureCompatible;
        // both export targets therefore share the same canonical identity.
        _ = new MapOutputOptions { CoordinateSystem = OutputCoordinateSystem.GeographicLongitudeLatitude };
        _ = new MapOutputOptions { CoordinateSystem = OutputCoordinateSystem.WebMercator3857 };
        RegionDraftCompatibility.EnsureCompatible(document, mask, options, [landmass]);
    }

    [Fact]
    public void DifferentCoverageOrCanonicalGridIsRejected()
    {
        var document = CreateDocument(MapProjectionMode.EquirectangularWorld);
        var mask = FullMask(new GridWindow(0, 0, 2, 2));
        var landmass = new Landmass(new LandmassId(1), Square(0, 0, 2, 2));
        var options = new MapGenerationOptions { Seed = 2 };

        var coverageChanged = document with
        {
            SpatialReference = document.SpatialReference! with
            {
                Coverage = MapCoverage.Regional(new LongitudeInterval(10, 20), -20, 20)
            }
        };
        Assert.Throws<InvalidOperationException>(() => RegionDraftCompatibility.EnsureCompatible(coverageChanged, mask, options, [landmass]));

        var gridChanged = document with
        {
            SpatialReference = document.SpatialReference! with { GridWidth = 3 }
        };
        Assert.Throws<InvalidOperationException>(() => RegionDraftCompatibility.EnsureCompatible(gridChanged, mask, options, [landmass]));
    }

    private static RegionDraftDocument CreateDocument(MapProjectionMode projection)
    {
        var mask = FullMask(new GridWindow(0, 0, 2, 2));
        var landmass = new Landmass(new LandmassId(1), Square(0, 0, 2, 2));
        var options = new MapGenerationOptions { ProjectionMode = projection };
        return RegionDraftCompatibility.CreateDocument(
            mask,
            options,
            [landmass],
            new RegionDraft([new RegionDraftRegion(new RegionId(1), landmass.Id, landmass.Shape)]),
            applyBoundaryDistortion: false);
    }

    private static MapMask FullMask(GridWindow window) => new(
        window,
        Enumerable.Range(0, window.Height)
            .SelectMany(y => Enumerable.Range(0, window.Width).Select(x => new GridPoint(x, y)))
            .ToHashSet());

    private static Polygon Square(double x, double y, double width, double height) => new GeometryFactory().CreatePolygon([
        new Coordinate(x, y),
        new Coordinate(x + width, y),
        new Coordinate(x + width, y + height),
        new Coordinate(x, y + height),
        new Coordinate(x, y)
    ]);
}
