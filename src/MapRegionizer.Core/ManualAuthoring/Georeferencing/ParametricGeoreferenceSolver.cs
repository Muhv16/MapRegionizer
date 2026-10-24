using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.ManualAuthoring.Georeferencing;

public sealed record GeographicCalibrationRequest(
    MapSpatialReference SourceReference,
    IReadOnlyList<GeographicControlPoint> ControlPoints,
    BackgroundImagePlacement? BackgroundPlacement = null,
    double TargetP95Degrees = 5,
    double TargetMaximumDegrees = 10);

/// <summary>Fits a longitude offset and a latitude scale/offset using calibration points only.</summary>
public sealed class ParametricGeoreferenceSolver
{
    public GeographicCalibrationResult Solve(GeographicCalibrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceReference);
        ArgumentNullException.ThrowIfNull(request.ControlPoints);
        var diagnostics = new List<GeographicCalibrationDiagnostic>();
        try
        {
            request.SourceReference.Validate();
            if (!double.IsFinite(request.TargetP95Degrees) || request.TargetP95Degrees <= 0 ||
                !double.IsFinite(request.TargetMaximumDegrees) || request.TargetMaximumDegrees <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), "Calibration quality thresholds must be positive and finite.");
            if (request.BackgroundPlacement is not null)
                request.BackgroundPlacement.Validate();
        }
        catch (ArgumentException exception)
        {
            return Failed("invalid-calibration-request", exception.Message);
        }

        var ids = new HashSet<int>();
        var activePoints = request.ControlPoints.Where(point => point.IsActive).ToArray();
        foreach (var point in activePoints)
        {
            if (point.Id <= 0 || !ids.Add(point.Id))
                diagnostics.Add(new("invalid-control-point-id", "Active control-point ids must be positive and unique.", true, point.Id));
            if (!Enum.IsDefined(point.AnchorKind) || !Enum.IsDefined(point.Role))
                diagnostics.Add(new("invalid-control-point-kind", $"Control point '{point.Name}' has an unknown anchor or role.", true, point.Id));
            if (!double.IsFinite(point.GeographicPosition.LongitudeDegrees) || point.GeographicPosition.LongitudeDegrees < -180 || point.GeographicPosition.LongitudeDegrees > 180 ||
                !double.IsFinite(point.GeographicPosition.LatitudeDegrees) ||
                point.GeographicPosition.LatitudeDegrees < -90 || point.GeographicPosition.LatitudeDegrees > 90)
                diagnostics.Add(new("invalid-control-point-coordinate", $"Control point '{point.Name}' has invalid geographic coordinates.", true, point.Id));
            if (point.AnchorKind == GeographicControlPointAnchorKind.BackgroundImageUv &&
                (point.BackgroundU is not { } u || point.BackgroundV is not { } v ||
                 !double.IsFinite(u) || !double.IsFinite(v) || u < 0 || u > 1 || v < 0 || v > 1))
                diagnostics.Add(new("invalid-background-control-point", $"Control point '{point.Name}' must lie within the normalized source image.", true, point.Id));
        }

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new(null, [], null, diagnostics, false);

        var resolved = new List<ResolvedControlPoint>();
        MapSpatialContext sourceContext;
        try
        {
            sourceContext = MapSpatialContext.Create(request.SourceReference);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failed("invalid-source-mapping", exception.Message);
        }
        foreach (var point in activePoints)
        {
            try
            {
                var pointBackgroundPlacement = point.BackgroundPlacementAtCapture ?? request.BackgroundPlacement;
                var mapPosition = point.AnchorKind switch
                {
                    GeographicControlPointAnchorKind.MapUnits when point.MapPosition is { } mapAnchor => mapAnchor,
                    GeographicControlPointAnchorKind.BackgroundImageUv when point.BackgroundU is { } u && point.BackgroundV is { } v && pointBackgroundPlacement is not null => pointBackgroundPlacement.ToMapPoint(u, v),
                    GeographicControlPointAnchorKind.BackgroundImageUv => throw new ArgumentException("This control point refers to a background image but no valid image placement is available."),
                    _ => throw new ArgumentException("This control point has no valid map position.")
                };
                if (!double.IsFinite(mapPosition.X) || !double.IsFinite(mapPosition.Y))
                    throw new ArgumentException("The map coordinate is not finite.");
                var gridPosition = new GridCoordinate(
                    mapPosition.X / request.SourceReference.UnitsPerCell,
                    mapPosition.Y / request.SourceReference.UnitsPerCell);
                var baseGeographic = sourceContext.GridToGeographic(gridPosition);
                resolved.Add(new ResolvedControlPoint(point, mapPosition, gridPosition, baseGeographic));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new("unresolved-control-point", $"Control point '{point.Name}' could not be located: {exception.Message}", true, point.Id));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new(null, [], null, diagnostics, false);

        var calibration = resolved.Where(point => point.Point.Role == GeographicControlPointRole.Calibration).ToArray();
        var validation = resolved.Where(point => point.Point.Role == GeographicControlPointRole.Validation).ToArray();
        if (calibration.Length < 2)
            return Failed("insufficient-calibration-points", "At least two calibration control points are required to fit the source latitude bounds.");

        var latitudeSamples = new List<(double V, double ProjectedLatitude)>();
        foreach (var point in calibration)
        {
            try
            {
                var expected = point.Point.GeographicPosition.LatitudeDegrees;
                var projectedLatitude = request.SourceReference.GridMapping == GridMappingKind.WebMercator
                    ? WebMercatorGridMapping.MercatorY(expected)
                    : expected;
                latitudeSamples.Add((point.GridPosition.Y / request.SourceReference.GridHeight, projectedLatitude));
            }
            catch (ArgumentOutOfRangeException)
            {
                return Failed("latitude-outside-source-projection", $"A calibration latitude exceeds the valid range for {request.SourceReference.GridMapping}.");
            }
        }

        var meanV = latitudeSamples.Average(point => point.V);
        var meanProjectedLatitude = latitudeSamples.Average(point => point.ProjectedLatitude);
        var denominator = latitudeSamples.Sum(point => Math.Pow(point.V - meanV, 2));
        if (denominator <= 1e-12)
            return Failed("underdetermined-latitude-fit", "Calibration points must span more than one image row to determine the north and south latitude bounds.");
        var slope = latitudeSamples.Sum(point => (point.V - meanV) * (point.ProjectedLatitude - meanProjectedLatitude)) / denominator;
        if (!double.IsFinite(slope) || slope >= 0)
            return Failed("invalid-latitude-fit", "A flat or vertically inverted latitude fit cannot be represented by this north-up model.");
        var projectedNorth = meanProjectedLatitude - slope * meanV;
        var projectedSouth = meanProjectedLatitude + slope * (1 - meanV);
        var mercator = request.SourceReference.GridMapping == GridMappingKind.WebMercator;
        var latitudeLimit = mercator ? WebMercatorGridMapping.WebMercatorLatitudeLimit : 90;
        var minimum = mercator ? WebMercatorGridMapping.MercatorY(-latitudeLimit) : -latitudeLimit;
        var maximum = mercator ? WebMercatorGridMapping.MercatorY(latitudeLimit) : latitudeLimit;
        if (projectedSouth < minimum || projectedNorth > maximum || projectedNorth < minimum || projectedSouth > maximum)
        {
            var unconstrainedNorth = mercator ? WebMercatorGridMapping.InverseMercatorY(projectedNorth) : projectedNorth;
            var unconstrainedSouth = mercator ? WebMercatorGridMapping.InverseMercatorY(projectedSouth) : projectedSouth;
            (projectedNorth, projectedSouth) = FitBoundedLatitudes(latitudeSamples, minimum, maximum);
            diagnostics.Add(new("latitude-fit-constrained", FormattableString.Invariant(
                $"Unconstrained source latitude bounds {unconstrainedSouth:R}..{unconstrainedNorth:R} exceed the projection limit ±{latitudeLimit:R}. Refit within that limit using all calibration points; inspect their residuals.")));
        }
        var northLatitude = Math.Clamp(mercator ? WebMercatorGridMapping.InverseMercatorY(projectedNorth) : projectedNorth, -latitudeLimit, latitudeLimit);
        var southLatitude = Math.Clamp(mercator ? WebMercatorGridMapping.InverseMercatorY(projectedSouth) : projectedSouth, -latitudeLimit, latitudeLimit);
        if (!double.IsFinite(southLatitude) || !double.IsFinite(northLatitude) || southLatitude >= northLatitude)
            return Failed("invalid-latitude-fit", "The best bounded latitude fit collapses the map's north/south extent.");

        var longitudeDifferences = calibration.Select(point => LongitudeInterval.Normalize(
            point.Point.GeographicPosition.LongitudeDegrees - point.BaseGeographic.LongitudeDegrees)).ToArray();
        var sin = longitudeDifferences.Sum(value => Math.Sin(value * Math.PI / 180));
        var cos = longitudeDifferences.Sum(value => Math.Cos(value * Math.PI / 180));
        var magnitude = Math.Sqrt(sin * sin + cos * cos) / longitudeDifferences.Length;
        if (magnitude < 0.25)
            diagnostics.Add(new("conflicting-longitudes", "Calibration points contain conflicting longitude offsets; inspect their residuals before applying this model."));
        var longitudeOffset = LongitudeInterval.Normalize(Math.Atan2(sin, cos) * 180 / Math.PI);

        var parameters = new GeographicCalibrationParameters(
            Version: 1,
            SourceMapping: request.SourceReference.GridMapping,
            LongitudeOffsetDegrees: longitudeOffset,
            SourceSouthLatitude: southLatitude,
            SourceNorthLatitude: northLatitude,
            SourceReference: request.SourceReference);
        var transform = new ParametricGeoreferenceTransform(request.SourceReference, parameters);
        var residuals = resolved.Select(point =>
        {
            var corrected = transform.ToGeographic(point.MapPosition);
            var deltaLongitude = LongitudeInterval.Normalize(corrected.LongitudeDegrees - point.Point.GeographicPosition.LongitudeDegrees);
            var deltaLatitude = corrected.LatitudeDegrees - point.Point.GeographicPosition.LatitudeDegrees;
            return new GeographicPointResidual(
                point.Point.Id,
                point.Point.Name,
                point.Point.Role,
                point.Point.GeographicPosition,
                point.BaseGeographic,
                corrected,
                deltaLongitude,
                deltaLatitude,
                AngularDistance(corrected, point.Point.GeographicPosition));
        }).ToArray();

        var calibrationResiduals = residuals.Where(residual => residual.Role == GeographicControlPointRole.Calibration).ToArray();
        var validationResiduals = residuals.Where(residual => residual.Role == GeographicControlPointRole.Validation).ToArray();
        var calibrationRms = Rms(calibrationResiduals.Select(residual => residual.AngularErrorDegrees));
        var validationRms = Rms(validationResiduals.Select(residual => residual.AngularErrorDegrees));
        var validationP95 = Percentile(validationResiduals.Select(residual => residual.AngularErrorDegrees), 0.95);
        var validationMax = validationResiduals.Select(residual => residual.AngularErrorDegrees).DefaultIfEmpty(double.NaN).Max();
        // Optional check points report their measured residuals. These thresholds
        // describe only the supplied points, not accuracy across the entire map.
        var validationTargetReached = validation.Length > 0 && validationP95 <= request.TargetP95Degrees && validationMax <= request.TargetMaximumDegrees;
        var status = validation.Length == 0
            ? "Независимая проверка не выполнялась."
            : validationTargetReached ? "Ошибка на проверочных точках в пределах выбранных порогов." : "Ошибка на проверочных точках превышает выбранные пороги.";
        var metrics = new GeographicCalibrationMetrics(
            calibration.Length,
            validation.Length,
            calibrationRms,
            calibrationResiduals.Select(residual => residual.AngularErrorDegrees).DefaultIfEmpty(double.NaN).Max(),
            validationRms,
            validationP95,
            validationMax,
            validationTargetReached,
            status,
            MeanAbsolute(calibrationResiduals.Select(residual => residual.DeltaLongitudeDegrees)),
            MeanAbsolute(calibrationResiduals.Select(residual => residual.DeltaLatitudeDegrees)),
            MeanAbsolute(validationResiduals.Select(residual => residual.DeltaLongitudeDegrees)),
            MeanAbsolute(validationResiduals.Select(residual => residual.DeltaLatitudeDegrees)));

        if (calibrationRms > request.TargetMaximumDegrees)
            diagnostics.Add(new("high-calibration-error", "Calibration points remain inconsistent with the fitted model."));

        return new(parameters, residuals, metrics, diagnostics, true);
    }

    public static ParametricGeoreferenceTransform CreateTransform(MapSpatialReference sourceReference, GeographicCalibrationParameters parameters) =>
        new(sourceReference, parameters);

    private static (double North, double South) FitBoundedLatitudes(
        IReadOnlyList<(double V, double ProjectedLatitude)> samples, double minimum, double maximum)
    {
        // Minimize sum(((1-v)*north + v*south - latitude)^2) on the
        // triangle minimum <= south <= north <= maximum. An infeasible
        // unconstrained optimum puts the constrained optimum on an edge.
        // Solve on every edge, refitting the free bound instead of simply
        // clamping the two unconstrained bounds. Order is deterministic.
        var northWeightSquared = samples.Sum(point => (1 - point.V) * (1 - point.V));
        var southWeightSquared = samples.Sum(point => point.V * point.V);
        var candidates = new List<(double North, double South)>();
        foreach (var bound in new[] { minimum, maximum })
        {
            var south = samples.Sum(point => point.V * (point.ProjectedLatitude - (1 - point.V) * bound)) / southWeightSquared;
            candidates.Add((bound, Math.Clamp(south, minimum, bound)));
            var north = samples.Sum(point => (1 - point.V) * (point.ProjectedLatitude - point.V * bound)) / northWeightSquared;
            candidates.Add((Math.Clamp(north, bound, maximum), bound));
        }
        var collapsed = Math.Clamp(samples.Average(point => point.ProjectedLatitude), minimum, maximum);
        candidates.Add((collapsed, collapsed));
        return candidates.MinBy(candidate => samples.Sum(point =>
        {
            var residual = (1 - point.V) * candidate.North + point.V * candidate.South - point.ProjectedLatitude;
            return residual * residual;
        }));
    }

    private static GeographicCalibrationResult Failed(string code, string message) =>
        new(null, [], null, [new GeographicCalibrationDiagnostic(code, message, true)], false);

    private static double Rms(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? double.NaN : Math.Sqrt(array.Average(value => value * value));
    }

    private static double MeanAbsolute(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? double.NaN : array.Average(Math.Abs);
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double AngularDistance(GeoCoordinate first, GeoCoordinate second)
    {
        var lat1 = first.LatitudeDegrees * Math.PI / 180;
        var lat2 = second.LatitudeDegrees * Math.PI / 180;
        var deltaLat = lat2 - lat1;
        var deltaLon = LongitudeInterval.Normalize(second.LongitudeDegrees - first.LongitudeDegrees) * Math.PI / 180;
        var haversine = Math.Pow(Math.Sin(deltaLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        return 2 * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1))) * 180 / Math.PI;
    }

    private sealed record ResolvedControlPoint(GeographicControlPoint Point, MapPoint MapPosition, GridCoordinate GridPosition, GeoCoordinate BaseGeographic);
}

/// <summary>Runtime transform materialized from the stable calibration parameter DTO.</summary>
public sealed class ParametricGeoreferenceTransform : IManualMapGeoreferenceTransform
{
    private readonly MapSpatialReference _sourceReference;
    private readonly IGridMapping _mapping;
    private readonly GeographicCalibrationParameters _parameters;
    private readonly double _longitudeCycleOffset;

    public ParametricGeoreferenceTransform(MapSpatialReference sourceReference, GeographicCalibrationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(sourceReference);
        ArgumentNullException.ThrowIfNull(parameters);
        sourceReference.Validate();
        var calibrationSourceReference = parameters.SourceReference ?? sourceReference;
        calibrationSourceReference.Validate();
        if (!Enum.IsDefined(parameters.SourceMapping) || !double.IsFinite(parameters.LongitudeOffsetDegrees) ||
            !double.IsFinite(parameters.SourceSouthLatitude) || !double.IsFinite(parameters.SourceNorthLatitude) ||
            parameters.SourceSouthLatitude < -90 || parameters.SourceNorthLatitude > 90 || parameters.SourceSouthLatitude >= parameters.SourceNorthLatitude)
            throw new ArgumentException("Calibration parameters are invalid.", nameof(parameters));
        var coverage = MapCoverage.Create(calibrationSourceReference.Coverage.Kind, calibrationSourceReference.Longitude,
            parameters.SourceSouthLatitude, parameters.SourceNorthLatitude);
        _sourceReference = calibrationSourceReference with
        {
            GridMapping = parameters.SourceMapping,
            Coverage = coverage,
            PreserveProjectedCellAspectRatio = false
        };
        _mapping = _sourceReference.GridMapping switch
        {
            GridMappingKind.Equirectangular => new EquirectangularGridMapping(_sourceReference),
            GridMappingKind.WebMercator => new WebMercatorGridMapping(_sourceReference),
            _ => throw new ArgumentException("Calibration source mapping is not supported.", nameof(parameters))
        };
        _parameters = parameters;
        var origin = _sourceReference.Longitude.Start + parameters.LongitudeOffsetDegrees;
        _longitudeCycleOffset = origin - LongitudeInterval.Normalize(origin);
    }

    /// <summary>
    /// Encodes the fitted interpretation as a spatial reference on the same
    /// grid and in the same map units. Mapping into this reference preserves
    /// authored contours while giving every position its calibrated geography.
    /// </summary>
    public MapSpatialReference CreateCalibratedReference() => _sourceReference with
    {
        Coverage = MapCoverage.Create(_sourceReference.Coverage.Kind,
            new LongitudeInterval(_sourceReference.Longitude.Start + _parameters.LongitudeOffsetDegrees, _sourceReference.Longitude.SpanDegrees),
            _sourceReference.SouthLatitude, _sourceReference.NorthLatitude)
    };

    public GeoCoordinate ToGeographic(MapPoint sourceMapPoint)
    {
        var geographic = ToUnwrappedGeographic(sourceMapPoint);
        return geographic with { LongitudeDegrees = LongitudeInterval.Normalize(geographic.LongitudeDegrees) };
    }

    public GeoCoordinate ToUnwrappedGeographic(MapPoint sourceMapPoint)
    {
        var grid = new GridCoordinate(sourceMapPoint.X / _sourceReference.UnitsPerCell, sourceMapPoint.Y / _sourceReference.UnitsPerCell);
        var geographic = _mapping.GridToGeographic(grid);
        return new GeoCoordinate(
            geographic.LongitudeDegrees + _parameters.LongitudeOffsetDegrees - _longitudeCycleOffset,
            geographic.LatitudeDegrees);
    }

    /// <summary>Inverse of <see cref="ToGeographic"/>, used for image resampling.</summary>
    public MapPoint ToSourceMap(GeoCoordinate calibratedGeographic)
    {
        var sourceGeographic = new GeoCoordinate(
            calibratedGeographic.LongitudeDegrees - _parameters.LongitudeOffsetDegrees + _longitudeCycleOffset,
            calibratedGeographic.LatitudeDegrees);
        var grid = _mapping.GeographicToGrid(sourceGeographic);
        return new MapPoint(grid.X * _sourceReference.UnitsPerCell, grid.Y * _sourceReference.UnitsPerCell);
    }
}
