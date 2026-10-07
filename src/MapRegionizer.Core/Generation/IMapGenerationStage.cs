namespace MapRegionizer.Core.Generation;

using System.Threading;

public interface IMapGenerationStage
{
    string Id { get; }
    IReadOnlySet<MapDataKey> Requires { get; }
    IReadOnlySet<MapDataKey> Produces { get; }
    void Execute(MapGenerationContext context);
}

public interface ICancellableMapGenerationStage : IMapGenerationStage
{
    void Execute(MapGenerationContext context, CancellationToken cancellationToken);
}
