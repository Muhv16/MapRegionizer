using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateCrustFieldsStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateCrustFields;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.TectonicHistory
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.CrustFields };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var generator = new CrustFieldGenerator(context.Random);
        context.CrustFields = generator.Generate(context.Mask, history, context.Options.TectonicPlates);
    }
}
