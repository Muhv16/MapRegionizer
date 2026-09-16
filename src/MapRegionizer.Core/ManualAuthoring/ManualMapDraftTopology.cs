using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>Atomic shared-topology operations used by manual editors.</summary>
public static class ManualMapDraftTopology
{
    public static bool TrySplitEdge(
        ManualMapDraft draft,
        int startVertexId,
        int endVertexId,
        int newVertexId,
        MapPoint position,
        out ManualMapDraft? result,
        out ManualMapDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(draft);
        result = null;
        diagnostic = null;

        if (startVertexId == endVertexId)
        {
            diagnostic = new("degenerate-edge", ManualMapDiagnosticSeverity.Error, "An edge needs two different vertices.");
            return false;
        }

        if (draft.Vertices.Any(vertex => vertex.Id == newVertexId))
        {
            diagnostic = new("duplicate-vertex-id", ManualMapDiagnosticSeverity.Error, "The new vertex id is already in use.", VertexId: newVertexId);
            return false;
        }

        var start = draft.Vertices.SingleOrDefault(vertex => vertex.Id == startVertexId);
        var end = draft.Vertices.SingleOrDefault(vertex => vertex.Id == endVertexId);
        if (start is null || end is null)
        {
            diagnostic = new("unknown-vertex", ManualMapDiagnosticSeverity.Error, "The selected edge references an unknown vertex.");
            return false;
        }

        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            diagnostic = new("non-finite-coordinate", ManualMapDiagnosticSeverity.Error, "The new vertex coordinate must be finite.", VertexId: newVertexId);
            return false;
        }

        var canonicalPosition = new MapPoint(
            RegionGeometryPrecision.Canonicalize(position.X),
            RegionGeometryPrecision.Canonicalize(position.Y));
        var canonicalKey = RegionGeometryPrecision.GetCoordinateKey(
            new Coordinate(canonicalPosition.X, canonicalPosition.Y));
        if (draft.Vertices.Any(vertex =>
            RegionGeometryPrecision.GetCoordinateKey(new Coordinate(vertex.Position.X, vertex.Position.Y)) == canonicalKey))
        {
            diagnostic = new(
                "duplicate-coordinate-vertices",
                ManualMapDiagnosticSeverity.Error,
                "The new vertex coordinate is already used by the draft.",
                VertexId: newVertexId);
            return false;
        }

        var segment = new LineSegment(
            new Coordinate(start.Position.X, start.Position.Y),
            new Coordinate(end.Position.X, end.Position.Y));
        var coordinate = new Coordinate(canonicalPosition.X, canonicalPosition.Y);
        var projection = segment.ProjectionFactor(coordinate);
        if (projection <= RegionGeometryPrecision.LengthTolerance ||
            projection >= 1 - RegionGeometryPrecision.LengthTolerance ||
            segment.Distance(coordinate) > RegionGeometryPrecision.LengthTolerance)
        {
            diagnostic = new("vertex-not-on-edge", ManualMapDiagnosticSeverity.Error, "The new vertex must lie strictly on the selected edge.", VertexId: newVertexId);
            return false;
        }

        var updatedFaces = new List<ManualRegionFace>(draft.Regions.Count);
        var splitCount = 0;
        foreach (var face in draft.Regions)
        {
            var ids = Normalize(face.VertexIds);
            var updated = new List<int>(ids.Count + 1);
            for (var index = 0; index < ids.Count; index++)
            {
                var current = ids[index];
                var next = ids[(index + 1) % ids.Count];
                updated.Add(current);
                if ((current == startVertexId && next == endVertexId) ||
                    (current == endVertexId && next == startVertexId))
                {
                    updated.Add(newVertexId);
                    splitCount++;
                }
            }

            updatedFaces.Add(new ManualRegionFace(face.Id, updated, face.Name));
        }

        if (splitCount == 0)
        {
            diagnostic = new("edge-not-found", ManualMapDiagnosticSeverity.Error, "No manual region uses the selected edge.");
            return false;
        }

        var updatedVertices = draft.Vertices
            .Append(new ManualMapVertex(
                newVertexId,
                canonicalPosition))
            .OrderBy(vertex => vertex.Id)
            .ToArray();
        result = new ManualMapDraft(draft.GridWidth, draft.GridHeight, updatedVertices, updatedFaces);
        return true;
    }

    public static ManualMapDraft SplitEdge(
        ManualMapDraft draft,
        int startVertexId,
        int endVertexId,
        int newVertexId,
        MapPoint position)
    {
        if (TrySplitEdge(draft, startVertexId, endVertexId, newVertexId, position, out var result, out var diagnostic))
            return result!;

        throw new ArgumentException(diagnostic?.Message ?? "The edge could not be split.", nameof(position));
    }

    private static List<int> Normalize(IReadOnlyList<int> ids)
    {
        var result = ids.ToList();
        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;
    }
}
