using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateTectonicFeaturesStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateTectonicFeatures;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.Landmasses,
        MapDataKeys.TectonicHistory,
        MapDataKeys.CrustFields,
        MapDataKeys.PlateDomains,
        MapDataKeys.TectonicBoundaries
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicFeatures };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var plateDomains = context.PlateDomains ?? throw new InvalidOperationException("Plate domains are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var generator = new TectonicFeatureGenerator();
        context.TectonicFeatures = generator.Generate(context.Mask, history, crustFields, plateDomains, boundaries, context.Landmasses);
    }
}
