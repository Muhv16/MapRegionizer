using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

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
