namespace MapRegionizer.Core.Terrain;

internal sealed record TectonicFields(
    double[] RidgeMask,
    double[] CollisionMask,
    double[] MassifMask,
    double[] ForelandMask,
    double[] SubductionMask,
    double[] PassiveMask,
    double[] Uplift,
    double[] Subsidence,
    double[] Volcanism,
    double[] HeatFlow,
    double[] SedimentSupply,
    double[] OrogenProvince,
    double[] OrogenStrength,
    double[] RiftProvince,
    double[] RiftGraben,
    double[] RiftShoulder,
    double[] RiftHeat,
    double[] RiftBreakup);

internal sealed record MountainFields(
    double[] RidgeContinuity,
    double[] MountainPassPotential,
    double[] FoothillInfluence);

internal sealed record BasinFields(double[] BasinInfluence);

internal sealed record ElevationRasterSet(
    double[] Elevation,
    double[] BaseElevation,
    double[] TectonicElevation,
    double[] Roughness,
    double[] ErosionMask,
    byte[] TerrainClasses,
    double[] MountainPassPotential,
    double[] RidgeContinuity,
    double[] FoothillInfluence,
    double[] BasinInfluence);
