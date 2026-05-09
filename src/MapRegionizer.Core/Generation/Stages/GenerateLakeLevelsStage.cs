using MapRegionizer.Core.Terrain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateLakeLevelsStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateLakeLevels;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.BaseTerrain,
        MapDataKeys.WaterBodies,
        MapDataKeys.WaterBodyTopology,
        MapDataKeys.CrustFields,
        MapDataKeys.TectonicBoundaries,
        MapDataKeys.RiftProvinces,
        MapDataKeys.TectonicFeatures
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Elevation,
        MapDataKeys.WaterSurfaces
    };

    public void Execute(MapGenerationContext context)
    {
        var baseTerrain = context.BaseTerrain ?? throw new InvalidOperationException("Base terrain is required.");
        var waterBodyTopology = context.WaterBodyTopology ?? throw new InvalidOperationException("Water body topology is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var riftProvinces = context.RiftProvinces ?? throw new InvalidOperationException("Rift provinces are required.");
        var features = context.TectonicFeatures ?? throw new InvalidOperationException("Tectonic features are required.");
        var generator = new LakeLevelGenerator();
        var elevation = generator.Generate(context.Mask, baseTerrain, crustFields, boundaries, riftProvinces, features, waterBodyTopology, context.Options.Elevation);
        context.Elevation = elevation;
        context.WaterSurfaces = elevation.WaterSurfaces ?? throw new InvalidOperationException("Lake level generation did not produce water surfaces.");
    }
}
