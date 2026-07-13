using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace MapRegionizer.Core.Regions;

/// <summary>Deterministic face-level edit operations used by graphical clients.</summary>
public static class RegionDraftEditor
{
    public static bool TryMerge(
        RegionDraft draft,
        RegionId retainedId,
        RegionId removedId,
        out RegionDraft? result,
        out RegionDiagnostic? diagnostic)
    {
        var retained = draft.Regions.SingleOrDefault(region => region.Id == retainedId);
        var removed = draft.Regions.SingleOrDefault(region => region.Id == removedId);
        if (retained is null || removed is null || retained.LandmassId != removed.LandmassId ||
            retained.Shape is not Polygon retainedShape || removed.Shape is not Polygon removedShape ||
            !RegionGeometryContract.ShareBoundary(retainedShape, removedShape))
        {
            result = null;
            diagnostic = new RegionDiagnostic("non-neighbour-merge", RegionDiagnosticSeverity.Error,
                "Only adjacent regions of the same landmass can be merged.", retainedId);
            return false;
        }

        var merged = retainedShape.Union(removedShape);
        if (merged is not Polygon polygon || !polygon.IsValid)
        {
            result = null;
            diagnostic = new RegionDiagnostic("invalid-merge", RegionDiagnosticSeverity.Error,
                "The selected regions cannot be represented as one valid polygon.", retainedId);
            return false;
        }

        result = new RegionDraft(draft.Regions.Where(region => region.Id != removedId)
            .Select(region => region.Id == retainedId
                ? region with { Shape = polygon, Origin = RegionDraftOrigin.GeneratedAndEdited }
                : region).ToList());
        diagnostic = null;
        return true;
    }

    public static bool TrySplit(
        RegionDraft draft,
        RegionId regionId,
        LineString cut,
        double endpointSnapTolerance,
        out RegionDraft? result,
        out RegionDiagnostic? diagnostic)
    {
        var region = draft.Regions.SingleOrDefault(candidate => candidate.Id == regionId);
        ArgumentOutOfRangeException.ThrowIfNegative(endpointSnapTolerance);
        if (region?.Shape is not Polygon polygon || cut.NumPoints < 2 ||
            !TryGetInteriorCutSegments(polygon, cut, out var interiorCut))
        {
            result = null;
            diagnostic = new RegionDiagnostic("invalid-split-line", RegionDiagnosticSeverity.Error,
                "The split line does not cross the selected region boundary twice.", regionId);
            return false;
        }

        var polygonizer = new Polygonizer();
        polygonizer.Add(polygon.Boundary.Union(interiorCut));
        var pieces = polygonizer.GetPolygons().OfType<Polygon>()
            .Where(piece => polygon.Covers(piece.InteriorPoint))
            .OrderByDescending(piece => piece.Area)
            .ThenBy(piece => RegionGeometryPrecision.GetCoordinateKey(piece.Coordinate))
            .ToList();
        if (pieces.Count < 2)
        {
            result = null;
            diagnostic = new RegionDiagnostic("split-did-not-divide", RegionDiagnosticSeverity.Error,
                "The line did not divide the selected region into two faces.", regionId);
            return false;
        }

        var nextId = draft.Regions.Where(candidate => candidate.Id.HasValue).Max(candidate => candidate.Id!.Value.Value) + 1;
        var replacements = pieces.Select((piece, index) => new RegionDraftRegion(
            index == 0 ? region.Id : new RegionId(nextId + index - 1),
            region.LandmassId,
            piece,
            RegionDraftOrigin.GeneratedAndEdited,
            index == 0 ? region.Name : null,
            index == 0 ? region.Metadata : null)).ToList();
        result = new RegionDraft(draft.Regions.Where(candidate => candidate.Id != regionId).Concat(replacements).ToList());
        diagnostic = null;
        return true;
    }

    public static bool TrySplit(
        RegionDraft draft,
        RegionId regionId,
        LineString cut,
        out RegionDraft? result,
        out RegionDiagnostic? diagnostic) =>
        TrySplit(draft, regionId, cut, RegionGeometryPrecision.LengthTolerance, out result, out diagnostic);

    /// <summary>
    /// Treats two user points as a directional split line, extends it through the
    /// selected face, and keeps only the portions whose endpoints meet its boundary.
    /// Thus a user need not hit the boundary precisely with either click.
    /// </summary>
    private static bool TryGetInteriorCutSegments(Polygon polygon, LineString cut, out Geometry interiorCut)
    {
        var first = cut.GetCoordinateN(0);
        var last = cut.GetCoordinateN(cut.NumPoints - 1);
        var directionX = last.X - first.X;
        var directionY = last.Y - first.Y;
        var directionLength = Math.Sqrt(directionX * directionX + directionY * directionY);
        if (directionLength <= RegionGeometryPrecision.LengthTolerance)
        {
            interiorCut = null!;
            return false;
        }

        var envelope = polygon.EnvelopeInternal;
        var extension = Math.Sqrt(envelope.Width * envelope.Width + envelope.Height * envelope.Height) + directionLength;
        var extended = polygon.Factory.CreateLineString([
            new Coordinate(first.X - directionX / directionLength * extension, first.Y - directionY / directionLength * extension),
            new Coordinate(last.X + directionX / directionLength * extension, last.Y + directionY / directionLength * extension)
        ]);
        var clipped = polygon.Intersection(extended);
        var segments = GetLineStrings(clipped)
            .Where(segment => segment.Length > RegionGeometryPrecision.LengthTolerance)
            .Where(segment => IsBoundaryEndpoint(polygon, segment.GetCoordinateN(0)) &&
                              IsBoundaryEndpoint(polygon, segment.GetCoordinateN(segment.NumPoints - 1)))
            .ToList();
        if (segments.Count == 0)
        {
            interiorCut = null!;
            return false;
        }

        interiorCut = polygon.Factory.BuildGeometry(segments);
        return true;
    }

    private static bool IsBoundaryEndpoint(Polygon polygon, Coordinate point) =>
        polygon.Boundary.Distance(polygon.Factory.CreatePoint(point)) <= RegionGeometryPrecision.LengthTolerance;

    private static IEnumerable<LineString> GetLineStrings(Geometry geometry)
    {
        if (geometry is LineString lineString)
        {
            yield return lineString;
            yield break;
        }

        for (var index = 0; index < geometry.NumGeometries; index++)
        {
            foreach (var childLine in GetLineStrings(geometry.GetGeometryN(index)))
                yield return childLine;
        }
    }
}
