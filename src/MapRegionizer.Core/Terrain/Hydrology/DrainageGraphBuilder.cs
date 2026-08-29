using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Terrain;

internal sealed class DrainageGraphBuilder
{
    private readonly FlowDirectionSolver _flowDirections;

    public DrainageGraphBuilder(int seed)
    {
        _flowDirections = new FlowDirectionSolver(seed);
    }

    public double[] BuildLocalRunoff(HydrologyGenerationContext context) =>
        FlowAccumulationSolver.BuildLocalRunoff(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes);

    public HydrologyFlowState BuildStabilizedFlow(
        HydrologyGenerationContext context,
        double[] hydroSurface,
        int[] lakeIds,
        int[] lakeNext,
        IReadOnlyList<LakeOutlet> outlets,
        double[] localRunoff)
    {
        var flowDirections = _flowDirections.BuildFlowDirections(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, lakeNext, outlets, context.Options, context.GridTopology);
        return RestabilizeFlow(context, hydroSurface, lakeIds, flowDirections, localRunoff);
    }

    public HydrologyFlowState RestabilizeFlow(
        HydrologyGenerationContext context,
        double[] hydroSurface,
        int[] lakeIds,
        int[] flowDirections,
        double[] localRunoff)
    {
        _flowDirections.ResolveInvalidDryTerminals(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, flowDirections, context.Options, context.GridTopology);
        FlowDirectionSolver.BreakCycles(flowDirections, context.Width, context.Height, context.GridTopology);
        _flowDirections.ResolveInvalidDryTerminals(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, flowDirections, context.Options, context.GridTopology);
        FlowDirectionSolver.BreakCycles(flowDirections, context.Width, context.Height, context.GridTopology);
        var accumulation = FlowAccumulationSolver.AccumulateFlow(flowDirections, localRunoff, context.Width, context.Height, context.GridTopology);
        if (_flowDirections.RegularizeLongStraightRuns(context.Mask, context.Elevation, context.Topology, hydroSurface, lakeIds, flowDirections, accumulation, context.Options, context.GridTopology))
        {
            _flowDirections.ResolveInvalidDryTerminals(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, flowDirections, context.Options, context.GridTopology);
            FlowDirectionSolver.BreakCycles(flowDirections, context.Width, context.Height, context.GridTopology);
            accumulation = FlowAccumulationSolver.AccumulateFlow(flowDirections, localRunoff, context.Width, context.Height, context.GridTopology);
        }

        return new HydrologyFlowState(flowDirections, accumulation);
    }
}
