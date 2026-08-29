using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Spatial;
using NetTopologySuite.Geometries;

namespace MapRegionizer.Core.Generation;

/// <summary>
/// Final output boundary for working-domain generation.  It copies raster
/// storage and clips/ translates canonical geometry; no stage sees this crop.
/// </summary>
internal static class GeneratedMapCropper
{
    public static GeneratedMap Create(MapGenerationContext context)
    {
        var requested = context.RequestedDomain;
        var working = context.WorkingDomain;
        if (requested.Window.Equals(working.Window))
        {
            var waterSurfaces = CropWaterSurfaces(context.WaterSurfaces, context, requested);
            return new GeneratedMap(
                context.Bounds,
                CloneLandmasses(context.Landmasses, context.GeometryFactory),
                CloneWaterBodies(context.WaterBodies, context.GeometryFactory),
                CloneRegions(context.Regions, context.GeometryFactory),
                CropTectonicPlates(context.TectonicPlates, context, requested),
                CropElevation(context.Elevation, context, requested, waterSurfaces),
                CropWaterTopology(context.WaterBodyTopology, context, requested),
                waterSurfaces,
                CropHydrology(context.Hydrology, context, requested),
                CropClimate(context.Climate, context, requested),
                CropRegionRaster(context.RegionRaster, context, requested),
                RequestedSpatialReference(context, requested),
                requested,
                working,
                context.GenerationMode);
        }

        var croppedWaterSurfaces = CropWaterSurfaces(context.WaterSurfaces, context, requested);
        return new GeneratedMap(
            RequestedBounds(context, requested),
            CropLandmasses(context.Landmasses, context, requested),
            CropWaterBodies(context.WaterBodies, context, requested),
            CropRegions(context.Regions, context, requested),
            CropTectonicPlates(context.TectonicPlates, context, requested),
            CropElevation(context.Elevation, context, requested, croppedWaterSurfaces),
            CropWaterTopology(context.WaterBodyTopology, context, requested),
            croppedWaterSurfaces,
            CropHydrology(context.Hydrology, context, requested),
            CropClimate(context.Climate, context, requested),
            CropRegionRaster(context.RegionRaster, context, requested),
            RequestedSpatialReference(context, requested),
            requested,
            working,
            context.GenerationMode);
    }

    private static MapBounds RequestedBounds(MapGenerationContext context, RequestedDomain requested)
    {
        var reference = RequestedSpatialReference(context, requested);
        return new MapBounds(reference.WidthInMapUnits, reference.HeightInMapUnits, reference.UnitsPerCell);
    }

    private static MapSpatialReference RequestedSpatialReference(MapGenerationContext context, RequestedDomain requested)
    {
        // Coverage describes the requested raster in the public result.  For
        // a working run its spatial context describes the overscan, so create
        // the equivalent reference at the exact crop dimensions.
        return context.RequestedSpatialOptions.CreateReference(requested.Width, requested.Height);
    }

    private static IReadOnlyList<Landmass> CloneLandmasses(IEnumerable<Landmass> landmasses, GeometryFactory factory) =>
        landmasses.Select(item => new Landmass(item.Id, (Polygon)item.Shape.Copy())).ToArray();

    private static IReadOnlyList<WaterBody> CloneWaterBodies(IEnumerable<WaterBody> waterBodies, GeometryFactory factory) =>
        waterBodies.Select(item => new WaterBody(item.Id, (Polygon)item.Shape.Copy())).ToArray();

    private static IReadOnlyList<MapRegion> CloneRegions(IEnumerable<MapRegion> regions, GeometryFactory factory) =>
        regions.Select(item => new MapRegion(item.Id, item.LandmassId, (Polygon)item.Shape.Copy())).ToArray();

    private static IReadOnlyList<Landmass> CropLandmasses(IEnumerable<Landmass> landmasses, MapGenerationContext context, RequestedDomain requested) =>
        landmasses.SelectMany(item => ClipPolygon(item.Shape, context, requested)
            .Select(shape => new Landmass(item.Id, shape))).ToArray();

    private static IReadOnlyList<WaterBody> CropWaterBodies(IEnumerable<WaterBody> waterBodies, MapGenerationContext context, RequestedDomain requested) =>
        waterBodies.SelectMany(item => ClipPolygon(item.Shape, context, requested)
            .Select(shape => new WaterBody(item.Id, shape))).ToArray();

    private static IReadOnlyList<MapRegion> CropRegions(IEnumerable<MapRegion> regions, MapGenerationContext context, RequestedDomain requested) =>
        regions.SelectMany(item => ClipPolygon(item.Shape, context, requested)
            .Select(shape => new MapRegion(item.Id, item.LandmassId, shape))).ToArray();

    private static IEnumerable<Polygon> ClipPolygon(Polygon polygon, MapGenerationContext context, RequestedDomain requested)
    {
        var (offsetX, offsetY) = context.WorkingDomain.OffsetOf(requested);
        var units = context.SpatialReference.UnitsPerCell;
        var envelope = new Envelope(offsetX * units, (offsetX + requested.Width) * units,
            offsetY * units, (offsetY + requested.Height) * units);
        var clipped = polygon.Intersection(context.GeometryFactory.ToGeometry(envelope));
        foreach (var child in EnumeratePolygons(clipped))
        {
            if (child.IsEmpty || child.Area <= 1e-10)
                continue;
            yield return TranslatePolygon(child, -offsetX * units, -offsetY * units, context.GeometryFactory);
        }
    }

    private static Polygon TranslatePolygon(Polygon polygon, double dx, double dy, GeometryFactory factory)
    {
        static Coordinate[] Translate(IEnumerable<Coordinate> coordinates, double dx, double dy) =>
            coordinates.Select(point => new Coordinate(point.X + dx, point.Y + dy)).ToArray();
        var shell = factory.CreateLinearRing(Translate(polygon.ExteriorRing.Coordinates, dx, dy));
        var holes = polygon.InteriorRings.Select(ring => factory.CreateLinearRing(Translate(ring.Coordinates, dx, dy))).ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private static IEnumerable<Polygon> EnumeratePolygons(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }
        if (geometry is not GeometryCollection collection)
            yield break;
        foreach (var child in collection.Geometries)
            foreach (var polygonChild in EnumeratePolygons(child))
                yield return polygonChild;
    }

    private static ElevationMap? CropElevation(
        ElevationMap? source,
        MapGenerationContext context,
        RequestedDomain requested,
        WaterSurfaceMap? waterSurfaces)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        return new ElevationMap(
            requested.Width, requested.Height,
            Crop(source.ElevationMetersSpan, source.Width, arrays),
            Crop(source.BaseElevationMetersSpan, source.Width, arrays),
            Crop(source.TectonicElevationMetersSpan, source.Width, arrays),
            Crop(source.RoughnessSpan, source.Width, arrays),
            Crop(source.ErosionMaskSpan, source.Width, arrays),
            Crop(source.TerrainClassSpan, source.Width, arrays),
            Crop(source.MountainPassPotentialSpan, source.Width, arrays),
            Crop(source.RidgeContinuitySpan, source.Width, arrays),
            Crop(source.FoothillInfluenceSpan, source.Width, arrays),
            Crop(source.BasinInfluenceSpan, source.Width, arrays),
            Crop(source.BedElevationMetersSpan, source.Width, arrays),
            Crop(source.WaterSurfaceMetersSpan, source.Width, arrays),
            waterSurfaces);
    }

    private static ClimateMap? CropClimate(ClimateMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        return new ClimateMap(
            requested.Width, requested.Height,
            Crop(source.LatitudeNormSpan, source.Width, arrays),
            Crop(source.MeanAnnualTemperatureSpan, source.Width, arrays),
            Crop(source.SummerTemperatureSpan, source.Width, arrays),
            Crop(source.WinterTemperatureSpan, source.Width, arrays),
            Crop(source.SeasonalitySpan, source.Width, arrays),
            Crop(source.AtmosphericMoistureSpan, source.Width, arrays),
            Crop(source.PrecipitationSpan, source.Width, arrays),
            Crop(source.MoistureSpan, source.Width, arrays),
            Crop(source.BiomeMoistureSpan, source.Width, arrays),
            Crop(source.RainShadowSpan, source.Width, arrays),
            Crop(source.MonsoonInfluenceSpan, source.Width, arrays),
            Crop(source.RiverValleyInfluenceSpan, source.Width, arrays),
            Crop(source.WetlandInfluenceSpan, source.Width, arrays),
            Crop(source.SnowOverlaySpan, source.Width, arrays),
            Crop(source.MountainOverlaySpan, source.Width, arrays),
            Crop(source.IceScoreSpan, source.Width, arrays),
            Crop(source.HabitabilitySpan, source.Width, arrays),
            Crop(source.AgriculturalPotentialSpan, source.Width, arrays),
            Crop(source.ClimateClassSpan, source.Width, arrays),
            Crop(source.BiomeSpan, source.Width, arrays));
    }

    private static HydrologyMap? CropHydrology(HydrologyMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var (offsetX, offsetY) = context.WorkingDomain.OffsetOf(requested);
        var rivers = source.Rivers
            .Select(river => CropRiver(river, offsetX, offsetY, requested))
            .Where(river => river is not null)
            .Select(river => river!)
            .ToArray();
        var mouths = source.Mouths
            .Where(mouth => IsInside(mouth.Cell, offsetX, offsetY, requested))
            .Select(mouth => mouth with { Cell = Translate(mouth.Cell, offsetX, offsetY) })
            .ToArray();
        var drainageBasinIds = Crop(source.DrainageBasinIdsSpan, source.Width, arrays);
        var basins = CropBasins(source.Basins, drainageBasinIds, requested.Width, requested.Height, offsetX, offsetY);
        var outlets = source.LakeOutlets
            .Select(outlet => CropLakeOutlet(outlet, offsetX, offsetY, requested))
            .Where(outlet => outlet is not null)
            .Select(outlet => outlet!)
            .ToArray();
        return new HydrologyMap(
            requested.Width, requested.Height,
            Crop(source.HydroSurfaceMetersSpan, source.Width, arrays),
            Crop(source.FlowDirectionsSpan, source.Width, arrays),
            Crop(source.FlowAccumulationSpan, source.Width, arrays),
            drainageBasinIds,
            Crop(source.RiverCellsSpan, source.Width, arrays),
            rivers,
            mouths,
            outlets,
            basins);
    }

    private static IReadOnlyList<DrainageBasin> CropBasins(
        IReadOnlyList<DrainageBasin> source,
        int[] basinIds,
        int width,
        int height,
        int offsetX,
        int offsetY)
    {
        var result = new List<DrainageBasin>(source.Count);
        foreach (var basin in source)
        {
            var points = new List<GridPoint>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (basinIds[y * width + x] == basin.Id)
                        points.Add(new GridPoint(x, y));
                }

            if (points.Count == 0)
                continue;

            var terminal = basin.TerminalCell.X >= offsetX && basin.TerminalCell.X < offsetX + width &&
                           basin.TerminalCell.Y >= offsetY && basin.TerminalCell.Y < offsetY + height
                ? Translate(basin.TerminalCell, offsetX, offsetY)
                : points[0];
            result.Add(basin with { TerminalCell = terminal, CellCount = points.Count });
        }

        return result;
    }

    private static LakeOutlet? CropLakeOutlet(LakeOutlet outlet, int offsetX, int offsetY, RequestedDomain requested)
    {
        var outletInside = outlet.OutletCell is { } outletPoint && IsInside(outletPoint, offsetX, offsetY, requested);
        var downstreamInside = outlet.DownstreamCell is { } downstreamPoint && IsInside(downstreamPoint, offsetX, offsetY, requested);
        if (!outletInside && !downstreamInside)
            return null;

        return outlet with
        {
            OutletCell = outlet.OutletCell is { } outletValue && IsInside(outletValue, offsetX, offsetY, requested)
                ? Translate(outletValue, offsetX, offsetY)
                : null,
            DownstreamCell = outlet.DownstreamCell is { } downstreamValue && IsInside(downstreamValue, offsetX, offsetY, requested)
                ? Translate(downstreamValue, offsetX, offsetY)
                : null
        };
    }

    private static RiverSegment? CropRiver(RiverSegment river, int offsetX, int offsetY, RequestedDomain requested)
    {
        var cells = river.Cells
            .Where(cell => IsInside(cell, offsetX, offsetY, requested))
            .Select(cell => Translate(cell, offsetX, offsetY))
            .ToArray();
        if (cells.Length == 0)
            return null;
        var polyline = ClipPolyline(river.Polyline, offsetX, offsetY, requested);
        if (polyline.Length < 2)
            polyline = cells.Select(cell => new MapPoint(cell.X + 0.5, cell.Y + 0.5)).ToArray();
        return river with
        {
            Cells = cells,
            Polyline = polyline,
            Source = Translate(river.Source, offsetX, offsetY),
            Mouth = Translate(river.Mouth, offsetX, offsetY),
            DrainageTerminal = Translate(river.DrainageTerminal, offsetX, offsetY)
        };
    }

    private static MapPoint[] ClipPolyline(IReadOnlyList<MapPoint> points, int offsetX, int offsetY, RequestedDomain requested)
    {
        if (points.Count == 0)
            return [];
        var minX = (double)offsetX;
        var maxX = offsetX + requested.Width;
        var minY = (double)offsetY;
        var maxY = offsetY + requested.Height;
        var result = new List<MapPoint>();
        for (var index = 1; index < points.Count; index++)
        {
            var a = points[index - 1];
            var b = points[index];
            if (!ClipSegment(a, b, minX, maxX, minY, maxY, out var first, out var last))
                continue;
            AddClippedPoint(result, first, offsetX, offsetY);
            AddClippedPoint(result, last, offsetX, offsetY);
        }
        return result.ToArray();
    }

    private static double Distance(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ClipSegment(MapPoint a, MapPoint b, double minX, double maxX, double minY, double maxY, out MapPoint first, out MapPoint last)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t0 = 0.0;
        var t1 = 1.0;
        foreach (var (p, q) in new[]
        {
            (-dx, a.X - minX), (dx, maxX - a.X),
            (-dy, a.Y - minY), (dy, maxY - a.Y)
        })
        {
            if (Math.Abs(p) <= 1e-12)
            {
                if (q < 0)
                {
                    first = default;
                    last = default;
                    return false;
                }
                continue;
            }
            var ratio = q / p;
            if (p < 0)
            {
                if (ratio > t1)
                {
                    first = default;
                    last = default;
                    return false;
                }
                if (ratio > t0)
                    t0 = ratio;
            }
            else
            {
                if (ratio < t0)
                {
                    first = default;
                    last = default;
                    return false;
                }
                if (ratio < t1)
                    t1 = ratio;
            }
        }
        first = new MapPoint(a.X + dx * t0, a.Y + dy * t0);
        last = new MapPoint(a.X + dx * t1, a.Y + dy * t1);
        return true;
    }

    private static void AddClippedPoint(ICollection<MapPoint> result, MapPoint point, int offsetX, int offsetY)
    {
        var translated = new MapPoint(point.X - offsetX, point.Y - offsetY);
        if (result.Count == 0 || Distance(result.Last(), translated) > 1e-10)
            result.Add(translated);
    }

    private static TectonicPlateMap? CropTectonicPlates(TectonicPlateMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Raster.Width, source.Raster.Height, context, requested);
        var raster = new TectonicPlateRaster(
            requested.Width,
            requested.Height,
            Crop(source.Raster.PlatesSpan, source.Raster.Width, arrays),
            Crop(source.Raster.CrustSpan, source.Raster.Width, arrays));
        var plates = source.Plates
            .Select(plate => CropPlateMetadata(plate, raster))
            .Where(plate => plate.PointCount > 0)
            .ToArray();
        return source with
        {
            Width = requested.Width,
            Height = requested.Height,
            Plates = plates,
            History = CropHistory(source.History, context, requested),
            CrustFields = CropCrustFields(source.CrustFields, context, requested),
            Raster = raster,
            PlateDomains = CropPlateDomains(source.PlateDomains, context, requested),
            BoundaryMap = CropBoundaryMap(source.BoundaryMap, context, requested),
            Features = CropFeatures(source.Features, context, requested),
            OrogenProvinces = CropOrogenProvinces(source.OrogenProvinces, context, requested),
            RiftProvinces = CropRiftProvinces(source.RiftProvinces, context, requested),
            Boundaries = source.Boundaries
                .Select(boundary => boundary with
                {
                    Points = boundary.Points.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray()
                })
                .Where(boundary => boundary.Points.Count > 0)
                .ToArray()
        };
    }

    private static TectonicPlate CropPlateMetadata(TectonicPlate source, TectonicPlateRaster raster)
    {
        var points = new List<GridPoint>();
        for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
            {
                if (raster.GetPlate(x, y).Value == source.Id.Value)
                    points.Add(new GridPoint(x, y));
            }

        return source with
        {
            PointCount = points.Count,
            Centroid = points.Count == 0
                ? new GridPoint(0, 0)
                : new GridPoint(
                    (int)Math.Round(points.Average(point => point.X)),
                    (int)Math.Round(points.Average(point => point.Y)))
        };
    }

    private static PlateDomainMap? CropPlateDomains(PlateDomainMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var domains = source.Domains
            .Select(domain =>
            {
                var points = Enumerable.Range(0, requested.Height)
                    .SelectMany(y => Enumerable.Range(0, requested.Width).Select(x => new GridPoint(arrays.OffsetX + x, arrays.OffsetY + y)))
                    .Count(point => source.GetPlate(point) == domain.Id);
                return domain with
                {
                    PointCount = points,
                    Centroid = points == 0 ? new GridPoint(0, 0) : new GridPoint(
                        (int)Math.Round(Enumerable.Range(0, requested.Height)
                            .SelectMany(y => Enumerable.Range(0, requested.Width).Select(x => new GridPoint(arrays.OffsetX + x, arrays.OffsetY + y)))
                            .Where(point => source.GetPlate(point) == domain.Id)
                            .Average(point => point.X - arrays.OffsetX)),
                        (int)Math.Round(Enumerable.Range(0, requested.Height)
                            .SelectMany(y => Enumerable.Range(0, requested.Width).Select(x => new GridPoint(arrays.OffsetX + x, arrays.OffsetY + y)))
                            .Where(point => source.GetPlate(point) == domain.Id)
                            .Average(point => point.Y - arrays.OffsetY)))
                };
            })
            .Where(domain => domain.PointCount > 0)
            .ToArray();
        return new PlateDomainMap(requested.Width, requested.Height, Crop(source.PlatesSpan, source.Width, arrays), domains);
    }

    private static TectonicHistory? CropHistory(TectonicHistory? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var lineaments = source.Lineaments.Select(lineament => lineament with
        {
            Points = lineament.Points.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray()
        }).Where(lineament => lineament.Points.Count > 0).ToArray();
        var events = source.Events
            .Where(item => IsInside(item.Center, arrays.OffsetX, arrays.OffsetY, requested))
            .Select(item => item with { Center = Translate(item.Center, arrays.OffsetX, arrays.OffsetY) })
            .ToArray();
        var cratons = source.CratonCenters.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray();
        var hotspots = source.Hotspots.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray();
        return new TectonicHistory(requested.Width, requested.Height, lineaments, events, cratons, hotspots);
    }

    private static CrustFieldMap? CropCrustFields(CrustFieldMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        return new CrustFieldMap(requested.Width, requested.Height,
            Crop(source.CrustSpan, source.Width, arrays),
            Crop(source.CoastalZoneSpan, source.Width, arrays),
            Crop(source.OceanicAgeSpan, source.Width, arrays),
            Crop(source.ContinentalAgeSpan, source.Width, arrays),
            Crop(source.LastRiftingAgeSpan, source.Width, arrays),
            Crop(source.LastOrogenyAgeSpan, source.Width, arrays),
            Crop(source.LastVolcanismAgeSpan, source.Width, arrays));
    }

    private static TectonicBoundaryMap? CropBoundaryMap(TectonicBoundaryMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var segments = source.Segments
            .Select(segment => segment with
            {
                Points = segment.Points.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray()
            })
            .Where(segment => segment.Points.Count > 0)
            .ToArray();
        return new TectonicBoundaryMap(requested.Width, requested.Height, segments);
    }

    private static TectonicFeatureMap? CropFeatures(TectonicFeatureMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var features = source.Features
            .Select(feature => feature with
            {
                Points = feature.Points.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray()
            })
            .Where(feature => feature.Points.Count > 0)
            .ToArray();
        var islands = source.Islands
            .Where(island => IsInside(island.Center, arrays.OffsetX, arrays.OffsetY, requested))
            .Select(island => island with { Center = Translate(island.Center, arrays.OffsetX, arrays.OffsetY) })
            .ToArray();
        return new TectonicFeatureMap(
            requested.Width,
            requested.Height,
            features,
            islands,
            Crop(source.UpliftSpan, source.Width, arrays),
            Crop(source.SubsidenceSpan, source.Width, arrays),
            Crop(source.VolcanismSpan, source.Width, arrays),
            Crop(source.SeismicitySpan, source.Width, arrays),
            Crop(source.HeatFlowSpan, source.Width, arrays),
            Crop(source.SedimentSupplySpan, source.Width, arrays));
    }

    private static OrogenProvinceMap? CropOrogenProvinces(OrogenProvinceMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var provinces = source.Provinces.Select(province => province with
        {
            AxisPoints = province.AxisPoints.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray()
        }).Where(province => province.AxisPoints.Count > 0).ToArray();
        return new OrogenProvinceMap(requested.Width, requested.Height, provinces,
            Crop(source.InfluenceSpan, source.Width, arrays),
            Crop(source.StrengthSpan, source.Width, arrays),
            Crop(source.AxisSpan, source.Width, arrays));
    }

    private static RiftProvinceMap? CropRiftProvinces(RiftProvinceMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var provinces = source.Provinces.Select(province => province with
        {
            AxisPoints = province.AxisPoints.Where(point => IsInside(point, arrays.OffsetX, arrays.OffsetY, requested)).Select(point => Translate(point, arrays.OffsetX, arrays.OffsetY)).ToArray(),
            Segments = province.Segments
                .Where(segment => IsInside(segment.Center, arrays.OffsetX, arrays.OffsetY, requested))
                .Select(segment => segment with { Center = Translate(segment.Center, arrays.OffsetX, arrays.OffsetY) })
                .ToArray()
        }).Where(province => province.AxisPoints.Count > 0).ToArray();
        return new RiftProvinceMap(requested.Width, requested.Height, provinces,
            Crop(source.RiftInfluenceSpan, source.Width, arrays),
            Crop(source.RiftAxisSpan, source.Width, arrays),
            Crop(source.GrabenMaskSpan, source.Width, arrays),
            Crop(source.ShoulderUpliftMaskSpan, source.Width, arrays),
            Crop(source.HeatFlowMaskSpan, source.Width, arrays),
            Crop(source.BreakupMaskSpan, source.Width, arrays));
    }

    private static WaterBodyTopology? CropWaterTopology(WaterBodyTopology? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var waterBodyIds = Crop(source.WaterBodyIdsSpan, source.Width, arrays);
        var waterBodyKinds = Crop(source.WaterBodyKindsSpan, source.Width, arrays);
        return new WaterBodyTopology(
            requested.Width,
            requested.Height,
            waterBodyIds,
            waterBodyKinds,
            CropWaterBodyMetadata(
                source.Bodies,
                waterBodyIds,
                requested.Width,
                requested.Height,
                CreateRequestedTopology(context, requested)));
    }

    private static IReadOnlyList<WaterBodyClassification> CropWaterBodyMetadata(
        IReadOnlyList<WaterBodyClassification> source,
        int[] waterBodyIds,
        int width,
        int height,
        IGridTopology topology)
    {
        var result = new List<WaterBodyClassification>(source.Count);
        foreach (var body in source)
        {
            var points = new List<GridPoint>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (waterBodyIds[y * width + x] == body.Id.Value)
                        points.Add(new GridPoint(x, y));
                }

            if (points.Count == 0)
                continue;

            var touchesEdge = points.Any(point => GridTopologyMath.IsOpenBoundary(topology, point));
            result.Add(body with
            {
                CellCount = points.Count,
                TouchesMapEdge = touchesEdge,
                AreaRatio = points.Count / (double)(width * height)
            });
        }

        return result;
    }

    private static IGridTopology CreateRequestedTopology(MapGenerationContext context, RequestedDomain requested)
    {
        var reference = RequestedSpatialReference(context, requested);
        if (reference.LegacyCompatibility is LegacyCompatibilityProfile.Flat or LegacyCompatibilityProfile.Regional)
            return new OpenRectangularTopology(requested.Width, requested.Height);

        return reference.Topology switch
        {
            GridTopologyKind.OpenRectangular => new OpenRectangularTopology(requested.Width, requested.Height),
            GridTopologyKind.CylindricalX => new CylindricalXTopology(requested.Width, requested.Height),
            _ => throw new InvalidOperationException($"Unknown requested grid topology: {reference.Topology}.")
        };
    }

    private static WaterSurfaceMap? CropWaterSurfaces(WaterSurfaceMap? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        var bodies = source.Bodies
            .Select(body => CropWaterSurfaceMetadata(body, context.WaterBodyTopology, arrays, requested.Width, requested.Height))
            .Where(body => body is not null)
            .Select(body => body!)
            .ToArray();
        return new WaterSurfaceMap(requested.Width, requested.Height, Crop(source.WaterSurfaceMetersSpan, source.Width, arrays), bodies);
    }

    private static WaterBodySurface? CropWaterSurfaceMetadata(
        WaterBodySurface source,
        WaterBodyTopology? topology,
        (int OffsetX, int OffsetY, int Width, int Height) arrays,
        int width,
        int height)
    {
        if (topology is null)
            return source;

        var points = new List<GridPoint>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sourcePoint = new GridPoint(arrays.OffsetX + x, arrays.OffsetY + y);
                if (topology.GetWaterBodyId(sourcePoint)?.Value == source.Id.Value)
                    points.Add(new GridPoint(x, y));
            }

        if (points.Count == 0)
            return null;

        return source with
        {
            CellCount = points.Count,
            ShorelineCellCount = Math.Min(source.ShorelineCellCount, points.Count),
            Centroid = new GridPoint(
                (int)Math.Round(points.Average(point => point.X)),
                (int)Math.Round(points.Average(point => point.Y)))
        };
    }

    private static RegionRaster? CropRegionRaster(RegionRaster? source, MapGenerationContext context, RequestedDomain requested)
    {
        if (source is null)
            return null;
        var arrays = CropOffsets(source.Width, source.Height, context, requested);
        return new RegionRaster(requested.Width, requested.Height, Crop(source.RegionIdsSpan, source.Width, arrays));
    }

    private static (int OffsetX, int OffsetY, int Width, int Height) CropOffsets(int sourceWidth, int sourceHeight, MapGenerationContext context, RequestedDomain requested)
    {
        if (sourceWidth != context.Mask.Width || sourceHeight != context.Mask.Height)
            throw new InvalidOperationException("Generated raster dimensions must match the working mask.");
        var offsets = context.WorkingDomain.OffsetOf(requested);
        return (offsets.X, offsets.Y, requested.Width, requested.Height);
    }

    private static T[] Crop<T>(ReadOnlySpan<T> source, int sourceWidth, (int OffsetX, int OffsetY, int Width, int Height) offsets)
    {
        var result = new T[offsets.Width * offsets.Height];
        for (var y = 0; y < offsets.Height; y++)
            source.Slice((offsets.OffsetY + y) * sourceWidth + offsets.OffsetX, offsets.Width).CopyTo(result.AsSpan(y * offsets.Width, offsets.Width));
        return result;
    }

    private static GridPoint Translate(GridPoint point, int offsetX, int offsetY) => new(point.X - offsetX, point.Y - offsetY);

    private static bool IsInside(GridPoint point, int offsetX, int offsetY, RequestedDomain requested) =>
        point.X >= offsetX && point.X < offsetX + requested.Width && point.Y >= offsetY && point.Y < offsetY + requested.Height;
}
