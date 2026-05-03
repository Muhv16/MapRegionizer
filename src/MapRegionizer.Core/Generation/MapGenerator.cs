using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerator
{
    private readonly GeometryFactory _geometryFactory;
    private readonly MapGenerationPipeline _pipeline;

    public MapGenerator()
        : this(new GeometryFactory(), MapGenerationPipelineBuilder.CreateDefault().Build())
    {
    }

    public MapGenerator(GeometryFactory geometryFactory)
        : this(geometryFactory, MapGenerationPipelineBuilder.CreateDefault().Build())
    {
    }

    public MapGenerator(MapGenerationPipeline pipeline)
        : this(new GeometryFactory(), pipeline)
    {
    }

    public MapGenerator(GeometryFactory geometryFactory, MapGenerationPipeline pipeline)
    {
        _geometryFactory = geometryFactory;
        _pipeline = pipeline;
    }

    public GeneratedMap Generate(MapMask mask, MapGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        options ??= new MapGenerationOptions();
        var session = MapGenerationSession.Create(mask, options, _pipeline, _geometryFactory);
        session.RunFull();
        return session.CurrentMap;
    }
}
