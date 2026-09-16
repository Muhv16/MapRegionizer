using System.Text.Json.Nodes;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Options;
using MapRegionizer.GeoJson;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class MapPackageContractTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public void BasicPackageExportStoresSchemaBoundsSpatialReferenceAndIds()
    {
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 6, 4)],
            [SquareRegion(1, 1, 0, 0, 3, 4), SquareRegion(2, 1, 3, 0, 3, 4)]);

        var json = MapPackageWriter.Write(map);
        var document = JsonNode.Parse(json)!.AsObject();
        var spatial = document["spatialReference"]!.AsObject();
        var bounds = document["bounds"]!.AsObject();
        var landmasses = document["landmasses"]!.AsArray();
        var regions = document["regions"]!.AsArray();

        Assert.Equal(MapPackageDocument.CurrentSchemaId, document["schema"]!.GetValue<string>());
        Assert.Equal("MapRegionizer", document["generator"]!["name"]!.GetValue<string>());
        Assert.NotNull(document["generator"]!["version"]);
        Assert.Equal(6, spatial["gridWidth"]!.GetValue<int>());
        Assert.Equal(4, spatial["gridHeight"]!.GetValue<int>());
        Assert.Equal(12, bounds["width"]!.GetValue<double>());
        Assert.Equal(8, bounds["height"]!.GetValue<double>());
        Assert.Equal(2, bounds["unitsPerCell"]!.GetValue<double>());
        Assert.Single(landmasses);
        Assert.Equal(2, regions.Count);
        Assert.Equal(1, landmasses[0]!["id"]!.GetValue<int>());
        Assert.Equal(1, regions[0]!["id"]!.GetValue<int>());
        Assert.Equal(1, regions[0]!["landmassId"]!.GetValue<int>());
        Assert.Equal(2, regions[1]!["id"]!.GetValue<int>());
        Assert.Equal(1, regions[1]!["landmassId"]!.GetValue<int>());
        Assert.Equal("Polygon", landmasses[0]!["shape"]!["type"]!.GetValue<string>());
        Assert.Equal("Polygon", regions[0]!["shape"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void PackageKeepsLandmassWaterHoles()
    {
        var hole = Factory.CreateLinearRing([
            new Coordinate(2, 1), new Coordinate(4, 1), new Coordinate(4, 3),
            new Coordinate(2, 3), new Coordinate(2, 1)
        ]);
        var landmassShape = Factory.CreatePolygon(
            Factory.CreateLinearRing([
                new Coordinate(0, 0), new Coordinate(6, 0), new Coordinate(6, 4),
                new Coordinate(0, 4), new Coordinate(0, 0)
            ]),
            [hole]);
        var landmass = new Landmass(new LandmassId(1), landmassShape);
        var map = CreateMap(
            CreateReference(),
            [landmass],
            [
                SquareRegion(1, 1, 0, 0, 2, 4),
                SquareRegion(2, 1, 4, 0, 2, 4),
                SquareRegion(3, 1, 0, 0, 2, 1)
            ]);

        var json = MapPackageWriter.Write(map);
        var coordinates = JsonNode.Parse(json)!["landmasses"]![0]!["shape"]!["coordinates"]!.AsArray();

        Assert.Equal(2, coordinates.Count);
        var roundTripped = MapPackageReader.Read(json).Landmasses.Single().Shape;
        Assert.Equal(1, roundTripped.NumInteriorRings);
        Assert.True(roundTripped.InteriorRings[0].EqualsTopologically(hole));
    }

    [Fact]
    public void SeparateIslandLandmassIsKept()
    {
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 3, 4), SquareLandmass(2, 4, 1, 2, 2)],
            [SquareRegion(1, 1, 0, 0, 3, 4), SquareRegion(2, 2, 4, 1, 2, 2)]);

        var document = JsonNode.Parse(MapPackageWriter.Write(map))!.AsObject();
        var landmasses = document["landmasses"]!.AsArray();
        var regions = document["regions"]!.AsArray();

        Assert.Equal(2, landmasses.Count);
        Assert.Equal([1, 2], landmasses.Select(landmass => landmass!["id"]!.GetValue<int>()).ToArray());
        Assert.Equal(2, regions.Count);
        Assert.Equal(2, regions[1]!["landmassId"]!.GetValue<int>());
    }

    [Fact]
    public void ExportIsByteForByteDeterministicAndIndependentOfInputOrder()
    {
        var landmasses = new[] { SquareLandmass(1, 0, 0, 3, 4), SquareLandmass(2, 4, 1, 2, 2) };
        var regions = new[]
        {
            SquareRegion(2, 2, 4, 1, 2, 2),
            SquareRegion(1, 1, 0, 0, 3, 4)
        };
        var map = CreateMap(CreateReference(), landmasses, regions);
        var reordered = new GeneratedMap(
            map.Bounds,
            landmasses.Reverse().ToArray(),
            [],
            regions.Select(region => region with { }).Reverse().ToArray(),
            WorldContextMode.Isolated,
            SpatialReference: map.SpatialReference);

        var first = MapPackageWriter.Write(map);
        var second = MapPackageWriter.Write(map);
        var reorderedJson = MapPackageWriter.Write(reordered);

        Assert.Equal(first, second);
        Assert.Equal(first, reorderedJson);
        var ids = JsonNode.Parse(first)!["regions"]!.AsArray()
            .Select(region => region!["id"]!.GetValue<int>()).ToArray();
        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public void ManualMapPipelineExportsTheAuthoritativeVectorGeometry()
    {
        var draft = new ManualMapDraft(
            10,
            10,
            [Vertex(1, 1, 1), Vertex(2, 6, 1), Vertex(3, 6, 6), Vertex(4, 1, 6)],
            [new ManualRegionFace(1, [1, 2, 3, 4], "Heartland")]);
        var finalization = new ManualMapDraftFinalizer(Factory).FinalizeDraft(draft, CreateManualSpatialReference(10, 10));
        Assert.True(finalization.IsSuccessful, Diagnostics(finalization));

        var session = MapGenerationSession.Create(
            MapGenerationRequest.Isolated(
                new RequestedDomain(finalization.DerivedMask.Window),
                finalization.DerivedMask,
                CreateManualOptions(CreateManualSpatialReference(10, 10))),
            new MapGeometrySeed(finalization.Landmasses, finalization.RegionDraft));
        session.RunUntil(MapDataKeys.Regions);

        var json = MapPackageWriter.Write(session.CurrentMap);
        var roundTripped = MapPackageReader.Read(json);

        // The manual vector coastline is authoritative; the derived raster mask
        // must never reappear as package geometry.
        Assert.Null(JsonNode.Parse(json)!["mask"]);
        var vectorLandmass = Assert.Single(finalization.Landmasses);
        var packageLandmass = Assert.Single(roundTripped.Landmasses);
        Assert.Equal(vectorLandmass.Id, packageLandmass.Id);
        Assert.True(vectorLandmass.Shape.EqualsTopologically(packageLandmass.Shape));

        var regions = Assert.Single(roundTripped.Regions);
        Assert.Equal(vectorLandmass.Id, regions.LandmassId);
        Assert.True(session.Regions.Single().Shape.EqualsTopologically(regions.Shape));
    }

    [Fact]
    public void WriterRejectsRegionReferencingUnknownLandmass()
    {
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 3, 4)],
            [SquareRegion(1, 7, 0, 0, 3, 4)]);

        var exception = Assert.Throws<InvalidOperationException>(() => MapPackageWriter.Write(map));
        Assert.Contains("references unknown landmass 7", exception.Message);
    }

    [Fact]
    public void WriterRejectsDuplicateRegionIds()
    {
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 3, 4)],
            [SquareRegion(1, 1, 0, 0, 3, 4), SquareRegion(1, 1, 0, 0, 1, 1)]);

        var exception = Assert.Throws<InvalidOperationException>(() => MapPackageWriter.Write(map));
        Assert.Contains("region id 1 is not unique", exception.Message);
    }

    [Fact]
    public void SpatialReferenceRoundTripsAllCanonicalFields()
    {
        var reference = new MapSpatialReference
        {
            GridWidth = 9,
            GridHeight = 7,
            UnitsPerCell = 2.5,
            WorldModel = WorldModelDescriptor.Spherical(6371),
            Coverage = MapCoverage.Regional(new LongitudeInterval(170, 25), -12.5, 42.25),
            GridMapping = GridMappingKind.WebMercator,
            Topology = GridTopologyKind.OpenRectangular,
            CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits,
            PreserveProjectedCellAspectRatio = false,
            LegacyCompatibility = LegacyCompatibilityProfile.Flat
        };
        var map = CreateMap(
            reference,
            [SquareLandmass(1, 0, 0, 6, 4)],
            [SquareRegion(1, 1, 0, 0, 6, 4)]);

        var json = MapPackageWriter.Write(map);
        var spatial = JsonNode.Parse(json)!["spatialReference"]!.AsObject();

        Assert.Equal(9, spatial["gridWidth"]!.GetValue<int>());
        Assert.Equal(7, spatial["gridHeight"]!.GetValue<int>());
        Assert.Equal(2.5, spatial["unitsPerCell"]!.GetValue<double>());
        Assert.Equal(nameof(WorldModelKind.Spherical), spatial["worldModel"]!["kind"]!.GetValue<string>());
        Assert.Equal(6371, spatial["worldModel"]!["planetRadius"]!.GetValue<double>());
        Assert.Equal(nameof(MapCoverageKind.Regional), spatial["coverage"]!["kind"]!.GetValue<string>());
        Assert.Equal(170, spatial["coverage"]!["longitudeStart"]!.GetValue<double>());
        Assert.Equal(25, spatial["coverage"]!["longitudeSpan"]!.GetValue<double>());
        Assert.Equal(-12.5, spatial["coverage"]!["southLatitude"]!.GetValue<double>());
        Assert.Equal(42.25, spatial["coverage"]!["northLatitude"]!.GetValue<double>());
        Assert.Equal(nameof(GridMappingKind.WebMercator), spatial["gridMapping"]!.GetValue<string>());
        Assert.Equal(nameof(GridTopologyKind.OpenRectangular), spatial["topology"]!.GetValue<string>());
        Assert.Equal(nameof(CoordinateSpaceKind.GridMapUnits), spatial["canonicalCoordinates"]!.GetValue<string>());
        Assert.False(spatial["preserveProjectedCellAspectRatio"]!.GetValue<bool>());
        Assert.Equal(nameof(LegacyCompatibilityProfile.Flat), spatial["legacyCompatibility"]!.GetValue<string>());

        var roundTripped = MapPackageReader.Read(json);
        Assert.Equal(reference, roundTripped.SpatialReference);
    }

    [Fact]
    public void ReaderRoundTripsGeometryAndMetadata()
    {
        var reference = CreateReference();
        var landmasses = new[]
        {
            new Landmass(new LandmassId(1), Square(0, 0, 6, 4)),
            new Landmass(new LandmassId(2), Square(8, 1, 2, 2))
        };
        var regions = new[]
        {
            SquareRegion(1, 1, 0, 0, 3, 4),
            SquareRegion(2, 1, 3, 0, 3, 4),
            SquareRegion(3, 2, 8, 1, 2, 2)
        };
        var map = CreateMap(reference, landmasses, regions);

        var roundTripped = MapPackageReader.Read(MapPackageWriter.Write(map));

        Assert.Equal(MapPackageDocument.CurrentSchemaId, roundTripped.SchemaId);
        Assert.Equal(reference, roundTripped.SpatialReference);
        Assert.Equal(new MapBounds(12, 8, 2), roundTripped.Bounds);
        Assert.Equal("MapRegionizer", roundTripped.Generator!.Name);
        Assert.Equal(landmasses.Select(landmass => landmass.Id), roundTripped.Landmasses.Select(landmass => landmass.Id));
        Assert.Equal(regions.Select(region => region.Id), roundTripped.Regions.Select(region => region.Id));
        Assert.Equal(regions.Select(region => region.LandmassId), roundTripped.Regions.Select(region => region.LandmassId));
        Assert.All(landmasses.Zip(roundTripped.Landmasses, (expected, actual) => (expected, actual)), pair =>
            Assert.True(pair.expected.Shape.EqualsTopologically(pair.actual.Shape)));
        Assert.All(regions.Zip(roundTripped.Regions, (expected, actual) => (expected, actual)), pair =>
            Assert.True(pair.expected.Shape.EqualsTopologically(pair.actual.Shape)));
    }

    [Fact]
    public void WriterRejectsNonFiniteRegionCoordinates()
    {
        var shape = Factory.CreatePolygon([
            new Coordinate(0, 0), new Coordinate(6, 0), new Coordinate(6, double.NaN),
            new Coordinate(0, double.NaN), new Coordinate(0, 0)
        ]);
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 6, 4)],
            [new MapRegion(new RegionId(1), new LandmassId(1), shape)]);

        var exception = Assert.Throws<InvalidOperationException>(() => MapPackageWriter.Write(map));
        Assert.Contains("Region 1", exception.Message);
    }

    [Fact]
    public void WriterRejectsEmptyPolygon()
    {
        var map = CreateMap(
            CreateReference(),
            [new Landmass(new LandmassId(1), Factory.CreatePolygon())],
            []);

        Assert.Throws<InvalidOperationException>(() => MapPackageWriter.Write(map));
    }

    [Fact]
    public void ReaderRejectsUnknownSchema()
    {
        var map = CreateMap(
            CreateReference(),
            [SquareLandmass(1, 0, 0, 6, 4)],
            [SquareRegion(1, 1, 0, 0, 6, 4)]);
        var json = JsonNode.Parse(MapPackageWriter.Write(map))!.AsObject();
        json["schema"] = "MapRegionizer.MapPackage.v2";

        var exception = Assert.Throws<InvalidOperationException>(() => MapPackageReader.Read(json.ToJsonString()));
        Assert.Contains("Unsupported map package schema", exception.Message);
    }

    [Fact]
    public void WriterRequiresMap()
    {
        Assert.Throws<ArgumentNullException>(() => MapPackageWriter.Write(null!));
    }

    private static GeneratedMap CreateMap(
        MapSpatialReference reference,
        IReadOnlyList<Landmass> landmasses,
        IReadOnlyList<MapRegion> regions) => new(
            new MapBounds(reference.GridWidth * reference.UnitsPerCell, reference.GridHeight * reference.UnitsPerCell, reference.UnitsPerCell),
            landmasses,
            [],
            regions,
            WorldContextMode.Isolated,
            SpatialReference: reference);

    private static MapSpatialReference CreateReference() => new()
    {
        GridWidth = 6,
        GridHeight = 4,
        UnitsPerCell = 2,
        WorldModel = WorldModelDescriptor.Spherical(),
        Coverage = MapCoverage.Regional(new LongitudeInterval(10, 20), -10, 10),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.OpenRectangular,
        CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits
    };

    private static Landmass SquareLandmass(int id, double x, double y, double width, double height) =>
        new(new LandmassId(id), Square(x, y, width, height));

    private static MapRegion SquareRegion(int id, int landmassId, double x, double y, double width, double height) =>
        new(new RegionId(id), new LandmassId(landmassId), Square(x, y, width, height));

    private static Polygon Square(double x, double y, double width, double height) => Factory.CreatePolygon([
        new Coordinate(x, y),
        new Coordinate(x + width, y),
        new Coordinate(x + width, y + height),
        new Coordinate(x, y + height),
        new Coordinate(x, y)
    ]);

    private static MapGenerationOptions CreateManualOptions(MapSpatialReference reference) => new()
    {
        Seed = 42,
        Spatial = new MapSpatialOptions
        {
            WorldModel = reference.WorldModel,
            Coverage = reference.Coverage,
            GridMapping = reference.GridMapping,
            Topology = reference.Topology,
            UnitsPerCell = reference.UnitsPerCell,
            LegacyCompatibility = reference.LegacyCompatibility
        },
        ShapeExtraction = new ShapeExtractionOptions { SimplifyTolerance = 0 },
        Boundaries = new BoundaryDistortionOptions { Enabled = false }
    };

    private static MapSpatialReference CreateManualSpatialReference(int width, int height) => new()
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

    private static ManualMapVertex Vertex(int id, double x, double y) => new(id, new MapPoint(x, y));

    private static string Diagnostics(ManualMapFinalizationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
