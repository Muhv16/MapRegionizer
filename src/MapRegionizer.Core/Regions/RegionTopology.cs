using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Regions;

public readonly record struct RegionTopologyVertexId(int Value);

/// <summary>A shared vertex in an editable planar region coverage.</summary>
public sealed record RegionTopologyVertex(RegionTopologyVertexId Id, MapPoint Position, bool IsCoastal);

/// <summary>
/// An edge is shared by one (coast) or two region faces. Coast edges are immutable
/// from the topology editor, so moving an internal vertex updates every incident face.
/// </summary>
public sealed record RegionTopologyEdge(
    int Id,
    RegionTopologyVertexId StartVertexId,
    RegionTopologyVertexId EndVertexId,
    IReadOnlyList<RegionId> FaceIds,
    bool IsCoastal);

public sealed record RegionTopologyFace(RegionId RegionId, LandmassId LandmassId);

/// <summary>
/// Core representation of an editable planar coverage. UI clients operate on shared
/// vertices and edges rather than maintaining independent neighbour polygons.
/// </summary>
public sealed class RegionTopology
{
    private readonly IReadOnlyList<MapRegion> _regions;
    private readonly Dictionary<string, RegionTopologyVertex> _verticesByKey;

    private RegionTopology(
        IReadOnlyList<MapRegion> regions,
        Dictionary<string, RegionTopologyVertex> verticesByKey,
        IReadOnlyList<RegionTopologyEdge> edges)
    {
        _regions = regions;
        _verticesByKey = verticesByKey;
        Vertices = verticesByKey.Values.OrderBy(vertex => vertex.Id.Value).ToList();
        Edges = edges;
        Faces = regions.Select(region => new RegionTopologyFace(region.Id, region.LandmassId)).ToList();
    }

    public IReadOnlyList<RegionTopologyVertex> Vertices { get; }
    public IReadOnlyList<RegionTopologyEdge> Edges { get; }
    public IReadOnlyList<RegionTopologyFace> Faces { get; }

    public static RegionTopology Create(IReadOnlyList<Landmass> landmasses, IReadOnlyList<MapRegion> regions)
    {
        var violations = RegionGeometryContract.Validate(landmasses, regions);
        if (violations.Count != 0)
            throw new ArgumentException($"A topology requires a canonical coverage: {string.Join(" ", violations)}", nameof(regions));

        return CreateVerifiedCoverage(regions);
    }

    /// <summary>
    /// Creates editor topology from a coverage that has just passed
    /// <see cref="RegionCoverageCanonicalizer"/>. It avoids a second complete
    /// coverage validation, which is costly for detailed manually edited borders.
    /// </summary>
    public static RegionTopology CreateFromVerifiedCoverage(IReadOnlyList<MapRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return CreateVerifiedCoverage(regions);
    }

    private static RegionTopology CreateVerifiedCoverage(IReadOnlyList<MapRegion> regions)
    {
        var verticesByKey = new Dictionary<string, RegionTopologyVertex>(StringComparer.Ordinal);
        var edgeFaces = new Dictionary<string, List<RegionId>>(StringComparer.Ordinal);
        var edgeEndpoints = new Dictionary<string, (string Start, string End)>();
        var nextVertexId = 1;

        foreach (var region in regions.OrderBy(region => region.Id.Value))
        {
            foreach (var ring in GetRings(region.Shape))
            {
                for (var index = 0; index < ring.NumPoints - 1; index++)
                {
                    var start = ring.GetCoordinateN(index);
                    var end = ring.GetCoordinateN(index + 1);
                    var startKey = RegionGeometryPrecision.GetCoordinateKey(start);
                    var endKey = RegionGeometryPrecision.GetCoordinateKey(end);
                    AddVertex(startKey, start);
                    AddVertex(endKey, end);

                    var edgeKey = RegionGeometryPrecision.GetUndirectedSegmentKey(start, end);
                    if (!edgeFaces.TryGetValue(edgeKey, out var faces))
                    {
                        faces = [];
                        edgeFaces.Add(edgeKey, faces);
                        edgeEndpoints.Add(edgeKey, (startKey, endKey));
                    }
                    faces.Add(region.Id);
                }
            }
        }

        var coastalKeys = edgeFaces.Where(pair => pair.Value.Count == 1).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in coastalKeys)
        {
            var endpoints = edgeEndpoints[key];
            verticesByKey[endpoints.Start] = verticesByKey[endpoints.Start] with { IsCoastal = true };
            verticesByKey[endpoints.End] = verticesByKey[endpoints.End] with { IsCoastal = true };
        }

        var edges = edgeFaces.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select((pair, index) =>
        {
            var endpoints = edgeEndpoints[pair.Key];
            return new RegionTopologyEdge(
                index + 1,
                verticesByKey[endpoints.Start].Id,
                verticesByKey[endpoints.End].Id,
                pair.Value.OrderBy(id => id.Value).ToList(),
                pair.Value.Count == 1);
        }).ToList();

        return new RegionTopology(regions.Select(region => region with { Shape = (Polygon)region.Shape.Copy() }).ToList(), verticesByKey, edges);

        void AddVertex(string key, Coordinate coordinate)
        {
            if (!verticesByKey.ContainsKey(key))
                verticesByKey.Add(key, new RegionTopologyVertex(new RegionTopologyVertexId(nextVertexId++), new MapPoint(coordinate.X, coordinate.Y), false));
        }
    }

    /// <summary>
    /// Produces a new draft with the selected non-coastal vertex moved in every face
    /// that references it. The caller canonicalizes the returned draft before applying it.
    /// </summary>
    public bool TryMoveVertex(RegionTopologyVertexId vertexId, MapPoint position, out RegionDraft? draft, out RegionDiagnostic? diagnostic)
    {
        var vertex = Vertices.SingleOrDefault(candidate => candidate.Id == vertexId);
        if (vertex is null)
        {
            draft = null;
            diagnostic = new RegionDiagnostic("unknown-topology-vertex", RegionDiagnosticSeverity.Error, "The topology vertex does not exist.");
            return false;
        }

        if (vertex.IsCoastal)
        {
            draft = null;
            diagnostic = new RegionDiagnostic("protected-coast", RegionDiagnosticSeverity.Error, "Coastal vertices cannot be moved.");
            return false;
        }

        var oldKey = RegionGeometryPrecision.GetCoordinateKey(new Coordinate(vertex.Position.X, vertex.Position.Y));
        var movedRegions = _regions.Select(region => new RegionDraftRegion(region.Id, region.LandmassId, Move(region.Shape, oldKey, position))).ToList();
        draft = new RegionDraft(movedRegions);
        diagnostic = null;
        return true;
    }

    public RegionDraft ToDraft() => RegionDraft.FromRegions(_regions);

    public bool TryInsertVertex(int edgeId, MapPoint position, out RegionDraft? draft, out RegionDiagnostic? diagnostic)
    {
        var edge = Edges.SingleOrDefault(candidate => candidate.Id == edgeId);
        if (edge is null || edge.IsCoastal)
        {
            draft = null;
            diagnostic = new RegionDiagnostic("protected-coast", RegionDiagnosticSeverity.Error, "Coastal edges cannot be edited.");
            return false;
        }

        var start = Vertices.Single(vertex => vertex.Id == edge.StartVertexId).Position;
        var end = Vertices.Single(vertex => vertex.Id == edge.EndVertexId).Position;
        var segment = new LineSegment(new Coordinate(start.X, start.Y), new Coordinate(end.X, end.Y));
        if (!IsStrictlyOnSegment(segment, new Coordinate(position.X, position.Y)))
        {
            draft = null;
            diagnostic = new RegionDiagnostic("vertex-not-on-edge", RegionDiagnosticSeverity.Error, "The new vertex must lie on the selected shared edge.");
            return false;
        }

        draft = new RegionDraft(_regions.Select(region => new RegionDraftRegion(
            region.Id, region.LandmassId, InsertVertex(region.Shape, segment, position), RegionDraftOrigin.GeneratedAndEdited)).ToList());
        diagnostic = null;
        return true;
    }

    public bool TryDeleteVertex(RegionTopologyVertexId vertexId, out RegionDraft? draft, out RegionDiagnostic? diagnostic)
    {
        var vertex = Vertices.SingleOrDefault(candidate => candidate.Id == vertexId);
        var incident = Edges.Where(edge => edge.StartVertexId == vertexId || edge.EndVertexId == vertexId).ToList();
        if (vertex is null || vertex.IsCoastal || incident.Count != 2 || !incident[0].FaceIds.SequenceEqual(incident[1].FaceIds))
        {
            draft = null;
            diagnostic = new RegionDiagnostic("vertex-not-removable", RegionDiagnosticSeverity.Error, "Only a degree-two internal shared-edge vertex can be removed.");
            return false;
        }

        var firstOther = incident[0].StartVertexId == vertexId ? incident[0].EndVertexId : incident[0].StartVertexId;
        var secondOther = incident[1].StartVertexId == vertexId ? incident[1].EndVertexId : incident[1].StartVertexId;
        var first = Vertices.Single(candidate => candidate.Id == firstOther).Position;
        var second = Vertices.Single(candidate => candidate.Id == secondOther).Position;
        if (new LineSegment(new Coordinate(first.X, first.Y), new Coordinate(second.X, second.Y)).Distance(new Coordinate(vertex.Position.X, vertex.Position.Y)) > RegionGeometryPrecision.LengthTolerance)
        {
            draft = null;
            diagnostic = new RegionDiagnostic("vertex-not-collinear", RegionDiagnosticSeverity.Error, "Removing this vertex would alter a boundary; move it or keep it instead.");
            return false;
        }

        var key = RegionGeometryPrecision.GetCoordinateKey(new Coordinate(vertex.Position.X, vertex.Position.Y));
        draft = new RegionDraft(_regions.Select(region => new RegionDraftRegion(region.Id, region.LandmassId, RemoveVertex(region.Shape, key), RegionDraftOrigin.GeneratedAndEdited)).ToList());
        diagnostic = null;
        return true;
    }

    private static Polygon Move(Polygon polygon, string oldKey, MapPoint position)
    {
        var shell = MoveRing(polygon.ExteriorRing, oldKey, position, polygon.Factory);
        var holes = Enumerable.Range(0, polygon.NumInteriorRings)
            .Select(index => MoveRing(polygon.GetInteriorRingN(index), oldKey, position, polygon.Factory)).ToArray();
        return polygon.Factory.CreatePolygon(shell, holes);
    }

    private static Polygon InsertVertex(Polygon polygon, LineSegment target, MapPoint position)
    {
        var shell = InsertVertex(polygon.ExteriorRing, target, position, polygon.Factory);
        var holes = Enumerable.Range(0, polygon.NumInteriorRings)
            .Select(index => InsertVertex(polygon.GetInteriorRingN(index), target, position, polygon.Factory)).ToArray();
        return polygon.Factory.CreatePolygon(shell, holes);
    }

    private static Polygon RemoveVertex(Polygon polygon, string key)
    {
        var shell = RemoveVertex(polygon.ExteriorRing, key, polygon.Factory);
        var holes = Enumerable.Range(0, polygon.NumInteriorRings).Select(index => RemoveVertex(polygon.GetInteriorRingN(index), key, polygon.Factory)).ToArray();
        return polygon.Factory.CreatePolygon(shell, holes);
    }

    private static LinearRing RemoveVertex(LineString ring, string key, GeometryFactory factory)
    {
        var coordinates = ring.Coordinates.Take(ring.NumPoints - 1)
            .Where(coordinate => RegionGeometryPrecision.GetCoordinateKey(coordinate) != key).Select(coordinate => coordinate.Copy()).ToList();
        if (coordinates.Count < 3)
            return factory.CreateLinearRing(ring.Coordinates);
        coordinates.Add(coordinates[0].Copy());
        return factory.CreateLinearRing(coordinates.ToArray());
    }

    private static LinearRing InsertVertex(LineString ring, LineSegment target, MapPoint position, GeometryFactory factory)
    {
        var coordinates = new List<Coordinate>();
        for (var index = 0; index < ring.NumPoints - 1; index++)
        {
            var start = ring.GetCoordinateN(index);
            var end = ring.GetCoordinateN(index + 1);
            coordinates.Add(start.Copy());
            if (IsSameUndirectedSegment(start, end, target) && IsStrictlyOnSegment(new LineSegment(start, end), new Coordinate(position.X, position.Y)))
                coordinates.Add(new Coordinate(position.X, position.Y));
        }
        coordinates.Add(coordinates[0].Copy());
        return factory.CreateLinearRing(coordinates.ToArray());
    }

    private static bool IsSameUndirectedSegment(Coordinate start, Coordinate end, LineSegment target) =>
        (RegionGeometryPrecision.IsEquivalent(start, target.P0) && RegionGeometryPrecision.IsEquivalent(end, target.P1)) ||
        (RegionGeometryPrecision.IsEquivalent(start, target.P1) && RegionGeometryPrecision.IsEquivalent(end, target.P0));

    private static bool IsStrictlyOnSegment(LineSegment segment, Coordinate coordinate)
    {
        var projection = segment.ProjectionFactor(coordinate);
        return projection > RegionGeometryPrecision.LengthTolerance && projection < 1 - RegionGeometryPrecision.LengthTolerance &&
            segment.Distance(coordinate) <= RegionGeometryPrecision.LengthTolerance;
    }

    private static LinearRing MoveRing(LineString ring, string oldKey, MapPoint position, GeometryFactory factory)
    {
        var coordinates = ring.Coordinates.Select(coordinate =>
            RegionGeometryPrecision.GetCoordinateKey(coordinate) == oldKey
                ? new Coordinate(position.X, position.Y)
                : coordinate.Copy()).ToArray();
        return factory.CreateLinearRing(coordinates);
    }

    private static IEnumerable<LineString> GetRings(Polygon polygon)
    {
        yield return polygon.ExteriorRing;
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            yield return polygon.GetInteriorRingN(index);
    }
}
