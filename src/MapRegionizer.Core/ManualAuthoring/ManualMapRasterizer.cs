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
        var unitsPerCell = spatialReference.UnitsPerCell;
        foreach (var landmass in landmasses)
        {
            if (landmass.Shape.IsEmpty)
                continue;

            var bounds = landmass.Shape.EnvelopeInternal;
            var minX = Math.Max(0, Math.Ceiling(bounds.MinX / unitsPerCell - 0.5 - 1e-12));
            var maxX = Math.Min(spatialReference.GridWidth - 1, Math.Floor(bounds.MaxX / unitsPerCell - 0.5 + 1e-12));
            var minY = Math.Max(0, Math.Ceiling(bounds.MinY / unitsPerCell - 0.5 - 1e-12));
            var maxY = Math.Min(spatialReference.GridHeight - 1, Math.Floor(bounds.MaxY / unitsPerCell - 0.5 + 1e-12));
            if (minX > maxX || minY > maxY)
                continue;

            for (var y = (int)minY; y <= (int)maxY; y++)
            {
                for (var x = (int)minX; x <= (int)maxX; x++)
                {
                    var sample = _geometryFactory.CreatePoint(new Coordinate(
                        (x + 0.5) * unitsPerCell,
                        (y + 0.5) * unitsPerCell));
                    if (landmass.Shape.Covers(sample))
                        landPoints.Add(new GridPoint(x, y));
                }
            }
        }

        return new MapMask(GridWindow.FromSize(spatialReference.GridWidth, spatialReference.GridHeight), landPoints);
    }
}
