using System.Globalization;

namespace MapRegionizer.Core.Domain;

/// <summary>Describes the abstract surface represented by a generated map.</summary>
public enum WorldModelKind
{
    Planar,
    Spherical
}

/// <summary>
/// A world model descriptor. A spherical world is not implicitly the Earth; the
/// radius is only a scale for consumers that need surface distances.
/// </summary>
public sealed record WorldModelDescriptor
{
    public WorldModelKind Kind { get; init; }
    public double? PlanetRadius { get; init; }

    public static WorldModelDescriptor Planar() => new() { Kind = WorldModelKind.Planar };

    public static WorldModelDescriptor Spherical(double? planetRadius = null)
    {
        if (planetRadius is <= 0 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
            throw new ArgumentOutOfRangeException(nameof(planetRadius), "Planet radius must be finite and greater than zero.");

        return new WorldModelDescriptor { Kind = WorldModelKind.Spherical, PlanetRadius = planetRadius };
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Unknown world model kind.");

        if (PlanetRadius is <= 0 or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
            throw new ArgumentOutOfRangeException(nameof(PlanetRadius), "Planet radius must be finite and greater than zero.");

        if (Kind == WorldModelKind.Planar && PlanetRadius.HasValue)
            throw new ArgumentException("A planar world cannot define a planet radius.", nameof(PlanetRadius));
    }
}

/// <summary>An oriented longitude interval that may cross the antimeridian.</summary>
public readonly record struct LongitudeInterval
{
    public LongitudeInterval(double startLongitudeDegrees, double spanDegrees)
    {
        if (!double.IsFinite(startLongitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(startLongitudeDegrees), "Longitude must be finite.");
        if (!double.IsFinite(spanDegrees) || spanDegrees <= 0 || spanDegrees > 360)
            throw new ArgumentOutOfRangeException(nameof(spanDegrees), "Longitude span must be greater than zero and at most 360 degrees.");

        StartLongitudeDegrees = Normalize(startLongitudeDegrees);
        SpanDegrees = spanDegrees;
    }

    public double StartLongitudeDegrees { get; }
    public double SpanDegrees { get; }

    public double Start => StartLongitudeDegrees;
    public double Span => SpanDegrees;

    public double EndLongitudeDegrees => StartLongitudeDegrees + SpanDegrees;

    public double End => EndLongitudeDegrees;

    public double WestLongitudeDegrees => StartLongitudeDegrees;

    public double EastLongitudeDegrees => Normalize(StartLongitudeDegrees + SpanDegrees);

    public bool CrossesAntimeridian => EndLongitudeDegrees > 180 || EndLongitudeDegrees < -180;

    public static LongitudeInterval FromWestEast(double westLongitudeDegrees, double eastLongitudeDegrees)
    {
        var start = Normalize(westLongitudeDegrees);
        var east = Normalize(eastLongitudeDegrees);
        var span = east - start;
        if (span <= 0)
            span += 360;

        return new LongitudeInterval(start, span);
    }

    public bool Contains(double longitudeDegrees)
    {
        if (!double.IsFinite(longitudeDegrees))
            return false;

        var delta = NormalizePositive(longitudeDegrees - StartLongitudeDegrees);
        return delta <= SpanDegrees + 1e-12;
    }

    public double NormalizeLongitude(double longitudeDegrees) => Normalize(longitudeDegrees);

    public void Validate()
    {
        if (!double.IsFinite(StartLongitudeDegrees) || StartLongitudeDegrees < -180 || StartLongitudeDegrees >= 180)
            throw new ArgumentOutOfRangeException(nameof(StartLongitudeDegrees), "Longitude start must be in [-180, 180) degrees.");
        if (!double.IsFinite(SpanDegrees) || SpanDegrees <= 0 || SpanDegrees > 360)
            throw new ArgumentOutOfRangeException(nameof(SpanDegrees), "Longitude span must be greater than zero and at most 360 degrees.");
    }

    public override string ToString() =>
        $"{StartLongitudeDegrees.ToString("R", CultureInfo.InvariantCulture)}° + {SpanDegrees.ToString("R", CultureInfo.InvariantCulture)}°";

    public static double Normalize(double longitudeDegrees)
    {
        if (!double.IsFinite(longitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), "Longitude must be finite.");

        var normalized = longitudeDegrees % 360.0;
        if (normalized >= 180.0)
            normalized -= 360.0;
        if (normalized < -180.0)
            normalized += 360.0;
        return normalized == -0.0 ? 0.0 : normalized;
    }

    /// <summary>
    /// Normalizes a directed longitude delta to the positive interval [0, 360).
    /// Unlike <see cref="Normalize"/>, this preserves the direction of a
    /// coverage interval wider than 180 degrees.
    /// </summary>
    public static double NormalizePositive(double longitudeDegrees)
    {
        if (!double.IsFinite(longitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), "Longitude must be finite.");

        var normalized = longitudeDegrees % 360.0;
        if (normalized < 0)
            normalized += 360.0;
        return normalized == -0.0 ? 0.0 : normalized;
    }
}

/// <summary>Whether coverage represents the whole longitude interval or a local window.</summary>
public enum MapCoverageKind
{
    Global,
    Regional
}

/// <summary>Geographic extent represented by a generation grid.</summary>
public sealed record MapCoverage
{
    public MapCoverageKind Kind { get; init; }
    public LongitudeInterval Longitude { get; init; }
    public LongitudeInterval LongitudeInterval => Longitude;
    public double SouthLatitude { get; init; }
    public double NorthLatitude { get; init; }

    public bool IsGlobal => Kind == MapCoverageKind.Global;
    public double LatitudeSpan => NorthLatitude - SouthLatitude;
    public double LongitudeSpan => Longitude.SpanDegrees;
    public double South => SouthLatitude;
    public double North => NorthLatitude;
    public double West => Longitude.StartLongitudeDegrees;
    public double East => Longitude.EastLongitudeDegrees;

    public static MapCoverage Global(double southLatitude = -90, double northLatitude = 90) =>
        Create(MapCoverageKind.Global, new LongitudeInterval(-180, 360), southLatitude, northLatitude);

    public static MapCoverage Regional(
        LongitudeInterval longitude,
        double southLatitude,
        double northLatitude) =>
        Create(MapCoverageKind.Regional, longitude, southLatitude, northLatitude);

    public static MapCoverage Regional(
        double westLongitude,
        double eastLongitude,
        double southLatitude,
        double northLatitude) =>
        Regional(LongitudeInterval.FromWestEast(westLongitude, eastLongitude), southLatitude, northLatitude);

    public static MapCoverage Create(
        MapCoverageKind kind,
        LongitudeInterval longitude,
        double southLatitude,
        double northLatitude)
    {
        var coverage = new MapCoverage
        {
            Kind = kind,
            Longitude = longitude,
            SouthLatitude = southLatitude,
            NorthLatitude = northLatitude
        };
        coverage.Validate();
        return coverage;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Unknown coverage kind.");
        if (!double.IsFinite(SouthLatitude) || SouthLatitude < -90 || SouthLatitude > 90)
            throw new ArgumentOutOfRangeException(nameof(SouthLatitude), "Latitude must be within -90..90 degrees.");
        if (!double.IsFinite(NorthLatitude) || NorthLatitude < -90 || NorthLatitude > 90)
            throw new ArgumentOutOfRangeException(nameof(NorthLatitude), "Latitude must be within -90..90 degrees.");
        if (SouthLatitude >= NorthLatitude)
            throw new ArgumentException("South latitude must be less than north latitude.", nameof(NorthLatitude));
        Longitude.Validate();
        if (Kind == MapCoverageKind.Global && Longitude.SpanDegrees < 360 - 1e-12)
            throw new ArgumentException("Global coverage must span the full 360 degrees of longitude.", nameof(Longitude));
    }
}

public enum GridMappingKind
{
    Equirectangular,
    WebMercator
}

public enum GridTopologyKind
{
    OpenRectangular,
    CylindricalX,

    // Short aliases make configuration convenient while retaining the explicit
    // names used by the spatial design.
    Open = OpenRectangular,
    Cylindrical = CylindricalX
}

public enum CoordinateSpaceKind
{
    GridMapUnits,
    GeographicLongitudeLatitude,
    WebMercator
}

/// <summary>
/// Historical projection compatibility profile. It records old edge policies
/// without turning the legacy enum into the new spatial model.
/// </summary>
public enum LegacyCompatibilityProfile
{
    None,
    EquirectangularWorld,
    Flat,
    Regional
}

public readonly record struct GridCoordinate(double X, double Y)
{
    public GridCoordinate ToCellCenter() => new(X + 0.5, Y + 0.5);
}

public readonly record struct GeoCoordinate(double LongitudeDegrees, double LatitudeDegrees)
{
    public double Longitude => LongitudeDegrees;
    public double Latitude => LatitudeDegrees;
}

/// <summary>
/// Immutable metadata describing the canonical grid and its placement on the
/// abstract world surface.
/// </summary>
public sealed record MapSpatialReference
{
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public double UnitsPerCell { get; init; }
    public WorldModelDescriptor WorldModel { get; init; } = WorldModelDescriptor.Spherical();
    public MapCoverage Coverage { get; init; } = MapCoverage.Global();
    public GridMappingKind GridMapping { get; init; } = GridMappingKind.Equirectangular;
    public GridTopologyKind Topology { get; init; } = GridTopologyKind.CylindricalX;
    public CoordinateSpaceKind CanonicalCoordinates { get; init; } = CoordinateSpaceKind.GridMapUnits;
    public LegacyCompatibilityProfile LegacyCompatibility { get; init; } = LegacyCompatibilityProfile.None;

    public double WidthInMapUnits => GridWidth * UnitsPerCell;
    public double HeightInMapUnits => GridHeight * UnitsPerCell;
    public int Width => GridWidth;
    public int Height => GridHeight;
    public double LongitudeSpan => Coverage.Longitude.SpanDegrees;
    public double SouthLatitude => Coverage.SouthLatitude;
    public double NorthLatitude => Coverage.NorthLatitude;
    public LongitudeInterval Longitude => Coverage.Longitude;

    public void Validate()
    {
        if (GridWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(GridWidth), "Grid width must be greater than zero.");
        if (GridHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(GridHeight), "Grid height must be greater than zero.");
        if (!double.IsFinite(UnitsPerCell) || UnitsPerCell <= 0)
            throw new ArgumentOutOfRangeException(nameof(UnitsPerCell), "Units per cell must be finite and greater than zero.");
        if (WorldModel is null)
            throw new ArgumentNullException(nameof(WorldModel));
        if (Coverage is null)
            throw new ArgumentNullException(nameof(Coverage));
        if (!Enum.IsDefined(GridMapping))
            throw new ArgumentOutOfRangeException(nameof(GridMapping), "Unknown grid mapping.");
        if (!Enum.IsDefined(Topology))
            throw new ArgumentOutOfRangeException(nameof(Topology), "Unknown grid topology.");
        if (!Enum.IsDefined(CanonicalCoordinates))
            throw new ArgumentOutOfRangeException(nameof(CanonicalCoordinates), "Unknown canonical coordinate space.");
        if (!Enum.IsDefined(LegacyCompatibility))
            throw new ArgumentOutOfRangeException(nameof(LegacyCompatibility), "Unknown legacy compatibility profile.");
        WorldModel.Validate();
        Coverage.Validate();
        if (GridMapping == GridMappingKind.WebMercator &&
            (Coverage.SouthLatitude < -85.0511287798066 || Coverage.NorthLatitude > 85.0511287798066))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Coverage),
                $"WebMercator coverage must be within ±{85.0511287798066:R} degrees of latitude.");
        }
        if (CanonicalCoordinates != CoordinateSpaceKind.GridMapUnits)
            throw new ArgumentException("Generated maps use GridMapUnits as their canonical coordinate space.", nameof(CanonicalCoordinates));
    }
}
