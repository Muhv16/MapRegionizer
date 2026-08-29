using MapRegionizer.Core.Tectonics;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateTectonicHistoryStage : IMapGenerationStage, ISpatialBoundaryAwareStage
{
    public string Id => MapStageIds.GenerateTectonicHistory;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.Landmasses,
        MapDataKeys.WaterBodies,
        MapDataKeys.SpatialContext,
        MapDataKeys.TectonicWorldContext
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.TectonicHistory };

    public StageBoundaryMetadata BoundaryMetadata => StageBoundaryMetadata.Global();

    public void Execute(MapGenerationContext context)
    {
        if (context.GenerationMode == RegionalGenerationMode.Automatic && context.TectonicWorldContext is not null)
        {
            context.TectonicHistory = context.TectonicWorldContext.CreateHistory(context.WorkingDomain.Window);
            return;
        }

        var generator = new TectonicHistoryGenerator(context.Random, context.SpatialContext.GridTopology);
        context.TectonicHistory = generator.Generate(context.Mask, context.Landmasses, context.WaterBodies, context.Options.TectonicPlates);
    }
}
