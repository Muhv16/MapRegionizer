using MapRegionizer.Core.Terrain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateElevationStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateElevation;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.CrustFields,
        MapDataKeys.PlateDomains,
        MapDataKeys.TectonicBoundaries,
        MapDataKeys.OrogenProvinces,
        MapDataKeys.RiftProvinces,
        MapDataKeys.TectonicFeatures,
        MapDataKeys.WaterBodyTopology
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Elevation };

    public void Execute(MapGenerationContext context)
    {
        var crustFields = context.CrustFields ?? throw new InvalidOperationException("Crust fields are required.");
        var plateDomains = context.PlateDomains ?? throw new InvalidOperationException("Plate domains are required.");
        var boundaries = context.TectonicBoundaries ?? throw new InvalidOperationException("Tectonic boundaries are required.");
        var orogenProvinces = context.OrogenProvinces ?? throw new InvalidOperationException("Orogen provinces are required.");
        var riftProvinces = context.RiftProvinces ?? throw new InvalidOperationException("Rift provinces are required.");
        var features = context.TectonicFeatures ?? throw new InvalidOperationException("Tectonic features are required.");
        var waterBodyTopology = context.WaterBodyTopology ?? throw new InvalidOperationException("Water body topology is required.");
        var generator = new ElevationGenerator(CreateElevationSeed(context));
        context.Elevation = generator.Generate(context.Mask, crustFields, plateDomains, boundaries, orogenProvinces, riftProvinces, features, waterBodyTopology, context.Options.Elevation);
    }

    private static int CreateElevationSeed(MapGenerationContext context)
    {
        if (!context.Options.Seed.HasValue)
            return context.Random.Next();

        unchecked
        {
            return context.Options.Seed.Value * 397 ^ 0x4D52454C;
        }
    }
}
