using System.Globalization;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>Builds authoritative vector landmasses from validated manual faces.</summary>
public sealed class ManualLandmassBuilder
{
    public IReadOnlyList<Landmass> Build(
        IEnumerable<Polygon> regionPolygons,
        GeometryFactory? geometryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(regionPolygons);
        var polygons = regionPolygons.Where(polygon => !polygon.IsEmpty).Cast<Geometry>().ToArray();
        if (polygons.Length == 0)
            return [];

        var union = UnaryUnionOp.Union(polygons);
        if (union.IsEmpty)
            return [];
        if (!union.IsValid)
            union = union.Buffer(0);

        var components = ExtractPolygons(union)
            .Where(polygon => !polygon.IsEmpty && polygon.Area > 0)
            .OrderBy(polygon => CanonicalSortKey(polygon), StringComparer.Ordinal)
            .ToArray();

        return components
            .Select((polygon, index) => new Landmass(new LandmassId(index + 1), (Polygon)polygon.Copy()))
            .ToArray();
    }

    private static IEnumerable<Polygon> ExtractPolygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        for (var index = 0; index < geometry.NumGeometries; index++)
        {
            foreach (var child in ExtractPolygons(geometry.GetGeometryN(index)))
                yield return child;
        }
    }

    private static string CanonicalSortKey(Polygon polygon)
    {
        var envelope = polygon.EnvelopeInternal;
        var coordinates = polygon.Coordinates
            .Select(coordinate => string.Join(
                ",",
                RegionGeometryPrecision.Canonicalize(coordinate.X).ToString("R", CultureInfo.InvariantCulture),
                RegionGeometryPrecision.Canonicalize(coordinate.Y).ToString("R", CultureInfo.InvariantCulture)))
            .ToArray();
        return string.Join(
            "|",
            RegionGeometryPrecision.Canonicalize(envelope.MinX).ToString("R", CultureInfo.InvariantCulture),
            RegionGeometryPrecision.Canonicalize(envelope.MinY).ToString("R", CultureInfo.InvariantCulture),
            polygon.Area.ToString("R", CultureInfo.InvariantCulture),
            string.Join(";", coordinates));
    }
}
