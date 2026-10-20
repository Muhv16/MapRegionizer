using MapRegionizer.Core.Domain;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using System.Threading;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Projects authoritative vector land geometry into the simulation mask. A
/// cell is land when its center is covered by a landmass polygon.
/// </summary>
public sealed class ManualMapRasterizer
{
    public ManualMapRasterizer(GeometryFactory? geometryFactory = null)
    {
        // Retained for source compatibility; indexed coordinate lookup does
        // not need to allocate point geometries through this factory.
        _ = geometryFactory;
    }

    public MapMask Rasterize(IReadOnlyList<Landmass> landmasses, MapSpatialReference spatialReference)
        => Rasterize(landmasses, spatialReference, CancellationToken.None);

    public MapMask Rasterize(
        IReadOnlyList<Landmass> landmasses,
        MapSpatialReference spatialReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(spatialReference);
        cancellationToken.ThrowIfCancellationRequested();
        spatialReference.Validate();

        var landPoints = new HashSet<GridPoint>();
        var unitsPerCell = spatialReference.UnitsPerCell;
        foreach (var landmass in landmasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (landmass.Shape.IsEmpty)
                continue;

            // Rasterization queries many cell centers against each polygon.
            // Build the point-in-area index once instead of repeating a full
            // geometry Covers operation for every center.
            var locator = new IndexedPointInAreaLocator(landmass.Shape);
            var sample = new Coordinate(0, 0);

            var bounds = landmass.Shape.EnvelopeInternal;
            var minX = Math.Max(0, Math.Ceiling(bounds.MinX / unitsPerCell - 0.5 - 1e-12));
            var maxX = Math.Min(spatialReference.GridWidth - 1, Math.Floor(bounds.MaxX / unitsPerCell - 0.5 + 1e-12));
            var minY = Math.Max(0, Math.Ceiling(bounds.MinY / unitsPerCell - 0.5 - 1e-12));
            var maxY = Math.Min(spatialReference.GridHeight - 1, Math.Floor(bounds.MaxY / unitsPerCell - 0.5 + 1e-12));
            if (minX > maxX || minY > maxY)
                continue;

            for (var y = (int)minY; y <= (int)maxY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = (int)minX; x <= (int)maxX; x++)
                {
                    if ((x & 0xFF) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    sample.X = (x + 0.5) * unitsPerCell;
                    sample.Y = (y + 0.5) * unitsPerCell;
                    var location = locator.Locate(sample);
                    if (location is Location.Interior or Location.Boundary)
                        landPoints.Add(new GridPoint(x, y));
                }
            }
        }

        return new MapMask(GridWindow.FromSize(spatialReference.GridWidth, spatialReference.GridHeight), landPoints);
    }
}
