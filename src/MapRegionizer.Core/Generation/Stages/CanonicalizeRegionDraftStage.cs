using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation.Stages;

/// <summary>Converts the sole selected draft source into the canonical RawRegions branch.</summary>
public sealed class CanonicalizeRegionDraftStage : IMapGenerationStage
{
    public string Id => MapStageIds.CanonicalizeRegionDraft;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Landmasses,
        MapDataKeys.RegionDraft
    };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.RawRegions };

    public void Execute(MapGenerationContext context)
    {
        if (context.RegionDraft is null)
            throw new InvalidOperationException("The region draft source did not provide a draft.");

        var result = new RegionCoverageCanonicalizer().Canonicalize(context.Landmasses, context.RegionDraft);
        context.RegionDiagnostics = result.Diagnostics;
        if (!result.IsSuccessful)
            throw new RegionCanonicalizationException(result.Diagnostics);

        context.RawRegions.AddRange(result.Regions);
    }
}
