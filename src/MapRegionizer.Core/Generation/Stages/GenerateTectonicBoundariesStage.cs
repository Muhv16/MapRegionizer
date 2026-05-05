using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateTectonicBoundariesStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateTectonicBoundaries;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.PlateDomains,
        MapDataKeys.CrustFields
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicBoundaries };

    public void Execute(MapGenerationContext context)
    {
        var plateDomains = context.PlateDomains ?? throw new InvalidOperationException("Plate domains are required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var generator = new TectonicBoundaryGenerator();
        context.TectonicBoundaries = generator.Generate(plateDomains, crustFields, context.Options.TectonicPlates);
    }
}
