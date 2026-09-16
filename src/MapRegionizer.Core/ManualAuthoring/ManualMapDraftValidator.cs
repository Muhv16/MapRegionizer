using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Valid;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Validates completed manual faces without requiring them to cover the whole
/// map.  Uncovered map area is water in this workflow, not a validation gap.
/// </summary>
public sealed class ManualMapDraftValidator
{
    private readonly GeometryFactory _geometryFactory;

    public ManualMapDraftValidator(GeometryFactory? geometryFactory = null)
    {
        _geometryFactory = geometryFactory ?? new GeometryFactory();
    }

    public ManualMapValidationResult Validate(ManualMapDraft draft) =>
        Validate(draft, CreateUnitSpatialReference(draft));

    public ManualMapValidationResult Validate(ManualMapDraft draft, MapSpatialReference spatialReference)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        spatialReference.Validate();

        var diagnostics = new List<ManualMapDiagnostic>();
        var polygons = new Dictionary<int, Polygon>();
        var verticesById = new Dictionary<int, ManualMapVertex>();
        var canonicalCoordinates = new Dictionary<string, ManualMapVertex>(StringComparer.Ordinal);
        var mapWidth = spatialReference.WidthInMapUnits;
        var mapHeight = spatialReference.HeightInMapUnits;

        foreach (var vertex in draft.Vertices)
        {
            if (vertex.Id <= 0)
            {
                diagnostics.Add(new(
                    "invalid-vertex-id",
                    ManualMapDiagnosticSeverity.Error,
                    "A manual vertex id must be positive.",
                    VertexId: vertex.Id));
                continue;
            }

            if (!verticesById.TryAdd(vertex.Id, vertex))
            {
                diagnostics.Add(new(
                    "duplicate-vertex-id",
                    ManualMapDiagnosticSeverity.Error,
                    $"Vertex id {vertex.Id} is used more than once.",
                    VertexId: vertex.Id));
                continue;
            }

            if (!double.IsFinite(vertex.Position.X) || !double.IsFinite(vertex.Position.Y))
            {
                diagnostics.Add(new(
                    "non-finite-coordinate",
                    ManualMapDiagnosticSeverity.Error,
                    "A vertex coordinate must be finite.",
                    VertexId: vertex.Id));
                continue;
            }

            var canonical = new Coordinate(
                RegionGeometryPrecision.Canonicalize(vertex.Position.X),
                RegionGeometryPrecision.Canonicalize(vertex.Position.Y));
            var coordinateKey = RegionGeometryPrecision.GetCoordinateKey(canonical);
            if (canonicalCoordinates.TryGetValue(coordinateKey, out var existing) && existing.Id != vertex.Id)
            {
                diagnostics.Add(new(
                    "duplicate-coordinate-vertices",
                    ManualMapDiagnosticSeverity.Error,
                    $"Vertices {existing.Id} and {vertex.Id} have the same canonical coordinate; use one shared vertex.",
                    VertexId: vertex.Id));
            }
            else
            {
                canonicalCoordinates[coordinateKey] = vertex;
            }

            if (canonical.X < -RegionGeometryPrecision.LengthTolerance ||
                canonical.X > mapWidth + RegionGeometryPrecision.LengthTolerance ||
                canonical.Y < -RegionGeometryPrecision.LengthTolerance ||
                canonical.Y > mapHeight + RegionGeometryPrecision.LengthTolerance)
            {
                diagnostics.Add(new(
                    "outside-map-bounds",
                    ManualMapDiagnosticSeverity.Error,
                    $"Vertex {vertex.Id} lies outside the {mapWidth:R} × {mapHeight:R} map bounds.",
                    VertexId: vertex.Id));
            }
        }

        var regionIds = new HashSet<int>();
        foreach (var face in draft.Regions)
        {
            if (face.Id <= 0 || !regionIds.Add(face.Id))
            {
                diagnostics.Add(new(
                    "duplicate-or-invalid-region-id",
                    ManualMapDiagnosticSeverity.Error,
                    "A manual region id must be positive and unique.",
                    RegionId: face.Id));
                continue;
            }

            var vertexIds = NormalizeVertexIds(face.VertexIds);
            if (vertexIds.Count < 3 || vertexIds.Distinct().Count() < 3)
            {
                diagnostics.Add(new(
                    "region-too-few-vertices",
                    ManualMapDiagnosticSeverity.Error,
                    "A region needs at least three unique vertices.",
                    RegionId: face.Id));
                continue;
            }

            var missingVertex = vertexIds.FirstOrDefault(id => !verticesById.ContainsKey(id));
            if (missingVertex == 0 && vertexIds.Any(id => id <= 0))
                missingVertex = vertexIds.First(id => id <= 0);
            if (missingVertex != 0)
            {
                diagnostics.Add(new(
                    "unknown-vertex",
                    ManualMapDiagnosticSeverity.Error,
                    $"Region {face.Id} references vertex {missingVertex}, which does not exist.",
                    RegionId: face.Id,
                    VertexId: missingVertex));
                continue;
            }

            if (vertexIds.Count != vertexIds.Distinct().Count())
            {
                diagnostics.Add(new(
                    "duplicate-vertex-reference",
                    ManualMapDiagnosticSeverity.Error,
                    "A region ring cannot repeat a vertex except for its optional closing vertex.",
                    RegionId: face.Id));
                continue;
            }

            Polygon polygon;
            try
            {
                var coordinates = vertexIds
                    .Select(id => verticesById[id].Position)
                    .Select(position => new Coordinate(
                        RegionGeometryPrecision.Canonicalize(position.X),
                        RegionGeometryPrecision.Canonicalize(position.Y)))
                    .ToList();
                coordinates.Add(coordinates[0].Copy());
                polygon = _geometryFactory.CreatePolygon(_geometryFactory.CreateLinearRing(coordinates.ToArray()));
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add(new(
                    "invalid-polygon",
                    ManualMapDiagnosticSeverity.Error,
                    $"Region {face.Id} could not be converted to a polygon: {exception.Message}",
                    RegionId: face.Id));
                continue;
            }

            if (!polygon.IsValid)
            {
                var reason = new IsValidOp(polygon).ValidationError?.Message;
                var code = reason?.Contains("Self-intersection", StringComparison.OrdinalIgnoreCase) == true
                    ? "self-intersection"
                    : "invalid-polygon";
                diagnostics.Add(new(
                    code,
                    ManualMapDiagnosticSeverity.Error,
                    string.IsNullOrWhiteSpace(reason)
                        ? "A region polygon is self-intersecting or otherwise invalid."
                        : $"A region polygon is invalid: {reason}",
                    RegionId: face.Id));
                continue;
            }

            if (polygon.Area <= RegionGeometryPrecision.GetAreaTolerance(polygon))
            {
                diagnostics.Add(new(
                    "zero-area-polygon",
                    ManualMapDiagnosticSeverity.Error,
                    "A region polygon must have non-zero area.",
                    RegionId: face.Id));
                continue;
            }

            if (polygon.EnvelopeInternal.MinX < -RegionGeometryPrecision.LengthTolerance ||
                polygon.EnvelopeInternal.MaxX > mapWidth + RegionGeometryPrecision.LengthTolerance ||
                polygon.EnvelopeInternal.MinY < -RegionGeometryPrecision.LengthTolerance ||
                polygon.EnvelopeInternal.MaxY > mapHeight + RegionGeometryPrecision.LengthTolerance)
            {
                diagnostics.Add(new(
                    "outside-map-bounds",
                    ManualMapDiagnosticSeverity.Error,
                    "A region polygon extends outside the map bounds.",
                    RegionId: face.Id));
                continue;
            }

            polygons[face.Id] = polygon;
        }

        AddOverlapDiagnostics(polygons, diagnostics);
        return new ManualMapValidationResult(diagnostics, polygons);
    }

    private static void AddOverlapDiagnostics(
        IReadOnlyDictionary<int, Polygon> polygons,
        ICollection<ManualMapDiagnostic> diagnostics)
    {
        var index = new STRtree<int>();
        var ordered = polygons.OrderBy(pair => pair.Key).ToArray();
        for (var i = 0; i < ordered.Length; i++)
            index.Insert(ordered[i].Value.EnvelopeInternal, i);

        index.Build();
        for (var i = 0; i < ordered.Length; i++)
        {
            foreach (var j in index.Query(ordered[i].Value.EnvelopeInternal).Where(candidate => candidate > i))
            {
                Geometry intersection;
                try
                {
                    intersection = ordered[i].Value.Intersection(ordered[j].Value);
                }
                catch (TopologyException exception)
                {
                    diagnostics.Add(new(
                        "overlap-check-failed",
                        ManualMapDiagnosticSeverity.Error,
                        $"Regions {ordered[i].Key} and {ordered[j].Key} could not be compared: {exception.Message}",
                        RegionId: ordered[j].Key));
                    continue;
                }

                var tolerance = Math.Max(
                    RegionGeometryPrecision.GetAreaTolerance(ordered[i].Value),
                    RegionGeometryPrecision.GetAreaTolerance(ordered[j].Value));
                if (intersection.Area <= tolerance)
                    continue;

                diagnostics.Add(new(
                    "material-overlap",
                    ManualMapDiagnosticSeverity.Error,
                    $"Regions {ordered[i].Key} and {ordered[j].Key} have a material overlap.",
                    RegionId: ordered[j].Key));
            }
        }
    }

    private static List<int> NormalizeVertexIds(IReadOnlyList<int> vertexIds)
    {
        var result = vertexIds?.ToList() ?? [];
        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static MapSpatialReference CreateUnitSpatialReference(ManualMapDraft draft) => new()
    {
        GridWidth = draft.GridWidth,
        GridHeight = draft.GridHeight,
        UnitsPerCell = 1,
        WorldModel = WorldModelDescriptor.Planar(),
        Coverage = MapCoverage.Regional(new LongitudeInterval(0, 1), 0, 1),
        GridMapping = GridMappingKind.Equirectangular,
        Topology = GridTopologyKind.OpenRectangular,
        LegacyCompatibility = LegacyCompatibilityProfile.None
    };
}
