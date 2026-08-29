using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#pragma warning disable CS0618

using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Regions;

/// <summary>Creates and verifies the compatibility identity of a portable draft.</summary>
public static class RegionDraftCompatibility
{
    public static RegionDraftDocument CreateDocument(
        MapMask mask,
        MapGenerationOptions options,
        IReadOnlyList<Landmass> landmasses,
        RegionDraft draft,
        bool applyBoundaryDistortion)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(draft);

        var spatial = options.EffectiveSpatial.CreateReference(mask.Width, mask.Height);
        return new RegionDraftDocument(
            RegionDraftDocument.CurrentSchemaVersion,
            options.ProjectionMode,
            new MapBounds(spatial.WidthInMapUnits, spatial.HeightInMapUnits, spatial.UnitsPerCell),
            CreateMaskFingerprint(mask),
            CreateLandmassFingerprint(landmasses),
            applyBoundaryDistortion,
            draft,
            spatial);
    }

    public static void EnsureCompatible(
        RegionDraftDocument document,
        MapMask mask,
        MapGenerationOptions options,
        IReadOnlyList<Landmass> landmasses)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(landmasses);

        if (document.SchemaVersion is not (RegionDraftDocument.CurrentSchemaVersion or "1.0"))
            throw new InvalidOperationException($"Unsupported region draft schema version '{document.SchemaVersion}'.");

        var expectedSpatial = options.EffectiveSpatial.CreateReference(mask.Width, mask.Height);
        var documentSpatial = document.SchemaVersion == "1.0"
            ? CreateLegacyReference(document)
            : document.SpatialReference
                ?? throw new InvalidOperationException("Schema 2.0 region drafts require a canonical spatial reference.");
        EnsureSpatiallyCompatible(documentSpatial, expectedSpatial);

        var expectedBounds = new MapBounds(expectedSpatial.WidthInMapUnits, expectedSpatial.HeightInMapUnits, expectedSpatial.UnitsPerCell);
        if (document.Bounds != expectedBounds)
            throw new InvalidOperationException("Region draft bounds or pixel size do not match the current generation.");
        var expectedMaskFingerprint = document.SchemaVersion == "1.0"
            ? CreateLegacyMaskFingerprint(mask)
            : CreateMaskFingerprint(mask);
        if (!string.Equals(document.MaskFingerprint, expectedMaskFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Region draft mask fingerprint does not match the current mask.");
        if (!string.Equals(document.LandmassFingerprint, CreateLandmassFingerprint(landmasses), StringComparison.Ordinal))
            throw new InvalidOperationException("Region draft landmass fingerprint does not match the current land geometry.");
    }

    /// <summary>
    /// Compares only canonical generation identity.  In particular, output
    /// coordinate system and output densification policy are not fields of a
    /// <see cref="MapSpatialReference"/> and cannot make a draft incompatible.
    /// </summary>
    public static bool AreSpatiallyCompatible(MapSpatialReference left, MapSpatialReference right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        left.Validate();
        right.Validate();

        return left.GridWidth == right.GridWidth &&
               left.GridHeight == right.GridHeight &&
               NearlyEqual(left.UnitsPerCell, right.UnitsPerCell) &&
               left.WorldModel.Kind == right.WorldModel.Kind &&
               NullableNearlyEqual(left.WorldModel.PlanetRadius, right.WorldModel.PlanetRadius) &&
               left.Coverage.Kind == right.Coverage.Kind &&
               NearlyEqual(left.Coverage.SouthLatitude, right.Coverage.SouthLatitude) &&
               NearlyEqual(left.Coverage.NorthLatitude, right.Coverage.NorthLatitude) &&
               NearlyEqual(left.Longitude.StartLongitudeDegrees, right.Longitude.StartLongitudeDegrees) &&
               NearlyEqual(left.Longitude.SpanDegrees, right.Longitude.SpanDegrees) &&
               left.GridMapping == right.GridMapping &&
               left.Topology == right.Topology &&
               left.CanonicalCoordinates == right.CanonicalCoordinates &&
               left.PreserveProjectedCellAspectRatio == right.PreserveProjectedCellAspectRatio &&
               left.LegacyCompatibility == right.LegacyCompatibility;
    }

    public static string CreateMaskFingerprint(MapMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var builder = new StringBuilder().Append(mask.Width).Append('x').Append(mask.Height).Append('|');
        foreach (var point in mask.LandPoints.OrderBy(point => point.Y).ThenBy(point => point.X))
            builder.Append(point.X).Append(',').Append(point.Y).Append(';');
        return Hash(builder.ToString());
    }

    /// <summary>Fingerprint format used by schema v1 documents.</summary>
    private static string CreateLegacyMaskFingerprint(MapMask mask)
    {
        var builder = new StringBuilder().Append(mask.Width).Append('x').Append(mask.Height).Append('|');
        foreach (var point in mask.LandPoints.OrderBy(point => point.Y).ThenBy(point => point.X))
            builder.Append(point.X).Append(',').Append(point.Y).Append(';');
        return Hash(builder.ToString());
    }

    public static string CreateLandmassFingerprint(IEnumerable<Landmass> landmasses)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        var builder = new StringBuilder();
        foreach (var landmass in landmasses.OrderBy(landmass => landmass.Id.Value))
        {
            builder.Append(landmass.Id.Value).Append(':');
            foreach (var coordinate in landmass.Shape.Coordinates)
                builder.Append(coordinate.X.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(coordinate.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            builder.Append('|');
        }
        return Hash(builder.ToString());
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static MapSpatialReference CreateLegacyReference(RegionDraftDocument document)
    {
        if (document.Bounds.UnitsPerCell <= 0 ||
            !double.IsFinite(document.Bounds.Width) ||
            !double.IsFinite(document.Bounds.Height))
        {
            throw new InvalidOperationException("Legacy region draft bounds are invalid.");
        }

        var width = document.Bounds.Width / document.Bounds.UnitsPerCell;
        var height = document.Bounds.Height / document.Bounds.UnitsPerCell;
        if (!IsWholePositive(width) || !IsWholePositive(height))
            throw new InvalidOperationException("Legacy region draft bounds do not describe an integral canonical grid.");

        var spatial = MapSpatialOptions.FromLegacy(document.ProjectionMode, document.Bounds.UnitsPerCell);
        return spatial.CreateReference((int)Math.Round(width), (int)Math.Round(height));
    }

    private static void EnsureSpatiallyCompatible(MapSpatialReference actual, MapSpatialReference expected)
    {
        if (!AreSpatiallyCompatible(actual, expected))
        {
            throw new InvalidOperationException(
                "Region draft spatial reference does not match the current canonical world, coverage, grid mapping, topology, or units-per-cell.");
        }
    }

    private static bool IsWholePositive(double value) => value > 0 && Math.Abs(value - Math.Round(value)) <= 1e-9;

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 1e-10;

    private static bool NullableNearlyEqual(double? left, double? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || NearlyEqual(left.Value, right!.Value));
}
