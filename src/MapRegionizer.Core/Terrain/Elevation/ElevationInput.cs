using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed record ElevationInput(
    MapMask Mask,
    CrustFieldMap CrustFields,
    PlateDomainMap PlateDomains,
    TectonicBoundaryMap Boundaries,
    OrogenProvinceMap OrogenProvinces,
    RiftProvinceMap RiftProvinces,
    TectonicFeatureMap Features,
    WaterBodyTopology? WaterBodyTopology,
    ElevationGenerationOptions Options,
    int Length,
    double[] DistanceToLand,
    double[] DistanceToWater,
    double[] LandEnclosure,
    IReadOnlyDictionary<int, PlateDomain> Domains,
    double ShelfWidth,
    double InlandScale,
    double DeepOceanScale)
{
    public static ElevationInput Prepare(
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
        var minDimension = Math.Max(1, Math.Min(mask.Width, mask.Height));
        return new ElevationInput(
            mask,
            crustFields,
            plateDomains,
            boundaries,
            orogenProvinces,
            riftProvinces,
            features,
            waterBodyTopology,
            options,
            mask.Width * mask.Height,
            ElevationGridMath.ComputeDistance(mask, sourceIsLand: true),
            ElevationGridMath.ComputeDistance(mask, sourceIsLand: false),
            ElevationGridMath.BuildLandEnclosureField(mask),
            plateDomains.Domains.ToDictionary(d => d.Id.Value),
            Math.Max(2.0, minDimension * 0.035 * options.ShelfWidthFactor),
            Math.Max(4.0, minDimension * 0.16),
            Math.Max(5.0, minDimension * 0.24));
    }
}
