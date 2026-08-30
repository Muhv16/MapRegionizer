using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using NetTopologySuite.Geometries;
using Xunit;

namespace MapRegionizer.Core.Tests;

public sealed class TopologyBoundaryContractTests
{
    [Fact]
    public void TopologyOwnsWorldBoundarySemantics()
    {
        var cylindrical = new CylindricalXTopology(5, 4);
        var open = new OpenRectangularTopology(5, 4);

        Assert.False(GridTopologyMath.IsOpenBoundary(cylindrical, new GridPoint(0, 1)));
        Assert.True(GridTopologyMath.IsOpenBoundary(cylindrical, new GridPoint(2, 0)));
        Assert.True(GridTopologyMath.IsOpenBoundary(open, new GridPoint(0, 1)));
        Assert.True(GridTopologyMath.IsOpenBoundary(open, new GridPoint(2, 0)));
    }

    [Fact]
    public void CroppedWaterBodyMetadataUsesRequestedTopologyDimensions()
    {
        var factory = new GeometryFactory();
        var working = new GridWindow(0, 0, 4, 4);
        var requested = new RequestedDomain(new GridWindow(1, 1, 2, 2));
        var mask = new MapMask(working, Enumerable.Range(0, 16)
            .Select(index => new GridPoint(index % 4, index / 4))
            .ToHashSet());
        var options = new MapGenerationOptions
        {
            Spatial = new MapSpatialOptions { Topology = GridTopologyKind.OpenRectangular }
        };
        var context = new MapGenerationContext(
            mask,
            options,
            factory,
            randomSeed: 1,
            requested,
            new WorkingDomain(working),
            WorldContextMode.Automatic);

        var ids = new int[16];
        ids[2 * 4 + 2] = 1;
        context.WaterBodyTopology = new WaterBodyTopology(
            4,
            4,
            ids,
            new byte[16],
            [new WaterBodyClassification(new WaterBodyId(1), WaterBodyKind.InlandLake, 1, false, 1 / 16.0)]);

        var cropped = context.ToGeneratedMap().WaterBodyTopology;

        Assert.NotNull(cropped);
        var body = Assert.Single(cropped!.Bodies);
        Assert.True(body.TouchesMapEdge);
    }

    [Fact]
    public void NoNewGeneratorDefinesLocalWrapX()
    {
        var stageSources = ReadGeneratorSources();

        Assert.NotEmpty(stageSources);
        Assert.All(stageSources, source =>
            Assert.DoesNotContain("CylindricalXTopology.NormalizeX", source, StringComparison.Ordinal));
    }

    [Fact]
    public void NoNewGeneratorComputesLatitudeFromRasterHeight()
    {
        var stageSources = ReadGeneratorSources();

        Assert.All(stageSources, source =>
        {
            foreach (var line in source.Split('\n'))
            {
                var normalized = line.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                var usesHeightArithmetic = normalized.Contains("/height", StringComparison.Ordinal) ||
                                           normalized.Contains("*height", StringComparison.Ordinal) ||
                                           normalized.Contains("height*", StringComparison.Ordinal) ||
                                           normalized.Contains("height/", StringComparison.Ordinal) ||
                                           normalized.Contains("height-", StringComparison.Ordinal) ||
                                           normalized.Contains("height+", StringComparison.Ordinal);
                Assert.False(
                    normalized.Contains("latitude", StringComparison.Ordinal) &&
                    normalized.Contains("height", StringComparison.Ordinal) &&
                    usesHeightArithmetic,
                    $"A generation stage appears to derive latitude from raster height: {line.Trim()}");
            }
        });
    }

    [Fact]
    public void NoNewGeneratorDependsOnOutputCoordinateSystem()
    {
        var stageSources = ReadGeneratorSources();

        Assert.All(stageSources, source =>
        {
            Assert.DoesNotContain("MapOutputOptions", source, StringComparison.Ordinal);
            Assert.DoesNotContain("OutputCoordinateSystem", source, StringComparison.Ordinal);
        });
    }

    private static IReadOnlyList<string> ReadGeneratorSources()
    {
        var root = FindRepositoryRoot();
        var sourceDirectories = new[]
        {
            Path.Combine(root, "src", "MapRegionizer.Core", "Climate"),
            Path.Combine(root, "src", "MapRegionizer.Core", "Tectonics"),
            Path.Combine(root, "src", "MapRegionizer.Core", "Terrain"),
            Path.Combine(root, "src", "MapRegionizer.Core", "Generation", "Stages")
        };
        return sourceDirectories
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "MapRegionizer.Core")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the MapRegionizer repository root.");
    }
}
