using MapRegionizer.Core.Climate;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateClimateStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateClimate;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Elevation,
        MapDataKeys.WaterSurfaces,
        MapDataKeys.WaterBodyTopology,
        MapDataKeys.Hydrology
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Climate
    };

    public void Execute(MapGenerationContext context)
    {
        var elevation = context.Elevation ?? throw new InvalidOperationException("Elevation is required.");
        var waterSurfaces = context.WaterSurfaces ?? throw new InvalidOperationException("Water surfaces are required.");
        var waterBodyTopology = context.WaterBodyTopology ?? throw new InvalidOperationException("Water body topology is required.");
        var hydrology = context.Hydrology ?? throw new InvalidOperationException("Hydrology is required.");
        var seed = context.Options.Seed ?? 0;
        var generator = new ClimateGenerator(unchecked(seed * 397 ^ 0x5C11A7E));
        context.Climate = generator.Generate(context.Mask, elevation, waterBodyTopology, waterSurfaces, hydrology, context.Options.Climate);
    }
}
