using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateTectonicPlatesStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateTectonicPlates;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.Landmasses,
        MapDataKeys.WaterBodies
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicPlates };

    public void Execute(MapGenerationContext context)
    {
        var generator = new TectonicPlateGenerator(context.Random);
        context.TectonicPlates = generator.Generate(context.Mask, context.Landmasses, context.WaterBodies, context.Options);
    }
}
