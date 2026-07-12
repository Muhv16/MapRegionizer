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

    private static Polygon Move(Polygon polygon, string oldKey, MapPoint position)
    {
        var shell = MoveRing(polygon.ExteriorRing, oldKey, position, polygon.Factory);
        var holes = Enumerable.Range(0, polygon.NumInteriorRings)
            .Select(index => MoveRing(polygon.GetInteriorRingN(index), oldKey, position, polygon.Factory)).ToArray();
        return polygon.Factory.CreatePolygon(shell, holes);
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
