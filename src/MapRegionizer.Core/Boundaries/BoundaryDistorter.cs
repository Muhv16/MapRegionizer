using MapRegionizer.Core.Options;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Boundaries;

internal sealed class BoundaryDistorter
{
    private readonly SharedBorderFinder _borderFinder;
    private readonly BorderReplacementBuilder _replacementBuilder;
    private readonly PolygonBoundaryUpdater _polygonUpdater;

    public BoundaryDistorter(GeometryFactory geometryFactory, Random random)
    {
        _borderFinder = new SharedBorderFinder(geometryFactory);
        _replacementBuilder = new BorderReplacementBuilder(geometryFactory, random);
        _polygonUpdater = new PolygonBoundaryUpdater(geometryFactory);
    }

    public IReadOnlyList<Polygon> Distort(IReadOnlyList<Polygon> regions, Polygon landmass, BoundaryDistortionOptions options)
    {
        var borders = _borderFinder.FindSharedBorders(regions);
        var replacements = _replacementBuilder.BuildReplacements(borders, landmass, options);
        return _polygonUpdater.UpdatePolygons(regions, replacements);
    }
}
