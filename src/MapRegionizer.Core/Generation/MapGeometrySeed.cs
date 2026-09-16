using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation;

/// <summary>
/// Explicit vector geometry supplied to a generation session. The mask remains
/// the raster input for simulation stages, while these landmasses remain the
/// authoritative coastline and region source.
/// </summary>
public sealed record MapGeometrySeed
{
    public MapGeometrySeed(
        IReadOnlyList<Landmass> Landmasses,
        RegionDraft RegionDraft)
    {
        ArgumentNullException.ThrowIfNull(Landmasses);
        ArgumentNullException.ThrowIfNull(RegionDraft);
        this.Landmasses = Landmasses
            .Select(landmass => landmass with
            {
                Shape = (NetTopologySuite.Geometries.Polygon)landmass.Shape.Copy()
            })
            .ToArray();
        this.RegionDraft = RegionDraft;
    }

    public IReadOnlyList<Landmass> Landmasses { get; }
    public RegionDraft RegionDraft { get; }
}
