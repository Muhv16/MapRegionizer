using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

internal sealed class TectonicHistoryGenerator
{
    private readonly Random _random;

    public TectonicHistoryGenerator(Random random)
    {
        _random = random;
    }

    public TectonicHistory Generate(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies, TectonicPlateGenerationOptions options)
    {
        var lineaments = new List<TectonicLineament>();
        var events = new List<TectonicEvent>();
        var cratonCenters = CreateCratonCenters(mask, landmasses);
        var hotspots = CreateHotspots(mask, options);
        var nextId = 1;

        foreach (var center in cratonCenters)
        {
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Craton, [center], 2500 + _random.NextDouble() * 1400, 0.6 + _random.NextDouble() * 0.35));
            events.Add(new TectonicEvent(nextId++, TectonicEventKind.CratonStabilization, center, 1800 + _random.NextDouble() * 1800, Math.Max(mask.Width, mask.Height) * 0.08, 0.6));
        }

        var ridgeCount = Math.Clamp(waterBodies.Count == 0 ? 1 : waterBodies.Count / 2 + 1, 1, 5);
        for (var i = 0; i < ridgeCount; i++)
        {
            var points = BuildCurvedMeridian(mask, _random.Next(mask.Width), 0.18 + _random.NextDouble() * 0.3, preferWater: true);
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Ridge, points, _random.NextDouble() * 35, 0.65 + _random.NextDouble() * 0.35));
            events.Add(new TectonicEvent(nextId++, TectonicEventKind.OceanOpening, points[points.Count / 2], _random.NextDouble() * 120, Math.Max(mask.Width, mask.Height) * 0.16, 0.75));
        }

        var coast = FindCoastPoints(mask);
        var activeMarginCount = Math.Clamp((int)Math.Round(2 + coast.Count / Math.Max(1.0, mask.Width * 0.08) * options.ActiveMarginRatio), 1, 8);
        for (var i = 0; i < activeMarginCount && coast.Count > 0; i++)
        {
            var start = coast[_random.Next(coast.Count)];
            var points = TraceCoast(mask, start, Math.Max(12, Math.Min(mask.Width, mask.Height) / 5));
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Trench, points, _random.NextDouble() * 180, 0.7 + _random.NextDouble() * 0.3));
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Arc, OffsetPoints(mask, points, 2 + _random.Next(4)), _random.NextDouble() * 90, 0.65 + _random.NextDouble() * 0.35));
            events.Add(new TectonicEvent(nextId++, TectonicEventKind.OceanClosing, start, _random.NextDouble() * 160, Math.Max(mask.Width, mask.Height) * 0.12, 0.75));
        }

        if (_random.NextDouble() < options.RiftChance || landmasses.Count > 1)
        {
            var riftCount = Math.Clamp(landmasses.Count / 2 + 1, 1, 5);
            for (var i = 0; i < riftCount; i++)
            {
                var points = BuildCurvedMeridian(mask, _random.Next(mask.Width), 0.08 + _random.NextDouble() * 0.18, preferWater: false);
                lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Rift, points, _random.NextDouble() * 140, 0.55 + _random.NextDouble() * 0.35));
                events.Add(new TectonicEvent(nextId++, TectonicEventKind.ContinentalRifting, points[points.Count / 2], _random.NextDouble() * 180, Math.Max(mask.Width, mask.Height) * 0.08, 0.65));
            }
        }

        foreach (var center in cratonCenters.Take(Math.Max(1, cratonCenters.Count / 2)))
        {
            var points = BuildCurvedLatitude(mask, center.Y, 0.08 + _random.NextDouble() * 0.18, preferLand: true);
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Suture, points, 250 + _random.NextDouble() * 1100, 0.45 + _random.NextDouble() * 0.35));
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Orogen, points, 80 + _random.NextDouble() * 320, 0.45 + _random.NextDouble() * 0.35));
            events.Add(new TectonicEvent(nextId++, TectonicEventKind.Orogeny, center, 80 + _random.NextDouble() * 450, Math.Max(mask.Width, mask.Height) * 0.1, 0.55));
        }

        foreach (var hotspot in hotspots)
        {
            lineaments.Add(new TectonicLineament(nextId++, TectonicFeatureKind.Hotspot, BuildHotspotTrack(mask, hotspot), _random.NextDouble() * 120, 0.5 + _random.NextDouble() * 0.45));
            events.Add(new TectonicEvent(nextId++, TectonicEventKind.Volcanism, hotspot, _random.NextDouble() * 80, Math.Max(mask.Width, mask.Height) * 0.04, 0.8));
        }

        return new TectonicHistory(mask.Width, mask.Height, lineaments, events, cratonCenters, hotspots);
    }

    private List<GridPoint> CreateCratonCenters(MapMask mask, IReadOnlyList<Landmass> landmasses)
    {
        var centers = new List<GridPoint>();
        foreach (var landmass in landmasses)
        {
            var centroid = landmass.Shape.Centroid.Coordinate;
            centers.Add(new GridPoint(ClampX((int)Math.Round(centroid.X), mask.Width), Math.Clamp((int)Math.Round(centroid.Y), 0, mask.Height - 1)));
        }

        if (centers.Count == 0 && mask.LandPoints.Count > 0)
            centers.Add(mask.LandPoints.ElementAt(_random.Next(mask.LandPoints.Count)));

        return centers;
    }

    private List<GridPoint> CreateHotspots(MapMask mask, TectonicPlateGenerationOptions options)
    {
        var count = options.HotspotCount ?? Math.Clamp((int)Math.Round(Math.Sqrt(mask.Width * mask.Height) / 36.0), 1, 8);
        var hotspots = new List<GridPoint>(count);
        for (var i = 0; i < count; i++)
            hotspots.Add(new GridPoint(_random.Next(mask.Width), _random.Next(mask.Height)));

        return hotspots;
    }

    private List<GridPoint> BuildCurvedMeridian(MapMask mask, int xStart, double wiggle, bool preferWater)
    {
        var points = new List<GridPoint>();
        var phase = _random.NextDouble() * Math.PI * 2;
        var amplitude = Math.Max(2, (int)(mask.Width * wiggle));

        for (var y = 0; y < mask.Height; y++)
        {
            var x = WrapX(xStart + (int)Math.Round(Math.Sin(y / (double)Math.Max(1, mask.Height) * Math.PI * 3 + phase) * amplitude), mask.Width);
            var point = new GridPoint(x, y);
            if (preferWater && mask.IsLand(point))
                point = NearestMatching(mask, point, wantsLand: false, Math.Max(2, mask.Width / 24));
            else if (!preferWater && !mask.IsLand(point))
                point = NearestMatching(mask, point, wantsLand: true, Math.Max(2, mask.Width / 24));

            points.Add(point);
        }

        return points.Distinct().ToList();
    }

    private List<GridPoint> BuildCurvedLatitude(MapMask mask, int yStart, double wiggle, bool preferLand)
    {
        var points = new List<GridPoint>();
        var phase = _random.NextDouble() * Math.PI * 2;
        var amplitude = Math.Max(2, (int)(mask.Height * wiggle));

        for (var x = 0; x < mask.Width; x++)
        {
            var y = Math.Clamp(yStart + (int)Math.Round(Math.Sin(x / (double)Math.Max(1, mask.Width) * Math.PI * 3 + phase) * amplitude), 0, mask.Height - 1);
            var point = new GridPoint(x, y);
            if (preferLand && !mask.IsLand(point))
                point = NearestMatching(mask, point, wantsLand: true, Math.Max(2, mask.Height / 18));

            points.Add(point);
        }

        return points.Distinct().ToList();
    }

    private List<GridPoint> BuildHotspotTrack(MapMask mask, GridPoint hotspot)
    {
        var points = new List<GridPoint>();
        var dx = _random.Next(-1, 2);
        var dy = _random.Next(-1, 2);
        if (dx == 0 && dy == 0)
            dx = 1;

        var length = Math.Max(6, Math.Min(mask.Width, mask.Height) / 7);
        for (var i = 0; i < length; i++)
            points.Add(new GridPoint(WrapX(hotspot.X - dx * i, mask.Width), Math.Clamp(hotspot.Y - dy * i, 0, mask.Height - 1)));

        return points;
    }

    private static List<GridPoint> FindCoastPoints(MapMask mask)
    {
        var result = new List<GridPoint>();
        foreach (var point in EnumeratePoints(mask.Width, mask.Height))
        {
            if (mask.IsLand(point))
                continue;

            if (Neighbors4(point, mask.Width, mask.Height).Any(mask.IsLand))
                result.Add(point);
        }

        return result;
    }

    private List<GridPoint> TraceCoast(MapMask mask, GridPoint start, int length)
    {
        var points = new List<GridPoint> { start };
        var current = start;
        for (var i = 0; i < length; i++)
        {
            var candidates = Neighbors8(current, mask.Width, mask.Height)
                .Where(p => !mask.IsLand(p) && Neighbors4(p, mask.Width, mask.Height).Any(mask.IsLand))
                .Where(p => !points.Contains(p))
                .ToArray();
            if (candidates.Length == 0)
                break;

            current = candidates[_random.Next(candidates.Length)];
            points.Add(current);
        }

        return points;
    }

    private List<GridPoint> OffsetPoints(MapMask mask, IReadOnlyList<GridPoint> points, int offset)
    {
        return points
            .Select(p => new GridPoint(WrapX(p.X + (_random.Next(2) == 0 ? offset : -offset), mask.Width), Math.Clamp(p.Y + _random.Next(-offset, offset + 1), 0, mask.Height - 1)))
            .Distinct()
            .ToList();
    }

    private static GridPoint NearestMatching(MapMask mask, GridPoint origin, bool wantsLand, int radius)
    {
        for (var r = 1; r <= radius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r)
                        continue;

                    var point = new GridPoint(WrapX(origin.X + dx, mask.Width), Math.Clamp(origin.Y + dy, 0, mask.Height - 1));
                    if (mask.IsLand(point) == wantsLand)
                        return point;
                }
            }
        }

        return origin;
    }

    private static int ClampX(int x, int width) => Math.Clamp(x, 0, width - 1);
    private static int WrapX(int x, int width) => (x % width + width) % width;
    private static IEnumerable<GridPoint> EnumeratePoints(int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                yield return new GridPoint(x, y);
        }
    }

    private static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height)
    {
        yield return new GridPoint(WrapX(point.X - 1, width), point.Y);
        yield return new GridPoint(WrapX(point.X + 1, width), point.Y);
        if (point.Y > 0) yield return new GridPoint(point.X, point.Y - 1);
        if (point.Y < height - 1) yield return new GridPoint(point.X, point.Y + 1);
    }

    private static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= height)
                continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                yield return new GridPoint(WrapX(point.X + dx, width), y);
            }
        }
    }
}
