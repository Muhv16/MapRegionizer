namespace MapRegionizer.Core.Generation;

internal interface IMapGenerationStage
{
    void Execute(MapGenerationContext context);
}
