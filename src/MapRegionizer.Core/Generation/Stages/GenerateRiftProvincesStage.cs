using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateRiftProvincesStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateRiftProvinces;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.TectonicHistory,
        MapDataKeys.CrustFields,
        MapDataKeys.TectonicBoundaries
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.RiftProvinces };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var generator = new RiftProvinceGenerator();
        context.RiftProvinces = generator.Generate(context.Mask, history, crustFields, boundaries);
    }
}
