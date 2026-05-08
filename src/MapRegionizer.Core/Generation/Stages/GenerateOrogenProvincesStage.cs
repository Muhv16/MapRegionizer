using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateOrogenProvincesStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateOrogenProvinces;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.TectonicHistory,
        MapDataKeys.CrustFields,
        MapDataKeys.TectonicBoundaries
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.OrogenProvinces };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var generator = new OrogenProvinceGenerator();
        context.OrogenProvinces = generator.Generate(context.Mask, history, crustFields, boundaries);
    }
}
