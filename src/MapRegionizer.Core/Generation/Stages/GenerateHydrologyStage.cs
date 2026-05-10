using MapRegionizer.Core.Terrain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateHydrologyStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateHydrology;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Elevation,
        MapDataKeys.WaterSurfaces,
        MapDataKeys.WaterBodyTopology,
        MapDataKeys.GeneratedLakes
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Hydrology
    };

    public void Execute(MapGenerationContext context)
    {
        var elevation = context.Elevation ?? throw new InvalidOperationException("Elevation is required.");
        var waterSurfaces = context.WaterSurfaces ?? throw new InvalidOperationException("Water surfaces are required.");
        var waterBodyTopology = context.WaterBodyTopology ?? throw new InvalidOperationException("Water body topology is required.");
        var generatedLakes = context.GeneratedLakes ?? throw new InvalidOperationException("Generated lakes are required.");
        var seed = context.Options.Seed ?? 0;
        var generator = new HydrologyGenerator(unchecked(seed * 397 ^ 0x48D1F1));
        context.Hydrology = generator.Generate(context.Mask, elevation, waterBodyTopology, generatedLakes, waterSurfaces, context.Options.Hydrology);
    }
}
