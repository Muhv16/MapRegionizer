using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class AssembleTectonicPlateMapStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateTectonicPlates;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.TectonicHistory,
        MapDataKeys.CrustFields,
        MapDataKeys.PlateDomains,
        MapDataKeys.TectonicBoundaries,
        MapDataKeys.TectonicFeatures
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicPlates };

    public void Execute(MapGenerationContext context)
    {
        var history = context.TectonicHistory ?? throw new InvalidOperationException("Tectonic history is required.");
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var plateDomains = context.PlateDomains ?? throw new InvalidOperationException("Plate domains are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var features = context.TectonicFeatures ?? throw new InvalidOperationException("Tectonic features are required.");
        var assembler = new TectonicPlateAssembler();
        context.TectonicPlates = assembler.Assemble(history, crustFields, plateDomains, boundaries, features);
    }
}
