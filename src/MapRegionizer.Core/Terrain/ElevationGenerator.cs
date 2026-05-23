using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed class ElevationGenerator
{
    private readonly TectonicFieldBuilder _tectonicFieldBuilder = new();
    private readonly CoastalFieldBuilder _coastalFieldBuilder = new();
    private readonly MountainFieldBuilder _mountainFieldBuilder = new();
    private readonly BasinFieldBuilder _basinFieldBuilder = new();
    private readonly ElevationComposer _elevationComposer;
    private readonly ErosionPass _erosionPass = new();
    private readonly TerrainClassifier _terrainClassifier = new();

    public ElevationGenerator(int seed)
    {
        _elevationComposer = new ElevationComposer(new ElevationNoise(seed));
    }

    public ElevationMap Generate(
        MapMask mask,
        CrustFieldMap crustFields,
        PlateDomainMap plateDomains,
        TectonicBoundaryMap boundaries,
        OrogenProvinceMap orogenProvinces,
        RiftProvinceMap riftProvinces,
        TectonicFeatureMap features,
        WaterBodyTopology? waterBodyTopology,
        ElevationGenerationOptions options)
    {
        var context = ElevationInput.Prepare(mask, crustFields, plateDomains, boundaries, orogenProvinces, riftProvinces, features, waterBodyTopology, options);

        var tectonicFields = _tectonicFieldBuilder.Build(context);
        var coastalFields = _coastalFieldBuilder.Build(context);
        var mountainFields = _mountainFieldBuilder.Build(context, tectonicFields);
        var basinFields = _basinFieldBuilder.Build(context, tectonicFields, mountainFields);

        var raw = _elevationComposer.Compose(context, tectonicFields, coastalFields, mountainFields, basinFields);
        var eroded = _erosionPass.Apply(raw, context, tectonicFields);
        _terrainClassifier.Classify(eroded, context, tectonicFields, basinFields, mountainFields);

        return ElevationMapFactory.CreateBaseTerrain(context, eroded);
    }
}
