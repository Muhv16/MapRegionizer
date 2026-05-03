using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

internal sealed class TectonicPlateGenerator
{
    private readonly Random _random;

    public TectonicPlateGenerator(Random random)
    {
        _random = random;
    }

    public TectonicPlateMap Generate(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies, MapGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);

        if (mask.Width <= 0 || mask.Height <= 0)
            throw new ArgumentException("Map mask dimensions must be greater than zero.", nameof(mask));

        if (options.ProjectionMode != MapProjectionMode.EquirectangularWorld)
            throw new NotSupportedException("Tectonic plate generation currently supports only equirectangular world maps.");

        var crustByPoint = BuildCrustMap(mask);
        var seeds = CreateSeeds(mask, landmasses, waterBodies, options);
        var plateByPoint = AssignPointsToPlates(mask, crustByPoint, seeds, options);
        var plates = BuildPlates(mask, crustByPoint, plateByPoint, seeds, options);
        var boundaries = BuildBoundaries(mask, crustByPoint, plateByPoint, plates);

        return new TectonicPlateMap(mask.Width, mask.Height, plates, boundaries, plateByPoint, crustByPoint);
    }

    private static Dictionary<GridPoint, CrustKind> BuildCrustMap(MapMask mask)
    {
        var crustByPoint = new Dictionary<GridPoint, CrustKind>(mask.Width * mask.Height);

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                crustByPoint[point] = mask.IsLand(point) ? CrustKind.Continental : CrustKind.Oceanic;
            }
        }

        return crustByPoint;
    }

    private List<PlateSeed> CreateSeeds(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies, MapGenerationOptions options)
    {
        var plateCount = options.TectonicPlates.PlateCount ?? EstimatePlateCount(mask, landmasses, waterBodies);
        plateCount = Math.Clamp(plateCount, 1, Math.Max(1, mask.Width * mask.Height));

        var landPoints = mask.LandPoints.Where(IsInside(mask)).ToArray();
        var waterPoints = EnumeratePoints(mask).Where(p => !mask.IsLand(p)).ToArray();
        var targetContinentalSeeds = (int)Math.Round(plateCount * options.TectonicPlates.ContinentalSeedRatio);
        targetContinentalSeeds = Math.Clamp(targetContinentalSeeds, landPoints.Length > 0 ? 1 : 0, landPoints.Length == 0 ? 0 : plateCount);

        var seeds = new List<PlateSeed>(plateCount);
        AddSeeds(seeds, landPoints, targetContinentalSeeds, CrustKind.Continental, mask);
        AddSeeds(seeds, waterPoints, plateCount - seeds.Count, CrustKind.Oceanic, mask);

        if (seeds.Count < plateCount)
            AddSeeds(seeds, landPoints.Concat(waterPoints).ToArray(), plateCount - seeds.Count, CrustKind.Oceanic, mask);

        return seeds;
    }

    private static int EstimatePlateCount(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies)
    {
        var area = mask.Width * mask.Height;
        var areaFactor = Math.Sqrt(area) / 12.0;
        var geographyFactor = Math.Sqrt(Math.Max(1, landmasses.Count + waterBodies.Count));
        return Math.Clamp((int)Math.Round(8 + areaFactor + geographyFactor), 6, 24);
    }

    private void AddSeeds(List<PlateSeed> seeds, IReadOnlyList<GridPoint> candidates, int count, CrustKind preferredCrust, MapMask mask)
    {
        if (count <= 0 || candidates.Count == 0)
            return;

        var minDistance = Math.Max(2, Math.Min(mask.Width, mask.Height) / Math.Max(3, count + seeds.Count));
        var targetCount = seeds.Count + count;
        var attempts = 0;

        while (seeds.Count < targetCount && attempts++ < count * 300)
        {
            var point = candidates[_random.Next(candidates.Count)];
            if (seeds.Any(seed => WrappedDistanceSquared(seed.Point, point, mask.Width, false) < minDistance * minDistance))
                continue;

            seeds.Add(CreateSeed(seeds.Count + 1, point, preferredCrust));
        }

        while (seeds.Count < targetCount)
        {
            var point = candidates[_random.Next(candidates.Count)];
            seeds.Add(CreateSeed(seeds.Count + 1, point, preferredCrust));
        }
    }

    private PlateSeed CreateSeed(int id, GridPoint point, CrustKind preferredCrust)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var baseSpeed = preferredCrust == CrustKind.Oceanic ? 1.15 : 0.85;
        var speed = baseSpeed * (0.45 + _random.NextDouble() * 0.8);

        return new PlateSeed(
            new TectonicPlateId(id),
            point,
            preferredCrust,
            new GridVector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
            0.75 + _random.NextDouble() * 0.5);
    }

    private Dictionary<GridPoint, TectonicPlateId> AssignPointsToPlates(
        MapMask mask,
        IReadOnlyDictionary<GridPoint, CrustKind> crustByPoint,
        IReadOnlyList<PlateSeed> seeds,
        MapGenerationOptions options)
    {
        var plateByPoint = new Dictionary<GridPoint, TectonicPlateId>(mask.Width * mask.Height);
        var mapScale = Math.Max(mask.Width, mask.Height);
        var noiseScale = mapScale * 12.0;
        var noiseCellSize = Math.Max(8, mapScale / 32);
        var transitionScale = 0.5 + options.TectonicPlates.EarthLikeFactor;

        foreach (var point in EnumeratePoints(mask))
        {
            var crust = crustByPoint[point];
            PlateSeed? bestSeed = null;
            var bestScore = double.MaxValue;

            foreach (var seed in seeds)
            {
                var distance = WrappedDistanceSquared(point, seed.Point, mask.Width, true);
                var transitionPenalty = seed.PreferredCrust == crust ? 0 : options.TectonicPlates.LandWaterTransitionPenalty * mapScale * transitionScale;
                var noise = CoherentNoise(point, seed.Id.Value, noiseCellSize, mask.Width) * options.TectonicPlates.BoundaryNoise * noiseScale;
                var score = distance + transitionPenalty + noise;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestSeed = seed;
            }

            plateByPoint[point] = bestSeed?.Id ?? seeds[0].Id;
        }

        SmoothPlateMap(mask, plateByPoint, 4);
        return plateByPoint;
    }

    private static void SmoothPlateMap(MapMask mask, Dictionary<GridPoint, TectonicPlateId> plateByPoint, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var changes = new List<(GridPoint Point, TectonicPlateId Plate)>();

            foreach (var point in EnumeratePoints(mask))
            {
                var current = plateByPoint[point];
                var neighborCounts = GetNeighbors(point, mask).GroupBy(n => plateByPoint[n]).ToDictionary(g => g.Key, g => g.Count());

                if (neighborCounts.TryGetValue(current, out var sameCount) && sameCount >= 2)
                    continue;

                var replacement = neighborCounts.OrderByDescending(kv => kv.Value).First().Key;
                if (replacement != current)
                    changes.Add((point, replacement));
            }

            foreach (var change in changes)
                plateByPoint[change.Point] = change.Plate;
        }
    }

    private IReadOnlyList<TectonicPlate> BuildPlates(
        MapMask mask,
        IReadOnlyDictionary<GridPoint, CrustKind> crustByPoint,
        IReadOnlyDictionary<GridPoint, TectonicPlateId> plateByPoint,
        IReadOnlyList<PlateSeed> seeds,
        MapGenerationOptions options)
    {
        var pointsByPlate = plateByPoint.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToHashSet());
        var plates = new List<TectonicPlate>(pointsByPlate.Count);

        foreach (var seed in seeds)
        {
            if (!pointsByPlate.TryGetValue(seed.Id, out var points) || points.Count == 0)
                continue;

            var continentalRatio = points.Count(p => crustByPoint[p] == CrustKind.Continental) / (double)points.Count;
            var kind = continentalRatio switch
            {
                >= 0.55 => TectonicPlateKind.Continental,
                <= 0.15 => TectonicPlateKind.Oceanic,
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

            plates.Add(new TectonicPlate(seed.Id, kind, points, seed.Motion, seed.Activity * options.TectonicPlates.Activity, density, thickness));
        }

        return plates;
    }

    private static IReadOnlyList<PlateBoundary> BuildBoundaries(
        MapMask mask,
        IReadOnlyDictionary<GridPoint, CrustKind> crustByPoint,
        IReadOnlyDictionary<GridPoint, TectonicPlateId> plateByPoint,
        IReadOnlyList<TectonicPlate> plates)
    {
        var plateLookup = plates.ToDictionary(p => p.Id);
        var samplesByPair = new Dictionary<PlatePair, List<BoundarySample>>();

        foreach (var point in EnumeratePoints(mask))
        {
            var plate = plateByPoint[point];
            foreach (var neighbor in GetPositiveNeighbors(point, mask))
            {
                var neighborPlate = plateByPoint[neighbor];
                if (plate == neighborPlate)
                    continue;

                var pair = PlatePair.Create(plate, neighborPlate);
                if (!samplesByPair.TryGetValue(pair, out var samples))
                {
                    samples = [];
                    samplesByPair[pair] = samples;
                }

                samples.Add(new BoundarySample(point, neighbor, plate, neighborPlate, crustByPoint[point], crustByPoint[neighbor]));
            }
        }

        var boundaries = new List<PlateBoundary>(samplesByPair.Count);

        foreach (var (pair, samples) in samplesByPair)
        {
            if (!plateLookup.TryGetValue(pair.A, out var plateA) || !plateLookup.TryGetValue(pair.B, out var plateB))
                continue;

            var convergence = 0.0;
            var divergence = 0.0;
            var shear = 0.0;

            foreach (var sample in samples)
            {
                var normal = BoundaryNormal(sample.PointA, sample.PointB, sample.PlateA == pair.A);
                var relativeMotion = new GridVector(plateB.Motion.X - plateA.Motion.X, plateB.Motion.Y - plateA.Motion.Y);
                var normalMotion = relativeMotion.X * normal.X + relativeMotion.Y * normal.Y;
                var tangentMotion = relativeMotion.X * -normal.Y + relativeMotion.Y * normal.X;

                if (normalMotion > 0)
                    divergence += normalMotion;
                else
                    convergence += -normalMotion;

                shear += Math.Abs(tangentMotion);
            }

            convergence /= samples.Count;
            divergence /= samples.Count;
            shear /= samples.Count;

            var kind = ClassifyBoundary(convergence, divergence, shear);
            var subductingPlate = kind == PlateBoundaryKind.Convergent ? FindSubductingPlate(pair, samples, plateA, plateB) : null;
            var points = samples.Select(s => s.PointA).Concat(samples.Select(s => s.PointB)).Distinct().ToArray();

            boundaries.Add(new PlateBoundary(pair.A, pair.B, points, kind, convergence, divergence, shear, subductingPlate));
        }

        return boundaries;
    }

    private static PlateBoundaryKind ClassifyBoundary(double convergence, double divergence, double shear)
    {
        var strongestNormal = Math.Max(convergence, divergence);
        var threshold = 0.12;

        if (strongestNormal < threshold && shear < threshold)
            return PlateBoundaryKind.Passive;

        if (shear > strongestNormal * 1.25)
            return PlateBoundaryKind.Transform;

        return convergence >= divergence ? PlateBoundaryKind.Convergent : PlateBoundaryKind.Divergent;
    }

    private static TectonicPlateId? FindSubductingPlate(PlatePair pair, IReadOnlyList<BoundarySample> samples, TectonicPlate plateA, TectonicPlate plateB)
    {
        var aOceanic = 0;
        var bOceanic = 0;

        foreach (var sample in samples)
        {
            var crustA = sample.PlateA == pair.A ? sample.CrustA : sample.CrustB;
            var crustB = sample.PlateA == pair.A ? sample.CrustB : sample.CrustA;

            if (crustA == CrustKind.Oceanic)
                aOceanic++;
            if (crustB == CrustKind.Oceanic)
                bOceanic++;
        }

        if (aOceanic == 0 && bOceanic == 0)
            return null;

        if (aOceanic != bOceanic)
            return aOceanic > bOceanic ? pair.A : pair.B;

        return plateA.Density >= plateB.Density ? pair.A : pair.B;
    }

    private static GridVector BoundaryNormal(GridPoint pointA, GridPoint pointB, bool fromAtoB)
    {
        var dx = pointB.X - pointA.X;
        if (Math.Abs(dx) > 1)
            dx = dx > 0 ? -1 : 1;

        var dy = pointB.Y - pointA.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            return new GridVector(0, 0);

        var x = dx / length;
        var y = dy / length;
        return fromAtoB ? new GridVector(x, y) : new GridVector(-x, -y);
    }

    private static IEnumerable<GridPoint> EnumeratePoints(MapMask mask)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
                yield return new GridPoint(x, y);
        }
    }

    private static IEnumerable<GridPoint> GetNeighbors(GridPoint point, MapMask mask)
    {
        yield return new GridPoint(WrapX(point.X - 1, mask.Width), point.Y);
        yield return new GridPoint(WrapX(point.X + 1, mask.Width), point.Y);

        if (point.Y > 0)
            yield return new GridPoint(point.X, point.Y - 1);

        if (point.Y < mask.Height - 1)
            yield return new GridPoint(point.X, point.Y + 1);
    }

    private static IEnumerable<GridPoint> GetPositiveNeighbors(GridPoint point, MapMask mask)
    {
        yield return new GridPoint(WrapX(point.X + 1, mask.Width), point.Y);

        if (point.Y < mask.Height - 1)
            yield return new GridPoint(point.X, point.Y + 1);
    }

    private static int WrapX(int x, int width)
    {
        if (x < 0)
            return width - 1;

        if (x >= width)
            return 0;

        return x;
    }

    private static double WrappedDistanceSquared(GridPoint a, GridPoint b, int width, bool wrapX)
    {
        var dx = Math.Abs(a.X - b.X);
        if (wrapX)
            dx = Math.Min(dx, width - dx);

        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double CoherentNoise(GridPoint point, int seed, int cellSize, int width)
    {
        var cellX = point.X / cellSize;
        var cellY = point.Y / cellSize;
        var nextCellX = (cellX + 1) % Math.Max(1, (int)Math.Ceiling(width / (double)cellSize));
        var nextCellY = cellY + 1;
        var localX = SmoothStep((point.X % cellSize) / (double)cellSize);
        var localY = SmoothStep((point.Y % cellSize) / (double)cellSize);

        var top = Lerp(HashNoise(cellX, cellY, seed), HashNoise(nextCellX, cellY, seed), localX);
        var bottom = Lerp(HashNoise(cellX, nextCellY, seed), HashNoise(nextCellX, nextCellY, seed), localX);
        return Lerp(top, bottom, localY);
    }

    private static double HashNoise(int x, int y, int seed)
    {
        var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
        value = (value << 13) ^ value;
        return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
    }

    private static double SmoothStep(double value) => value * value * (3 - 2 * value);

    private static double Lerp(double a, double b, double amount)
    {
        return a + (b - a) * amount;
    }

    private static Func<GridPoint, bool> IsInside(MapMask mask) => point => point.X >= 0 && point.X < mask.Width && point.Y >= 0 && point.Y < mask.Height;

    private sealed record PlateSeed(TectonicPlateId Id, GridPoint Point, CrustKind PreferredCrust, GridVector Motion, double Activity);

    private readonly record struct PlatePair(TectonicPlateId A, TectonicPlateId B)
    {
        public static PlatePair Create(TectonicPlateId first, TectonicPlateId second)
        {
            return first.Value <= second.Value ? new PlatePair(first, second) : new PlatePair(second, first);
        }
    }

    private sealed record BoundarySample(
        GridPoint PointA,
        GridPoint PointB,
        TectonicPlateId PlateA,
        TectonicPlateId PlateB,
        CrustKind CrustA,
        CrustKind CrustB);
}
