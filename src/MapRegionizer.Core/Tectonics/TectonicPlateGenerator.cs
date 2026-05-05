using System.Diagnostics;
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

        var crustArray = BuildCrustMap(mask);
        var seeds = CreateSeeds(mask, landmasses, waterBodies, options);
        var plateArray = AssignPointsToPlates(mask, crustArray, seeds, options);

        if (options.TectonicPlates.ValidateGeometry)
            ValidateAndFixPlateGeometry(mask, plateArray, options.TectonicPlates);

        var plates = BuildPlates(mask, crustArray, plateArray, seeds, options);
        var boundaries = BuildBoundaries(mask, crustArray, plateArray, plates);
        var raster = new TectonicPlateRaster(mask.Width, mask.Height, plateArray, crustArray);

        return new TectonicPlateMap(mask.Width, mask.Height, plates, boundaries, raster);
    }

    private static byte[] BuildCrustMap(MapMask mask)
    {
        var crust = new byte[mask.Width * mask.Height];

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                crust[index] = mask.IsLand(point) ? (byte)CrustKind.Continental : (byte)CrustKind.Oceanic;
            }
        }

        return crust;
    }

    private List<PlateSeed> CreateSeeds(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies, MapGenerationOptions options)
    {
        var plateCount = options.TectonicPlates.PlateCount ?? EstimatePlateCount(mask, landmasses, waterBodies, options.TectonicPlates.EarthLikeFactor);
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

    private static int EstimatePlateCount(MapMask mask, IReadOnlyList<Landmass> landmasses, IReadOnlyList<WaterBody> waterBodies, double earthLikeFactor)
    {
        var area = mask.Width * mask.Height;

        // Logarithmic scaling for area - grows much slower than linear
        var areaFactor = Math.Log10(area / 10000.0) * 2.5;

        // Geography factor - more landmasses/water bodies suggest more complexity
        var geographyFactor = Math.Sqrt(Math.Max(1, landmasses.Count + waterBodies.Count)) * 0.5;

        // Base count interpolates between chaotic (many plates) and Earth-like (fewer plates)
        // EarthLikeFactor = 1.0 → ~15 plates (Earth-like)
        // EarthLikeFactor = 0.0 → ~20 plates (more chaotic)
        var baseCount = 8 + areaFactor + geographyFactor;
        var chaoticCount = baseCount * 1.2;  // Reduced multiplier for more conservative plate count
        var earthLikeCount = 15.0;  // Earth has ~15 major plates

        var targetCount = chaoticCount * (1.0 - earthLikeFactor) + earthLikeCount * earthLikeFactor;

        return Math.Clamp((int)Math.Round(targetCount), 6, 24);
    }

    private void AddSeeds(List<PlateSeed> seeds, IReadOnlyList<GridPoint> candidates, int count, CrustKind preferredCrust, MapMask mask)
    {
        if (count <= 0 || candidates.Count == 0)
            return;

        var minDistance = Math.Max(2, Math.Min(mask.Width, mask.Height) / Math.Max(3, count + seeds.Count));
        var targetCount = seeds.Count + count;
        var attempts = 0;
        var maxAttempts = count * 1000;  // Increased from 300 to 1000

        // First pass: try to place seeds with minimum distance constraint
        while (seeds.Count < targetCount && attempts++ < maxAttempts)
        {
            var point = candidates[_random.Next(candidates.Count)];
            if (seeds.Any(seed => WrappedDistanceSquared(seed.Point, point, mask.Width, false) < minDistance * minDistance))
                continue;

            seeds.Add(CreateSeed(seeds.Count + 1, point, preferredCrust));
        }

        // Second pass: if we couldn't place all seeds, reduce minimum distance and retry
        if (seeds.Count < targetCount)
        {
            minDistance = (int)(minDistance * 0.7);
            attempts = 0;

            while (seeds.Count < targetCount && attempts++ < maxAttempts)
            {
                var point = candidates[_random.Next(candidates.Count)];
                if (seeds.Any(seed => WrappedDistanceSquared(seed.Point, point, mask.Width, false) < minDistance * minDistance))
                    continue;

                seeds.Add(CreateSeed(seeds.Count + 1, point, preferredCrust));
            }
        }

        // Final fallback: place remaining seeds without distance constraint
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

    private short[] AssignPointsToPlates(
        MapMask mask,
        byte[] crustArray,
        IReadOnlyList<PlateSeed> seeds,
        MapGenerationOptions options)
    {
        var plateArray = new short[mask.Width * mask.Height];
        var mapScale = Math.Max(mask.Width, mask.Height);
        var noiseScale = mapScale * options.TectonicPlates.BoundaryNoiseScale;
        var noiseCellSize = Math.Max(16, mapScale / 16);  // Larger cells for smoother noise
        var transitionScale = 0.5 + options.TectonicPlates.EarthLikeFactor;

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var index = y * mask.Width + x;
                var point = new GridPoint(x, y);
                var crust = (CrustKind)crustArray[index];
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

                plateArray[index] = (short)(bestSeed?.Id.Value ?? seeds[0].Id.Value);
            }
        }

        SmoothPlateMap(mask, plateArray, 4);
        return plateArray;
    }

    private static void SmoothPlateMap(MapMask mask, short[] plateArray, int passes)
    {
        var width = mask.Width;
        var height = mask.Height;

        // Adaptive passes: larger maps need more smoothing
        var area = width * height;
        var adaptivePasses = Math.Max(passes, (int)Math.Ceiling(Math.Log10(area) / 2.0));

        var changes = new List<(int Index, short Plate)>();

        for (var pass = 0; pass < adaptivePasses; pass++)
        {
            changes.Clear();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var current = plateArray[index];

                    // Count neighbor plates with 8-connectivity (including diagonals)
                    var counts = new Dictionary<short, int>();

                    // Horizontal and vertical neighbors
                    AddNeighborCount(plateArray, counts, width, height, x - 1, y);  // Left (wrapped)
                    AddNeighborCount(plateArray, counts, width, height, x + 1, y);  // Right (wrapped)
                    if (y > 0)
                        AddNeighborCount(plateArray, counts, width, height, x, y - 1);  // Top
                    if (y < height - 1)
                        AddNeighborCount(plateArray, counts, width, height, x, y + 1);  // Bottom

                    // Diagonal neighbors
                    if (y > 0)
                    {
                        AddNeighborCount(plateArray, counts, width, height, x - 1, y - 1);  // Top-left
                        AddNeighborCount(plateArray, counts, width, height, x + 1, y - 1);  // Top-right
                    }
                    if (y < height - 1)
                    {
                        AddNeighborCount(plateArray, counts, width, height, x - 1, y + 1);  // Bottom-left
                        AddNeighborCount(plateArray, counts, width, height, x + 1, y + 1);  // Bottom-right
                    }

                    // Keep current plate if it has at least 3 same neighbors (out of up to 8)
                    if (counts.TryGetValue(current, out var sameCount) && sameCount >= 3)
                        continue;

                    // Replace with most common neighbor
                    var replacement = counts.OrderByDescending(kv => kv.Value).First().Key;
                    if (replacement != current)
                        changes.Add((index, replacement));
                }
            }

            foreach (var change in changes)
                plateArray[change.Index] = change.Plate;
        }
    }

    private static void AddNeighborCount(short[] plateArray, Dictionary<short, int> counts, int width, int height, int x, int y)
    {
        // Wrap X coordinate
        if (x < 0)
            x = width - 1;
        else if (x >= width)
            x = 0;

        // Y doesn't wrap
        if (y < 0 || y >= height)
            return;

        var plate = plateArray[y * width + x];
        counts[plate] = counts.GetValueOrDefault(plate) + 1;
    }

    private static void ValidateAndFixPlateGeometry(MapMask mask, short[] plateArray, TectonicPlateGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;
        var minPlateSize = Math.Max(options.MinPlateSize, (int)Math.Ceiling(width * height * options.MinPlateSizeRatio));
        var maxHoleSize = Math.Max(1, Math.Min(minPlateSize, Math.Max(10, minPlateSize / 4)));
        PlateGeometryIssues? issues = null;

        for (var cycle = 0; cycle < options.MaxValidationCycles; cycle++)
        {
            issues = DetectPlateGeometryIssues(plateArray, width, height, minPlateSize, maxHoleSize);
            if (issues.IsEmpty)
                return;

            if (issues.Holes.Count > 0)
                FillHoles(plateArray, issues.Holes);

            if (issues.FragmentGroups.Count > 0)
                MergeFragments(plateArray, issues.FragmentGroups, width, height);

            if (issues.SmallPlates.Count > 0)
                RemoveSmallPlates(plateArray, issues.SmallPlates, width, height);

            SmoothPlateMap(mask, plateArray, 2);
        }

        issues = DetectPlateGeometryIssues(plateArray, width, height, minPlateSize, maxHoleSize);
        if (!issues.IsEmpty)
        {
            Trace.TraceWarning(
                "Tectonic plate geometry validation reached the cycle limit with {0} fragmented plates, {1} holes, and {2} small plates remaining.",
                issues.FragmentGroups.Count,
                issues.Holes.Count,
                issues.SmallPlates.Count);
        }
    }

    private static PlateGeometryIssues DetectPlateGeometryIssues(short[] plateArray, int width, int height, int minPlateSize, int maxHoleSize)
    {
        var plateIds = plateArray.Distinct().ToArray();
        var fragmentGroups = new List<PlateFragmentGroup>();
        var holes = new List<PlateHole>();
        var smallPlates = new List<short>();

        foreach (var plateId in plateIds)
        {
            var fragments = FindPlateFragments(plateArray, plateId, width, height);
            if (!IsPlateConnected(fragments))
                fragmentGroups.Add(new PlateFragmentGroup(plateId, fragments));

            var totalSize = fragments.Sum(fragment => fragment.Size);
            if (totalSize < minPlateSize)
                smallPlates.Add(plateId);

            holes.AddRange(FindHoles(plateArray, plateId, width, height, maxHoleSize));
        }

        return new PlateGeometryIssues(fragmentGroups, holes, smallPlates);
    }

    private static bool IsPlateConnected(short[] plateArray, short plateId, int width, int height)
    {
        return FindPlateFragments(plateArray, plateId, width, height).Count <= 1;
    }

    private static bool IsPlateConnected(IReadOnlyList<PlateFragment> fragments) => fragments.Count <= 1;

    private static List<PlateFragment> FindPlateFragments(short[] plateArray, short plateId, int width, int height)
    {
        var visited = new bool[plateArray.Length];
        var fragments = new List<PlateFragment>();
        var queue = new Queue<int>();

        for (var index = 0; index < plateArray.Length; index++)
        {
            if (visited[index] || plateArray[index] != plateId)
                continue;

            var pixels = new List<int>();
            visited[index] = true;
            queue.Enqueue(index);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                pixels.Add(current);

                foreach (var neighbor in GetNeighborIndices4(current, width, height))
                {
                    if (visited[neighbor] || plateArray[neighbor] != plateId)
                        continue;

                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            fragments.Add(new PlateFragment(plateId, pixels.Count, pixels));
        }

        return fragments;
    }

    private static List<PlateHole> FindHoles(short[] plateArray, short plateId, int width, int height, int maxHoleSize)
    {
        var visited = new bool[plateArray.Length];
        var holes = new List<PlateHole>();
        var queue = new Queue<int>();

        for (var index = 0; index < plateArray.Length; index++)
        {
            if (visited[index] || plateArray[index] == plateId)
                continue;

            var pixels = new List<int>();
            var touchesOpenEdge = false;
            var surroundedByPlate = true;
            visited[index] = true;
            queue.Enqueue(index);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                pixels.Add(current);
                var y = current / width;
                if (y == 0 || y == height - 1)
                    touchesOpenEdge = true;

                foreach (var neighbor in GetNeighborIndices8(current, width, height))
                {
                    if (plateArray[neighbor] == plateId)
                        continue;

                    if (visited[neighbor])
                        continue;

                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            if (pixels.Count > maxHoleSize)
                continue;

            var pixelSet = pixels.ToHashSet();
            foreach (var pixel in pixels)
            {
                foreach (var neighbor in GetNeighborIndices8(pixel, width, height))
                {
                    if (plateArray[neighbor] != plateId && !pixelSet.Contains(neighbor))
                    {
                        surroundedByPlate = false;
                        break;
                    }
                }

                if (!surroundedByPlate)
                    break;
            }

            if (!touchesOpenEdge && surroundedByPlate)
                holes.Add(new PlateHole(plateId, pixels.Count, pixels));
        }

        return holes;
    }

    private static void FillHoles(short[] plateArray, IReadOnlyList<PlateHole> holes)
    {
        foreach (var hole in holes)
        {
            foreach (var pixel in hole.PixelIndices)
                plateArray[pixel] = hole.SurroundingPlateId;
        }
    }

    private static void MergeFragments(short[] plateArray, IReadOnlyList<PlateFragmentGroup> fragmentGroups, int width, int height)
    {
        foreach (var group in fragmentGroups)
        {
            var largest = group.Fragments.OrderByDescending(fragment => fragment.Size).First();

            foreach (var fragment in group.Fragments)
            {
                if (ReferenceEquals(fragment, largest))
                    continue;

                var replacement = FindBestNeighborPlate(plateArray, fragment.PixelIndices, group.PlateId, width, height);
                if (replacement is null)
                    continue;

                foreach (var pixel in fragment.PixelIndices)
                    plateArray[pixel] = replacement.Value;
            }
        }
    }

    private static void RemoveSmallPlates(short[] plateArray, IReadOnlyList<short> smallPlates, int width, int height)
    {
        foreach (var plateId in smallPlates)
        {
            var pixels = new List<int>();
            for (var index = 0; index < plateArray.Length; index++)
            {
                if (plateArray[index] == plateId)
                    pixels.Add(index);
            }

            if (pixels.Count == 0)
                continue;

            var replacement = FindBestNeighborPlate(plateArray, pixels, plateId, width, height);
            if (replacement is null)
                continue;

            foreach (var pixel in pixels)
                plateArray[pixel] = replacement.Value;
        }
    }

    private static short? FindBestNeighborPlate(short[] plateArray, IReadOnlyList<int> pixels, short excludedPlateId, int width, int height)
    {
        var counts = new Dictionary<short, int>();

        foreach (var pixel in pixels)
        {
            foreach (var neighbor in GetNeighborIndices8(pixel, width, height))
            {
                var neighborPlate = plateArray[neighbor];
                if (neighborPlate == excludedPlateId)
                    continue;

                counts[neighborPlate] = counts.GetValueOrDefault(neighborPlate) + 1;
            }
        }

        return counts.Count == 0 ? null : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    private static IEnumerable<int> GetNeighborIndices4(int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;

        yield return y * width + WrapX(x - 1, width);
        yield return y * width + WrapX(x + 1, width);

        if (y > 0)
            yield return (y - 1) * width + x;

        if (y < height - 1)
            yield return (y + 1) * width + x;
    }

    private static IEnumerable<int> GetNeighborIndices8(int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;

        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                yield return ny * width + WrapX(x + dx, width);
            }
        }
    }

    private IReadOnlyList<TectonicPlate> BuildPlates(
        MapMask mask,
        byte[] crustArray,
        short[] plateArray,
        IReadOnlyList<PlateSeed> seeds,
        MapGenerationOptions options)
    {
        var width = mask.Width;
        var height = mask.Height;

        // Create arrays to accumulate statistics for each plate
        var maxPlateId = seeds.Max(s => s.Id.Value);
        var pointCounts = new int[maxPlateId + 1];
        var continentalCounts = new int[maxPlateId + 1];

        // For wrapped centroid calculation, use circular mean for X coordinate
        var sumSinX = new double[maxPlateId + 1];
        var sumCosX = new double[maxPlateId + 1];
        var sumY = new long[maxPlateId + 1];

        // Single pass over the raster to compute statistics
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var plateId = plateArray[index];
                var crust = (CrustKind)crustArray[index];

                pointCounts[plateId]++;
                if (crust == CrustKind.Continental)
                    continentalCounts[plateId]++;

                // Circular mean for X to handle wrap-around
                var angle = 2.0 * Math.PI * x / width;
                sumSinX[plateId] += Math.Sin(angle);
                sumCosX[plateId] += Math.Cos(angle);

                sumY[plateId] += y;
            }
        }

        var plates = new List<TectonicPlate>(seeds.Count);

        foreach (var seed in seeds)
        {
            var plateId = seed.Id.Value;
            var count = pointCounts[plateId];

            if (count == 0)
                continue;

            var continentalRatio = continentalCounts[plateId] / (double)count;
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

            // Calculate centroid with wrap-around support for X
            var meanAngle = Math.Atan2(sumSinX[plateId] / count, sumCosX[plateId] / count);
            var centroidX = (int)Math.Round((meanAngle / (2.0 * Math.PI) * width + width) % width);
            var centroidY = (int)(sumY[plateId] / count);
            var centroid = new GridPoint(centroidX, centroidY);

            plates.Add(new TectonicPlate(
                seed.Id,
                kind,
                count,
                centroid,
                seed.Motion,
                seed.Activity * options.TectonicPlates.Activity,
                density,
                thickness));
        }

        return plates;
    }

    private static IReadOnlyList<PlateBoundary> BuildBoundaries(
        MapMask mask,
        byte[] crustArray,
        short[] plateArray,
        IReadOnlyList<TectonicPlate> plates)
    {
        var width = mask.Width;
        var height = mask.Height;
        var plateLookup = plates.ToDictionary(p => p.Id);
        var samplesByPair = new Dictionary<PlatePair, List<BoundarySample>>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var point = new GridPoint(x, y);
                var plate = new TectonicPlateId(plateArray[index]);
                var crust = (CrustKind)crustArray[index];

                // Check right neighbor (wrapped)
                var rightX = x == width - 1 ? 0 : x + 1;
                var rightIndex = y * width + rightX;
                var rightPoint = new GridPoint(rightX, y);
                var rightPlate = new TectonicPlateId(plateArray[rightIndex]);
                var rightCrust = (CrustKind)crustArray[rightIndex];

                if (plate != rightPlate)
                {
                    var pair = PlatePair.Create(plate, rightPlate);
                    if (!samplesByPair.TryGetValue(pair, out var samples))
                    {
                        samples = [];
                        samplesByPair[pair] = samples;
                    }
                    samples.Add(new BoundarySample(point, rightPoint, plate, rightPlate, crust, rightCrust));
                }

                // Check bottom neighbor
                if (y < height - 1)
                {
                    var bottomIndex = (y + 1) * width + x;
                    var bottomPoint = new GridPoint(x, y + 1);
                    var bottomPlate = new TectonicPlateId(plateArray[bottomIndex]);
                    var bottomCrust = (CrustKind)crustArray[bottomIndex];

                    if (plate != bottomPlate)
                    {
                        var pair = PlatePair.Create(plate, bottomPlate);
                        if (!samplesByPair.TryGetValue(pair, out var samples))
                        {
                            samples = [];
                            samplesByPair[pair] = samples;
                        }
                        samples.Add(new BoundarySample(point, bottomPoint, plate, bottomPlate, crust, bottomCrust));
                    }
                }
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

    private sealed record PlateGeometryIssues(
        IReadOnlyList<PlateFragmentGroup> FragmentGroups,
        IReadOnlyList<PlateHole> Holes,
        IReadOnlyList<short> SmallPlates)
    {
        public bool IsEmpty => FragmentGroups.Count == 0 && Holes.Count == 0 && SmallPlates.Count == 0;
    }

    private sealed record PlateFragmentGroup(short PlateId, IReadOnlyList<PlateFragment> Fragments);

    private sealed record PlateFragment(short PlateId, int Size, IReadOnlyList<int> PixelIndices);

    private sealed record PlateHole(short SurroundingPlateId, int Size, IReadOnlyList<int> PixelIndices);

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
