namespace MapRegionizer.Core.ManualAuthoring;

public enum ManualMapDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A stable, presentation-neutral diagnostic for manual-map authoring.
/// </summary>
public sealed record ManualMapDiagnostic(
    string Code,
    ManualMapDiagnosticSeverity Severity,
    string Message,
    int? RegionId = null,
    int? VertexId = null,
    int? EdgeIndex = null)
{
    public bool IsBlocking => Severity == ManualMapDiagnosticSeverity.Error;
}

public sealed record ManualMapValidationResult(
    IReadOnlyList<ManualMapDiagnostic> Diagnostics,
    IReadOnlyDictionary<int, NetTopologySuite.Geometries.Polygon> RegionPolygons)
{
    public bool IsSuccessful => Diagnostics.All(diagnostic => !diagnostic.IsBlocking);
}

public sealed record ManualMapFinalizationResult(
    MapRegionizer.Core.Domain.MapMask DerivedMask,
    IReadOnlyList<MapRegionizer.Core.Domain.Landmass> Landmasses,
    MapRegionizer.Core.Regions.RegionDraft RegionDraft,
    IReadOnlyList<ManualMapDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Diagnostics.All(diagnostic => !diagnostic.IsBlocking);

    public int RegionCount => RegionDraft.Regions.Count;
    public int LandmassCount => Landmasses.Count;
}
