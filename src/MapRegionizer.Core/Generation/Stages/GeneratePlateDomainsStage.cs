using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GeneratePlateDomainsStage : IMapGenerationStage
{
    public string Id => MapStageIds.GeneratePlateDomains;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.CrustFields,
        MapDataKeys.TectonicHistory
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.PlateDomains };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var generator = new PlateDomainGenerator(context.Random);
        context.PlateDomains = generator.Generate(context.Mask, crustFields, history, context.Options.TectonicPlates);
    }
}
