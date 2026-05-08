namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateLakeLevelsStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateLakeLevels;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Elevation,
        MapDataKeys.WaterBodies,
        MapDataKeys.WaterBodyTopology
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.WaterSurfaces };

    public void Execute(MapGenerationContext context)
    {
        var elevation = context.Elevation ?? throw new InvalidOperationException("Elevation is required.");
        context.WaterSurfaces = elevation.WaterSurfaces ?? throw new InvalidOperationException("Elevation does not contain generated water surfaces.");
    }
}
