using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class LegacySpatialRegressionTests
{
    [Fact]
    public void LegacyDefaultGoldenRasterHashesRemainStable()
    {
        var first = GenerateBaseline();
        var second = GenerateBaseline();

        var firstHash = RasterGoldenHash(first);
        Assert.Equal(firstHash, RasterGoldenHash(second));
        Assert.Equal(
            "legacy-raster-golden:8A1386266070EAFAD849ED8D68CA85AA1F0E19F9793762F3285FC97E611B84CB",
            firstHash);
    }

    [Fact]
    public void LegacyDefaultGoldenGeometryRemainsStable()
    {
        var first = GenerateBaseline();
        var second = GenerateBaseline();

        var firstHash = GeometryGoldenHash(first);
        Assert.Equal(firstHash, GeometryGoldenHash(second));
        Assert.Equal(
            "legacy-geometry-golden:D91EB257C7D932860E712709F3C3921048D63C162444AC61DA1A9DD195D1ADD1",
            firstHash);
    }

    [Fact]
    public void LegacyShapeExtractionDoesNotJoinLandAcrossXSeam()
    {
        var width = 10;
        var height = 5;
        var land = new HashSet<GridPoint>();
        for (var y = 1; y < height - 1; y++)
        {
            land.Add(new GridPoint(0, y));
            land.Add(new GridPoint(1, y));
            land.Add(new GridPoint(width - 2, y));
            land.Add(new GridPoint(width - 1, y));
        }

        var session = MapGenerationSession.Create(new MapMask(width, height, land), new MapGenerationOptions { Seed = 23 });
        session.RunUntil(MapDataKeys.Landmasses);

        Assert.Equal(2, session.Landmasses.Count);
    }

    [Fact]
    public void LegacyShapeExtractionKeepsTopAndBottomEdgesOpen()
    {
        var land = new HashSet<GridPoint>();
        for (var y = 0; y <= 1; y++)
        {
            for (var x = 2; x <= 7; x++)
                land.Add(new GridPoint(x, y));
        }

        for (var y = 8; y <= 9; y++)
        {
            for (var x = 2; x <= 7; x++)
                land.Add(new GridPoint(x, y));
        }

        var session = MapGenerationSession.Create(new MapMask(10, 10, land), new MapGenerationOptions { Seed = 23 });
        session.RunUntil(MapDataKeys.Landmasses);

        Assert.Equal(2, session.Landmasses.Count);
    }

    [Fact]
    public void LegacyRepresentativeFixtureRetainsHoleAndSeparateIslands()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });
        session.RunUntil(MapDataKeys.Landmasses);

        Assert.True(session.Landmasses.Count >= 3);
        Assert.Contains(session.Landmasses, landmass => landmass.Shape.NumInteriorRings > 0);
    }

    [Fact]
    public void LegacyRegularFixtureHasPinnedCanonicalGeometry()
    {
        var session = MapGenerationSession.Create(RegularMask(), new MapGenerationOptions { Seed = 913 });
        session.RunUntil(MapDataKeys.Landmasses);

        Assert.Equal(
            "legacy-geometry-golden:5408B778C902F96B375004922CFD0908A34BD3389C2BF949DCED762400CCAE93",
            GeometryGoldenHash(session));
    }

    [Fact]
    public void LegacyBothXEdgesFixtureHasPinnedCanonicalGeometry()
    {
        var session = MapGenerationSession.Create(BothXEdgesMask(), new MapGenerationOptions { Seed = 913 });
        session.RunUntil(MapDataKeys.Landmasses);

        Assert.Equal(
            "legacy-geometry-golden:30FF0B6DC7095C2E6F6FF45015692C944F08BC003E237FEF007333E8318AA152",
            GeometryGoldenHash(session));
    }

    [Fact]
    public void LegacyTectonicsHydrologyAndClimateUseCylindricalXTopology()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });

        Assert.IsType<CylindricalXTopology>(session.SpatialContext.GridTopology);
        Assert.True(session.SpatialContext.GridTopology.TryResolve(new GridPoint(0, 5), -1, 0, out var left));
        Assert.Equal(session.Mask.Width - 1, left.X);

        session.RunUntil(MapDataKeys.Climate);
        Assert.NotNull(session.TectonicHistory);
        Assert.NotNull(session.Hydrology);
        Assert.NotNull(session.Climate);
    }

    [Fact]
    public void LegacyTectonicsSeamNeighborIsPeriodic()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });
        var leftEdge = new GridPoint(0, 5);

        Assert.Contains(new GridPoint(session.Mask.Width - 1, 5), session.SpatialContext.GridTopology.GetNeighbors4(leftEdge));
    }

    [Fact]
    public void LegacyHydrologySeamNeighborIsPeriodic()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });
        session.RunUntil(MapDataKeys.Hydrology);
        var leftEdge = new GridPoint(0, 5);

        // Hydrology's D8 routing and visible river graph use this same
        // topology-provided candidate across the legacy X seam.
        Assert.Contains(new GridPoint(session.Mask.Width - 1, 5), session.SpatialContext.GridTopology.GetNeighbors8(leftEdge));
        Assert.Equal(session.Mask.Width, session.Hydrology!.Width);
    }

    [Fact]
    public void LegacyClimateSeamNeighborIsPeriodic()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });
        session.RunUntil(MapDataKeys.Climate);
        var leftEdge = new GridPoint(0, 5);

        // Climate diffusion and wind marching receive the shared topology;
        // the edge candidate must remain periodic under the legacy profile.
        Assert.Contains(new GridPoint(session.Mask.Width - 1, 5), session.SpatialContext.GridTopology.GetNeighbors8(leftEdge));
        Assert.Equal(session.Mask.Height, session.Climate!.Height);
    }

    private static MapGenerationSession GenerateBaseline()
    {
        var session = MapGenerationSession.Create(BaselineMask(), new MapGenerationOptions { Seed = 913 });
        session.RunFull();
        return session;
    }

    private static MapMask BaselineMask()
    {
        const int width = 32;
        const int height = 20;
        var land = new HashSet<GridPoint>();
        for (var y = 4; y < 16; y++)
        {
            for (var x = 4; x < 28; x++)
            {
                if (x is >= 12 and <= 18 && y is >= 8 and <= 11)
                    continue;
                land.Add(new GridPoint(x, y));
            }
        }

        for (var y = 1; y < 4; y++)
        {
            for (var x = 1; x < 4; x++)
                land.Add(new GridPoint(x, y));
        }

        for (var y = 16; y < 19; y++)
        {
            for (var x = 28; x < 31; x++)
                land.Add(new GridPoint(x, y));
        }

        return new MapMask(width, height, land);
    }

    private static MapMask RegularMask()
    {
        var land = new HashSet<GridPoint>();
        for (var y = 2; y < 6; y++)
        {
            for (var x = 2; x < 8; x++)
                land.Add(new GridPoint(x, y));
        }

        return new MapMask(10, 8, land);
    }

    private static MapMask BothXEdgesMask()
    {
        var land = new HashSet<GridPoint>();
        for (var y = 1; y < 5; y++)
        {
            land.Add(new GridPoint(0, y));
            land.Add(new GridPoint(1, y));
            land.Add(new GridPoint(8, y));
            land.Add(new GridPoint(9, y));
        }

        return new MapMask(10, 6, land);
    }

    private static string RasterGoldenHash(MapGenerationSession session)
    {
        var parts = new List<string>();
        if (session.TectonicHistory is { } history)
        {
            parts.Add(HashText(string.Join(
                "|",
                history.Lineaments.OrderBy(item => item.Id).Select(item =>
                    FormattableString.Invariant($"{item.Id}:{item.Kind}:{item.Age:R}:{item.Intensity:R}:{CanonicalPoints(item.Points)}"))), "tectonic-history"));
        }

        if (session.PlateDomains is { } domains)
        {
            parts.Add(HashSpan(domains.PlatesSpan, "plate-raster"));
            parts.Add(HashText(string.Join(
                "|",
                domains.Domains.OrderBy(item => item.Id.Value).Select(item =>
                    FormattableString.Invariant($"{item.Id.Value}:{item.Kind}:{item.PointCount}:{item.Centroid.X:R},{item.Centroid.Y:R}:{item.Motion.X:R},{item.Motion.Y:R}:{item.Activity:R}:{item.Density:R}:{item.Thickness:R}:{item.MeanOceanicAge:R}:{item.IsMicroplate}"))), "plate-domains"));
        }

        if (session.Elevation is { } elevation)
        {
            parts.Add(HashSpan(elevation.ElevationMetersSpan, "elevation"));
            parts.Add(HashSpan(elevation.BedElevationMetersSpan, "bed-elevation"));
            parts.Add(HashSpan(elevation.TerrainClassSpan, "terrain"));
        }

        if (session.Hydrology is { } hydrology)
        {
            parts.Add(HashSpan(hydrology.HydroSurfaceMetersSpan, "hydro-surface"));
            parts.Add(HashSpan(hydrology.FlowDirectionsSpan, "flow-direction"));
            parts.Add(HashSpan(hydrology.FlowAccumulationSpan, "flow-accumulation"));
            parts.Add(HashSpan(hydrology.DrainageBasinIdsSpan, "basins"));
            parts.Add(HashSpan(hydrology.RiverCellsSpan, "river-cells"));
        }

        if (session.Climate is { } climate)
        {
            parts.Add(HashSpan(climate.LatitudeNormSpan, "latitude"));
            parts.Add(HashSpan(climate.MeanAnnualTemperatureSpan, "temperature"));
            parts.Add(HashSpan(climate.MoistureSpan, "moisture"));
            parts.Add(HashSpan(climate.PrecipitationSpan, "precipitation"));
            parts.Add(HashSpan(climate.ClimateClassSpan, "climate-class"));
            parts.Add(HashSpan(climate.BiomeSpan, "biome"));
        }

        return HashText(string.Join("|", parts), "legacy-raster-golden");
    }

    private static string GeometryGoldenHash(MapGenerationSession session)
    {
        var parts = new List<string>();
        parts.AddRange(session.Landmasses.OrderBy(item => item.Id.Value).Select(item => $"landmass:{item.Id.Value}:{CanonicalGeometry(item.Shape)}"));
        parts.AddRange(session.WaterBodies.OrderBy(item => item.Id.Value).Select(item => $"water:{item.Id.Value}:{CanonicalGeometry(item.Shape)}"));
        parts.AddRange(session.Regions.OrderBy(item => item.Id.Value).Select(item => $"region:{item.Id.Value}:{item.LandmassId.Value}:{CanonicalGeometry(item.Shape)}"));
        return HashText(string.Join("|", parts), "legacy-geometry-golden");
    }

    private static string HashSpan<T>(ReadOnlySpan<T> values, string label) where T : struct
    {
        var builder = new StringBuilder(label);
        foreach (var value in values)
            builder.Append('|').Append(value switch
            {
                double item => BitConverter.DoubleToInt64Bits(item).ToString("X16", CultureInfo.InvariantCulture),
                float item => BitConverter.SingleToInt32Bits(item).ToString("X8", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            });
        return HashText(builder.ToString(), label);
    }

    private static string HashText(string value, string label)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return label + ":" + Convert.ToHexString(bytes);
    }

    private static string CanonicalPoints(IEnumerable<GridPoint> points) =>
        string.Join(';', points.Select(point =>
            FormattableString.Invariant($"{point.X},{point.Y}")));

    private static string CanonicalGeometry(Geometry geometry) => geometry switch
    {
        Polygon polygon => string.Join(
            "|",
            CanonicalRing(polygon.ExteriorRing),
            string.Join(";", polygon.InteriorRings.Select(CanonicalRing).OrderBy(value => value, StringComparer.Ordinal))),
        MultiPolygon multi => string.Join(";", multi.Geometries.Cast<Polygon>().Select(CanonicalGeometry).OrderBy(value => value, StringComparer.Ordinal)),
        _ => CanonicalRing((LineString)geometry)
    };

    private static string CanonicalRing(LineString ring)
    {
        var points = ring.Coordinates
            .Take(Math.Max(0, ring.Coordinates.Length - 1))
            .Select(point =>
            (
                Math.Round(point.X, 6).ToString("F6", CultureInfo.InvariantCulture),
                Math.Round(point.Y, 6).ToString("F6", CultureInfo.InvariantCulture)))
            .ToList();
        if (points.Count == 0)
            return string.Empty;

        var variants = new List<string>(points.Count * 2);
        for (var reverse = 0; reverse < 2; reverse++)
        {
            var ordered = reverse == 0 ? points : points.AsEnumerable().Reverse().ToList();
            for (var offset = 0; offset < ordered.Count; offset++)
            {
                variants.Add(string.Join(';', Enumerable.Range(0, ordered.Count).Select(i =>
                {
                    var point = ordered[(offset + i) % ordered.Count];
                    return point.Item1 + "," + point.Item2;
                })));
            }
        }

        return variants.OrderBy(value => value, StringComparer.Ordinal).First();
    }
}
