using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.ManualAuthoring.Georeferencing;

public enum GeographicControlPointAnchorKind
{
    MapUnits,
    BackgroundImageUv
}

public enum GeographicControlPointRole
{
    Calibration,
    Validation
}

/// <summary>A known geographic location tied to an authoring map or background image.</summary>
public sealed record GeographicControlPoint(
    int Id,
    string Name,
    GeographicControlPointAnchorKind AnchorKind,
    GeographicControlPointRole Role,
    GeoCoordinate GeographicPosition,
    MapPoint? MapPosition = null,
    double? BackgroundU = null,
    double? BackgroundV = null,
    bool IsActive = true,
    BackgroundImagePlacement? BackgroundPlacementAtCapture = null,
    string? BackgroundImageId = null,
    MapSpatialReference? SourceReferenceAtCapture = null);

/// <summary>The image's independent placement in map units, including its display rotation.</summary>
public sealed record BackgroundImagePlacement(
    double X,
    double Y,
    double Width,
    double Height,
    double RotationDegrees = 0)
{
    public MapPoint ToMapPoint(double u, double v)
    {
        Validate();
        if (!double.IsFinite(u) || !double.IsFinite(v))
            throw new ArgumentOutOfRangeException(nameof(u), "Background image coordinates must be finite.");
        var centerX = X + Width / 2;
        var centerY = Y + Height / 2;
        var pointX = X + u * Width;
        var pointY = Y + v * Height;
        var angle = RotationDegrees * Math.PI / 180;
        var dx = pointX - centerX;
        var dy = pointY - centerY;
        return new MapPoint(centerX + dx * Math.Cos(angle) - dy * Math.Sin(angle), centerY + dx * Math.Sin(angle) + dy * Math.Cos(angle));
    }

    public (double U, double V) ToImageUv(MapPoint mapPosition)
    {
        Validate();
        var centerX = X + Width / 2;
        var centerY = Y + Height / 2;
        var dx = mapPosition.X - centerX;
        var dy = mapPosition.Y - centerY;
        var angle = -RotationDegrees * Math.PI / 180;
        var unrotatedX = centerX + dx * Math.Cos(angle) - dy * Math.Sin(angle);
        var unrotatedY = centerY + dx * Math.Sin(angle) + dy * Math.Cos(angle);
        return ((unrotatedX - X) / Width, (unrotatedY - Y) / Height);
    }

    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0 || !double.IsFinite(RotationDegrees))
            throw new ArgumentException("Background image placement must have finite position, positive size, and finite rotation.");
    }
}

/// <summary>Serializable inverse-map recipe for rebuilding a transformed background from its source image.</summary>
public sealed record BackgroundReprojectionState(
    MapSpatialReference SourceReference,
    MapSpatialReference TargetReference,
    BackgroundImagePlacement SourcePlacement,
    double SourceLongitudeOffsetDegrees = 0,
    GeographicCalibrationParameters? CalibrationParameters = null,
    string? SourceImageId = null);

/// <summary>Stable, JSON-friendly parameters for the MVP geographic correction model.</summary>
public sealed record GeographicCalibrationParameters(
    int Version,
    GridMappingKind SourceMapping,
    double LongitudeOffsetDegrees,
    double SourceSouthLatitude,
    double SourceNorthLatitude,
    bool IsApplied = false,
    bool IsAppliedToBackground = false,
    MapSpatialReference? SourceReference = null);

public sealed record GeographicPointResidual(
    int ControlPointId,
    string Name,
    GeographicControlPointRole Role,
    GeoCoordinate Expected,
    GeoCoordinate Before,
    GeoCoordinate After,
    double DeltaLongitudeDegrees,
    double DeltaLatitudeDegrees,
    double AngularErrorDegrees);

public sealed record GeographicCalibrationMetrics(
    int CalibrationPointCount,
    int ValidationPointCount,
    double CalibrationRmsDegrees,
    double CalibrationMaximumDegrees,
    double ValidationRmsDegrees,
    double ValidationP95Degrees,
    double ValidationMaximumDegrees,
    bool ValidationTargetReached,
    string ValidationStatus,
    double CalibrationMeanAbsoluteLongitudeErrorDegrees = double.NaN,
    double CalibrationMeanAbsoluteLatitudeErrorDegrees = double.NaN,
    double ValidationMeanAbsoluteLongitudeErrorDegrees = double.NaN,
    double ValidationMeanAbsoluteLatitudeErrorDegrees = double.NaN);

public sealed record GeographicCalibrationDiagnostic(
    string Code,
    string Message,
    bool IsError = false,
    int? ControlPointId = null);

public sealed record GeographicCalibrationResult(
    GeographicCalibrationParameters? Parameters,
    IReadOnlyList<GeographicPointResidual> Residuals,
    GeographicCalibrationMetrics? Metrics,
    IReadOnlyList<GeographicCalibrationDiagnostic> Diagnostics,
    bool IsSuccessful);
