using MapRegionizer.Core.Tectonics;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GeneratePlateDomainsStage : IMapGenerationStage, ISpatialBoundaryAwareStage
{
    public string Id => MapStageIds.GeneratePlateDomains;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.CrustFields,
        MapDataKeys.TectonicHistory,
        MapDataKeys.SpatialContext,
        MapDataKeys.TectonicWorldContext
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.PlateDomains };

    public StageBoundaryMetadata BoundaryMetadata => StageBoundaryMetadata.Global();

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        if ((context.GenerationMode is RegionalGenerationMode.Automatic or RegionalGenerationMode.Custom) &&
            context.TectonicWorldContext is not null)
        {
            context.PlateDomains = WorldPlateDomainSampler.Sample(context.Mask, crustFields, context.TectonicWorldContext, context.Options.TectonicPlates);
            return;
        }

        var generator = new PlateDomainGenerator(context.Random, context.SpatialContext.GridTopology);
        context.PlateDomains = generator.Generate(context.Mask, crustFields, history, context.Options.TectonicPlates);
    }
}
