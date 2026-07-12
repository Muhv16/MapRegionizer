using System.Globalization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class RegionGenerationContractTests
{
    [Fact]
    public void RawAndDistortedRegionsSatisfyGeometryContractForLandmassWithHoleAndIsland()
    {
        var session = MapGenerationSession.Create(CreateMask(), CreateOptions());

        session.RunUntil(MapDataKeys.RawRegions);
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.RawRegions));

        session.RunUntil(MapDataKeys.Regions);
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));

        Assert.Contains(session.Landmasses, landmass => landmass.Shape.NumInteriorRings == 1);
        Assert.Equal(session.RawRegions.Select(region => region.Id), session.Regions.Select(region => region.Id));
    }

    [Fact]
    public void FixedSeedProducesStableRegionIdentifiersAndGeometry()
    {
        var first = Generate(CreateMask(), CreateOptions());
        var second = Generate(CreateMask(), CreateOptions());

        Assert.Equal(Signature(first.RawRegions), Signature(second.RawRegions));
        Assert.Equal(Signature(first.Regions), Signature(second.Regions));
    }

    [Fact]
    public void DisabledDistortionPreservesRawRegionGeometry()
    {
        var session = Generate(CreateMask(), CreateOptions(distortionEnabled: false));

        Assert.Equal(Signature(session.RawRegions), Signature(session.Regions));
        Assert.Empty(RegionGeometryContract.Validate(session.Landmasses, session.Regions));
    }

    [Fact]
    public void AdjacencyRequiresSharedBoundaryWithNonZeroLength()
    {
        var factory = new GeometryFactory();
        var first = Square(factory, 0, 0);
        var edgeNeighbor = Square(factory, 1, 0);
        var vertexNeighbor = Square(factory, 1, 1);

        Assert.True(RegionGeometryContract.ShareBoundary(first, edgeNeighbor));
        Assert.False(RegionGeometryContract.ShareBoundary(first, vertexNeighbor));
    }

    private static MapGenerationSession Generate(MapMask mask, MapGenerationOptions options)
    {
        var session = MapGenerationSession.Create(mask, options);
        session.RunUntil(MapDataKeys.Regions);
        return session;
    }

    private static MapGenerationOptions CreateOptions(bool distortionEnabled = true) => new()
    {
        Seed = 124_578,
        Regions = new RegionGenerationOptions
        {
            TargetArea = 150,
            PointsMultiplier = 2.5,
            MinAreaRatio = 0.6,
            MaxAreaRatio = 2
        },
        Boundaries = new BoundaryDistortionOptions
        {
            Enabled = distortionEnabled,
            Detail = 0.2,
            MaxOffset = 1.5,
            MinLineLengthToCurve = 5
        }
    };

    private static MapMask CreateMask()
    {
        var land = new HashSet<GridPoint>();

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 50; x++)
            {
                if (x is < 17 or > 32 || y is < 12 or > 27)
                    land.Add(new GridPoint(x, y));
            }
        }

        for (var y = 4; y < 10; y++)
        {
            for (var x = 58; x < 64; x++)
                land.Add(new GridPoint(x, y));
        }

        return new MapMask(70, 50, land);
    }

    private static Polygon Square(GeometryFactory factory, double x, double y)
    {
        return factory.CreatePolygon([
            new Coordinate(x, y),
            new Coordinate(x + 1, y),
            new Coordinate(x + 1, y + 1),
            new Coordinate(x, y + 1),
            new Coordinate(x, y)
        ]);
    }

    private static IReadOnlyList<string> Signature(IEnumerable<MapRegion> regions)
    {
        return regions.Select(region => string.Join(
            '|',
            region.Id.Value.ToString(CultureInfo.InvariantCulture),
            region.LandmassId.Value.ToString(CultureInfo.InvariantCulture),
            string.Join(';', region.Shape.Coordinates.Select(coordinate => $"{coordinate.X:R},{coordinate.Y:R}")))).ToList();
    }
}
