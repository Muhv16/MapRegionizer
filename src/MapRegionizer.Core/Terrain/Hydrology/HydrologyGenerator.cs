using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Terrain;

internal sealed class HydrologyGenerator
{
    private readonly int _seed;

    public HydrologyGenerator(int seed)
    {
        _seed = seed;
    }

    public HydrologyMap Generate(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology waterBodyTopology,
        GeneratedLakeMap generatedLakes,
        WaterSurfaceMap waterSurfaces,
        HydrologyGenerationOptions options)
    {
        var context = new HydrologyGenerationContext(mask, elevation, waterBodyTopology, generatedLakes, waterSurfaces, options, _seed);
        var lakes = new LakeConnector(_seed);
        var graph = new DrainageGraphBuilder(_seed);
        var basins = new BasinDelineator();
        var overflow = new EndorheicOverflowConnector(_seed);
        var rivers = new RiverNetworkExtractor(_seed);

        var hydroSurface = HydrologyMapAssembler.BuildHydroSurface(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes);
        var lakeIds = HydrologyMapAssembler.BuildLakeIdRaster(context.Mask, context.Topology, context.GeneratedLakes);
        var landComponents = HydrologyMapAssembler.BuildLandComponents(context.Mask, context.GeneratedLakes);
        var lakeCells = LakeConnector.BuildLakeCells(context.Width, context.Height, lakeIds, context.WaterSurfaces);
        var outlets = lakes.BuildLakeOutlets(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, lakeCells, context.Options);
        var lakeNext = LakeConnector.BuildLakeRouting(context.Width, context.Height, lakeCells, outlets);
        var localRunoff = graph.BuildLocalRunoff(context);
        var flowState = graph.BuildStabilizedFlow(context, hydroSurface, lakeIds, lakeNext, outlets, localRunoff);

        if (lakes.ForceLakeOutlets(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, lakeCells, lakeIds, flowState.FlowDirections, flowState.Accumulation, outlets, context.Options))
        {
            lakeNext = LakeConnector.BuildLakeRouting(context.Width, context.Height, lakeCells, outlets);
            flowState = graph.BuildStabilizedFlow(context, hydroSurface, lakeIds, lakeNext, outlets, localRunoff);
        }

        var basinState = basins.Build(context, flowState.FlowDirections, flowState.Accumulation, lakeIds);
        var endorheicPolicies = BasinDelineator.BuildEndorheicRiverPolicies(basinState.Basins, context.Elevation);

        for (var overflowPass = 0; overflowPass < 8; overflowPass++)
        {
            var changed = overflow.ForceEndorheicOverflow(
                context.Mask,
                context.Elevation,
                context.Topology,
                hydroSurface,
                lakeIds,
                flowState.FlowDirections,
                flowState.Accumulation,
                basinState.BasinIds,
                basinState.Basins,
                endorheicPolicies,
                outlets);

            if (!changed)
                break;

            flowState = graph.RestabilizeFlow(context, hydroSurface, lakeIds, flowState.FlowDirections, localRunoff);
            basinState = basins.Build(context, flowState.FlowDirections, flowState.Accumulation, lakeIds);
            endorheicPolicies = BasinDelineator.BuildEndorheicRiverPolicies(basinState.Basins, context.Elevation);
        }

        var validEndorheicBasins = BasinDelineator.BuildValidEndorheicBasinSet(basinState.Basins, endorheicPolicies);
        var allowedRiverBasins = BasinDelineator.BuildAllowedRiverBasinSet(basinState.Basins, validEndorheicBasins);
        var riverCells = rivers.SelectRiverCells(context, flowState.Accumulation, flowState.FlowDirections, basinState.BasinIds, allowedRiverBasins, lakeIds, landComponents);
        rivers.EnsureInlandSeaInflowRiverCells(context, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, allowedRiverBasins, lakeIds, riverCells);
        var forcedLongPaths = rivers.BuildForcedLongRiverPaths(context, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, basinState.Basins, allowedRiverBasins, lakeIds, riverCells);
        rivers.MarkForcedLongRiverCells(forcedLongPaths, riverCells, lakeIds);
        rivers.AddMajorRiverTributaryCells(context, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, allowedRiverBasins, lakeIds, riverCells, forcedLongPaths);

        var riverTopology = rivers.BuildTopology(flowState.FlowDirections, riverCells, lakeIds, context.Width, context.Height);
        RiverTopologyPlanarityResolver.ResolveCrossingEdges(riverTopology, context.Mask, context.Elevation, hydroSurface, flowState.Accumulation, basinState.BasinIds, lakeIds);

        var mouths = new List<RiverMouth>();
        var riverSegments = rivers.Extract(context, riverTopology, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, lakeIds, landComponents, validEndorheicBasins, mouths, forcedLongPaths, outlets);
        riverSegments = rivers.FinalizeVisibleRivers(riverSegments, context.Width, context.Height);
        riverSegments = rivers.ResolveVisibleCrossings(riverSegments, context.Width);
        riverSegments = rivers.FinalizeVisibleRivers(riverSegments, context.Width, context.Height);
        riverSegments = rivers.ResolveVisibleCrossings(riverSegments, context.Width);
        riverSegments = rivers.FinalizeVisibleRivers(riverSegments, context.Width, context.Height);
        mouths.Clear();
        mouths.AddRange(riverSegments.Select(r => new RiverMouth(r.Id, r.Mouth, r.TargetKind, r.TargetId, r.MouthKind ?? RiverMouthKind.SimpleMouth, r.Discharge)));

        var finalRiverCells = HydrologyMapAssembler.BuildRiverCellRaster(context.Width, context.Height, riverSegments);
        return HydrologyMapAssembler.Create(context, hydroSurface, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, finalRiverCells, riverSegments, mouths, outlets, basinState.Basins);
    }
}
