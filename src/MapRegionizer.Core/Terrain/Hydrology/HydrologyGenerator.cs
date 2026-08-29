using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;

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
        ArgumentNullException.ThrowIfNull(mask);
        return Generate(
            mask,
            elevation,
            waterBodyTopology,
            generatedLakes,
            waterSurfaces,
            MapSpatialContext.Create(mask.Width, mask.Height, MapSpatialOptions.LegacyDefault()),
            options);
    }

    public HydrologyMap Generate(
        MapMask mask,
        ElevationMap elevation,
        WaterBodyTopology waterBodyTopology,
        GeneratedLakeMap generatedLakes,
        WaterSurfaceMap waterSurfaces,
        MapSpatialContext spatialContext,
        HydrologyGenerationOptions options,
        IHydrologyBoundaryContext? boundaryContext = null,
        int worldOriginX = 0,
        int worldOriginY = 0)
    {
        ArgumentNullException.ThrowIfNull(spatialContext);
        var context = new HydrologyGenerationContext(mask, elevation, waterBodyTopology, generatedLakes, waterSurfaces, options, _seed, spatialContext.GridTopology, boundaryContext, worldOriginX, worldOriginY);
        var lakes = new LakeConnector(_seed);
        var graph = new DrainageGraphBuilder(_seed);
        var basins = new BasinDelineator();
        var overflow = new EndorheicOverflowConnector(_seed);
        var rivers = new RiverNetworkExtractor(_seed, context.GridTopology);

        var hydroSurface = HydrologyMapAssembler.BuildHydroSurface(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes);
        var lakeIds = HydrologyMapAssembler.BuildLakeIdRaster(context.Mask, context.Topology, context.GeneratedLakes);
        var landComponents = HydrologyMapAssembler.BuildLandComponents(context.Mask, context.GeneratedLakes, context.GridTopology);
        var lakeCells = LakeConnector.BuildLakeCells(context.Width, context.Height, lakeIds, context.WaterSurfaces);
        var outlets = lakes.BuildLakeOutlets(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, lakeCells, context.Options, context.GridTopology);
        var lakeNext = LakeConnector.BuildLakeRouting(context.Width, context.Height, lakeCells, outlets, context.GridTopology);
        var localRunoff = graph.BuildLocalRunoff(context);
        var flowState = graph.BuildStabilizedFlow(context, hydroSurface, lakeIds, lakeNext, outlets, localRunoff);

        if (lakes.ForceLakeOutlets(context.Mask, context.Elevation, context.Topology, context.WaterSurfaces, lakeCells, lakeIds, flowState.FlowDirections, flowState.Accumulation, outlets, context.Options, context.GridTopology))
        {
            lakeNext = LakeConnector.BuildLakeRouting(context.Width, context.Height, lakeCells, outlets, context.GridTopology);
            flowState = graph.BuildStabilizedFlow(context, hydroSurface, lakeIds, lakeNext, outlets, localRunoff);
        }

        // Cache outlet lake IDs once — outlets do not change during the overflow loop.
        var outletLakeIdsCache = outlets.Where(o => o.HasOutlet).Select(o => o.LakeId.Value).ToHashSet();

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
                outlets,
                outletLakeIdsCache,
                context.GridTopology);

            if (!changed)
                break;

            flowState = graph.RestabilizeFlow(context, hydroSurface, lakeIds, flowState.FlowDirections, localRunoff);
            basinState = basins.Build(context, flowState.FlowDirections, flowState.Accumulation, lakeIds);
            endorheicPolicies = BasinDelineator.BuildEndorheicRiverPolicies(basinState.Basins, context.Elevation);
        }

        // Boundary contributions are injected once after the local flow graph
        // has stabilized, then propagated along the final downstream paths.
        ApplyIncomingBoundaryFlow(context, flowState.FlowDirections, flowState.Accumulation);
        basinState = basins.Build(context, flowState.FlowDirections, flowState.Accumulation, lakeIds);
        endorheicPolicies = BasinDelineator.BuildEndorheicRiverPolicies(basinState.Basins, context.Elevation);

        var validEndorheicBasins = BasinDelineator.BuildValidEndorheicBasinSet(basinState.Basins, endorheicPolicies, options.MaxEndorheicBasins);
        var allowedRiverBasins = BasinDelineator.BuildAllowedRiverBasinSet(basinState.Basins, validEndorheicBasins);

        // Build upstream cache once after flow directions stabilize.
        var flowDir = flowState.FlowDirections;
        var acc = flowState.Accumulation;
        var w = context.Width;
        var h = context.Height;
        var upCache = BuildUpstreamLists(flowDir, w, h, context.GridTopology);
        var upDepthCache = BuildLongestUpstreamDepths(flowDir, upCache, lakeIds, context.Mask, context.Topology, w, h, context.GridTopology);

        var riverCells = rivers.SelectRiverCells(context, acc, flowDir, basinState.BasinIds, allowedRiverBasins, lakeIds, landComponents, upCache);
        rivers.EnsureInlandSeaInflowRiverCells(context, flowDir, acc, basinState.BasinIds, allowedRiverBasins, lakeIds, riverCells, upCache, upDepthCache);
        var forcedLongPaths = rivers.BuildForcedLongRiverPaths(context, flowDir, acc, basinState.BasinIds, basinState.Basins, allowedRiverBasins, lakeIds, riverCells, upCache, upDepthCache);
        rivers.MarkForcedLongRiverCells(forcedLongPaths, riverCells, lakeIds);
        rivers.AddMajorRiverTributaryCells(context, flowDir, acc, basinState.BasinIds, allowedRiverBasins, lakeIds, riverCells, forcedLongPaths, upCache, upDepthCache);

        var riverTopology = rivers.BuildTopology(flowState.FlowDirections, riverCells, lakeIds, context.Width, context.Height);
        RiverTopologyPlanarityResolver.ResolveCrossingEdges(riverTopology, context.Mask, context.Elevation, hydroSurface, flowState.Accumulation, basinState.BasinIds, lakeIds);

        var mouths = new List<RiverMouth>();
        var riverSegments = rivers.Extract(context, riverTopology, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, lakeIds, landComponents, validEndorheicBasins, mouths, forcedLongPaths, outlets);
        riverSegments = rivers.FinalizeVisibleRivers(riverSegments, context.Width, context.Height, options.MaxEndorheicBasins, context.GridTopology);
        riverSegments = rivers.ResolveVisibleCrossings(riverSegments, context.Width, context.GridTopology);
        riverSegments = rivers.FinalizeVisibleRivers(riverSegments, context.Width, context.Height, options.MaxEndorheicBasins, context.GridTopology);
        riverSegments = ApplyExternalTargets(context, riverSegments);
        mouths.Clear();
        mouths.AddRange(riverSegments
            .Where(river => !IsExternalTerminal(context, river.DrainageTerminal))
            .Select(r => new RiverMouth(r.Id, r.Mouth, r.TargetKind, r.TargetId, r.MouthKind ?? RiverMouthKind.SimpleMouth, r.Discharge)));

        var finalRiverCells = HydrologyMapAssembler.BuildRiverCellRaster(context.Width, context.Height, riverSegments, context.GridTopology);
        return HydrologyMapAssembler.Create(context, hydroSurface, flowState.FlowDirections, flowState.Accumulation, basinState.BasinIds, finalRiverCells, riverSegments, mouths, outlets, basinState.Basins);
    }

    private static void ApplyIncomingBoundaryFlow(HydrologyGenerationContext context, int[] flowDirections, double[] accumulation)
    {
        if (context.Boundary is null || context.Boundary is IsolatedHydrologyBoundaryContext)
            return;

        for (var y = 0; y < context.Height; y++)
        {
            for (var x = 0; x < context.Width; x++)
            {
                if (!GridTopologyMath.IsOpenBoundary(context.GridTopology, new GridPoint(x, y)))
                    continue;
                var worldX = context.WorldOriginX + x;
                var worldY = context.WorldOriginY + y;
                var incoming = context.Boundary.GetIncomingFlow(worldX, worldY);
                // Only incoming flow is injected into accumulation. External
                // elevation/water are consulted by terminal classification,
                // never converted into synthetic runoff.
                var contribution = incoming;
                if (!double.IsFinite(contribution) || contribution <= 0)
                    continue;

                var current = y * context.Width + x;
                var remaining = contribution;
                var visited = new HashSet<int>();
                while (remaining > 0 && visited.Add(current))
                {
                    accumulation[current] += remaining;
                    var next = HydrologyGridMath.DownstreamIndex(current, flowDirections[current], context.GridTopology);
                    if (next < 0 || next == current)
                        break;
                    current = next;
                    remaining *= 0.995;
                }
            }
        }

    }

    private static bool IsExternalTerminal(HydrologyGenerationContext context, GridPoint terminal)
    {
        if (context.Boundary is null || context.Boundary is IsolatedHydrologyBoundaryContext)
            return false;

        if (!GridTopologyMath.IsOpenBoundary(context.GridTopology, terminal))
            return false;

        var worldX = context.WorldOriginX + terminal.X;
        var worldY = context.WorldOriginY + terminal.Y;
        var target = context.Boundary.GetExternalDownstreamTarget(worldX, worldY);
        if (target is not null || context.Boundary.GetExternalWaterInfluence(worldX, worldY))
            return true;

        var externalElevation = context.Boundary.GetExternalElevation(worldX, worldY);
        var localElevation = context.Elevation.GetElevation(terminal.X, terminal.Y);
        return context.Boundary.TreatUnspecifiedBoundaryAsExternal &&
               double.IsFinite(externalElevation) && localElevation > externalElevation;
    }

    private static List<RiverSegment> ApplyExternalTargets(
        HydrologyGenerationContext context,
        List<RiverSegment> rivers)
    {
        if (context.Boundary is null || context.Boundary is IsolatedHydrologyBoundaryContext)
            return rivers;

        return rivers.Select(river =>
        {
            var terminal = river.DrainageTerminal;
            if (!GridTopologyMath.IsOpenBoundary(context.GridTopology, terminal))
                return river;

            var target = context.Boundary.GetExternalDownstreamTarget(
                context.WorldOriginX + terminal.X,
                context.WorldOriginY + terminal.Y);
            return target is null
                ? river
                : river with { TargetKind = target.Kind, TargetId = target.TargetId };
        }).ToList();
    }
}
