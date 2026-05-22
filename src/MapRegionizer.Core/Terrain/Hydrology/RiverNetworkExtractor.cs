using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverNetworkExtractor
{
    private readonly RiverSourceSelector _sourceSelector;
    private readonly RiverSegmentExtractor _segmentExtractor;

    public RiverNetworkExtractor(int seed)
    {
        _sourceSelector = new RiverSourceSelector(seed);
        _segmentExtractor = new RiverSegmentExtractor(new ChannelPathTracer(seed));
    }

    public byte[] SelectRiverCells(
        HydrologyGenerationContext context,
        double[] accumulation,
        int[] flowDirections,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        LandComponentMap landComponents) =>
        _sourceSelector.SelectRiverCells(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, accumulation, flowDirections, basinIds, allowedRiverBasins, lakeIds, landComponents, context.Options);

    public void EnsureInlandSeaInflowRiverCells(
        HydrologyGenerationContext context,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells) =>
        ForcedLongRiverPlanner.EnsureInlandSeaInflowRiverCells(context.Mask, context.Topology, context.WaterSurfaces, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells, context.Options);

    public IReadOnlyDictionary<int, IReadOnlyList<int>> BuildForcedLongRiverPaths(
        HydrologyGenerationContext context,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        IReadOnlyList<DrainageBasin> basins,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells) =>
        ForcedLongRiverPlanner.BuildForcedLongRiverPaths(context.Mask, context.Topology, flowDirections, accumulation, basinIds, basins, allowedRiverBasins, lakeIds, riverCells, context.Options);

    public void MarkForcedLongRiverCells(IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths, byte[] riverCells, int[] lakeIds) =>
        ForcedLongRiverPlanner.MarkForcedLongRiverCells(forcedLongPaths, riverCells, lakeIds);

    public void AddMajorRiverTributaryCells(
        HydrologyGenerationContext context,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths) =>
        MajorTributaryInjector.AddMajorRiverTributaryCells(context.Mask, context.Topology, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells, forcedLongPaths, context.Options);

    public RiverTopologyGraph BuildTopology(int[] flowDirections, byte[] riverCells, int[] lakeIds, int width, int height) =>
        RiverTopologyGraph.Build(width, height, flowDirections, riverCells, lakeIds);

    public List<RiverSegment> Extract(
        HydrologyGenerationContext context,
        RiverTopologyGraph topologyGraph,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        int[] lakeIds,
        LandComponentMap landComponents,
        HashSet<int> validEndorheicBasins,
        List<RiverMouth> mouths,
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths,
        IReadOnlyList<LakeOutlet> outlets) =>
        _segmentExtractor.ExtractRivers(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, topologyGraph, flowDirections, accumulation, basinIds, lakeIds, landComponents, validEndorheicBasins, context.Options, mouths, forcedLongPaths, outlets);

    public List<RiverSegment> FinalizeVisibleRivers(IReadOnlyList<RiverSegment> rivers, int width, int height) =>
        RiverSegmentExtractor.FinalizeVisibleRivers(rivers, width, height);

    public List<RiverSegment> ResolveVisibleCrossings(IReadOnlyList<RiverSegment> rivers, int width) =>
        VisibleRiverCrossingRepairer.ResolvePolylineCrossings(rivers, width);
}
