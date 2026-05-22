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
        var flowDirections = _flowDirections.BuildFlowDirections(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, lakeNext, outlets, context.Options);
        return RestabilizeFlow(context, hydroSurface, lakeIds, flowDirections, localRunoff);
    }

    public HydrologyFlowState RestabilizeFlow(
        HydrologyGenerationContext context,
        double[] hydroSurface,
        int[] lakeIds,
        int[] flowDirections,
        double[] localRunoff)
    {
        _flowDirections.ResolveInvalidDryTerminals(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, flowDirections, context.Options);
        FlowDirectionSolver.BreakCycles(flowDirections, context.Width, context.Height);
        _flowDirections.ResolveInvalidDryTerminals(context.Mask, context.Elevation, context.Topology, context.GeneratedLakes, hydroSurface, lakeIds, flowDirections, context.Options);
        FlowDirectionSolver.BreakCycles(flowDirections, context.Width, context.Height);
        var accumulation = FlowAccumulationSolver.AccumulateFlow(flowDirections, localRunoff, context.Width, context.Height);
        return new HydrologyFlowState(flowDirections, accumulation);
    }
}
