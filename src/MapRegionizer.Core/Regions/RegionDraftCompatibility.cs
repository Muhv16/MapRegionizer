using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        bool applyBoundaryDistortion) => new(
        RegionDraftDocument.CurrentSchemaVersion,
        options.ProjectionMode,
        new MapBounds(mask.Width * options.PixelSize, mask.Height * options.PixelSize, options.PixelSize),
        CreateMaskFingerprint(mask),
        CreateLandmassFingerprint(landmasses),
        applyBoundaryDistortion,
        draft);

    public static void EnsureCompatible(
        RegionDraftDocument document,
        MapMask mask,
        MapGenerationOptions options,
        IReadOnlyList<Landmass> landmasses)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != RegionDraftDocument.CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported region draft schema version '{document.SchemaVersion}'.");
        if (document.ProjectionMode != options.ProjectionMode)
            throw new InvalidOperationException("Region draft projection mode does not match the current generation.");

        var expectedBounds = new MapBounds(mask.Width * options.PixelSize, mask.Height * options.PixelSize, options.PixelSize);
        if (document.Bounds != expectedBounds)
            throw new InvalidOperationException("Region draft bounds or pixel size do not match the current generation.");
        if (!string.Equals(document.MaskFingerprint, CreateMaskFingerprint(mask), StringComparison.Ordinal))
            throw new InvalidOperationException("Region draft mask fingerprint does not match the current mask.");
        if (!string.Equals(document.LandmassFingerprint, CreateLandmassFingerprint(landmasses), StringComparison.Ordinal))
            throw new InvalidOperationException("Region draft landmass fingerprint does not match the current land geometry.");
    }

    public static string CreateMaskFingerprint(MapMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
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
}
