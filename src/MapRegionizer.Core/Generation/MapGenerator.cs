using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation.Stages;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerator
{
    private readonly GeometryFactory _geometryFactory;

    public MapGenerator()
        : this(new GeometryFactory())
    {
    }

    public MapGenerator(GeometryFactory geometryFactory)
    {
        _geometryFactory = geometryFactory;
    }

    public GeneratedMap Generate(MapMask mask, MapGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        options ??= new MapGenerationOptions();
        options.Validate();

        var random = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var context = new MapGenerationContext(mask, options, _geometryFactory, random);
        var pipeline = CreateDefaultPipeline();
        pipeline.Execute(context);

        return context.ToGeneratedMap();
    }

    private static MapGenerationPipeline CreateDefaultPipeline()
    {
        return new MapGenerationPipeline(new IMapGenerationStage[]
        {
            new ExtractLandmassesStage(),
            new ExtractWaterBodiesStage(),
            new GenerateRegionsStage(),
            new DistortRegionBoundariesStage()
        });
    }
}
