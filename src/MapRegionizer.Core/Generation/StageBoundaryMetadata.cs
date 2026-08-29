namespace MapRegionizer.Core.Generation;

/// <summary>How a stage obtains information outside its working raster.</summary>
public enum SpatialBoundaryDependency
{
    FiniteLocal,
    Propagating,
    GlobalContextDependent
}

/// <summary>Explicit boundary contract exposed by spatially-sensitive stages.</summary>
public readonly record struct StageBoundaryMetadata
{
    public StageBoundaryMetadata(SpatialBoundaryDependency dependency, int requiredHaloCells = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredHaloCells);
        if (dependency != SpatialBoundaryDependency.FiniteLocal && requiredHaloCells != 0)
            throw new ArgumentException("Propagating and global-context stages cannot declare a finite halo.", nameof(requiredHaloCells));
        Dependency = dependency;
        RequiredHaloCells = requiredHaloCells;
    }

    public SpatialBoundaryDependency Dependency { get; }
    public int RequiredHaloCells { get; }

    public static StageBoundaryMetadata Finite(int radius) => new(SpatialBoundaryDependency.FiniteLocal, radius);
    public static StageBoundaryMetadata Propagating() => new(SpatialBoundaryDependency.Propagating);
    public static StageBoundaryMetadata Global() => new(SpatialBoundaryDependency.GlobalContextDependent);
}

public interface ISpatialBoundaryAwareStage
{
    StageBoundaryMetadata BoundaryMetadata { get; }
}

public static class StageBoundaryContracts
{
    public static StageBoundaryMetadata Get(IMapGenerationStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage is ISpatialBoundaryAwareStage aware)
            return aware.BoundaryMetadata;

        return stage.Id switch
        {
            MapStageIds.GenerateTectonicHistory or MapStageIds.GenerateTectonicWorldContext => StageBoundaryMetadata.Global(),
            MapStageIds.GenerateClimate => StageBoundaryMetadata.Propagating(),
            MapStageIds.GenerateHydrology => StageBoundaryMetadata.Propagating(),
            MapStageIds.GenerateTectonicFeatures => StageBoundaryMetadata.Global(),
            _ => StageBoundaryMetadata.Finite(0)
        };
    }

    public static IReadOnlyList<StageBoundaryMetadata> For(IEnumerable<IMapGenerationStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        return stages.Select(Get).ToArray();
    }

    public static int RequiredFiniteHalo(IEnumerable<IMapGenerationStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var metadata = stages.Select(Get).ToArray();
        return metadata
            .Where(item => item.Dependency == SpatialBoundaryDependency.FiniteLocal)
            .Select(item => item.RequiredHaloCells)
            .DefaultIfEmpty(0)
            .Max();
    }
}
