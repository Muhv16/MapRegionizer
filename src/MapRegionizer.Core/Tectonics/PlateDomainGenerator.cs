using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

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
