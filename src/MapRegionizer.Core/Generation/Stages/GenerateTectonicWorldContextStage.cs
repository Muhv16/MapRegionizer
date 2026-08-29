using MapRegionizer.Core.Tectonics;

namespace MapRegionizer.Core.Generation.Stages;

/// <summary>Builds stable tectonic identity once per world seed.</summary>
public sealed class GenerateTectonicWorldContextStage : IMapGenerationStage, ISpatialBoundaryAwareStage
{
    public string Id => MapStageIds.GenerateTectonicWorldContext;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.SpatialContext,
        MapDataKeys.WorldSeed
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.TectonicWorldContext
    };

    public StageBoundaryMetadata BoundaryMetadata => StageBoundaryMetadata.Global();

    public void Execute(MapGenerationContext context)
    {
        context.TectonicWorldContext = TectonicWorldContext.Create(context.WorldSeed, context.Options.TectonicPlates);
    }
}
