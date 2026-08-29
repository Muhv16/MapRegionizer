using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.Terrain;

internal sealed class RiverNetworkExtractor
{
    private readonly RiverSourceSelector _sourceSelector;
    private readonly RiverSegmentExtractor _segmentExtractor;

    public RiverNetworkExtractor(int seed, IGridTopology? gridTopology = null)
    {
        _sourceSelector = new RiverSourceSelector(seed);
        _segmentExtractor = new RiverSegmentExtractor(new ChannelPathTracer(seed, gridTopology));
    }

    public byte[] SelectRiverCells(
        HydrologyGenerationContext context,
        double[] accumulation,
        int[] flowDirections,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        LandComponentMap landComponents,
        List<int>[]? upstreamCache = null) =>
        _sourceSelector.SelectRiverCells(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, accumulation, flowDirections, basinIds, allowedRiverBasins, lakeIds, landComponents, context.Options, upstreamCache, context.GridTopology);

    public void EnsureInlandSeaInflowRiverCells(
        HydrologyGenerationContext context,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        List<int>[]? upstreamCache = null,
        int[]? upstreamDepthsCache = null) =>
        ForcedLongRiverPlanner.EnsureInlandSeaInflowRiverCells(context.Mask, context.Topology, context.WaterSurfaces, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells, context.Options, upstreamCache, upstreamDepthsCache, context.GridTopology);

    public IReadOnlyDictionary<int, IReadOnlyList<int>> BuildForcedLongRiverPaths(
        HydrologyGenerationContext context,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        IReadOnlyList<DrainageBasin> basins,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        List<int>[]? upstreamCache = null,
        int[]? upstreamDepthsCache = null) =>
        ForcedLongRiverPlanner.BuildForcedLongRiverPaths(context.Mask, context.Topology, flowDirections, accumulation, basinIds, basins, allowedRiverBasins, lakeIds, riverCells, context.Options, upstreamCache, upstreamDepthsCache, context.GridTopology);

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
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths,
        List<int>[]? upstreamCache = null,
        int[]? upstreamDepthsCache = null) =>
        MajorTributaryInjector.AddMajorRiverTributaryCells(context.Mask, context.Topology, flowDirections, accumulation, basinIds, allowedRiverBasins, lakeIds, riverCells, forcedLongPaths, context.Options, upstreamCache, upstreamDepthsCache, context.GridTopology);

    public RiverTopologyGraph BuildTopology(int[] flowDirections, byte[] riverCells, int[] lakeIds, int width, int height) =>
        RiverTopologyGraph.Build(width, height, flowDirections, riverCells, lakeIds, _segmentExtractor.GridTopology);

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

    // Width-only overloads preserve the old helper API; the hydrology stage
    // invokes the topology-aware overloads below.
    public List<RiverSegment> FinalizeVisibleRivers(IReadOnlyList<RiverSegment> rivers, int width, int height, int maxEndorheicCount = int.MaxValue) =>
        FinalizeVisibleRivers(rivers, width, height, maxEndorheicCount, _segmentExtractor.GridTopology);

    public List<RiverSegment> FinalizeVisibleRivers(
        IReadOnlyList<RiverSegment> rivers,
        int width,
        int height,
        int maxEndorheicCount,
        IGridTopology? gridTopology) =>
        RiverSegmentExtractor.FinalizeVisibleRivers(
            rivers,
            width,
            height,
            maxEndorheicCount,
            gridTopology ?? new CylindricalXTopology(width, height));

    public List<RiverSegment> ResolveVisibleCrossings(IReadOnlyList<RiverSegment> rivers, int width) =>
        ResolveVisibleCrossings(rivers, width, _segmentExtractor.GridTopology);

    public List<RiverSegment> ResolveVisibleCrossings(IReadOnlyList<RiverSegment> rivers, int width, IGridTopology? gridTopology) =>
        VisibleRiverCrossingRepairer.ResolvePolylineCrossings(
            rivers,
            width,
            gridTopology ?? new CylindricalXTopology(width, Math.Max(1, rivers.SelectMany(r => r.Cells).Select(c => c.Y).DefaultIfEmpty().Max() + 1)));
}
