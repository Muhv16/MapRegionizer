using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateTectonicHistoryStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateTectonicHistory;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.Landmasses,
        MapDataKeys.WaterBodies
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicHistory };

    public void Execute(MapGenerationContext context)
    {
        var generator = new TectonicHistoryGenerator(context.Random);
        context.TectonicHistory = generator.Generate(context.Mask, context.Landmasses, context.WaterBodies, context.Options.TectonicPlates);
    }
}
