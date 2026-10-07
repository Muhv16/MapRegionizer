using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;
using System.Threading;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Converts manual geography into the normal vector and raster inputs of the
/// MapRegionizer pipeline. Vector landmasses remain authoritative coastline
/// geometry; the mask is only a derived simulation representation.
/// </summary>
public sealed class ManualMapDraftFinalizer
{
    private readonly GeometryFactory _geometryFactory;
    private readonly ManualMapDraftValidator _validator;
    private readonly ManualLandmassBuilder _landmassBuilder;
    private readonly ManualMapRasterizer _rasterizer;

    public ManualMapDraftFinalizer(GeometryFactory? geometryFactory = null)
    {
        _geometryFactory = geometryFactory ?? new GeometryFactory();
        _validator = new ManualMapDraftValidator(_geometryFactory);
        _landmassBuilder = new ManualLandmassBuilder();
        _rasterizer = new ManualMapRasterizer(_geometryFactory);
    }

    public ManualMapFinalizationResult FinalizeDraft(ManualMapDraft draft, MapSpatialReference spatialReference)
        => FinalizeDraft(draft, spatialReference, CancellationToken.None);

    public ManualMapFinalizationResult FinalizeDraft(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        cancellationToken.ThrowIfCancellationRequested();
        spatialReference.Validate();

        if (draft.GridWidth != spatialReference.GridWidth || draft.GridHeight != spatialReference.GridHeight)
        {
            var gridDiagnostics = new[]
            {
                new ManualMapDiagnostic(
                    "grid-size-mismatch",
                    ManualMapDiagnosticSeverity.Error,
                    "The manual draft dimensions must match the spatial reference grid.")
            };
            return EmptyResult(spatialReference, gridDiagnostics);
        }

        var validation = _validator.Validate(draft, spatialReference, cancellationToken);
        if (!validation.IsSuccessful)
            return EmptyResult(spatialReference, validation.Diagnostics);

        cancellationToken.ThrowIfCancellationRequested();
        var polygonsById = validation.RegionPolygons;
        IReadOnlyList<Landmass> landmasses;
        try
        {
            landmasses = _landmassBuilder.Build(polygonsById.Values, _geometryFactory);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (TopologyException exception)
        {
            return EmptyResult(spatialReference, validation.Diagnostics.Concat([
                new ManualMapDiagnostic(
                    "landmass-union-failed",
                    ManualMapDiagnosticSeverity.Error,
                    $"The manual landmass union could not be completed: {exception.Message}")
            ]).ToArray());
        }

        var regionDraftRegions = new List<RegionDraftRegion>(polygonsById.Count);
        var diagnostics = validation.Diagnostics.ToList();

        foreach (var face in draft.Regions.OrderBy(region => region.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!polygonsById.TryGetValue(face.Id, out var polygon))
                continue;

            Landmass? landmass = null;
            foreach (var candidate in landmasses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!candidate.Shape.Covers(polygon))
                    continue;

                landmass = candidate;
                break;
            }

            if (landmass is null)
            {
                diagnostics.Add(new ManualMapDiagnostic(
                    "landmass-assignment-failed",
                    ManualMapDiagnosticSeverity.Error,
                    $"Region {face.Id} could not be assigned to a deterministic landmass.",
                    RegionId: face.Id));
                continue;
            }

            regionDraftRegions.Add(new RegionDraftRegion(
                new RegionId(face.Id),
                landmass.Id,
                polygon,
                RegionDraftOrigin.Manual,
                face.Name));
        }

        var regionDraft = new RegionDraft(regionDraftRegions);
        RegionCanonicalizationResult canonical;
        try
        {
            canonical = new RegionCoverageCanonicalizer().Canonicalize(landmasses, regionDraft, cancellationToken);
        }
        catch (TopologyException exception)
        {
            diagnostics.Add(new ManualMapDiagnostic(
                "canonicalization-failed",
                ManualMapDiagnosticSeverity.Error,
                $"The manual regions could not be checked against the standard geometry contract: {exception.Message}"));
            return EmptyResult(spatialReference, diagnostics);
        }

        diagnostics.AddRange(canonical.Diagnostics.Select(ToManualDiagnostic));
        if (!canonical.IsSuccessful)
        {
            diagnostics.Add(new ManualMapDiagnostic(
                "canonicalization-failed",
                ManualMapDiagnosticSeverity.Error,
                "The manual regions do not satisfy the standard region geometry contract."));
            return EmptyResult(spatialReference, diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var derivedMask = _rasterizer.Rasterize(landmasses, spatialReference, cancellationToken);
        return new ManualMapFinalizationResult(derivedMask, landmasses, regionDraft, diagnostics);
    }

    private ManualMapFinalizationResult EmptyResult(
        MapSpatialReference spatialReference,
        IReadOnlyList<ManualMapDiagnostic> diagnostics)
    {
        var emptyMask = new MapMask(
            GridWindow.FromSize(spatialReference.GridWidth, spatialReference.GridHeight),
            new HashSet<GridPoint>());
        return new ManualMapFinalizationResult(emptyMask, [], new RegionDraft([]), diagnostics);
    }

    private static ManualMapDiagnostic ToManualDiagnostic(RegionDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Severity switch
        {
            RegionDiagnosticSeverity.Info => ManualMapDiagnosticSeverity.Info,
            RegionDiagnosticSeverity.Warning => ManualMapDiagnosticSeverity.Warning,
            _ => ManualMapDiagnosticSeverity.Error
        },
        diagnostic.Message,
        diagnostic.RegionId?.Value,
        null);
}

/// <summary>Short alias for callers that prefer the concise service name.</summary>
public sealed class ManualMapFinalizer
{
    private readonly ManualMapDraftFinalizer _inner;

    public ManualMapFinalizer(GeometryFactory? geometryFactory = null)
    {
        _inner = new ManualMapDraftFinalizer(geometryFactory);
    }

    public ManualMapFinalizationResult FinalizeDraft(ManualMapDraft draft, MapSpatialReference spatialReference) =>
        _inner.FinalizeDraft(draft, spatialReference);
}
