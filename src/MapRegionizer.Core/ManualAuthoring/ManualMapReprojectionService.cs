using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>Transforms an authoring coordinate directly to geographic coordinates.</summary>
public interface IManualMapGeoreferenceTransform
{
    GeoCoordinate ToGeographic(MapPoint sourceMapPoint);

    /// <summary>Preserves longitude continuity, including the two sides of a full-world cut.</summary>
    GeoCoordinate ToUnwrappedGeographic(MapPoint sourceMapPoint) => ToGeographic(sourceMapPoint);
}

public sealed record ManualMapReprojectionRequest(
    ManualMapDraft Draft,
    MapSpatialReference SourceReference,
    MapSpatialReference TargetReference,
    double SourceLongitudeOffsetDegrees = 0,
    double MaxEdgeDeviationInTargetCells = 0.25,
    int MaximumAddedVertices = 250_000,
    int MaximumSubdivisionDepth = 24,
    IManualMapGeoreferenceTransform? GeoreferenceTransform = null);

public sealed record ManualMapReprojectionDiagnostic(
    string Code,
    string Message,
    int? RegionId = null,
    int? VertexId = null,
    bool IsError = true);

public sealed record ManualMapReprojectionResult(
    ManualMapDraft? Draft,
    MapSpatialReference TargetReference,
    IReadOnlyList<ManualMapReprojectionDiagnostic> Diagnostics,
    int AddedVertices,
    bool IsSuccessful,
    ManualMapFinalizationResult? Finalization = null);

/// <summary>
/// Reprojects a complete manual draft without mutating it. Shared edges are
/// sampled once and the resulting vertices are shared by every incident face.
/// </summary>
public sealed class ManualMapReprojectionService
{
    private const long MaximumGridCellCount = 100_000_000;
    private const double BoundsTolerance = 1e-9;

    private readonly ManualMapDraftValidator _validator;
    private readonly ManualMapDraftFinalizer _finalizer;

    public ManualMapReprojectionService(
        ManualMapDraftValidator? validator = null,
        ManualMapDraftFinalizer? finalizer = null)
    {
        _validator = validator ?? new ManualMapDraftValidator();
        _finalizer = finalizer ?? new ManualMapDraftFinalizer();
    }

    /// <summary>
    /// Suggests a full-world target cut aligned with the transformed source's
    /// left edge. This is an explicit alternative to splitting editable faces;
    /// callers must preview and validate the returned reference before applying.
    /// </summary>
    public static MapSpatialReference? SuggestContinuousTargetReference(ManualMapReprojectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        request.SourceReference.Validate();
        request.TargetReference.Validate();
        if (!request.SourceReference.Coverage.IsGlobal || !request.TargetReference.Coverage.IsGlobal)
            return null;
        var position = new MapPoint(0, request.SourceReference.HeightInMapUnits / 2);
        var geographic = request.GeoreferenceTransform?.ToUnwrappedGeographic(position)
            ?? MapSpatialContext.Create(request.SourceReference).GridToGeographic(new(0, request.SourceReference.GridHeight / 2.0));
        var longitude = new LongitudeInterval(geographic.LongitudeDegrees + request.SourceLongitudeOffsetDegrees, 360);
        if (Math.Abs(LongitudeInterval.Normalize(longitude.Start - request.TargetReference.Longitude.Start)) < 1e-10)
            return null;
        return request.TargetReference with
        {
            Coverage = MapCoverage.Create(MapCoverageKind.Global, longitude,
                request.TargetReference.SouthLatitude, request.TargetReference.NorthLatitude)
        };
    }

    /// <summary>Whole turns removed to keep the transformed source's left longitude canonical without folding its right edge.</summary>
    public static double GetSourceLongitudeCycleOffset(ManualMapReprojectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var origin = request.GeoreferenceTransform?.ToUnwrappedGeographic(new(0, request.SourceReference.HeightInMapUnits / 2))
            ?? MapSpatialContext.Create(request.SourceReference).GridToGeographic(new(0, request.SourceReference.GridHeight / 2.0));
        var longitude = origin.LongitudeDegrees + request.SourceLongitudeOffsetDegrees;
        return longitude - LongitudeInterval.Normalize(longitude);
    }

    public ManualMapReprojectionResult Reproject(
        ManualMapReprojectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<ManualMapReprojectionDiagnostic>();
        try
        {
            ValidateRequest(request);
            request.SourceReference.Validate();
            request.TargetReference.Validate();
            _ = MapSpatialContext.Create(request.SourceReference);
            _ = MapSpatialContext.Create(request.TargetReference);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(new("invalid-reference", exception.Message));
            return Failed(request.TargetReference, diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var draft = request.Draft;
        if (draft.GridWidth != request.SourceReference.GridWidth || draft.GridHeight != request.SourceReference.GridHeight ||
            request.TargetReference.GridWidth <= 0 || request.TargetReference.GridHeight <= 0 ||
            (long)request.TargetReference.GridWidth * request.TargetReference.GridHeight > MaximumGridCellCount)
        {
            diagnostics.Add(new("invalid-grid-size", "The draft and source grid must match, and the target grid must not exceed 100,000,000 cells."));
            return Failed(request.TargetReference, diagnostics);
        }

        var sourceValidation = _validator.Validate(draft, request.SourceReference, cancellationToken);
        if (!sourceValidation.IsSuccessful)
        {
            diagnostics.AddRange(sourceValidation.Diagnostics
                .Where(item => item.Severity == ManualMapDiagnosticSeverity.Error)
                .Select(item => new ManualMapReprojectionDiagnostic(item.Code, item.Message, item.RegionId, item.VertexId)));
            return Failed(request.TargetReference, diagnostics);
        }

        var sourceContext = MapSpatialContext.Create(request.SourceReference);
        var targetContext = MapSpatialContext.Create(request.TargetReference);
        var inputVertices = draft.Vertices.ToDictionary(vertex => vertex.Id);
        var transformed = new Dictionary<int, MapPoint>(draft.Vertices.Count);
        var nextVertexId = draft.Vertices.Select(vertex => vertex.Id).DefaultIfEmpty(0).Max() + 1;
        var addedVertices = 0;
        var longitudeCycleOffset = 0.0;

        try
        {
            longitudeCycleOffset = GetSourceLongitudeCycleOffset(request);
            foreach (var vertex in draft.Vertices.OrderBy(vertex => vertex.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                transformed.Add(vertex.Id, Transform(vertex.Position, vertex.Id));
            }

            var uniqueEdges = GetUniqueEdges(draft);
            var insertedByEdge = new Dictionary<EdgeKey, IReadOnlyList<int>>(uniqueEdges.Count);
            foreach (var edge in uniqueEdges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = inputVertices[edge.StartId];
                var end = inputVertices[edge.EndId];
                var insertedPositions = new List<MapPoint>();
                Subdivide(start.Position, end.Position, transformed[edge.StartId], transformed[edge.EndId], 0, insertedPositions);
                if (insertedPositions.Count == 0)
                {
                    insertedByEdge.Add(edge, Array.Empty<int>());
                    continue;
                }

                var ids = new int[insertedPositions.Count];
                for (var index = 0; index < insertedPositions.Count; index++)
                {
                    var id = nextVertexId++;
                    ids[index] = id;
                    inputVertices.Add(id, new ManualMapVertex(id, insertedPositions[index]));
                    transformed.Add(id, insertedPositions[index]);
                }
                insertedByEdge.Add(edge, ids);
            }

            var newVertices = draft.Vertices
                .Select(vertex => new ManualMapVertex(vertex.Id, transformed[vertex.Id]))
                .Concat(insertedByEdge.Values.SelectMany(ids => ids.Select(id => inputVertices[id])))
                .OrderBy(vertex => vertex.Id)
                .ToArray();
            var newRegions = new List<ManualRegionFace>(draft.Regions.Count);
            foreach (var face in draft.Regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var oldIds = NormalizeRing(face.VertexIds);
                if (oldIds.Count < 2)
                {
                    newRegions.Add(face);
                    continue;
                }

                var newIds = new List<int>(oldIds.Count);
                for (var index = 0; index < oldIds.Count; index++)
                {
                    var startId = oldIds[index];
                    var endId = oldIds[(index + 1) % oldIds.Count];
                    newIds.Add(startId);
                    if (!insertedByEdge.TryGetValue(EdgeKey.Create(startId, endId), out var inserted) || inserted.Count == 0)
                        continue;
                    if (startId < endId)
                        newIds.AddRange(inserted);
                    else
                        newIds.AddRange(inserted.Reverse());
                }

                // Preserve the source format's optional repeated closing vertex.
                if (face.VertexIds.Count > 1 && face.VertexIds[0] == face.VertexIds[^1])
                    newIds.Add(newIds[0]);
                newRegions.Add(new ManualRegionFace(face.Id, newIds, face.Name));
            }

            var output = new ManualMapDraft(request.TargetReference.GridWidth, request.TargetReference.GridHeight, newVertices, newRegions);
            var validation = _validator.Validate(output, request.TargetReference, cancellationToken);
            diagnostics.AddRange(validation.Diagnostics
                .Where(item => item.Severity == ManualMapDiagnosticSeverity.Error)
                .Select(item => new ManualMapReprojectionDiagnostic(item.Code, item.Message, item.RegionId, item.VertexId)));
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return Failed(request.TargetReference, diagnostics, addedVertices);

            var finalization = _finalizer.FinalizeDraft(output, request.TargetReference, cancellationToken);
            diagnostics.AddRange(finalization.Diagnostics
                .Where(item => item.IsBlocking)
                .Select(item => new ManualMapReprojectionDiagnostic(item.Code, item.Message, item.RegionId, item.VertexId)));
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return Failed(request.TargetReference, diagnostics, addedVertices);

            return new ManualMapReprojectionResult(output, request.TargetReference, diagnostics, addedVertices, true, finalization);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // Transform already records the failing vertex; do not repeat its exception.
            if (exception is not ReprojectionCoordinateException coordinateException || !diagnostics.Any(item => item.Code == coordinateException.Code))
                diagnostics.Add(new(exception is ReprojectionCoordinateException reprojectionException
                    ? reprojectionException.Code : "coordinate-transform-failed", exception.Message));
            return Failed(request.TargetReference, diagnostics, addedVertices);
        }

        MapPoint Transform(MapPoint sourcePoint, int? vertexId = null)
        {
            GeoCoordinate geographic;
            try
            {
                geographic = request.GeoreferenceTransform?.ToUnwrappedGeographic(sourcePoint)
                    ?? sourceContext.GridToGeographic(new GridCoordinate(
                        sourcePoint.X / request.SourceReference.UnitsPerCell,
                        sourcePoint.Y / request.SourceReference.UnitsPerCell));
                geographic = geographic with
                {
                    LongitudeDegrees = geographic.LongitudeDegrees + request.SourceLongitudeOffsetDegrees - longitudeCycleOffset
                };
                if (!double.IsFinite(geographic.LongitudeDegrees) || !double.IsFinite(geographic.LatitudeDegrees))
                    throw new ArgumentOutOfRangeException(nameof(sourcePoint), "The transformed geographic coordinate is not finite.");
                if (!request.TargetReference.Coverage.Longitude.Contains(geographic.LongitudeDegrees))
                    throw new ArgumentOutOfRangeException(nameof(sourcePoint), $"Longitude {geographic.LongitudeDegrees:R}° falls outside the target coverage.");

                var grid = targetContext.GeographicToGrid(geographic);
                var x = grid.X * request.TargetReference.UnitsPerCell;
                var y = grid.Y * request.TargetReference.UnitsPerCell;
                var maxX = request.TargetReference.WidthInMapUnits;
                var maxY = request.TargetReference.HeightInMapUnits;
                if (!double.IsFinite(x) || !double.IsFinite(y) ||
                    x < -BoundsTolerance || x > maxX + BoundsTolerance ||
                    y < -BoundsTolerance || y > maxY + BoundsTolerance)
                {
                    throw new ArgumentOutOfRangeException(nameof(sourcePoint), $"Geographic coordinate ({geographic.LongitudeDegrees:R}°, {geographic.LatitudeDegrees:R}°) falls outside the target grid.");
                }

                return new MapPoint(x, y);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                var suffix = vertexId.HasValue ? $" at vertex {vertexId.Value}" : string.Empty;
                diagnostics.Add(new("coordinate-out-of-range", $"Cannot reproject source coordinate{suffix}: {exception.Message}", VertexId: vertexId));
                throw new ReprojectionCoordinateException("coordinate-out-of-range", exception.Message);
            }
        }

        void Subdivide(MapPoint sourceStart, MapPoint sourceEnd, MapPoint targetStart, MapPoint targetEnd, int depth, List<MapPoint> inserted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceMidpoint = new MapPoint((sourceStart.X + sourceEnd.X) / 2, (sourceStart.Y + sourceEnd.Y) / 2);
            var targetMidpoint = Transform(sourceMidpoint);
            var deviation = 0.0;
            var maximumDeviationX = 0.0;
            // Mercator is symmetric about the equator: its midpoint can lie
            // exactly on the chord while both quarter points deviate materially.
            foreach (var fraction in new[] { .25, .5, .75 })
            {
                var sample = fraction == .5 ? targetMidpoint : Transform(new MapPoint(
                    sourceStart.X + (sourceEnd.X - sourceStart.X) * fraction,
                    sourceStart.Y + (sourceEnd.Y - sourceStart.Y) * fraction));
                var dx = (sample.X - (targetStart.X + (targetEnd.X - targetStart.X) * fraction)) / request.TargetReference.UnitsPerCell;
                var dy = (sample.Y - (targetStart.Y + (targetEnd.Y - targetStart.Y) * fraction)) / request.TargetReference.UnitsPerCell;
                deviation = Math.Max(deviation, Math.Sqrt(dx * dx + dy * dy));
                maximumDeviationX = Math.Max(maximumDeviationX, Math.Abs(dx));
            }
            if (deviation <= request.MaxEdgeDeviationInTargetCells)
                return;
            if (maximumDeviationX > request.TargetReference.GridWidth / 2.0)
                throw new ReprojectionCoordinateException("seam-crossing-unsupported", "The transformed edge crosses the antimeridian seam, which the manual polygon format cannot represent safely.");
            if (depth >= request.MaximumSubdivisionDepth)
                throw new ReprojectionCoordinateException("subdivision-depth-exceeded", $"An edge exceeded the maximum subdivision depth {request.MaximumSubdivisionDepth}.");
            if (addedVertices >= request.MaximumAddedVertices)
                throw new ReprojectionCoordinateException("vertex-budget-exceeded", $"Reprojection needs more than {request.MaximumAddedVertices} added vertices.");

            // Reserve before descending so pathological curvature cannot allocate
            // an unbounded recursion result before the per-edge budget check.
            addedVertices++;
            Subdivide(sourceStart, sourceMidpoint, targetStart, targetMidpoint, depth + 1, inserted);
            inserted.Add(targetMidpoint);
            Subdivide(sourceMidpoint, sourceEnd, targetMidpoint, targetEnd, depth + 1, inserted);
        }
    }

    private static void ValidateRequest(ManualMapReprojectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Draft);
        ArgumentNullException.ThrowIfNull(request.SourceReference);
        ArgumentNullException.ThrowIfNull(request.TargetReference);
        if (!double.IsFinite(request.SourceLongitudeOffsetDegrees))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!double.IsFinite(request.MaxEdgeDeviationInTargetCells) || request.MaxEdgeDeviationInTargetCells <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.MaximumAddedVertices < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.MaximumSubdivisionDepth is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    private static IReadOnlyList<EdgeKey> GetUniqueEdges(ManualMapDraft draft)
    {
        var edges = new HashSet<EdgeKey>();
        foreach (var face in draft.Regions)
        {
            var ring = NormalizeRing(face.VertexIds);
            if (ring.Count < 2)
                continue;
            for (var index = 0; index < ring.Count; index++)
                edges.Add(EdgeKey.Create(ring[index], ring[(index + 1) % ring.Count]));
        }
        return edges.OrderBy(edge => edge.StartId).ThenBy(edge => edge.EndId).ToArray();
    }

    private static List<int> NormalizeRing(IReadOnlyList<int> ids)
    {
        var result = ids.ToList();
        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static ManualMapReprojectionResult Failed(
        MapSpatialReference target,
        IReadOnlyList<ManualMapReprojectionDiagnostic> diagnostics,
        int addedVertices = 0) => new(null, target, diagnostics, addedVertices, false);

    private readonly record struct EdgeKey(int StartId, int EndId)
    {
        public static EdgeKey Create(int first, int second) => first < second ? new(first, second) : new(second, first);
    }

    private sealed class ReprojectionCoordinateException(string code, string message) : InvalidOperationException(message)
    {
        public string Code { get; } = code;
    }
}
