using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Spatial;

/// <summary>
/// The conventional spherical Web Mercator projection used by EPSG:3857.
/// Coordinates are metres on a sphere with the WGS84 semi-major radius.
/// </summary>
public static class WebMercator3857
{
    public const double EarthRadiusMeters = 6378137.0;
    public const double LatitudeLimitDegrees = WebMercatorGridMapping.WebMercatorLatitudeLimit;
    public const double HalfWorldMeters = 20037508.342789244;
    public const double WorldWidthMeters = HalfWorldMeters * 2.0;

    public static MapPoint Forward(GeoCoordinate geographic, LatitudeOverflowPolicy overflowPolicy = LatitudeOverflowPolicy.Reject)
    {
        if (!Enum.IsDefined(overflowPolicy))
            throw new ArgumentOutOfRangeException(nameof(overflowPolicy), "Unknown latitude overflow policy.");
        if (!double.IsFinite(geographic.LongitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(geographic), "Longitude must be finite.");
        if (!double.IsFinite(geographic.LatitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(geographic), "Latitude must be finite.");

        var latitude = geographic.LatitudeDegrees;
        if (latitude < -LatitudeLimitDegrees || latitude > LatitudeLimitDegrees)
        {
            if (overflowPolicy == LatitudeOverflowPolicy.Reject)
                throw new ArgumentOutOfRangeException(nameof(geographic), $"Web Mercator latitude must be within ±{LatitudeLimitDegrees:R} degrees.");

            latitude = Math.Clamp(latitude, -LatitudeLimitDegrees, LatitudeLimitDegrees);
        }

        var x = EarthRadiusMeters * geographic.LongitudeDegrees * Math.PI / 180.0;
        // Avoid the small floating-point residue produced by tan(pi/4) so the
        // projection's defined origin is exactly (0, 0).
        var y = latitude == 0
            ? 0.0
            : EarthRadiusMeters * Math.Log(Math.Tan(Math.PI / 4.0 + latitude * Math.PI / 360.0));
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new InvalidOperationException("Web Mercator produced a non-finite coordinate.");

        return new MapPoint(x, y);
    }

    public static GeoCoordinate Inverse(
        MapPoint projected,
        LatitudeOverflowPolicy overflowPolicy = LatitudeOverflowPolicy.Reject)
    {
        if (!Enum.IsDefined(overflowPolicy))
            throw new ArgumentOutOfRangeException(nameof(overflowPolicy), "Unknown latitude overflow policy.");
        if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y))
            throw new ArgumentOutOfRangeException(nameof(projected), "Projected coordinates must be finite.");

        var y = projected.Y;
        if (y < -HalfWorldMeters || y > HalfWorldMeters)
        {
            if (overflowPolicy == LatitudeOverflowPolicy.Reject)
                throw new ArgumentOutOfRangeException(nameof(projected), $"Projected Y must be within ±{HalfWorldMeters:R} metres.");

            y = Math.Clamp(y, -HalfWorldMeters, HalfWorldMeters);
        }

        var longitude = projected.X / EarthRadiusMeters * 180.0 / Math.PI;
        var latitude = Math.Atan(Math.Sinh(y / EarthRadiusMeters)) * 180.0 / Math.PI;
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude))
            throw new InvalidOperationException("Web Mercator inverse produced a non-finite coordinate.");

        return new GeoCoordinate(longitude, latitude);
    }
}
