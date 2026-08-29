#pragma warning disable CS0618

using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Regions;

/// <summary>
/// An editable, not necessarily valid, proposal for a region coverage.
/// It is deliberately separate from <see cref="MapRegion"/>: only the
/// canonicalizer may turn a draft into pipeline <c>RawRegions</c>.
/// </summary>
public sealed record RegionDraft(IReadOnlyList<RegionDraftRegion> Regions)
{
    public static RegionDraft FromRegions(IEnumerable<MapRegion> regions, RegionDraftOrigin origin = RegionDraftOrigin.Manual) => new(
        regions.Select(region => new RegionDraftRegion(region.Id, region.LandmassId, region.Shape.Copy(), origin)).ToList());
}

/// <summary>
/// A draft face may be incomplete or geometrically invalid while it is being edited.
/// A missing identifier is assigned deterministically during canonicalization.
/// </summary>
public sealed record RegionDraftRegion(
    RegionId? Id,
    LandmassId? LandmassId,
    Geometry Shape,
    RegionDraftOrigin Origin = RegionDraftOrigin.Manual,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public enum RegionDraftOrigin
{
    Manual,
    Generated,
    GeneratedAndEdited
}

/// <summary>
/// Format-neutral metadata for a portable region-draft document. Serialization is
/// owned by an adapter; Core owns compatibility and topology meaning.
/// </summary>
public sealed record RegionDraftDocument(
    string SchemaVersion,
    MapProjectionMode ProjectionMode,
    MapBounds Bounds,
    string MaskFingerprint,
    string LandmassFingerprint,
    bool ApplyBoundaryDistortion,
    RegionDraft Draft,
    MapSpatialReference? SpatialReference = null)
{
    /// <summary>
    /// The current portable authoring format.  Version 1.0 remains readable
    /// through the adapter migration path, but every newly written document
    /// carries the complete canonical spatial descriptor.
    /// </summary>
    public const string CurrentSchemaVersion = "2.0";

    /// <summary>
    /// True when the document was read from the historical v1 shape.  Keeping
    /// this bit separate from the compatibility profile lets consumers show a
    /// migration notice without changing the historical enum semantics.
    /// </summary>
    public bool IsMigratedFromV1 { get; init; }

    /// <summary>
    /// Canonical grid identity used by draft compatibility checks.  Output
    /// coordinates deliberately do not belong here: a draft can be exported
    /// as grid, geographic, or Web Mercator without becoming a different
    /// authoring document.
    /// </summary>
    public MapSpatialReference CanonicalSpatialReference => SpatialReference
        ?? throw new InvalidOperationException("The region draft does not contain a canonical spatial reference.");
}

public enum RegionDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Machine-readable result of validating or repairing a region draft.
/// Error diagnostics prevent finalization.
/// </summary>
public sealed record RegionDiagnostic(
    string Code,
    RegionDiagnosticSeverity Severity,
    string Message,
    RegionId? RegionId = null,
    LandmassId? LandmassId = null);

public sealed record RegionCanonicalizationResult(
    IReadOnlyList<MapRegion> Regions,
    IReadOnlyList<RegionDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Diagnostics.All(diagnostic => diagnostic.Severity != RegionDiagnosticSeverity.Error);
}

public sealed class RegionCanonicalizationException(IReadOnlyList<RegionDiagnostic> diagnostics)
    : InvalidOperationException("The region draft cannot be finalized.")
{
    public IReadOnlyList<RegionDiagnostic> Diagnostics { get; } = diagnostics;
}
