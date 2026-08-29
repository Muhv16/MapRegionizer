#pragma warning disable CS0618

using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.Options;

/// <summary>Generation-time spatial semantics. Output coordinate choices are separate.</summary>
public sealed record MapSpatialOptions
{
    public WorldModelDescriptor WorldModel { get; init; } = WorldModelDescriptor.Spherical();
    public MapCoverage Coverage { get; init; } = MapCoverage.Global();
    public GridMappingKind GridMapping { get; init; } = GridMappingKind.Equirectangular;
    public GridTopologyKind Topology { get; init; } = GridTopologyKind.CylindricalX;
    public double UnitsPerCell { get; init; } = 1.0;
    /// <summary>
    /// Requires Web Mercator sampling cells to have equal projected X/Y size.
    /// The check is applied when grid dimensions are known.
    /// </summary>
    public bool PreserveProjectedCellAspectRatio { get; init; } = true;
    public LegacyCompatibilityProfile LegacyCompatibility { get; init; } = LegacyCompatibilityProfile.None;

    [Obsolete("Use UnitsPerCell. This alias is retained for legacy configuration compatibility.")]
    public double PixelSize
    {
        get => UnitsPerCell;
        init => UnitsPerCell = value;
    }

    public static MapSpatialOptions LegacyDefault() => new()
    {
        LegacyCompatibility = LegacyCompatibilityProfile.EquirectangularWorld
    };

    public static MapSpatialOptions FromLegacy(MapProjectionMode projectionMode, double unitsPerCell = 1.0) =>
        projectionMode switch
        {
            MapProjectionMode.EquirectangularWorld => new MapSpatialOptions
            {
                UnitsPerCell = unitsPerCell,
                LegacyCompatibility = LegacyCompatibilityProfile.EquirectangularWorld
            },
            // Historically Flat and Regional only changed a subset of shape
            // extraction/lake edge policies. Preserve those semantics explicitly
            // instead of claiming a new projection contract for them.
            MapProjectionMode.Flat => new MapSpatialOptions
            {
                UnitsPerCell = unitsPerCell,
                WorldModel = WorldModelDescriptor.Planar(),
                Coverage = MapCoverage.Regional(new LongitudeInterval(-180, 360), -90, 90),
                Topology = GridTopologyKind.CylindricalX,
                LegacyCompatibility = LegacyCompatibilityProfile.Flat
            },
            MapProjectionMode.Regional => new MapSpatialOptions
            {
                UnitsPerCell = unitsPerCell,
                WorldModel = WorldModelDescriptor.Planar(),
                Coverage = MapCoverage.Regional(new LongitudeInterval(-180, 360), -90, 90),
                Topology = GridTopologyKind.CylindricalX,
                LegacyCompatibility = LegacyCompatibilityProfile.Regional
            },
            _ => throw new ArgumentOutOfRangeException(nameof(projectionMode), projectionMode, "Unknown legacy projection mode.")
        };

    public void Validate()
    {
        if (!double.IsFinite(UnitsPerCell) || UnitsPerCell <= 0)
            throw new ArgumentOutOfRangeException(nameof(UnitsPerCell), "Units per cell must be finite and greater than zero.");
        if (!Enum.IsDefined(GridMapping))
            throw new ArgumentOutOfRangeException(nameof(GridMapping), "Unknown grid mapping.");
        if (!Enum.IsDefined(Topology))
            throw new ArgumentOutOfRangeException(nameof(Topology), "Unknown grid topology.");
        if (!Enum.IsDefined(LegacyCompatibility))
            throw new ArgumentOutOfRangeException(nameof(LegacyCompatibility), "Unknown legacy compatibility profile.");
        if (WorldModel is null)
            throw new ArgumentNullException(nameof(WorldModel));
        if (Coverage is null)
            throw new ArgumentNullException(nameof(Coverage));
        WorldModel.Validate();
        Coverage.Validate();

        if (GridMapping == GridMappingKind.WebMercator &&
            (Coverage.SouthLatitude < -WebMercatorGridMapping.WebMercatorLatitudeLimit ||
             Coverage.NorthLatitude > WebMercatorGridMapping.WebMercatorLatitudeLimit))
        {
            throw new ArgumentException("Web Mercator coverage must stay within the mathematical poles.", nameof(Coverage));
        }

        // The old Flat/Regional projection modes used a full-width cylindrical
        // raster despite their historical name. Keep that behavior at the
        // compatibility boundary; new regional configurations must make the
        // open-edge policy explicit.
        if (Coverage.Kind == MapCoverageKind.Regional &&
            Topology == GridTopologyKind.CylindricalX &&
            LegacyCompatibility == LegacyCompatibilityProfile.None)
            throw new ArgumentException("Regional coverage uses open X edges; cylindrical topology is reserved for full-world coverage.", nameof(Topology));
    }

    internal MapSpatialReference CreateReference(int width, int height)
    {
        var reference = new MapSpatialReference
        {
            GridWidth = width,
            GridHeight = height,
            UnitsPerCell = UnitsPerCell,
            WorldModel = WorldModel,
            Coverage = Coverage,
            GridMapping = GridMapping,
            Topology = Topology,
            CanonicalCoordinates = CoordinateSpaceKind.GridMapUnits,
            LegacyCompatibility = LegacyCompatibility,
            PreserveProjectedCellAspectRatio = PreserveProjectedCellAspectRatio
        };
        reference.Validate();
        return reference;
    }
}
