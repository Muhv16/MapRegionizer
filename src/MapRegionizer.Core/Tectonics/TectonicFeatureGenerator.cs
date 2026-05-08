using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

internal sealed class TectonicFeatureGenerator
{
    public TectonicFeatureMap Generate(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, PlateDomainMap plateDomains, TectonicBoundaryMap boundaries, OrogenProvinceMap orogenProvinces, RiftProvinceMap riftProvinces, IReadOnlyList<Landmass> landmasses)
    {
        var length = mask.Width * mask.Height;
        var uplift = new double[length];
        var subsidence = new double[length];
        var volcanism = new double[length];
        var seismicity = new double[length];
        var heatFlow = new double[length];
        var sedimentSupply = new double[length];
        var features = new List<TectonicFeature>();
        var nextId = 1;

        foreach (var lineament in history.Lineaments)
        {
            features.Add(new TectonicFeature(nextId++, lineament.Kind, lineament.Points, lineament.Age, lineament.Intensity));
            if (!IsExtensionalFeature(lineament.Kind))
            {
                if (lineament.Kind == TectonicFeatureKind.Hotspot)
                    StampHotspotPatches(mask, lineament.Points, lineament.Intensity, lineament.Id, uplift, volcanism, heatFlow);
                else
                    StampFeature(mask, lineament.Kind, lineament.Points, lineament.Intensity, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);
            }
        }

        foreach (var province in orogenProvinces.Provinces)
            features.Add(new TectonicFeature(nextId++, TectonicFeatureKind.Orogen, province.AxisPoints, province.Age, province.Activity, province.SourceBoundarySegmentId));

        foreach (var province in riftProvinces.Provinces)
        {
            var kind = province.Kind == RiftProvinceKind.BackArcExtension ? TectonicFeatureKind.BackArcBasin : TectonicFeatureKind.Rift;
            features.Add(new TectonicFeature(nextId++, kind, province.Segments.Select(s => s.Center).ToArray(), province.Age, province.Activity, province.SourceBoundarySegmentId));
        }

        foreach (var segment in boundaries.Segments)
        {
            var kind = ToFeatureKind(segment.BoundaryMode);
            features.Add(new TectonicFeature(nextId++, kind, segment.Points, 0, segment.Activity, segment.Id));
            if (!IsExtensionalMode(segment.BoundaryMode))
                StampSegment(mask, segment, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);
        }

        StampOrogenProvinces(mask, orogenProvinces, uplift, seismicity);
        StampRiftProvinces(mask, riftProvinces, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                if (crustFields.GetCoastalZone(x, y) is CoastalZoneKind.Shelf or CoastalZoneKind.Slope or CoastalZoneKind.PassiveMargin)
                {
                    subsidence[index] += 0.18;
                    sedimentSupply[index] += 0.45;
                }

                if (crustFields.GetCrust(x, y) == CrustKind.Rift)
                {
                    heatFlow[index] += 0.10;
                    subsidence[index] += 0.06;
                }
            }
        }

        var islands = ClassifyIslands(mask, landmasses, crustFields, plateDomains, volcanism);
        return new TectonicFeatureMap(mask.Width, mask.Height, features, islands, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);
    }

    private static void StampFeature(MapMask mask, TectonicFeatureKind kind, IReadOnlyList<GridPoint> points, double intensity, double[] uplift, double[] subsidence, double[] volcanism, double[] seismicity, double[] heatFlow, double[] sedimentSupply)
    {
        foreach (var point in points)
        {
            foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, 2))
            {
                var index = stamped.Y * mask.Width + stamped.X;
                switch (kind)
                {
                    case TectonicFeatureKind.Ridge:
                        heatFlow[index] += 0.5 * intensity;
                        volcanism[index] += 0.35 * intensity;
                        uplift[index] += 0.2 * intensity;
                        break;
                    case TectonicFeatureKind.Trench:
                        subsidence[index] += 0.65 * intensity;
                        seismicity[index] += 0.75 * intensity;
                        break;
                    case TectonicFeatureKind.Arc:
                        volcanism[index] += 0.75 * intensity;
                        heatFlow[index] += 0.35 * intensity;
                        uplift[index] += 0.25 * intensity;
                        break;
                    case TectonicFeatureKind.Hotspot:
                        break;
                    case TectonicFeatureKind.Rift:
                    case TectonicFeatureKind.BackArcBasin:
                        break;
                    case TectonicFeatureKind.Suture:
                    case TectonicFeatureKind.Orogen:
                        uplift[index] += 0.06 * intensity;
                        break;
                    case TectonicFeatureKind.Craton:
                        uplift[index] += 0.08 * intensity;
                        break;
                    case TectonicFeatureKind.PassiveMargin:
                    case TectonicFeatureKind.SedimentaryBasin:
                        subsidence[index] += 0.35 * intensity;
                        sedimentSupply[index] += 0.35 * intensity;
                        break;
                }
            }
        }
    }

    private static void StampHotspotPatches(MapMask mask, IReadOnlyList<GridPoint> points, double intensity, int seed, double[] uplift, double[] volcanism, double[] heatFlow)
    {
        if (points.Count == 0)
            return;

        var patchCount = Math.Clamp(points.Count / 32 + 1, 1, 3);
        for (var patch = 0; patch < patchCount; patch++)
        {
            var pointIndex = patchCount == 1
                ? 0
                : Math.Clamp((int)Math.Round(patch * (points.Count - 1) / (double)(patchCount - 1)), 0, points.Count - 1);
            var center = points[pointIndex];
            var ageDecay = Math.Clamp(1.0 - patch * 0.26, 0.42, 1.0);
            var localStrength = intensity * ageDecay * (0.82 + Hash01(center.X, center.Y, seed + 3401) * 0.36);
            var radius = Math.Clamp(3 + (int)Math.Round(Hash01(center.X, center.Y, seed + 3407) * 4), 3, Math.Max(4, mask.Width / 80));

            foreach (var stamped in PointsInRadius(mask.Width, mask.Height, center, radius))
            {
                var distance = Math.Sqrt(WrappedDistanceSquared(center, stamped, mask.Width));
                var falloff = SmoothStep(Math.Clamp(1.0 - distance / Math.Max(1.0, radius), 0, 1));
                var index = stamped.Y * mask.Width + stamped.X;
                volcanism[index] += falloff * 0.72 * localStrength;
                heatFlow[index] += falloff * 0.34 * localStrength;
                uplift[index] += falloff * 0.20 * localStrength;
            }
        }
    }

    private static void StampSegment(MapMask mask, PlateBoundarySegment segment, double[] uplift, double[] subsidence, double[] volcanism, double[] seismicity, double[] heatFlow, double[] sedimentSupply)
    {
        foreach (var point in segment.Points)
        {
            foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, 2))
            {
                var index = stamped.Y * mask.Width + stamped.X;
                var strength = segment.Activity;
                seismicity[index] += strength;
                switch (segment.BoundaryMode)
                {
                    case BoundaryMode.OceanOceanSubduction:
                    case BoundaryMode.OceanContinentSubduction:
                    case BoundaryMode.ObliqueSubduction:
                    case BoundaryMode.AccretionaryBoundary:
                        subsidence[index] += 0.55 * strength;
                        volcanism[index] += 0.45 * strength;
                        if (segment.BoundaryMode is BoundaryMode.ObliqueSubduction or BoundaryMode.AccretionaryBoundary)
                            uplift[index] += 0.2 * strength;
                        break;
                    case BoundaryMode.ContinentContinentCollision:
                    case BoundaryMode.Transpression:
                        uplift[index] += 0.18 * strength;
                        break;
                    case BoundaryMode.MidOceanRidge:
                        uplift[index] += 0.25 * strength;
                        heatFlow[index] += 0.65 * strength;
                        volcanism[index] += 0.35 * strength;
                        break;
                    case BoundaryMode.ContinentalRift:
                    case BoundaryMode.Transtension:
                    case BoundaryMode.BackArcSpreading:
                        seismicity[index] += 0.25 * strength;
                        break;
                    case BoundaryMode.PassiveMargin:
                        sedimentSupply[index] += 0.3 * strength;
                        break;
                    case BoundaryMode.PureTransform:
                    case BoundaryMode.DiffuseIntraplateBoundary:
                    case BoundaryMode.MixedSegmentBoundary:
                        uplift[index] += 0.08 * strength;
                        break;
                }
            }
        }
    }

    private static void StampOrogenProvinces(MapMask mask, OrogenProvinceMap provinces, double[] uplift, double[] seismicity)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                var influence = provinces.GetInfluence(x, y);
                var strength = provinces.GetStrength(x, y);
                if (influence <= 0 && strength <= 0)
                    continue;

                uplift[index] += strength * 0.68 + influence * 0.18;
                seismicity[index] += provinces.GetAxis(x, y) * 0.10;
            }
        }
    }

    private static void StampRiftProvinces(MapMask mask, RiftProvinceMap provinces, double[] uplift, double[] subsidence, double[] volcanism, double[] seismicity, double[] heatFlow, double[] sedimentSupply)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                var influence = provinces.GetRiftInfluence(x, y);
                var graben = provinces.GetGrabenMask(x, y);
                var shoulder = provinces.GetShoulderUpliftMask(x, y);
                var heat = provinces.GetHeatFlowMask(x, y);
                var breakup = provinces.GetBreakupMask(x, y);
                if (influence <= 0 && graben <= 0 && shoulder <= 0 && heat <= 0)
                    continue;

                subsidence[index] += graben * 0.72 + influence * 0.16;
                uplift[index] += shoulder * 0.34;
                heatFlow[index] += heat * 0.68;
                volcanism[index] += heat * 0.18;
                seismicity[index] += breakup * 0.08;
                sedimentSupply[index] += graben * (mask.IsLand(new GridPoint(x, y)) ? 0.12 : 0.05);
            }
        }
    }

    private static IReadOnlyList<TectonicIsland> ClassifyIslands(MapMask mask, IReadOnlyList<Landmass> landmasses, CrustFieldMap crustFields, PlateDomainMap plateDomains, double[] volcanism)
    {
        var maxArea = Math.Max(8, mask.Width * mask.Height * 0.01);
        var islands = new List<TectonicIsland>();
        foreach (var landmass in landmasses.Where(l => l.Shape.Area <= maxArea))
        {
            var centerCoordinate = landmass.Shape.Centroid.Coordinate;
            var center = new GridPoint(Math.Clamp((int)Math.Round(centerCoordinate.X), 0, mask.Width - 1), Math.Clamp((int)Math.Round(centerCoordinate.Y), 0, mask.Height - 1));
            var crust = crustFields.GetCrust(center);
            var index = center.Y * mask.Width + center.X;
            var kind = crust switch
            {
                CrustKind.Arc => IslandKind.VolcanicArc,
                CrustKind.Shelf => IslandKind.ShelfArchipelago,
                CrustKind.Terrane or CrustKind.Continental => IslandKind.Microcontinent,
                CrustKind.Oceanic when volcanism[index] > 0.4 => IslandKind.Hotspot,
                _ => IslandKind.UpliftedRidge
            };

            islands.Add(new TectonicIsland(center, kind, landmass.Shape.Area, plateDomains.GetPlate(center)));
        }

        return islands;
    }

    private static TectonicFeatureKind ToFeatureKind(BoundaryMode mode) => mode switch
    {
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary => TectonicFeatureKind.Trench,
        BoundaryMode.ContinentContinentCollision or
        BoundaryMode.Transpression => TectonicFeatureKind.Orogen,
        BoundaryMode.ContinentalRift or
        BoundaryMode.Transtension => TectonicFeatureKind.Rift,
        BoundaryMode.MidOceanRidge => TectonicFeatureKind.Ridge,
        BoundaryMode.BackArcSpreading => TectonicFeatureKind.BackArcBasin,
        BoundaryMode.PassiveMargin => TectonicFeatureKind.PassiveMargin,
        _ => TectonicFeatureKind.Suture
    };

    private static bool IsExtensionalFeature(TectonicFeatureKind kind) =>
        kind is TectonicFeatureKind.Rift or TectonicFeatureKind.BackArcBasin;

    private static bool IsExtensionalMode(BoundaryMode mode) =>
        mode is BoundaryMode.ContinentalRift or BoundaryMode.Transtension or BoundaryMode.BackArcSpreading;

    private static double SmoothStep(double value) => value * value * (3.0 - 2.0 * value);

    private static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            value = (value << 13) ^ value;
            return Math.Clamp((1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0 + 1.0) * 0.5, 0, 1);
        }
    }

    private static double WrappedDistanceSquared(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, width - dx);
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static IEnumerable<GridPoint> PointsInRadius(int width, int height, GridPoint center, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = center.Y + dy;
            if (y < 0 || y >= height)
                continue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                yield return new GridPoint(WrapX(center.X + dx, width), y);
            }
        }
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;
}
