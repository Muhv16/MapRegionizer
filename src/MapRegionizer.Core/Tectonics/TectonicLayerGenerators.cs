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

internal sealed class CrustFieldGenerator
{
    private readonly Random _random;

    public CrustFieldGenerator(Random random)
    {
        _random = random;
    }

    public CrustFieldMap Generate(MapMask mask, TectonicHistory history, TectonicPlateGenerationOptions options)
    {
        var length = mask.Width * mask.Height;
        var crust = new byte[length];
        var coastal = new byte[length];
        var oceanicAge = Fill(length, double.NaN);
        var continentalAge = Fill(length, double.NaN);
        var lastRifting = Fill(length, double.NaN);
        var lastOrogeny = Fill(length, double.NaN);
        var lastVolcanism = Fill(length, double.NaN);
        var distanceToLand = ComputeDistance(mask, sourceIsLand: true);
        var distanceToWater = ComputeDistance(mask, sourceIsLand: false);
        var ridgeDistance = DistanceToLineaments(mask, history, TectonicFeatureKind.Ridge);
        var trenchDistance = DistanceToLineaments(mask, history, TectonicFeatureKind.Trench);
        var arcDistance = DistanceToLineaments(mask, history, TectonicFeatureKind.Arc);
        var shelfWidth = Math.Max(2, (int)Math.Round(Math.Min(mask.Width, mask.Height) * 0.025 * options.ShelfWidthFactor));
        var innerShelfWidth = Math.Max(1, shelfWidth / 2);
        var activeMarginWidth = Math.Max(1, shelfWidth);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                var isLand = mask.IsLand(point);
                var continentalNoise = Hash01(x, y, 17);
                var shelfNoise = SmoothNoise(x, y, 71, 9.0) * 0.45 + SmoothNoise(x + 37, y - 23, 79, 27.0) * 0.55;
                var shallowNoise = SmoothNoise(x, y, 73, 13.0) * 0.42 + SmoothNoise(x - 61, y + 17, 83, 39.0) * 0.58;
                var localShelfWidth = Math.Max(1.0, shelfWidth * Math.Clamp(0.56 + shelfNoise * 0.92, 0.52, 1.50));
                var localInnerShelfWidth = Math.Max(1.0, innerShelfWidth * Math.Clamp(0.58 + shelfNoise * 0.74, 0.52, 1.32));
                var localShallowSeaWidth = Math.Max(localShelfWidth + 1.0, shelfWidth * Math.Clamp(1.02 + shallowNoise * 1.18, 1.0, 2.20));

                if (isLand)
                {
                    crust[index] = (byte)CrustKind.Continental;
                    if (distanceToWater[index] <= localShelfWidth)
                    {
                        var activeMargin = Math.Min(trenchDistance[index], arcDistance[index]) <= activeMarginWidth;
                        coastal[index] = activeMargin ? (byte)CoastalZoneKind.ActiveMargin : (byte)CoastalZoneKind.PassiveMargin;
                    }
                    else
                    {
                        coastal[index] = (byte)CoastalZoneKind.None;
                    }

                    continentalAge[index] = 350 + continentalNoise * 3200 * (0.35 + options.HistoryDepth * 0.65);
                }
                else if (Math.Min(trenchDistance[index], arcDistance[index]) <= activeMarginWidth && distanceToLand[index] <= localShallowSeaWidth)
                {
                    crust[index] = distanceToLand[index] <= localShelfWidth ? (byte)CrustKind.Shelf : (byte)CrustKind.Oceanic;
                    coastal[index] = (byte)CoastalZoneKind.ActiveMargin;
                    continentalAge[index] = distanceToLand[index] <= localShelfWidth ? 250 + continentalNoise * 2500 : double.NaN;
                    if (crust[index] == (byte)CrustKind.Oceanic)
                        oceanicAge[index] = Math.Clamp(ridgeDistance[index] * 4.0 + Hash01(x, y, 23) * 18.0, 0, 220);
                }
                else if (distanceToLand[index] <= localShelfWidth)
                {
                    crust[index] = (byte)CrustKind.Shelf;
                    coastal[index] = distanceToLand[index] <= localInnerShelfWidth ? (byte)CoastalZoneKind.Shelf : (byte)CoastalZoneKind.Slope;
                    continentalAge[index] = 250 + continentalNoise * 2500;
                }
                else
                {
                    crust[index] = (byte)CrustKind.Oceanic;
                    coastal[index] = distanceToLand[index] <= localShallowSeaWidth
                        ? (byte)CoastalZoneKind.ShallowSea
                        : (byte)CoastalZoneKind.None;
                    oceanicAge[index] = Math.Clamp(ridgeDistance[index] * 4.0 + Hash01(x, y, 23) * 18.0, 0, 220);
                }
            }
        }

        StampLineaments(mask, history, crust, coastal, oceanicAge, continentalAge, lastRifting, lastOrogeny, lastVolcanism, shelfWidth);
        return new CrustFieldMap(mask.Width, mask.Height, crust, coastal, oceanicAge, continentalAge, lastRifting, lastOrogeny, lastVolcanism);
    }

    private static void StampLineaments(
        MapMask mask,
        TectonicHistory history,
        byte[] crust,
        byte[] coastal,
        double[] oceanicAge,
        double[] continentalAge,
        double[] lastRifting,
        double[] lastOrogeny,
        double[] lastVolcanism,
        int shelfWidth)
    {
        foreach (var lineament in history.Lineaments)
        {
            var radius = lineament.Kind switch
            {
                TectonicFeatureKind.Ridge => Math.Max(1, shelfWidth / 2),
                TectonicFeatureKind.Trench => Math.Max(1, shelfWidth / 2),
                TectonicFeatureKind.Arc => Math.Clamp(shelfWidth / 2, 1, 4),
                TectonicFeatureKind.Rift => Math.Clamp(shelfWidth / 3, 1, 3),
                TectonicFeatureKind.Suture => 1,
                TectonicFeatureKind.Orogen => Math.Max(1, shelfWidth / 2),
                TectonicFeatureKind.Hotspot => Math.Max(1, shelfWidth / 2),
                _ => Math.Max(1, shelfWidth / 3)
            };

            foreach (var point in lineament.Points)
            {
                foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, radius))
                {
                    var index = stamped.Y * mask.Width + stamped.X;
                    switch (lineament.Kind)
                    {
                        case TectonicFeatureKind.Ridge:
                            if (!mask.IsLand(stamped))
                            {
                                crust[index] = (byte)CrustKind.Oceanic;
                                oceanicAge[index] = Math.Min(Coalesce(oceanicAge[index], 220), 2 + lineament.Age * 0.1);
                            }
                            break;
                        case TectonicFeatureKind.Trench:
                            coastal[index] = (byte)CoastalZoneKind.ActiveMargin;
                            break;
                        case TectonicFeatureKind.Arc:
                            crust[index] = (byte)CrustKind.Arc;
                            lastVolcanism[index] = Math.Min(Coalesce(lastVolcanism[index], 999), Math.Max(0, lineament.Age));
                            coastal[index] = (byte)CoastalZoneKind.ActiveMargin;
                            break;
                        case TectonicFeatureKind.Rift:
                            if (mask.IsLand(stamped) || crust[index] == (byte)CrustKind.Shelf)
                            {
                                if (Hash01(stamped.X, stamped.Y, lineament.Id) > 0.35)
                                    crust[index] = (byte)CrustKind.Rift;
                                lastRifting[index] = Math.Min(Coalesce(lastRifting[index], 999), Math.Max(0, lineament.Age));
                            }
                            break;
                        case TectonicFeatureKind.Suture:
                            if (mask.IsLand(stamped))
                            {
                                if (Hash01(stamped.X, stamped.Y, lineament.Id) > 0.55)
                                    crust[index] = (byte)CrustKind.Terrane;
                                lastOrogeny[index] = Math.Min(Coalesce(lastOrogeny[index], 999), lineament.Age);
                            }
                            break;
                        case TectonicFeatureKind.Orogen:
                            if (mask.IsLand(stamped))
                                lastOrogeny[index] = Math.Min(Coalesce(lastOrogeny[index], 999), lineament.Age);
                            break;
                        case TectonicFeatureKind.Hotspot:
                            lastVolcanism[index] = Math.Min(Coalesce(lastVolcanism[index], 999), Math.Max(0, lineament.Age));
                            break;
                    }
                }
            }
        }
    }

    private static double[] ComputeDistance(MapMask mask, bool sourceIsLand)
    {
        var distance = Enumerable.Repeat(double.PositiveInfinity, mask.Width * mask.Height).ToArray();
        var queue = new PriorityQueue<GridPoint, double>();

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                if (mask.IsLand(point) != sourceIsLand)
                    continue;

                distance[y * mask.Width + x] = 0;
                queue.Enqueue(point, 0);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distance[current.Y * mask.Width + current.X];
            foreach (var (neighbor, cost) in Neighbors8WithCost(current, mask.Width, mask.Height))
            {
                var index = neighbor.Y * mask.Width + neighbor.X;
                var nextDistance = currentDistance + cost;
                if (distance[index] <= nextDistance)
                    continue;

                distance[index] = nextDistance;
                queue.Enqueue(neighbor, nextDistance);
            }
        }

        return distance;
    }

    private static double[] DistanceToLineaments(MapMask mask, TectonicHistory history, TectonicFeatureKind kind)
    {
        var distance = Enumerable.Repeat(double.PositiveInfinity, mask.Width * mask.Height).ToArray();
        var queue = new PriorityQueue<GridPoint, double>();
        foreach (var point in history.Lineaments.Where(l => l.Kind == kind).SelectMany(l => l.Points).Distinct())
        {
            var index = point.Y * mask.Width + point.X;
            distance[index] = 0;
            queue.Enqueue(point, 0);
        }

        if (queue.Count == 0)
            return distance.Select(_ => Math.Max(mask.Width, mask.Height) / 2.0).ToArray();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distance[current.Y * mask.Width + current.X];
            foreach (var (neighbor, cost) in Neighbors8WithCost(current, mask.Width, mask.Height))
            {
                var index = neighbor.Y * mask.Width + neighbor.X;
                var nextDistance = currentDistance + cost;
                if (distance[index] <= nextDistance)
                    continue;

                distance[index] = nextDistance;
                queue.Enqueue(neighbor, nextDistance);
            }
        }

        return distance;
    }

    private static double[] Fill(int length, double value)
    {
        var result = new double[length];
        Array.Fill(result, value);
        return result;
    }

    private static double Coalesce(double value, double fallback) => double.IsNaN(value) ? fallback : value;
    private static double Hash01(int x, int y, int seed)
    {
        var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
        value = (value << 13) ^ value;
        return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
    }

    private static double SmoothNoise(int x, int y, int seed, double scale)
    {
        var sampleX = x / Math.Max(1.0, scale);
        var sampleY = y / Math.Max(1.0, scale);
        var x0 = (int)Math.Floor(sampleX);
        var y0 = (int)Math.Floor(sampleY);
        var tx = SmoothStep(sampleX - x0);
        var ty = SmoothStep(sampleY - y0);
        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);
        return Math.Clamp((Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty) + 1.0) * 0.5, 0, 1);
    }

    private static double SmoothStep(double value) => value * value * (3.0 - 2.0 * value);

    private static double Lerp(double a, double b, double amount) => a + (b - a) * amount;

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

    private static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height)
    {
        yield return new GridPoint(WrapX(point.X - 1, width), point.Y);
        yield return new GridPoint(WrapX(point.X + 1, width), point.Y);
        if (point.Y > 0) yield return new GridPoint(point.X, point.Y - 1);
        if (point.Y < height - 1) yield return new GridPoint(point.X, point.Y + 1);
    }

    private static IEnumerable<(GridPoint Point, double Cost)> Neighbors8WithCost(GridPoint point, int width, int height)
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

                var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                yield return (new GridPoint(WrapX(point.X + dx, width), y), cost);
            }
        }
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;
}

internal sealed class PlateDomainGenerator
{
    private readonly Random _random;

    public PlateDomainGenerator(Random random)
    {
        _random = random;
    }

    public PlateDomainMap Generate(MapMask mask, CrustFieldMap crustFields, TectonicHistory history, TectonicPlateGenerationOptions options)
    {
        var seeds = CreateSeeds(mask, crustFields, history, options);
        var plateArray = AssignByConstrainedFloodFill(mask, crustFields, history, seeds.Major, options);
        ApplyMicroplates(mask, plateArray, seeds.Micro, options);

        if (options.ValidateGeometry)
            ValidateAndFixPlateDomains(mask, plateArray, seeds.All, options);

        var domains = BuildDomains(mask, crustFields, plateArray, seeds.All, options);
        return new PlateDomainMap(mask.Width, mask.Height, plateArray, domains);
    }

    private SeedSet CreateSeeds(MapMask mask, CrustFieldMap crustFields, TectonicHistory history, TectonicPlateGenerationOptions options)
    {
        var plateCount = options.PlateCount ?? EstimatePlateCount(mask, options.EarthLikeFactor);
        var microplateCount = Math.Clamp((int)Math.Round(plateCount * options.MicroplateRatio), 0, Math.Max(0, plateCount / 2));
        plateCount = Math.Clamp(plateCount, 1, Math.Max(1, mask.Width * mask.Height));
        var seeds = new List<DomainSeed>(plateCount);

        var attempts = 0;
        var maxAttempts = plateCount * 300;
        var minDistanceSquared = Math.Pow(Math.Max(2, Math.Min(mask.Width, mask.Height) / Math.Max(4, plateCount)), 2);

        while (seeds.Count < plateCount && attempts++ < maxAttempts)
        {
            var point = new GridPoint(_random.Next(mask.Width), _random.Next(mask.Height));
            var crust = crustFields.GetCrust(point);

            if (seeds.Any(s => WrappedDistanceSquared(s.Point, point, mask.Width) < minDistanceSquared))
                continue;

            seeds.Add(CreateSeed(seeds.Count + 1, point, crust, isMicroplate: false));
        }

        while (seeds.Count < plateCount)
        {
            var point = new GridPoint(_random.Next(mask.Width), _random.Next(mask.Height));
            seeds.Add(CreateSeed(seeds.Count + 1, point, crustFields.GetCrust(point), isMicroplate: false));
        }

        var microSeeds = CreateMicroplateSeeds(mask, crustFields, history, seeds.Count + 1, microplateCount);
        return new SeedSet(seeds, microSeeds);
    }

    private List<DomainSeed> CreateMicroplateSeeds(MapMask mask, CrustFieldMap crustFields, TectonicHistory history, int firstId, int count)
    {
        var candidates = CreateMicroplateCandidates(mask, crustFields, history)
            .OrderByDescending(c => c.Priority)
            .ToArray();

        if (candidates.Length == 0)
            return [];

        var microSeeds = new List<DomainSeed>(count);
        var usedSystems = new HashSet<string>();
        var minDistance = Math.Max(16, Math.Min(mask.Width, mask.Height) * 0.12);
        var minDistanceSquared = minDistance * minDistance;

        foreach (var candidate in candidates)
        {
            if (microSeeds.Count >= count)
                break;

            if (!usedSystems.Add(candidate.SystemKey))
                continue;

            if (microSeeds.Any(s => WrappedDistanceSquared(s.Point, candidate.Center, mask.Width) < minDistanceSquared))
                continue;

            var crust = crustFields.GetCrust(candidate.Center);
            if (crust is not (CrustKind.Oceanic or CrustKind.Arc or CrustKind.Shelf or CrustKind.Terrane))
                continue;

            microSeeds.Add(CreateSeed(
                firstId + microSeeds.Count,
                candidate.Center,
                crust,
                isMicroplate: true,
                candidate.Axis,
                candidate.Kind,
                candidate.SystemKey,
                candidate.Priority));
        }

        return microSeeds;
    }

    private IReadOnlyList<MicroplateCandidate> CreateMicroplateCandidates(MapMask mask, CrustFieldMap crustFields, TectonicHistory history)
    {
        var candidates = new List<MicroplateCandidate>();
        var coarseCell = Math.Max(1, (int)Math.Round(Math.Min(mask.Width, mask.Height) * 0.18));

        foreach (var lineament in history.Lineaments.Where(l => l.Kind is TectonicFeatureKind.Arc or TectonicFeatureKind.Trench or TectonicFeatureKind.Rift or TectonicFeatureKind.Suture))
        {
            if (lineament.Points.Count < 4)
                continue;

            var centerIndex = lineament.Points.Count / 2;
            var center = lineament.Points[centerIndex];
            var crust = crustFields.GetCrust(center);
            var coastal = crustFields.GetCoastalZone(center);
            var tectonicContext =
                lineament.Kind is TectonicFeatureKind.Arc or TectonicFeatureKind.Trench ||
                crust is CrustKind.Arc or CrustKind.Shelf or CrustKind.Terrane ||
                coastal is CoastalZoneKind.ActiveMargin or CoastalZoneKind.Shelf or CoastalZoneKind.Slope;

            if (!tectonicContext)
                continue;

            var axis = EstimateLineamentAxis(lineament.Points, centerIndex, mask.Width);
            var priority = lineament.Kind switch
            {
                TectonicFeatureKind.Arc => 1.0,
                TectonicFeatureKind.Trench => 0.95,
                TectonicFeatureKind.Rift => 0.55,
                TectonicFeatureKind.Suture => 0.45,
                _ => 0.3
            };
            priority += crust is CrustKind.Arc or CrustKind.Shelf ? 0.25 : 0.0;
            priority += coastal == CoastalZoneKind.ActiveMargin ? 0.25 : 0.0;
            priority += Hash01(center.X, center.Y, lineament.Id) * 0.1;

            var systemKey = $"{center.X / coarseCell}:{center.Y / coarseCell}:{lineament.Kind}";
            candidates.Add(new MicroplateCandidate(center, axis, lineament.Kind, lineament.Id, systemKey, priority));
        }

        foreach (var hotspot in history.Hotspots)
        {
            var crust = crustFields.GetCrust(hotspot);
            if (crust != CrustKind.Oceanic || crustFields.GetCoastalZone(hotspot) != CoastalZoneKind.None)
                continue;

            var axis = new GridVector(1, Hash01(hotspot.X, hotspot.Y, 811) - 0.5);
            var systemKey = $"{hotspot.X / coarseCell}:{hotspot.Y / coarseCell}:Hotspot";
            candidates.Add(new MicroplateCandidate(hotspot, Normalize(axis), TectonicFeatureKind.Hotspot, null, systemKey, 0.25 + Hash01(hotspot.X, hotspot.Y, 812) * 0.15));
        }

        return candidates;
    }

    private DomainSeed CreateSeed(
        int id,
        GridPoint point,
        CrustKind preferredCrust,
        bool isMicroplate,
        GridVector? axis = null,
        TectonicFeatureKind? sourceKind = null,
        string? systemKey = null,
        double priority = 0)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var baseSpeed = preferredCrust is CrustKind.Oceanic or CrustKind.Arc ? 1.15 : 0.85;
        if (isMicroplate)
            baseSpeed *= 1.25;

        return new DomainSeed(
            new TectonicPlateId(id),
            point,
            preferredCrust,
            new GridVector(Math.Cos(angle) * baseSpeed * (0.45 + _random.NextDouble() * 0.8), Math.Sin(angle) * baseSpeed * (0.45 + _random.NextDouble() * 0.8)),
            0.75 + _random.NextDouble() * 0.5,
            isMicroplate,
            Normalize(axis ?? new GridVector(Math.Cos(angle), Math.Sin(angle))),
            sourceKind,
            systemKey,
            priority);
    }

    private short[] AssignByConstrainedFloodFill(MapMask mask, CrustFieldMap crustFields, TectonicHistory history, IReadOnlyList<DomainSeed> seeds, TectonicPlateGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var length = width * height;
        var plates = new short[length];
        var costs = Enumerable.Repeat(double.PositiveInfinity, length).ToArray();
        var barrier = BuildBoundaryInfluence(mask, history);
        var queue = new PriorityQueue<(GridPoint Point, DomainSeed Seed), double>();

        foreach (var seed in seeds)
        {
            var index = seed.Point.Y * width + seed.Point.X;
            costs[index] = 0;
            plates[index] = (short)seed.Id.Value;
            queue.Enqueue((seed.Point, seed), 0);
        }

        while (queue.Count > 0)
        {
            var (current, seed) = queue.Dequeue();
            var currentIndex = current.Y * width + current.X;
            foreach (var neighbor in Neighbors4(current, width, height))
            {
                var index = neighbor.Y * width + neighbor.X;
                var crustPenalty = seed.PreferredCrust == crustFields.GetCrust(neighbor) ? 0.0 : 1.6 + options.LandWaterTransitionPenalty * 6.0;
                var barrierPenalty = Math.Max(barrier[currentIndex], barrier[index]) * (2.0 + options.EarthLikeFactor * 3.0);
                var nextCost = costs[currentIndex] + 1.0 + crustPenalty + barrierPenalty + Hash01(neighbor.X, neighbor.Y, seed.Id.Value) * options.BoundaryNoise;
                if (nextCost >= costs[index])
                    continue;

                costs[index] = nextCost;
                plates[index] = (short)seed.Id.Value;
                queue.Enqueue((neighbor, seed), nextCost);
            }
        }

        SmoothPlateMap(mask, plates, options.EarthLikeFactor > 0.7 ? 2 : 1);
        return plates;
    }

    private void ApplyMicroplates(MapMask mask, short[] plateArray, IReadOnlyList<DomainSeed> microSeeds, TectonicPlateGenerationOptions options)
    {
        if (microSeeds.Count == 0 || options.MaxMicroplateAreaRatio <= 0)
            return;

        var mapArea = mask.Width * mask.Height;
        var minArea = Math.Max(12, (int)Math.Round(mapArea * options.MinMicroplateAreaRatio));
        var maxArea = Math.Max(minArea, (int)Math.Round(mapArea * options.MaxMicroplateAreaRatio));
        var maxRadius = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(maxArea / Math.PI) * 2.4));

        foreach (var seed in microSeeds)
        {
            var areaScale = seed.SourceKind switch
            {
                TectonicFeatureKind.Arc or TectonicFeatureKind.Trench => 0.55 + _random.NextDouble() * 0.35,
                TectonicFeatureKind.Hotspot => 0.25 + _random.NextDouble() * 0.2,
                _ => 0.35 + _random.NextDouble() * 0.25
            };
            var targetArea = Math.Clamp((int)Math.Round(maxArea * areaScale), minArea, maxArea);
            var candidates = BuildMicroplateMask(mask, plateArray, seed, targetArea, maxRadius);

            if (candidates.Length < minArea)
                continue;

            foreach (var point in candidates)
                plateArray[point.Y * mask.Width + point.X] = (short)seed.Id.Value;
        }
    }

    private GridPoint[] BuildMicroplateMask(MapMask mask, short[] plateArray, DomainSeed seed, int targetArea, int maxRadius)
    {
        return seed.PreferredCrust is CrustKind.Continental or CrustKind.Shelf or CrustKind.Terrane or CrustKind.Arc
            ? BuildContextualMicroplate(mask, plateArray, seed, targetArea, maxRadius)
            : BuildAnisotropicMicroplate(mask, seed, targetArea, maxRadius);
    }

    private GridPoint[] BuildContextualMicroplate(MapMask mask, short[] plateArray, DomainSeed seed, int targetArea, int maxRadius)
    {
        var selected = new List<GridPoint>(targetArea);
        var selectedSet = new HashSet<GridPoint>();
        var queued = new HashSet<GridPoint>();
        var queue = new PriorityQueue<GridPoint, double>();
        var axis = Normalize(seed.Axis);
        var normal = new GridVector(-axis.Y, axis.X);
        var aspect = seed.SourceKind is TectonicFeatureKind.Arc or TectonicFeatureKind.Trench ? 2.2 : 1.7;
        var minorAxis = Math.Sqrt(targetArea / (Math.PI * aspect));
        var majorAxis = minorAxis * aspect;
        var sourcePlate = plateArray[seed.Point.Y * mask.Width + seed.Point.X];

        queue.Enqueue(seed.Point, 0);
        queued.Add(seed.Point);

        while (queue.Count > 0 && selected.Count < targetArea)
        {
            var current = queue.Dequeue();
            if (selectedSet.Contains(current))
                continue;

            var score = MicroplateScore(mask, current, seed, axis, normal, majorAxis, minorAxis)
                + ContextPenalty(mask, current, seed)
                + ExistingPlatePenalty(mask, plateArray, current, sourcePlate);

            if (score > 1.65 && selected.Count >= targetArea * 0.25)
                continue;

            selectedSet.Add(current);
            selected.Add(current);

            foreach (var neighbor in Neighbors4(current, mask.Width, mask.Height))
            {
                if (queued.Contains(neighbor))
                    continue;

                if (WrappedDistanceSquared(seed.Point, neighbor, mask.Width) > maxRadius * maxRadius)
                    continue;

                queued.Add(neighbor);
                var priority = MicroplateScore(mask, neighbor, seed, axis, normal, majorAxis, minorAxis)
                    + ContextPenalty(mask, neighbor, seed)
                    + ExistingPlatePenalty(mask, plateArray, neighbor, sourcePlate);
                queue.Enqueue(neighbor, priority);
            }
        }

        return CloseSmallGaps(mask, selectedSet, seed, maxRadius).ToArray();
    }

    private GridPoint[] BuildAnisotropicMicroplate(MapMask mask, DomainSeed seed, int targetArea, int maxRadius)
    {
        var selected = new List<GridPoint>(targetArea);
        var selectedSet = new HashSet<GridPoint>();
        var queued = new HashSet<GridPoint>();
        var queue = new PriorityQueue<GridPoint, double>();
        var aspect = 1.8 + Hash01(seed.Point.X, seed.Point.Y, seed.Id.Value) * 2.2;
        if (seed.SourceKind == TectonicFeatureKind.Hotspot)
            aspect = 1.5 + Hash01(seed.Point.X, seed.Point.Y, seed.Id.Value + 9) * 1.2;

        var minorAxis = Math.Sqrt(targetArea / (Math.PI * aspect));
        var majorAxis = minorAxis * aspect;
        var axis = Normalize(seed.Axis);
        var normal = new GridVector(-axis.Y, axis.X);

        queue.Enqueue(seed.Point, 0);
        queued.Add(seed.Point);

        while (queue.Count > 0 && selected.Count < targetArea)
        {
            var current = queue.Dequeue();
            if (selectedSet.Contains(current))
                continue;

            var score = MicroplateScore(mask, current, seed, axis, normal, majorAxis, minorAxis);
            if (score > 1.25 && selected.Count >= targetArea * 0.35)
                continue;

            selectedSet.Add(current);
            selected.Add(current);

            foreach (var neighbor in Neighbors4(current, mask.Width, mask.Height))
            {
                if (queued.Contains(neighbor))
                    continue;

                if (WrappedDistanceSquared(seed.Point, neighbor, mask.Width) > maxRadius * maxRadius)
                    continue;

                queued.Add(neighbor);
                queue.Enqueue(neighbor, MicroplateScore(mask, neighbor, seed, axis, normal, majorAxis, minorAxis));
            }
        }

        return selected.ToArray();
    }

    private static double ContextPenalty(MapMask mask, GridPoint point, DomainSeed seed)
    {
        var isLand = mask.IsLand(point);
        return seed.PreferredCrust switch
        {
            CrustKind.Continental or CrustKind.Terrane => isLand ? -0.2 : 0.75,
            CrustKind.Shelf or CrustKind.Arc => isLand ? -0.05 : 0.15,
            CrustKind.Oceanic => isLand ? 0.55 : 0,
            _ => 0
        };
    }

    private static double ExistingPlatePenalty(MapMask mask, short[] plateArray, GridPoint point, short sourcePlate)
    {
        var currentPlate = plateArray[point.Y * mask.Width + point.X];
        return currentPlate == sourcePlate ? -0.12 : 0.22;
    }

    private static IEnumerable<GridPoint> CloseSmallGaps(MapMask mask, HashSet<GridPoint> selected, DomainSeed seed, int maxRadius)
    {
        var result = selected.ToHashSet();
        foreach (var point in selected.ToArray())
        {
            foreach (var neighbor in Neighbors8(point, mask.Width, mask.Height))
            {
                if (result.Contains(neighbor))
                    continue;

                if (WrappedDistanceSquared(seed.Point, neighbor, mask.Width) > maxRadius * maxRadius)
                    continue;

                var selectedNeighbors = Neighbors8(neighbor, mask.Width, mask.Height).Count(result.Contains);
                if (selectedNeighbors >= 5)
                    result.Add(neighbor);
            }
        }

        return result;
    }

    private static double MicroplateScore(MapMask mask, GridPoint point, DomainSeed seed, GridVector axis, GridVector normal, double majorAxis, double minorAxis)
    {
        var dx = WrappedDeltaX(point.X - seed.Point.X, mask.Width);
        var dy = point.Y - seed.Point.Y;
        var along = Math.Abs(dx * axis.X + dy * axis.Y) / Math.Max(1.0, majorAxis);
        var across = Math.Abs(dx * normal.X + dy * normal.Y) / Math.Max(1.0, minorAxis);
        var warp = (Hash01(point.X / 8, point.Y / 8, seed.Id.Value) - 0.5) * 0.22;
        var asymmetry = Math.Max(0, dx * axis.X + dy * axis.Y) / Math.Max(1.0, majorAxis) * 0.08;

        return along * along + across * across + warp + asymmetry;
    }

    private static double[] BuildBoundaryInfluence(MapMask mask, TectonicHistory history)
    {
        var barrier = new double[mask.Width * mask.Height];
        foreach (var point in history.Lineaments
            .Where(l => l.Kind is TectonicFeatureKind.Ridge or TectonicFeatureKind.Trench or TectonicFeatureKind.Rift or TectonicFeatureKind.Suture)
            .SelectMany(l => l.Points))
        {
            foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, 2))
            {
                var distance = Math.Sqrt(WrappedDistanceSquared(point, stamped, mask.Width));
                var influence = Math.Max(0.0, 1.0 - distance / 3.0);
                var index = stamped.Y * mask.Width + stamped.X;
                barrier[index] = Math.Max(barrier[index], influence);
            }
        }

        return barrier;
    }

    private static void ValidateAndFixPlateDomains(MapMask mask, short[] plateArray, IReadOnlyList<DomainSeed> seeds, TectonicPlateGenerationOptions options)
    {
        var microplateIds = seeds.Where(s => s.IsMicroplate).Select(s => (short)s.Id.Value).ToHashSet();
        var microplateSeeds = seeds.Where(s => s.IsMicroplate).ToDictionary(s => (short)s.Id.Value);
        var minPlateSize = Math.Max(options.MinPlateSize, (int)Math.Ceiling(mask.Width * mask.Height * options.MinPlateSizeRatio));
        var minMicroplateSize = Math.Max(12, (int)Math.Round(mask.Width * mask.Height * options.MinMicroplateAreaRatio));
        var maxMicroplateSize = Math.Max(minMicroplateSize, (int)Math.Round(mask.Width * mask.Height * options.MaxMicroplateAreaRatio));
        ValidateMicroplateTopology(mask, plateArray, microplateSeeds, options);

        for (var cycle = 0; cycle < Math.Max(1, options.MaxValidationCycles); cycle++)
        {
            var changed = false;
            foreach (var plateId in plateArray.Distinct().ToArray())
            {
                var fragments = FindFragments(mask, plateArray, plateId);
                if (fragments.Count == 0)
                    continue;

                var totalSize = fragments.Sum(f => f.Count);
                var minimumSize = microplateIds.Contains(plateId) ? minMicroplateSize : minPlateSize;
                var metrics = MeasurePlate(mask, plateArray, plateId);
                if (!microplateIds.Contains(plateId) && totalSize < maxMicroplateSize && metrics.NeighborCounts.Count <= 3)
                {
                    foreach (var fragment in fragments)
                        changed |= ReassignPixels(mask, plateArray, fragment, plateId);
                    continue;
                }

                if (totalSize < minimumSize)
                {
                    foreach (var fragment in fragments)
                        changed |= ReassignPixels(mask, plateArray, fragment, plateId);
                    continue;
                }

                if (fragments.Count <= 1)
                    continue;

                var largest = fragments.OrderByDescending(f => f.Count).First();
                foreach (var fragment in fragments)
                {
                    if (ReferenceEquals(fragment, largest))
                        continue;

                    changed |= ReassignPixels(mask, plateArray, fragment, plateId);
                }
            }

            if (!changed)
                break;
        }
    }

    private static void ValidateMicroplateTopology(MapMask mask, short[] plateArray, IReadOnlyDictionary<short, DomainSeed> microplateSeeds, TectonicPlateGenerationOptions options)
    {
        if (microplateSeeds.Count == 0)
            return;

        var clusterDistance = Math.Max(16, Math.Min(mask.Width, mask.Height) * 0.12);
        var clusterDistanceSquared = clusterDistance * clusterDistance;
        var removed = new HashSet<short>();
        var orderedSeeds = microplateSeeds.Values.OrderByDescending(s => s.Priority).ToArray();

        for (var i = 0; i < orderedSeeds.Length; i++)
        {
            var keep = orderedSeeds[i];
            if (removed.Contains((short)keep.Id.Value))
                continue;

            for (var j = i + 1; j < orderedSeeds.Length; j++)
            {
                var candidate = orderedSeeds[j];
                var candidateId = (short)candidate.Id.Value;
                if (removed.Contains(candidateId))
                    continue;

                if (WrappedDistanceSquared(keep.Point, candidate.Point, mask.Width) < clusterDistanceSquared)
                {
                    MergePlateIntoBestNeighbor(mask, plateArray, candidateId);
                    removed.Add(candidateId);
                }
            }
        }

        foreach (var (plateId, seed) in microplateSeeds)
        {
            if (removed.Contains(plateId))
                continue;

            var metrics = MeasurePlate(mask, plateArray, plateId);
            if (metrics.Area == 0)
                continue;

            var singleNeighbor = metrics.NeighborCounts.Count <= 1;
            var tooCircular = metrics.Compactness < 2.05 && metrics.AspectRatio < 1.6;
            var tooShredded = metrics.Compactness > 35.0 || metrics.Perimeter / (double)Math.Max(1, metrics.Area) > 0.32;
            var hotspotException = seed.SourceKind == TectonicFeatureKind.Hotspot && !singleNeighbor;

            if ((singleNeighbor && !hotspotException) || tooCircular || tooShredded)
                MergePlateIntoBestNeighbor(mask, plateArray, plateId);
        }
    }

    private static PlateMetrics MeasurePlate(MapMask mask, short[] plateArray, short plateId)
    {
        var area = 0;
        var perimeter = 0;
        var minX = mask.Width;
        var maxX = 0;
        var minY = mask.Height;
        var maxY = 0;
        var neighborCounts = new Dictionary<short, int>();

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                if (plateArray[index] != plateId)
                    continue;

                area++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                foreach (var neighbor in Neighbors4(new GridPoint(x, y), mask.Width, mask.Height))
                {
                    var neighborPlate = plateArray[neighbor.Y * mask.Width + neighbor.X];
                    if (neighborPlate == plateId)
                        continue;

                    perimeter++;
                    neighborCounts[neighborPlate] = neighborCounts.GetValueOrDefault(neighborPlate) + 1;
                }
            }
        }

        if (area == 0)
            return new PlateMetrics(0, 0, 0, 0, neighborCounts);

        var width = Math.Max(1, maxX - minX + 1);
        var height = Math.Max(1, maxY - minY + 1);
        var aspectRatio = Math.Max(width, height) / (double)Math.Min(width, height);
        var compactness = perimeter * perimeter / (4.0 * Math.PI * area);
        return new PlateMetrics(area, perimeter, aspectRatio, compactness, neighborCounts);
    }

    private static bool MergePlateIntoBestNeighbor(MapMask mask, short[] plateArray, short plateId)
    {
        var pixels = new List<int>();
        for (var index = 0; index < plateArray.Length; index++)
        {
            if (plateArray[index] == plateId)
                pixels.Add(index);
        }

        return pixels.Count > 0 && ReassignPixels(mask, plateArray, pixels, plateId);
    }

    private static List<List<int>> FindFragments(MapMask mask, short[] plateArray, short plateId)
    {
        var visited = new bool[plateArray.Length];
        var fragments = new List<List<int>>();
        var queue = new Queue<int>();

        for (var index = 0; index < plateArray.Length; index++)
        {
            if (visited[index] || plateArray[index] != plateId)
                continue;

            var fragment = new List<int>();
            visited[index] = true;
            queue.Enqueue(index);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                fragment.Add(current);
                var point = new GridPoint(current % mask.Width, current / mask.Width);
                foreach (var neighbor in Neighbors4(point, mask.Width, mask.Height))
                {
                    var neighborIndex = neighbor.Y * mask.Width + neighbor.X;
                    if (visited[neighborIndex] || plateArray[neighborIndex] != plateId)
                        continue;

                    visited[neighborIndex] = true;
                    queue.Enqueue(neighborIndex);
                }
            }

            fragments.Add(fragment);
        }

        return fragments;
    }

    private static bool ReassignPixels(MapMask mask, short[] plateArray, IReadOnlyList<int> pixels, short excludedPlateId)
    {
        var counts = new Dictionary<short, int>();
        foreach (var pixel in pixels)
        {
            var point = new GridPoint(pixel % mask.Width, pixel / mask.Width);
            foreach (var neighbor in Neighbors8(point, mask.Width, mask.Height))
            {
                var neighborPlate = plateArray[neighbor.Y * mask.Width + neighbor.X];
                if (neighborPlate == excludedPlateId)
                    continue;

                counts[neighborPlate] = counts.GetValueOrDefault(neighborPlate) + 1;
            }
        }

        if (counts.Count == 0)
            return false;

        var replacement = counts.OrderByDescending(kv => kv.Value).First().Key;
        foreach (var pixel in pixels)
            plateArray[pixel] = replacement;

        return true;
    }

    private IReadOnlyList<PlateDomain> BuildDomains(MapMask mask, CrustFieldMap crustFields, short[] plateArray, IReadOnlyList<DomainSeed> seeds, TectonicPlateGenerationOptions options)
    {
        var maxPlateId = seeds.Max(s => s.Id.Value);
        var pointCounts = new int[maxPlateId + 1];
        var continentalCounts = new int[maxPlateId + 1];
        var oceanicCounts = new int[maxPlateId + 1];
        var oceanicAgeSums = new double[maxPlateId + 1];
        var oceanicAgeCounts = new int[maxPlateId + 1];
        var sumSinX = new double[maxPlateId + 1];
        var sumCosX = new double[maxPlateId + 1];
        var sumY = new long[maxPlateId + 1];

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                var plateId = plateArray[index];
                var crust = crustFields.GetCrust(x, y);
                pointCounts[plateId]++;
                if (IsContinentalLike(crust))
                    continentalCounts[plateId]++;
                if (crust == CrustKind.Oceanic)
                    oceanicCounts[plateId]++;
                if (crust is CrustKind.Oceanic or CrustKind.Arc)
                {
                    var oceanicAge = crustFields.GetOceanicAge(x, y);
                    if (!double.IsNaN(oceanicAge))
                    {
                        oceanicAgeSums[plateId] += oceanicAge;
                        oceanicAgeCounts[plateId]++;
                    }
                }

                var angle = 2.0 * Math.PI * x / mask.Width;
                sumSinX[plateId] += Math.Sin(angle);
                sumCosX[plateId] += Math.Cos(angle);
                sumY[plateId] += y;
            }
        }

        var domains = new List<PlateDomain>();
        foreach (var seed in seeds)
        {
            var plateId = seed.Id.Value;
            var count = pointCounts[plateId];
            if (count == 0)
                continue;

            var continentalRatio = continentalCounts[plateId] / (double)count;
            var oceanicRatio = oceanicCounts[plateId] / (double)count;
            var kind = continentalRatio switch
            {
                >= 0.55 => TectonicPlateKind.Continental,
                _ when oceanicRatio >= 0.65 => TectonicPlateKind.Oceanic,
                _ => TectonicPlateKind.Mixed
            };
            var density = kind switch
            {
                TectonicPlateKind.Oceanic => 1.0 + _random.NextDouble() * 0.2,
                TectonicPlateKind.Continental => 0.65 + _random.NextDouble() * 0.15,
                _ => 0.8 + _random.NextDouble() * 0.2
            };
            var thickness = kind switch
            {
                TectonicPlateKind.Oceanic => 0.45 + _random.NextDouble() * 0.25,
                TectonicPlateKind.Continental => 0.85 + _random.NextDouble() * 0.35,
                _ => 0.65 + _random.NextDouble() * 0.35
            };
            var meanAngle = Math.Atan2(sumSinX[plateId] / count, sumCosX[plateId] / count);
            var centroidX = (int)Math.Round((meanAngle / (2.0 * Math.PI) * mask.Width + mask.Width) % mask.Width);
            var centroidY = (int)(sumY[plateId] / count);
            var meanOceanicAge = oceanicAgeCounts[plateId] == 0
                ? (double?)null
                : oceanicAgeSums[plateId] / oceanicAgeCounts[plateId];

            domains.Add(new PlateDomain(seed.Id, kind, count, new GridPoint(centroidX, centroidY), seed.Motion, seed.Activity * options.Activity, density, thickness, meanOceanicAge, seed.IsMicroplate));
        }

        return domains;
    }

    private static int EstimatePlateCount(MapMask mask, double earthLikeFactor)
    {
        var area = mask.Width * mask.Height;
        var areaFactor = Math.Log10(area / 10000.0) * 2.5;
        var chaoticCount = (8 + areaFactor) * 1.25;
        var earthLikeCount = 15.0;
        return Math.Clamp((int)Math.Round(chaoticCount * (1.0 - earthLikeFactor) + earthLikeCount * earthLikeFactor), 6, 24);
    }

    private static void SmoothPlateMap(MapMask mask, short[] plateArray, int passes)
    {
        var changes = new List<(int Index, short Plate)>();
        for (var pass = 0; pass < passes; pass++)
        {
            changes.Clear();
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var counts = new Dictionary<short, int>();
                    foreach (var neighbor in Neighbors8(new GridPoint(x, y), mask.Width, mask.Height))
                    {
                        var neighborPlate = plateArray[neighbor.Y * mask.Width + neighbor.X];
                        counts[neighborPlate] = counts.GetValueOrDefault(neighborPlate) + 1;
                    }

                    var index = y * mask.Width + x;
                    var current = plateArray[index];
                    if (counts.GetValueOrDefault(current) >= 3)
                        continue;

                    var replacement = counts.OrderByDescending(kv => kv.Value).First().Key;
                    if (replacement != current)
                        changes.Add((index, replacement));
                }
            }

            foreach (var change in changes)
                plateArray[change.Index] = change.Plate;
        }
    }

    private static bool IsContinentalLike(CrustKind crust) => crust is CrustKind.Continental or CrustKind.Shelf or CrustKind.Rift or CrustKind.Terrane;
    private static GridVector EstimateLineamentAxis(IReadOnlyList<GridPoint> points, int index, int width)
    {
        var left = points[Math.Max(0, index - 3)];
        var right = points[Math.Min(points.Count - 1, index + 3)];
        var dx = WrappedDeltaX(right.X - left.X, width);
        var dy = right.Y - left.Y;
        return Normalize(new GridVector(dx, dy));
    }

    private static GridVector Normalize(GridVector vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return length <= 0.000001 ? new GridVector(1, 0) : new GridVector(vector.X / length, vector.Y / length);
    }

    private static int WrappedDeltaX(int dx, int width)
    {
        if (Math.Abs(dx) <= width / 2)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static double WrappedDistanceSquared(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, width - dx);
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Hash01(int x, int y, int seed)
    {
        var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
        value = (value << 13) ^ value;
        return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
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

    private static int WrapX(int x, int width) => (x % width + width) % width;
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

    private sealed record SeedSet(IReadOnlyList<DomainSeed> Major, IReadOnlyList<DomainSeed> Micro)
    {
        public IReadOnlyList<DomainSeed> All { get; } = Major.Concat(Micro).ToArray();
    }

    private sealed record MicroplateCandidate(
        GridPoint Center,
        GridVector Axis,
        TectonicFeatureKind Kind,
        int? SourceLineamentId,
        string SystemKey,
        double Priority);

    private sealed record PlateMetrics(
        int Area,
        int Perimeter,
        double AspectRatio,
        double Compactness,
        IReadOnlyDictionary<short, int> NeighborCounts);

    private sealed record DomainSeed(
        TectonicPlateId Id,
        GridPoint Point,
        CrustKind PreferredCrust,
        GridVector Motion,
        double Activity,
        bool IsMicroplate,
        GridVector Axis,
        TectonicFeatureKind? SourceKind,
        string? SystemKey,
        double Priority);
}

internal sealed class TectonicBoundaryGenerator
{
    public TectonicBoundaryMap Generate(PlateDomainMap plateDomains, CrustFieldMap crustFields, TectonicPlateGenerationOptions options)
    {
        var domains = plateDomains.Domains.ToDictionary(d => d.Id);
        var samples = new List<BoundarySample>();

        for (var y = 0; y < plateDomains.Height; y++)
        {
            for (var x = 0; x < plateDomains.Width; x++)
            {
                var point = new GridPoint(x, y);
                var current = plateDomains.GetPlate(point);
                AddSampleIfBoundary(point, new GridPoint(x + 1 == plateDomains.Width ? 0 : x + 1, y), current, plateDomains, crustFields, domains, samples);
                if (y < plateDomains.Height - 1)
                    AddSampleIfBoundary(point, new GridPoint(x, y + 1), current, plateDomains, crustFields, domains, samples);
            }
        }

        var segments = BuildSegments(samples, plateDomains.Width, plateDomains.Height, options.MinBoundarySegmentLength);
        return new TectonicBoundaryMap(plateDomains.Width, plateDomains.Height, segments);
    }

    private static void AddSampleIfBoundary(
        GridPoint a,
        GridPoint b,
        TectonicPlateId plateA,
        PlateDomainMap plateDomains,
        CrustFieldMap crustFields,
        IReadOnlyDictionary<TectonicPlateId, PlateDomain> domains,
        List<BoundarySample> samples)
    {
        var plateB = plateDomains.GetPlate(b);
        if (plateA == plateB)
            return;

        if (!domains.TryGetValue(plateA, out var domainA) || !domains.TryGetValue(plateB, out var domainB))
            return;

        var normal = BoundaryNormal(a, b);
        var relativeMotion = new GridVector(domainB.Motion.X - domainA.Motion.X, domainB.Motion.Y - domainA.Motion.Y);
        var normalMotion = relativeMotion.X * normal.X + relativeMotion.Y * normal.Y;
        var tangentMotion = relativeMotion.X * -normal.Y + relativeMotion.Y * normal.X;
        var convergence = normalMotion < 0 ? -normalMotion : 0;
        var divergence = normalMotion > 0 ? normalMotion : 0;
        var shear = Math.Abs(tangentMotion);
        var crustA = crustFields.GetCrust(a);
        var crustB = crustFields.GetCrust(b);
        var boundaryMode = ClassifyBoundaryMode(crustFields, a, b, crustA, crustB, convergence, divergence, shear);
        var kind = ToSegmentKind(boundaryMode);
        var subductingPlate = IsSubductionMode(boundaryMode)
            ? ChooseSubductingPlate(crustFields, a, b, plateA, plateB, domainA, domainB, crustA, crustB)
            : (TectonicPlateId?)null;
        var activity = ComputeActivity(convergence, divergence, shear);
        var meanOceanicAge = MeanOceanicAge(crustFields, a, b, crustA, crustB);
        var subductingOceanicAge = subductingPlate.HasValue
            ? OceanicAgeForPlateSide(crustFields, a, b, plateA, plateB, subductingPlate.Value)
            : null;

        samples.Add(new BoundarySample(a, b, plateA, plateB, kind, boundaryMode, convergence, divergence, shear, activity, meanOceanicAge, subductingOceanicAge, subductingPlate));
    }

    private static BoundaryMode ClassifyBoundaryMode(CrustFieldMap crustFields, GridPoint a, GridPoint b, CrustKind crustA, CrustKind crustB, double convergence, double divergence, double shear)
    {
        var strongestNormal = Math.Max(convergence, divergence);
        const double passiveThreshold = 0.1;
        const double strongNormalThreshold = 0.18;
        var hasObliqueShear = shear >= strongestNormal * 0.35 && shear >= passiveThreshold;
        var oceanicA = IsOceanicLike(crustA);
        var oceanicB = IsOceanicLike(crustB);
        var continentalA = IsContinentalLike(crustA);
        var continentalB = IsContinentalLike(crustB);

        if (strongestNormal < passiveThreshold && shear < passiveThreshold)
            return IsPassiveMarginContext(crustA, crustB) ? BoundaryMode.PassiveMargin : BoundaryMode.DiffuseIntraplateBoundary;

        if (shear > strongestNormal * 1.75 && strongestNormal < strongNormalThreshold)
            return BoundaryMode.PureTransform;

        if (convergence >= divergence)
        {
            if (hasObliqueShear && (oceanicA || oceanicB))
                return BoundaryMode.ObliqueSubduction;

            if (hasObliqueShear)
                return BoundaryMode.Transpression;

            if (oceanicA && oceanicB)
                return BoundaryMode.OceanOceanSubduction;

            if (oceanicA || oceanicB)
                return BoundaryMode.OceanContinentSubduction;

            if (continentalA && continentalB)
                return BoundaryMode.ContinentContinentCollision;

            return BoundaryMode.AccretionaryBoundary;
        }

        if (crustA == CrustKind.Arc || crustB == CrustKind.Arc)
            return BoundaryMode.BackArcSpreading;

        if (oceanicA && oceanicB)
        {
            var age = AverageKnown(crustFields.GetOceanicAge(a), crustFields.GetOceanicAge(b));
            if (age < 35)
                return BoundaryMode.MidOceanRidge;

            return hasObliqueShear ? BoundaryMode.Transtension : BoundaryMode.BackArcSpreading;
        }

        if (hasObliqueShear)
            return BoundaryMode.Transtension;

        return BoundaryMode.ContinentalRift;
    }

    private static TectonicPlateId ChooseSubductingPlate(CrustFieldMap crustFields, GridPoint a, GridPoint b, TectonicPlateId plateA, TectonicPlateId plateB, PlateDomain domainA, PlateDomain domainB, CrustKind crustA, CrustKind crustB)
    {
        var aOceanic = IsOceanicLike(crustA);
        var bOceanic = IsOceanicLike(crustB);
        if (aOceanic && !bOceanic)
            return plateA;
        if (bOceanic && !aOceanic)
            return plateB;

        var ageA = double.IsNaN(crustFields.GetOceanicAge(a)) ? 0 : crustFields.GetOceanicAge(a);
        var ageB = double.IsNaN(crustFields.GetOceanicAge(b)) ? 0 : crustFields.GetOceanicAge(b);
        if (Math.Abs(ageA - ageB) > 5)
            return ageA >= ageB ? plateA : plateB;

        return domainA.Density >= domainB.Density ? plateA : plateB;
    }

    private static BoundarySegmentKind ToSegmentKind(BoundaryMode mode) => mode switch
    {
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary => BoundarySegmentKind.Subduction,
        BoundaryMode.ContinentContinentCollision or
        BoundaryMode.Transpression => BoundarySegmentKind.Collision,
        BoundaryMode.MidOceanRidge => BoundarySegmentKind.MidOceanRidge,
        BoundaryMode.ContinentalRift or
        BoundaryMode.Transtension => BoundarySegmentKind.ContinentalRift,
        BoundaryMode.BackArcSpreading => BoundarySegmentKind.BackArcBasin,
        BoundaryMode.PureTransform => BoundarySegmentKind.Transform,
        _ => BoundarySegmentKind.PassiveMargin
    };

    private static bool IsSubductionMode(BoundaryMode mode) => mode is
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary;

    private static double ComputeActivity(double convergence, double divergence, double shear)
    {
        var normal = Math.Max(convergence, divergence);
        return Math.Max(normal, shear * 0.85);
    }

    private static bool IsOceanicLike(CrustKind crust) => crust is CrustKind.Oceanic or CrustKind.Arc;

    private static bool IsPassiveMarginContext(CrustKind crustA, CrustKind crustB)
    {
        return (IsContinentalLike(crustA) && IsOceanicLike(crustB)) ||
               (IsContinentalLike(crustB) && IsOceanicLike(crustA)) ||
               crustA == CrustKind.Shelf ||
               crustB == CrustKind.Shelf;
    }

    private static double? MeanOceanicAge(CrustFieldMap crustFields, GridPoint a, GridPoint b, CrustKind crustA, CrustKind crustB)
    {
        var sum = 0.0;
        var count = 0;
        AddAge(crustFields.GetOceanicAge(a), crustA);
        AddAge(crustFields.GetOceanicAge(b), crustB);
        return count == 0 ? null : sum / count;

        void AddAge(double age, CrustKind crust)
        {
            if (!IsOceanicLike(crust) || double.IsNaN(age))
                return;

            sum += age;
            count++;
        }
    }

    private static double? OceanicAgeForPlateSide(CrustFieldMap crustFields, GridPoint a, GridPoint b, TectonicPlateId plateA, TectonicPlateId plateB, TectonicPlateId targetPlate)
    {
        if (targetPlate == plateA)
        {
            var age = crustFields.GetOceanicAge(a);
            return double.IsNaN(age) ? null : age;
        }

        if (targetPlate == plateB)
        {
            var age = crustFields.GetOceanicAge(b);
            return double.IsNaN(age) ? null : age;
        }

        return null;
    }

    private static IReadOnlyList<PlateBoundarySegment> BuildSegments(IReadOnlyList<BoundarySample> samples, int width, int height, int minSegmentLength)
    {
        var result = new List<PlateBoundarySegment>();
        var nextId = 1;
        foreach (var group in samples.GroupBy(s => new SegmentGroupKey(PlatePair.Create(s.PlateA, s.PlateB), s.BoundaryMode)))
        {
            var pointToSamples = new Dictionary<GridPoint, List<BoundarySample>>();
            foreach (var sample in group)
            {
                Add(pointToSamples, sample.PointA, sample);
                Add(pointToSamples, sample.PointB, sample);
            }

            var remaining = pointToSamples.Keys.ToHashSet();
            while (remaining.Count > 0)
            {
                var start = remaining.First();
                var queue = new Queue<GridPoint>();
                var componentPoints = new HashSet<GridPoint>();
                var componentSamples = new HashSet<BoundarySample>();
                remaining.Remove(start);
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    componentPoints.Add(current);
                    foreach (var sample in pointToSamples[current])
                        componentSamples.Add(sample);

                    foreach (var neighbor in Neighbors8(current, width, height))
                    {
                        if (!remaining.Remove(neighbor))
                            continue;

                        queue.Enqueue(neighbor);
                    }
                }

                var pair = group.Key.Pair;
                var pointList = componentPoints.ToArray();
                result.Add(new PlateBoundarySegment(
                    nextId++,
                    pair.A,
                    pair.B,
                    pointList,
                    ToSegmentKind(group.Key.BoundaryMode),
                    group.Key.BoundaryMode,
                    componentSamples.Average(s => s.Convergence),
                    componentSamples.Average(s => s.Divergence),
                    componentSamples.Average(s => s.Shear),
                    componentSamples.Average(s => s.Activity),
                    AverageKnown(componentSamples.Select(s => s.MeanOceanicAge)),
                    AverageKnown(componentSamples.Select(s => s.SubductingOceanicAge)),
                    DominantSubductingPlate(group.Key.BoundaryMode, componentSamples)));
            }
        }

        return MergeShortSegments(result, Math.Max(1, minSegmentLength));

        static void Add(Dictionary<GridPoint, List<BoundarySample>> lookup, GridPoint point, BoundarySample sample)
        {
            if (!lookup.TryGetValue(point, out var samplesAtPoint))
            {
                samplesAtPoint = [];
                lookup[point] = samplesAtPoint;
            }

            samplesAtPoint.Add(sample);
        }
    }

    private static IReadOnlyList<PlateBoundarySegment> MergeShortSegments(IReadOnlyList<PlateBoundarySegment> segments, int minSegmentLength)
    {
        var merged = new List<PlateBoundarySegment>();
        var nextId = 1;

        foreach (var group in segments.GroupBy(s => PlatePair.Create(s.PlateA, s.PlateB)))
        {
            var longSegments = group.Where(s => s.Points.Count >= minSegmentLength).ToList();
            var shortSegments = group.Where(s => s.Points.Count < minSegmentLength).ToList();

            if (longSegments.Count == 0)
            {
                merged.Add(Renumber(MergeSegmentGroup(nextId++, group), nextId - 1));
                continue;
            }

            var buckets = longSegments
                .Select(s => new SegmentBucket(
                    s.Kind,
                    s.BoundaryMode,
                    s.PlateA,
                    s.PlateB,
                    s.Points.ToList(),
                    s.Convergence * s.Points.Count,
                    s.Divergence * s.Points.Count,
                    s.Shear * s.Points.Count,
                    s.Activity * s.Points.Count,
                    NullableWeightedSum(s.MeanOceanicAge, s.Points.Count),
                    NullableWeightedSum(s.SubductingOceanicAge, s.Points.Count),
                    s.MeanOceanicAge.HasValue ? s.Points.Count : 0,
                    s.SubductingOceanicAge.HasValue ? s.Points.Count : 0,
                    s.Points.Count,
                    s.SubductingPlate))
                .ToList();

            foreach (var segment in shortSegments)
            {
                var target = buckets
                    .OrderByDescending(b => b.Kind == segment.Kind ? 1 : 0)
                    .ThenBy(b => DistanceSquared(b.Centroid, SegmentCentroid(segment.Points)))
                    .First();

                target.Points.AddRange(segment.Points);
                target.ConvergenceSum += segment.Convergence * segment.Points.Count;
                target.DivergenceSum += segment.Divergence * segment.Points.Count;
                target.ShearSum += segment.Shear * segment.Points.Count;
                target.ActivitySum += segment.Activity * segment.Points.Count;
                target.MeanOceanicAgeSum += NullableWeightedSum(segment.MeanOceanicAge, segment.Points.Count);
                target.SubductingOceanicAgeSum += NullableWeightedSum(segment.SubductingOceanicAge, segment.Points.Count);
                target.MeanOceanicAgeWeight += segment.MeanOceanicAge.HasValue ? segment.Points.Count : 0;
                target.SubductingOceanicAgeWeight += segment.SubductingOceanicAge.HasValue ? segment.Points.Count : 0;
                target.ModeWeights[segment.BoundaryMode] = target.ModeWeights.GetValueOrDefault(segment.BoundaryMode) + segment.Points.Count;
                target.Weight += segment.Points.Count;
                target.SubductingPlate ??= segment.SubductingPlate;
            }

            foreach (var bucket in buckets)
            {
                var boundaryMode = DominantBoundaryMode(bucket.ModeWeights, bucket.Weight);
                var subductingPlate = IsSubductionMode(boundaryMode) ? bucket.SubductingPlate : null;
                var subductingOceanicAge = IsSubductionMode(boundaryMode)
                    ? AverageFromWeightedSum(bucket.SubductingOceanicAgeSum, bucket.SubductingOceanicAgeWeight)
                    : null;
                merged.Add(new PlateBoundarySegment(
                    nextId++,
                    bucket.PlateA,
                    bucket.PlateB,
                    bucket.Points.Distinct().ToArray(),
                    ToSegmentKind(boundaryMode),
                    boundaryMode,
                    bucket.ConvergenceSum / bucket.Weight,
                    bucket.DivergenceSum / bucket.Weight,
                    bucket.ShearSum / bucket.Weight,
                    bucket.ActivitySum / bucket.Weight,
                    AverageFromWeightedSum(bucket.MeanOceanicAgeSum, bucket.MeanOceanicAgeWeight),
                    subductingOceanicAge,
                    subductingPlate));
            }
        }

        return merged;
    }

    private static PlateBoundarySegment MergeSegmentGroup(int id, IEnumerable<PlateBoundarySegment> segments)
    {
        var array = segments.ToArray();
        var first = array[0];
        var points = array.SelectMany(s => s.Points).Distinct().ToArray();
        var weight = Math.Max(1, array.Sum(s => s.Points.Count));
        var mode = DominantBoundaryMode(array, weight);
        var subductingPlate = IsSubductionMode(mode) ? DominantSubductingPlate(array) : null;
        var subductingOceanicAge = IsSubductionMode(mode)
            ? WeightedAverageKnown(array.Select(s => (s.SubductingOceanicAge, s.Points.Count)))
            : null;

        return new PlateBoundarySegment(
            id,
            first.PlateA,
            first.PlateB,
            points,
            ToSegmentKind(mode),
            mode,
            array.Sum(s => s.Convergence * s.Points.Count) / weight,
            array.Sum(s => s.Divergence * s.Points.Count) / weight,
            array.Sum(s => s.Shear * s.Points.Count) / weight,
            array.Sum(s => s.Activity * s.Points.Count) / weight,
            WeightedAverageKnown(array.Select(s => (s.MeanOceanicAge, s.Points.Count))),
            subductingOceanicAge,
            subductingPlate);
    }

    private static PlateBoundarySegment Renumber(PlateBoundarySegment segment, int id)
    {
        return segment with { Id = id };
    }

    private static GridPoint SegmentCentroid(IReadOnlyList<GridPoint> points)
    {
        var x = (int)Math.Round(points.Average(p => p.X));
        var y = (int)Math.Round(points.Average(p => p.Y));
        return new GridPoint(x, y);
    }

    private static double DistanceSquared(GridPoint a, GridPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static BoundaryMode DominantBoundaryMode(IEnumerable<PlateBoundarySegment> segments, int weight)
    {
        var weights = segments
            .GroupBy(s => s.BoundaryMode)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Points.Count));

        return DominantBoundaryMode(weights, weight);
    }

    private static BoundaryMode DominantBoundaryMode(IReadOnlyDictionary<BoundaryMode, int> weights, int totalWeight)
    {
        if (weights.Count == 0 || totalWeight <= 0)
            return BoundaryMode.MixedSegmentBoundary;

        var dominant = weights.OrderByDescending(kv => kv.Value).First();
        return dominant.Value / (double)totalWeight >= 0.6
            ? dominant.Key
            : BoundaryMode.MixedSegmentBoundary;
    }

    private static double NullableWeightedSum(double? value, int weight) => value.HasValue ? value.Value * weight : 0;

    private static double? AverageFromWeightedSum(double sum, int weight) => weight == 0 ? null : sum / weight;

    private static double? AverageKnown(IEnumerable<double?> values)
    {
        var sum = 0.0;
        var count = 0;
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            sum += value.Value;
            count++;
        }

        return count == 0 ? null : sum / count;
    }

    private static double? WeightedAverageKnown(IEnumerable<(double? Value, int Weight)> values)
    {
        var sum = 0.0;
        var weight = 0;
        foreach (var (value, itemWeight) in values)
        {
            if (!value.HasValue || itemWeight <= 0)
                continue;

            sum += value.Value * itemWeight;
            weight += itemWeight;
        }

        return weight == 0 ? null : sum / weight;
    }

    private static TectonicPlateId? DominantSubductingPlate(BoundaryMode mode, IEnumerable<BoundarySample> samples)
    {
        if (!IsSubductionMode(mode))
            return null;

        return samples
            .Where(s => s.SubductingPlate.HasValue)
            .GroupBy(s => s.SubductingPlate!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => (TectonicPlateId?)g.Key)
            .FirstOrDefault();
    }

    private static TectonicPlateId? DominantSubductingPlate(IEnumerable<PlateBoundarySegment> segments)
    {
        return segments
            .Where(s => s.SubductingPlate.HasValue)
            .GroupBy(s => s.SubductingPlate!.Value)
            .OrderByDescending(g => g.Sum(s => s.Points.Count))
            .Select(g => (TectonicPlateId?)g.Key)
            .FirstOrDefault();
    }

    private static bool IsContinentalLike(CrustKind crust) => crust is CrustKind.Continental or CrustKind.Shelf or CrustKind.Rift or CrustKind.Terrane;
    private static double AverageKnown(double a, double b)
    {
        if (double.IsNaN(a)) return double.IsNaN(b) ? 999 : b;
        if (double.IsNaN(b)) return a;
        return (a + b) / 2.0;
    }

    private static GridVector BoundaryNormal(GridPoint a, GridPoint b)
    {
        var dx = b.X - a.X;
        if (Math.Abs(dx) > 1)
            dx = dx > 0 ? -1 : 1;

        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length == 0 ? new GridVector(0, 0) : new GridVector(dx / length, dy / length);
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

    private static int WrapX(int x, int width) => (x % width + width) % width;
    private sealed record BoundarySample(
        GridPoint PointA,
        GridPoint PointB,
        TectonicPlateId PlateA,
        TectonicPlateId PlateB,
        BoundarySegmentKind Kind,
        BoundaryMode BoundaryMode,
        double Convergence,
        double Divergence,
        double Shear,
        double Activity,
        double? MeanOceanicAge,
        double? SubductingOceanicAge,
        TectonicPlateId? SubductingPlate);

    private sealed class SegmentBucket
    {
        public SegmentBucket(
            BoundarySegmentKind kind,
            BoundaryMode boundaryMode,
            TectonicPlateId plateA,
            TectonicPlateId plateB,
            List<GridPoint> points,
            double convergenceSum,
            double divergenceSum,
            double shearSum,
            double activitySum,
            double meanOceanicAgeSum,
            double subductingOceanicAgeSum,
            int meanOceanicAgeWeight,
            int subductingOceanicAgeWeight,
            int weight,
            TectonicPlateId? subductingPlate)
        {
            Kind = kind;
            BoundaryMode = boundaryMode;
            PlateA = plateA;
            PlateB = plateB;
            Points = points;
            ConvergenceSum = convergenceSum;
            DivergenceSum = divergenceSum;
            ShearSum = shearSum;
            ActivitySum = activitySum;
            MeanOceanicAgeSum = meanOceanicAgeSum;
            SubductingOceanicAgeSum = subductingOceanicAgeSum;
            MeanOceanicAgeWeight = meanOceanicAgeWeight;
            SubductingOceanicAgeWeight = subductingOceanicAgeWeight;
            Weight = weight;
            SubductingPlate = subductingPlate;
            ModeWeights = new Dictionary<BoundaryMode, int> { [boundaryMode] = weight };
        }

        public BoundarySegmentKind Kind { get; }
        public BoundaryMode BoundaryMode { get; }
        public TectonicPlateId PlateA { get; }
        public TectonicPlateId PlateB { get; }
        public List<GridPoint> Points { get; }
        public double ConvergenceSum { get; set; }
        public double DivergenceSum { get; set; }
        public double ShearSum { get; set; }
        public double ActivitySum { get; set; }
        public double MeanOceanicAgeSum { get; set; }
        public double SubductingOceanicAgeSum { get; set; }
        public int MeanOceanicAgeWeight { get; set; }
        public int SubductingOceanicAgeWeight { get; set; }
        public int Weight { get; set; }
        public TectonicPlateId? SubductingPlate { get; set; }
        public Dictionary<BoundaryMode, int> ModeWeights { get; }
        public GridPoint Centroid => SegmentCentroid(Points);
    }

    private readonly record struct PlatePair(TectonicPlateId A, TectonicPlateId B)
    {
        public static PlatePair Create(TectonicPlateId first, TectonicPlateId second) => first.Value <= second.Value ? new PlatePair(first, second) : new PlatePair(second, first);
    }
    private readonly record struct SegmentGroupKey(PlatePair Pair, BoundaryMode BoundaryMode);
}

internal sealed class OrogenProvinceGenerator
{
    public OrogenProvinceMap Generate(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, TectonicBoundaryMap boundaries)
    {
        var length = mask.Width * mask.Height;
        var influence = new double[length];
        var strength = new double[length];
        var axis = new double[length];
        var support = BuildBoundarySupport(mask, boundaries);
        var landmassIds = BuildLandmassIds(mask, out var landmassSizes);
        var provinces = new List<OrogenProvince>();
        var nextId = 1;

        foreach (var candidate in CreateCandidates(history, boundaries))
        {
            var orderedPoints = OrderAxisPoints(candidate.Points, mask.Width, candidate.IsHistorical);
            if (orderedPoints.Count < 4)
                continue;

            foreach (var segment in ValidateCandidate(mask, history, crustFields, support, landmassIds, landmassSizes, candidate, orderedPoints))
            {
                var province = new OrogenProvince(
                    nextId++,
                    segment.Points,
                    candidate.Age,
                    segment.Activity,
                    segment.MeanScore,
                    segment.BaseWidth,
                    candidate.SourceLineamentId,
                    candidate.SourceBoundarySegmentId);
                provinces.Add(province);
                StampProvince(mask, province, influence, strength, axis);
            }
        }

        return new OrogenProvinceMap(mask.Width, mask.Height, provinces, influence, strength, axis);
    }

    private static IEnumerable<CandidateAxis> CreateCandidates(TectonicHistory history, TectonicBoundaryMap boundaries)
    {
        foreach (var segment in boundaries.Segments.Where(s => IsOrogenSupportMode(s.BoundaryMode)))
            yield return new CandidateAxis(segment.Points, 0, Math.Clamp(segment.Activity, 0.15, 1.35), null, segment.Id, false);

        foreach (var lineament in history.Lineaments.Where(l => l.Kind is TectonicFeatureKind.Orogen or TectonicFeatureKind.Suture))
            yield return new CandidateAxis(lineament.Points, lineament.Age, Math.Clamp(lineament.Intensity, 0.10, 1.0), lineament.Id, null, true);
    }

    private static IEnumerable<ValidatedSegment> ValidateCandidate(
        MapMask mask,
        TectonicHistory history,
        CrustFieldMap crustFields,
        BoundarySupportMap support,
        int[] landmassIds,
        IReadOnlyDictionary<int, int> landmassSizes,
        CandidateAxis candidate,
        IReadOnlyList<GridPoint> points)
    {
        var width = mask.Width;
        var supportRadius = SupportRadius(mask);
        var chunkBase = Math.Clamp((int)Math.Round(width * (0.03 + Hash01(candidate.Seed, points[0].Y, 701) * 0.05)), 4, Math.Max(5, (int)Math.Round(width * 0.08)));
        var maxContinuous = Math.Max(chunkBase, (int)Math.Round(width * (0.10 + Hash01(candidate.Seed, points[^1].Y, 709) * 0.08)));
        var minRunLength = Math.Max(4, (int)Math.Round(width * 0.012));
        var threshold = candidate.IsHistorical ? 0.13 : 0.17;
        var start = 0;

        while (start < points.Count)
        {
            var variedLength = Math.Clamp(
                chunkBase + (int)Math.Round((Hash01(points[start].X, points[start].Y, candidate.Seed + 719) - 0.5) * chunkBase),
                minRunLength,
                maxContinuous);
            var end = Math.Min(points.Count, start + variedLength);
            var run = new List<ScoredPoint>();
            var previousLandmassId = -2;
            var cratonRun = 0;

            for (var i = start; i < end; i++)
            {
                var point = points[i];
                var index = point.Y * width + point.X;
                var landmassId = landmassIds[index];
                var score = ScorePoint(mask, history, crustFields, support, point, candidate, supportRadius, out var cratonInterior, out var boundaryDistance);
                var basinLike = IsBasinLike(crustFields.GetCrust(point), crustFields.GetCoastalZone(point), boundaryDistance, supportRadius);
                var supported = boundaryDistance <= supportRadius * (candidate.IsHistorical ? 0.95 : 1.08);
                var valid = landmassId >= 0 && score >= threshold && supported && !basinLike;

                if (candidate.IsHistorical && cratonInterior > 0.78 && boundaryDistance > supportRadius * 0.42)
                    cratonRun++;
                else
                    cratonRun = 0;

                if (previousLandmassId != -2 && landmassId != previousLandmassId)
                    valid = false;
                if (cratonRun >= Math.Max(3, minRunLength / 2))
                    valid = false;

                if (valid)
                {
                    run.Add(new ScoredPoint(point, score, boundaryDistance, landmassId));
                    previousLandmassId = landmassId;
                    continue;
                }

                foreach (var segment in FinishRun(mask, support, landmassSizes, candidate, run, minRunLength, supportRadius))
                    yield return segment;

                run.Clear();
                previousLandmassId = landmassId;
            }

            foreach (var segment in FinishRun(mask, support, landmassSizes, candidate, run, minRunLength, supportRadius))
                yield return segment;

            start = end;
        }
    }

    private static IEnumerable<ValidatedSegment> FinishRun(
        MapMask mask,
        BoundarySupportMap support,
        IReadOnlyDictionary<int, int> landmassSizes,
        CandidateAxis candidate,
        List<ScoredPoint> run,
        int minRunLength,
        double supportRadius)
    {
        if (run.Count < minRunLength)
            yield break;

        var first = run[0].Point;
        var gapChance = 0.15 + Hash01(first.X, first.Y, candidate.Seed + 733) * 0.30;
        if (Hash01(first.X, first.Y, candidate.Seed + 739) < gapChance)
            yield break;

        var meanScore = run.Average(p => p.Score);
        var meanBoundaryDistance = run.Average(p => p.BoundaryDistance);
        var supported = meanBoundaryDistance <= supportRadius * 0.62;
        if (candidate.IsHistorical && !supported)
        {
            var landmassId = run[0].LandmassId;
            if (landmassId >= 0 && landmassSizes.TryGetValue(landmassId, out var size))
            {
                var maxUnsupportedLength = Math.Max(minRunLength, (int)Math.Round(Math.Sqrt(size) * 0.22));
                if (run.Count > maxUnsupportedLength)
                    yield break;
            }

            meanScore *= 0.58;
        }

        var activity = Math.Clamp(candidate.Activity * (0.70 + meanScore * 0.75), 0.12, 1.25);
        var ageWidth = candidate.Age <= 0 ? 1.0 : Math.Clamp(1.15 + candidate.Age / 900.0, 1.15, 2.25);
        var baseWidth = Math.Clamp(
            mask.Width * (candidate.IsHistorical ? 0.015 : 0.010) * ageWidth * (0.72 + activity * 0.42),
            candidate.IsHistorical ? 3.0 : 2.0,
            candidate.IsHistorical ? Math.Max(5.0, mask.Width * 0.055) : Math.Max(4.0, mask.Width * 0.035));

        yield return new ValidatedSegment(run.Select(p => p.Point).ToArray(), meanScore, activity, baseWidth);
    }

    private static double ScorePoint(
        MapMask mask,
        TectonicHistory history,
        CrustFieldMap crustFields,
        BoundarySupportMap support,
        GridPoint point,
        CandidateAxis candidate,
        double supportRadius,
        out double cratonInterior,
        out double boundaryDistance)
    {
        var crust = crustFields.GetCrust(point);
        var coastal = crustFields.GetCoastalZone(point);
        var supportIndex = point.Y * mask.Width + point.X;
        boundaryDistance = support.Distance[supportIndex];
        var boundaryProximity = Math.Clamp(1.0 - boundaryDistance / Math.Max(1.0, supportRadius), 0, 1);
        var mode = support.Mode[supportIndex];
        var convergence = BoundaryModeScore(mode) * Math.Clamp(support.Activity[supportIndex], 0.10, 1.35);
        var continentalLike = ContinentalLikeScore(crust);
        var ageContrast = CrustAgeContrast(mask, crustFields, point);
        var nonCoastalPreference = NonCoastalPreference(crust, coastal, mode, boundaryProximity);
        cratonInterior = CratonInteriorScore(mask, history, crustFields, point, boundaryDistance, supportRadius);
        var cratonPenalty = 1.0 - cratonInterior * (boundaryProximity > 0.55 ? 0.25 : 0.78);
        var noise = SmoothNoise(point.X, point.Y, candidate.Seed + 757, Math.Max(8.0, mask.Width * 0.075));
        var noiseGate = noise < 0.22 ? 0.18 + noise * 1.3 : 0.54 + noise * 0.58;

        if (candidate.IsHistorical && ageContrast > 0.55 && boundaryProximity > 0.32)
            boundaryProximity = Math.Max(boundaryProximity, 0.48);

        return Math.Clamp(
            continentalLike *
            convergence *
            boundaryProximity *
            Math.Clamp(0.45 + ageContrast * 0.85, 0.25, 1.25) *
            nonCoastalPreference *
            Math.Clamp(cratonPenalty, 0.08, 1.0) *
            noiseGate,
            0,
            1.5);
    }

    private static BoundarySupportMap BuildBoundarySupport(MapMask mask, TectonicBoundaryMap boundaries)
    {
        var length = mask.Width * mask.Height;
        var distance = Enumerable.Repeat(double.PositiveInfinity, length).ToArray();
        var activity = new double[length];
        var segmentId = new int[length];
        var mode = Enumerable.Repeat(BoundaryMode.MixedSegmentBoundary, length).ToArray();
        var queue = new PriorityQueue<BoundaryQueueItem, double>();
        var maxDistance = SupportRadius(mask) * 1.15;

        foreach (var segment in boundaries.Segments.Where(s => IsOrogenSupportMode(s.BoundaryMode)))
        {
            foreach (var point in segment.Points)
            {
                var index = point.Y * mask.Width + point.X;
                if (distance[index] == 0 && activity[index] >= segment.Activity)
                    continue;

                distance[index] = 0;
                activity[index] = segment.Activity;
                segmentId[index] = segment.Id;
                mode[index] = segment.BoundaryMode;
                queue.Enqueue(new BoundaryQueueItem(point, 0, segment.BoundaryMode, segment.Activity, segment.Id), 0);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentIndex = current.Point.Y * mask.Width + current.Point.X;
            if (Math.Abs(distance[currentIndex] - current.Distance) > 0.0001)
                continue;

            foreach (var (neighbor, cost) in Neighbors8WithCost(current.Point, mask.Width, mask.Height))
            {
                var nextDistance = current.Distance + cost;
                if (nextDistance > maxDistance)
                    continue;

                var index = neighbor.Y * mask.Width + neighbor.X;
                if (distance[index] <= nextDistance)
                    continue;

                distance[index] = nextDistance;
                activity[index] = current.Activity;
                segmentId[index] = current.SegmentId;
                mode[index] = current.Mode;
                queue.Enqueue(new BoundaryQueueItem(neighbor, nextDistance, current.Mode, current.Activity, current.SegmentId), nextDistance);
            }
        }

        return new BoundarySupportMap(distance, activity, segmentId, mode);
    }

    private static int[] BuildLandmassIds(MapMask mask, out IReadOnlyDictionary<int, int> sizes)
    {
        var ids = Enumerable.Repeat(-1, mask.Width * mask.Height).ToArray();
        var counts = new Dictionary<int, int>();
        var nextId = 1;

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var start = new GridPoint(x, y);
                var startIndex = y * mask.Width + x;
                if (!mask.IsLand(start) || ids[startIndex] >= 0)
                    continue;

                var id = nextId++;
                var count = 0;
                var queue = new Queue<GridPoint>();
                ids[startIndex] = id;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    count++;
                    foreach (var neighbor in Neighbors4(current, mask.Width, mask.Height))
                    {
                        var index = neighbor.Y * mask.Width + neighbor.X;
                        if (!mask.IsLand(neighbor) || ids[index] >= 0)
                            continue;

                        ids[index] = id;
                        queue.Enqueue(neighbor);
                    }
                }

                counts[id] = count;
            }
        }

        sizes = counts;
        return ids;
    }

    private static IReadOnlyList<GridPoint> OrderAxisPoints(IReadOnlyList<GridPoint> points, int width, bool preserveOrder)
    {
        var distinct = points.Distinct().ToArray();
        if (preserveOrder || distinct.Length <= 2)
            return distinct;

        var remaining = distinct.ToHashSet();
        var start = distinct
            .OrderBy(p => NeighborCount(p, remaining, width))
            .ThenBy(p => p.Y)
            .ThenBy(p => p.X)
            .First();
        var ordered = new List<GridPoint>(distinct.Length) { start };
        remaining.Remove(start);

        while (remaining.Count > 0)
        {
            var current = ordered[^1];
            var next = remaining
                .OrderBy(p => WrappedDistanceSquared(current, p, width))
                .ThenBy(p => p.Y)
                .ThenBy(p => p.X)
                .First();
            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    private static void StampProvince(MapMask mask, OrogenProvince province, double[] influence, double[] strength, double[] axis)
    {
        if (province.AxisPoints.Count == 0)
            return;

        var taperLength = Math.Max(2.0, province.AxisPoints.Count * 0.22);
        var ageDecay = province.Age <= 0 ? 1.0 : Math.Clamp(0.92 - province.Age / 1400.0, 0.22, 0.82);
        var baseStrength = Math.Clamp(province.MeanScore * province.Activity * ageDecay, 0.05, 1.0);

        for (var i = 0; i < province.AxisPoints.Count; i++)
        {
            var point = province.AxisPoints[i];
            var endDistance = Math.Min(i, province.AxisPoints.Count - 1 - i);
            var taper = SmoothStep(Math.Clamp(endDistance / taperLength, 0, 1));
            var widthNoise = SmoothNoise(point.X, point.Y, province.Id * 61 + 2503, 28.0);
            var breakupNoise = SmoothNoise(point.X - 17, point.Y + 29, province.Id * 67 + 2521, 11.0);
            var localWidth = province.BaseWidth
                * Lerp(0.5, 2.2, widthNoise)
                * Lerp(0.6, 1.6, province.Activity)
                * (0.45 + taper * 0.55);
            var localStrength = baseStrength * taper * (0.50 + breakupNoise * 0.62);
            var radius = Math.Clamp((int)Math.Ceiling(localWidth), 1, Math.Max(2, (int)Math.Round(mask.Width * 0.08)));

            axis[point.Y * mask.Width + point.X] = Math.Max(axis[point.Y * mask.Width + point.X], province.MeanScore * taper);
            foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, radius))
            {
                if (!mask.IsLand(stamped))
                    continue;

                var distance = Math.Sqrt(WrappedDistanceSquared(point, stamped, mask.Width));
                if (distance > localWidth)
                    continue;

                var falloff = SmoothStep(Math.Clamp(1.0 - distance / Math.Max(1.0, localWidth), 0, 1));
                var index = stamped.Y * mask.Width + stamped.X;
                influence[index] = Math.Max(influence[index], falloff * province.MeanScore * (0.62 + taper * 0.38));
                strength[index] = Math.Max(strength[index], falloff * localStrength);
            }
        }
    }

    private static double CrustAgeContrast(MapMask mask, CrustFieldMap crustFields, GridPoint point)
    {
        var centerAge = crustFields.GetContinentalAge(point);
        var minAge = double.IsNaN(centerAge) ? double.PositiveInfinity : centerAge;
        var maxAge = double.IsNaN(centerAge) ? double.NegativeInfinity : centerAge;
        var terraneOrArc = crustFields.GetCrust(point) is CrustKind.Terrane or CrustKind.Arc ? 0.22 : 0.0;
        var radius = Math.Clamp((int)Math.Round(Math.Min(mask.Width, mask.Height) * 0.025), 2, 7);

        foreach (var stamped in PointsInRadius(mask.Width, mask.Height, point, radius))
        {
            var crust = crustFields.GetCrust(stamped);
            if (crust is CrustKind.Terrane or CrustKind.Arc)
                terraneOrArc = Math.Max(terraneOrArc, 0.26);

            var age = crustFields.GetContinentalAge(stamped);
            if (double.IsNaN(age))
                continue;

            minAge = Math.Min(minAge, age);
            maxAge = Math.Max(maxAge, age);
        }

        var contrast = double.IsInfinity(minAge) || double.IsInfinity(maxAge)
            ? 0
            : Math.Clamp((maxAge - minAge) / 1800.0, 0, 1);
        return Math.Clamp(contrast + terraneOrArc, 0, 1);
    }

    private static double CratonInteriorScore(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, GridPoint point, double boundaryDistance, double supportRadius)
    {
        if (!mask.IsLand(point))
            return 0;

        var crust = crustFields.GetCrust(point);
        var coastal = crustFields.GetCoastalZone(point);
        var age = crustFields.GetContinentalAge(point);
        var ageScore = double.IsNaN(age) ? 0 : Math.Clamp((age - 1700.0) / 1700.0, 0, 1);
        var centerRadius = Math.Max(8.0, mask.Width * 0.14);
        var centerScore = 0.0;

        foreach (var center in history.CratonCenters)
        {
            var distance = Math.Sqrt(WrappedDistanceSquared(point, center, mask.Width));
            centerScore = Math.Max(centerScore, Math.Clamp(1.0 - distance / centerRadius, 0, 1));
        }

        var stableCrust = crust == CrustKind.Continental && coastal == CoastalZoneKind.None ? 1.0 : 0.42;
        var supportRelief = boundaryDistance <= supportRadius * 0.45 ? 0.42 : 1.0;
        return Math.Clamp(Math.Max(ageScore, centerScore) * stableCrust * supportRelief, 0, 1);
    }

    private static double ContinentalLikeScore(CrustKind crust) => crust switch
    {
        CrustKind.Continental => 1.0,
        CrustKind.Terrane => 0.96,
        CrustKind.Arc => 0.70,
        CrustKind.Rift => 0.58,
        CrustKind.Shelf => 0.24,
        CrustKind.Oceanic => 0.06,
        _ => 0.25
    };

    private static double BoundaryModeScore(BoundaryMode mode) => mode switch
    {
        BoundaryMode.ContinentContinentCollision => 1.0,
        BoundaryMode.Transpression => 0.88,
        BoundaryMode.AccretionaryBoundary => 0.54,
        _ => 0.0
    };

    private static double NonCoastalPreference(CrustKind crust, CoastalZoneKind coastal, BoundaryMode mode, double boundaryProximity)
    {
        if (crust == CrustKind.Arc || coastal == CoastalZoneKind.ActiveMargin)
            return 0.86 + boundaryProximity * 0.16;

        return coastal switch
        {
            CoastalZoneKind.None => 1.0,
            CoastalZoneKind.PassiveMargin => mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression ? 0.70 : 0.35,
            CoastalZoneKind.Shelf or CoastalZoneKind.Slope => mode == BoundaryMode.AccretionaryBoundary ? 0.62 : 0.34,
            CoastalZoneKind.ShallowSea => 0.22,
            _ => 0.65
        };
    }

    private static bool IsBasinLike(CrustKind crust, CoastalZoneKind coastal, double boundaryDistance, double supportRadius)
    {
        if (boundaryDistance <= supportRadius * 0.40)
            return false;

        return crust == CrustKind.Rift ||
               coastal is CoastalZoneKind.Shelf or CoastalZoneKind.Slope or CoastalZoneKind.PassiveMargin or CoastalZoneKind.ShallowSea;
    }

    private static bool IsOrogenSupportMode(BoundaryMode mode) =>
        mode is BoundaryMode.ContinentContinentCollision or BoundaryMode.Transpression or BoundaryMode.AccretionaryBoundary;

    private static double SupportRadius(MapMask mask) => Math.Clamp(mask.Width * 0.065, 6.0, 28.0);

    private static int NeighborCount(GridPoint point, IReadOnlySet<GridPoint> points, int width) =>
        Neighbors8(point, width, int.MaxValue).Count(points.Contains);

    private static double Hash01(int x, int y, int seed)
    {
        var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
        value = (value << 13) ^ value;
        return Math.Clamp((1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0 + 1.0) * 0.5, 0, 1);
    }

    private static double SmoothNoise(int x, int y, int seed, double scale)
    {
        var sampleX = x / Math.Max(1.0, scale);
        var sampleY = y / Math.Max(1.0, scale);
        var x0 = (int)Math.Floor(sampleX);
        var y0 = (int)Math.Floor(sampleY);
        var tx = SmoothStep(sampleX - x0);
        var ty = SmoothStep(sampleY - y0);
        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);
        return Math.Clamp(Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty), 0, 1);
    }

    private static double SmoothStep(double value) => value * value * (3.0 - 2.0 * value);

    private static double Lerp(double a, double b, double amount) => a + (b - a) * amount;

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

    private static IEnumerable<(GridPoint Point, double Cost)> Neighbors8WithCost(GridPoint point, int width, int height)
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

                var cost = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0;
                yield return (new GridPoint(WrapX(point.X + dx, width), y), cost);
            }
        }
    }

    private static double WrappedDistanceSquared(GridPoint a, GridPoint b, int width)
    {
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, width - dx);
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private sealed record CandidateAxis(
        IReadOnlyList<GridPoint> Points,
        double Age,
        double Activity,
        int? SourceLineamentId,
        int? SourceBoundarySegmentId,
        bool IsHistorical)
    {
        public int Seed => SourceBoundarySegmentId ?? SourceLineamentId ?? 0;
    }

    private sealed record ScoredPoint(GridPoint Point, double Score, double BoundaryDistance, int LandmassId);

    private sealed record ValidatedSegment(IReadOnlyList<GridPoint> Points, double MeanScore, double Activity, double BaseWidth);

    private sealed record BoundaryQueueItem(GridPoint Point, double Distance, BoundaryMode Mode, double Activity, int SegmentId);

    private sealed record BoundarySupportMap(double[] Distance, double[] Activity, int[] SegmentId, BoundaryMode[] Mode);
}

internal sealed class TectonicFeatureGenerator
{
    public TectonicFeatureMap Generate(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, PlateDomainMap plateDomains, TectonicBoundaryMap boundaries, OrogenProvinceMap orogenProvinces, IReadOnlyList<Landmass> landmasses)
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
            StampFeature(mask, lineament.Kind, lineament.Points, lineament.Intensity, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);
        }

        foreach (var province in orogenProvinces.Provinces)
            features.Add(new TectonicFeature(nextId++, TectonicFeatureKind.Orogen, province.AxisPoints, province.Age, province.Activity, province.SourceBoundarySegmentId));

        foreach (var segment in boundaries.Segments)
        {
            var kind = ToFeatureKind(segment.BoundaryMode);
            features.Add(new TectonicFeature(nextId++, kind, segment.Points, 0, segment.Activity, segment.Id));
            StampSegment(mask, segment, uplift, subsidence, volcanism, seismicity, heatFlow, sedimentSupply);
        }

        StampOrogenProvinces(mask, orogenProvinces, uplift, seismicity);

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
                    heatFlow[index] += 0.35;
                    subsidence[index] += 0.25;
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
                    case TectonicFeatureKind.Hotspot:
                        volcanism[index] += 0.75 * intensity;
                        heatFlow[index] += 0.35 * intensity;
                        uplift[index] += 0.25 * intensity;
                        break;
                    case TectonicFeatureKind.Rift:
                    case TectonicFeatureKind.BackArcBasin:
                        subsidence[index] += 0.45 * intensity;
                        heatFlow[index] += 0.45 * intensity;
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
                        subsidence[index] += 0.45 * strength;
                        heatFlow[index] += 0.45 * strength;
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

internal sealed class TectonicPlateAssembler
{
    public TectonicPlateMap Assemble(TectonicHistory history, CrustFieldMap crustFields, PlateDomainMap plateDomains, TectonicBoundaryMap boundaries, TectonicFeatureMap features, OrogenProvinceMap orogenProvinces)
    {
        var plates = plateDomains.Domains
            .Select(d => new TectonicPlate(d.Id, d.Kind, d.PointCount, d.Centroid, d.Motion, d.Activity, d.Density, d.Thickness, d.MeanOceanicAge))
            .ToArray();
        var plateBoundaries = BuildPlateBoundaries(boundaries);
        var raster = new TectonicPlateRaster(plateDomains.Width, plateDomains.Height, plateDomains.PlatesSpan.ToArray(), crustFields.CrustSpan.ToArray());

        return new TectonicPlateMap(plateDomains.Width, plateDomains.Height, plates, plateBoundaries, raster, history, crustFields, plateDomains, boundaries, features, orogenProvinces);
    }

    private static IReadOnlyList<PlateBoundary> BuildPlateBoundaries(TectonicBoundaryMap boundaries)
    {
        return boundaries.Segments
            .GroupBy(s => PlatePair.Create(s.PlateA, s.PlateB))
            .Select(group =>
            {
                var segments = group.ToArray();
                var points = segments.SelectMany(s => s.Points).Distinct().ToArray();
                var weight = Math.Max(1, segments.Sum(s => s.Points.Count));
                var boundaryMode = DominantBoundaryMode(segments, weight);
                var convergence = segments.Sum(s => s.Convergence * s.Points.Count) / weight;
                var divergence = segments.Sum(s => s.Divergence * s.Points.Count) / weight;
                var shear = segments.Sum(s => s.Shear * s.Points.Count) / weight;
                var subductingPlate = IsSubductionMode(boundaryMode) ? DominantSubductingPlate(segments) : null;
                var subductingOceanicAge = IsSubductionMode(boundaryMode)
                    ? WeightedAverageKnown(segments.Select(s => (s.SubductingOceanicAge, s.Points.Count)))
                    : null;
                var pair = group.Key;
                return new PlateBoundary(
                    pair.A,
                    pair.B,
                    points,
                    ToLegacyKind(boundaryMode, convergence, divergence, shear),
                    boundaryMode,
                    convergence,
                    divergence,
                    shear,
                    segments.Sum(s => s.Activity * s.Points.Count) / weight,
                    WeightedAverageKnown(segments.Select(s => (s.MeanOceanicAge, s.Points.Count))),
                    subductingOceanicAge,
                    subductingPlate,
                    segments,
                    segments.Select(s => s.Id).ToArray());
            })
            .ToArray();
    }

    private static PlateBoundaryKind ToLegacyKind(BoundaryMode mode, double convergence, double divergence, double shear) => mode switch
    {
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary or
        BoundaryMode.ContinentContinentCollision or
        BoundaryMode.Transpression => PlateBoundaryKind.Convergent,
        BoundaryMode.ContinentalRift or
        BoundaryMode.MidOceanRidge or
        BoundaryMode.BackArcSpreading or
        BoundaryMode.Transtension => PlateBoundaryKind.Divergent,
        BoundaryMode.PureTransform => PlateBoundaryKind.Transform,
        BoundaryMode.MixedSegmentBoundary => ToLegacyKindFromMotion(convergence, divergence, shear),
        _ => PlateBoundaryKind.Passive
    };

    private static PlateBoundaryKind ToLegacyKindFromMotion(double convergence, double divergence, double shear)
    {
        var normal = Math.Max(convergence, divergence);
        if (normal < 0.1 && shear < 0.1)
            return PlateBoundaryKind.Passive;

        if (shear > normal * 1.75 && normal < 0.18)
            return PlateBoundaryKind.Transform;

        return convergence >= divergence ? PlateBoundaryKind.Convergent : PlateBoundaryKind.Divergent;
    }

    private static BoundaryMode DominantBoundaryMode(IEnumerable<PlateBoundarySegment> segments, int weight)
    {
        var weights = segments
            .GroupBy(s => s.BoundaryMode)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Points.Count));

        if (weights.Count == 0 || weight <= 0)
            return BoundaryMode.MixedSegmentBoundary;

        var dominant = weights.OrderByDescending(kv => kv.Value).First();
        return dominant.Value / (double)weight >= 0.6
            ? dominant.Key
            : BoundaryMode.MixedSegmentBoundary;
    }

    private static bool IsSubductionMode(BoundaryMode mode) => mode is
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary;

    private static double? WeightedAverageKnown(IEnumerable<(double? Value, int Weight)> values)
    {
        var sum = 0.0;
        var weight = 0;
        foreach (var (value, itemWeight) in values)
        {
            if (!value.HasValue || itemWeight <= 0)
                continue;

            sum += value.Value * itemWeight;
            weight += itemWeight;
        }

        return weight == 0 ? null : sum / weight;
    }

    private static TectonicPlateId? DominantSubductingPlate(IEnumerable<PlateBoundarySegment> segments)
    {
        return segments
            .Where(s => s.SubductingPlate.HasValue)
            .GroupBy(s => s.SubductingPlate!.Value)
            .OrderByDescending(g => g.Sum(s => s.Points.Count))
            .Select(g => (TectonicPlateId?)g.Key)
            .FirstOrDefault();
    }

    private readonly record struct PlatePair(TectonicPlateId A, TectonicPlateId B)
    {
        public static PlatePair Create(TectonicPlateId first, TectonicPlateId second) => first.Value <= second.Value ? new PlatePair(first, second) : new PlatePair(second, first);
    }
}
