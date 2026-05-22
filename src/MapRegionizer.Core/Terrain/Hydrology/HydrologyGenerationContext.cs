using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed record HydrologyGenerationContext(
    MapMask Mask,
    ElevationMap Elevation,
    WaterBodyTopology Topology,
    GeneratedLakeMap GeneratedLakes,
    WaterSurfaceMap WaterSurfaces,
    HydrologyGenerationOptions Options,
    int Seed)
{
    public int Width => Mask.Width;
    public int Height => Mask.Height;
}

internal sealed record HydrologyFlowState(int[] FlowDirections, double[] Accumulation);

internal sealed record HydrologyBasinState(int[] BasinIds, IReadOnlyList<DrainageBasin> Basins);

internal sealed record LandComponentMap(int[] ComponentIds, IReadOnlyList<LandComponent> Components);

internal sealed record LandComponent(int Id, int CellCount);

internal sealed record TargetRef(DrainageTargetKind Kind, int? TargetId);

internal enum EndorheicRiverPolicy
{
    Suppress,
    EphemeralSmall,
    ForceOverflow
}

internal enum MountainSourceSide
{
    None,
    North,
    South,
    West,
    East
}