using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Projects authoritative vector land geometry into the simulation mask. A
/// cell is land when its center is covered by a landmass polygon.
/// </summary>
public sealed class ManualMapRasterizer
{
    private readonly GeometryFactory _geometryFactory;

    public ManualMapRasterizer(GeometryFactory? geometryFactory = null)
    {
        _geometryFactory = geometryFactory ?? new GeometryFactory();
    }

    public MapMask Rasterize(IReadOnlyList<Landmass> landmasses, MapSpatialReference spatialReference)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(spatialReference);
        spatialReference.Validate();

        var landPoints = new HashSet<GridPoint>();
        for (var y = 0; y < spatialReference.GridHeight; y++)
        {
            for (var x = 0; x < spatialReference.GridWidth; x++)
            {
                var sample = _geometryFactory.CreatePoint(new Coordinate(
                    (x + 0.5) * spatialReference.UnitsPerCell,
                    (y + 0.5) * spatialReference.UnitsPerCell));
                if (landmasses.Any(landmass => landmass.Shape.Covers(sample)))
                    landPoints.Add(new GridPoint(x, y));
            }
        }

        return new MapMask(GridWindow.FromSize(spatialReference.GridWidth, spatialReference.GridHeight), landPoints);
    }
}
