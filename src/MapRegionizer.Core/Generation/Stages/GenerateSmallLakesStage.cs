using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateSmallLakesStage : IMapGenerationStage
{
    private const int StageSeed = 0x4D524C4B;

    public string Id => MapStageIds.GenerateSmallLakes;

    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.Mask,
        MapDataKeys.WaterBodyTopology,
        MapDataKeys.BaseTerrain
    };

    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.GeneratedLakes };

    public void Execute(MapGenerationContext context)
    {
        var baseTerrain = context.BaseTerrain ?? throw new InvalidOperationException("Base terrain is required.");
        var waterBodyTopology = context.WaterBodyTopology ?? throw new InvalidOperationException("Water body topology is required.");

        if (!context.Options.Elevation.GenerateSmallLakes)
        {
            context.GeneratedLakes = GeneratedLakeMap.Empty(context.Mask.Width, context.Mask.Height);
            return;
        }

        var generator = new SmallLakeGenerator(CreateSmallLakeSeed(context));
        context.GeneratedLakes = generator.Generate(context.Mask, baseTerrain, waterBodyTopology, context.Options);
    }

    private static int CreateSmallLakeSeed(MapGenerationContext context)
    {
        if (!context.Options.Seed.HasValue)
            return context.Random.Next();

        unchecked
        {
            return context.Options.Seed.Value * 397 ^ StageSeed;
        }
    }

    private sealed class SmallLakeGenerator
    {
        private readonly Random _random;

        public SmallLakeGenerator(int seed)
        {
            _random = new Random(seed);
        }

        public GeneratedLakeMap Generate(
            MapMask mask,
            ElevationMap terrain,
            WaterBodyTopology topology,
            MapGenerationOptions options)
        {
            var width = mask.Width;
            var height = mask.Height;
            var length = width * height;
            var wrapX = options.ProjectionMode == MapProjectionMode.EquirectangularWorld;
            var elevationRange = ComputeElevationRange(mask, terrain);
            var countMultiplier = options.Elevation.SmallLakeCountMultiplier;
            var scatterMultiplier = options.Elevation.SmallLakeScatterMultiplier;
            var sizeMultiplier = options.Elevation.SmallLakeSizeMultiplier;
            if (elevationRange <= 0.0001 || countMultiplier <= 0)
                return GeneratedLakeMap.Empty(width, height);

            var waterDistance = ComputeDistanceToWater(mask, topology, wrapX);
            var existingLakeInfluence = BuildExistingLakeInfluence(mask, topology, wrapX);
            var eligible = new bool[length];
            var scores = new double[length];
            var localRelief = new double[length];
            var localReliefLimit = Math.Max(18.0, elevationRange * 0.026);
            var landCellCount = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var point = new GridPoint(x, y);
                    var index = y * width + x;
                    if (!mask.IsLand(point))
                        continue;

                    landCellCount++;
                    if (waterDistance[index] < 3)
                        continue;

                    var elevation = terrain.GetElevation(point);
                    if (elevation >= options.Elevation.MountainLakeElevationMeters * 0.92)
                        continue;

                    var relief3 = LocalRelief(terrain, point, 1, wrapX);
                    var relief5 = LocalRelief(terrain, point, 2, wrapX);
                    localRelief[index] = relief5;
                    if (relief5 > localReliefLimit || !IsLocalMinimum(terrain, point, wrapX, relief3 * 0.20 + 0.75))
                        continue;

                    var roughness = terrain.GetRoughness(point);
                    var ridge = terrain.GetRidgeContinuity(point);
                    var foothill = terrain.GetFoothillInfluence(point);
                    if (roughness > 0.58 || ridge > 0.48 || foothill > 0.56)
                        continue;

                    var lowland = Math.Clamp(1.0 - elevation / Math.Max(1.0, options.Elevation.MountainLakeElevationMeters), 0, 1);
                    var flatness = Math.Clamp(1.0 - relief5 / Math.Max(1.0, localReliefLimit), 0, 1);
                    var basin = Math.Clamp(terrain.GetBasinInfluence(point) * 0.55, 0, 0.35);
                    var satellite = existingLakeInfluence[index] * 0.35;
                    scores[index] = lowland * 0.38 + flatness * 0.42 + basin + satellite + _random.NextDouble() * 0.08;
                    eligible[index] = scores[index] >= 0.46;
                }
            }

            var components = FindComponents(eligible, width, height, wrapX);
            if (components.Count == 0)
                return GeneratedLakeMap.Empty(width, height);

            var occupied = BuildOccupied(mask, topology);
            var bodies = new List<GeneratedLakeBody>();
            var nextId = topology.Bodies.Select(b => b.Id.Value).DefaultIfEmpty(0).Max() + 1;
            var scatterBudget = Math.Clamp((int)Math.Round(landCellCount / 3600.0 * countMultiplier * scatterMultiplier), 0, 64);
            var maxGeneratedBodies = Math.Clamp((int)Math.Round(length / 1600.0 * countMultiplier) + scatterBudget, 0, 160);

            foreach (var component in components.OrderByDescending(c => c.Count))
            {
                if (bodies.Count >= maxGeneratedBodies)
                    break;
                if (component.Count < 28)
                    continue;

                PlaceClusterLakes(mask, terrain, options.Elevation, component, eligible, scores, localRelief, occupied, bodies, ref nextId, maxGeneratedBodies, countMultiplier, sizeMultiplier, wrapX);
                PlaceSolitaryLakes(mask, terrain, options.Elevation, component, eligible, scores, localRelief, occupied, bodies, ref nextId, maxGeneratedBodies, countMultiplier, sizeMultiplier, wrapX);
            }

            PlaceScatteredLakes(mask, terrain, options.Elevation, eligible, scores, localRelief, occupied, bodies, ref nextId, maxGeneratedBodies, scatterBudget, sizeMultiplier, wrapX);

            return new GeneratedLakeMap(width, height, bodies);
        }

        private void PlaceClusterLakes(
            MapMask mask,
            ElevationMap terrain,
            ElevationGenerationOptions options,
            IReadOnlyList<GridPoint> component,
            bool[] eligible,
            double[] scores,
            double[] localRelief,
            bool[] occupied,
            List<GeneratedLakeBody> bodies,
            ref int nextId,
            int maxBodies,
            double countMultiplier,
            double sizeMultiplier,
            bool wrapX)
        {
            if (component.Count < 120 || bodies.Count >= maxBodies)
                return;

            var clusterBudget = Math.Clamp((int)Math.Round(component.Count / 320.0 * countMultiplier), 1, 8);
            var clusterAreaBudget = Math.Max(5, (int)Math.Round(component.Count * Lerp(0.05, 0.16, _random.NextDouble()) * sizeMultiplier));
            var usedClusterArea = 0;
            var anchors = component
                .OrderByDescending(p => scores[p.Y * mask.Width + p.X])
                .Take(Math.Min(component.Count, 24))
                .OrderBy(_ => _random.NextDouble())
                .Take(clusterBudget)
                .ToList();

            foreach (var anchor in anchors)
            {
                var lakesAroundAnchor = _random.Next(1, 4);
                for (var i = 0; i < lakesAroundAnchor && bodies.Count < maxBodies && usedClusterArea < clusterAreaBudget; i++)
                {
                    var jitterX = anchor.X + _random.Next(-6, 7);
                    if (wrapX)
                        jitterX = WrapX(jitterX, mask.Width);
                    else
                        jitterX = Math.Clamp(jitterX, 0, mask.Width - 1);

                    var jitter = new GridPoint(jitterX, Math.Clamp(anchor.Y + _random.Next(-6, 7), 0, mask.Height - 1));
                    if (!eligible[jitter.Y * mask.Width + jitter.X])
                        jitter = anchor;

                    var body = TryCreateLake(mask, terrain, options, component.Count, jitter, eligible, scores, localRelief, occupied, isCluster: true, nextId, sizeMultiplier, wrapX);
                    if (body is null)
                        continue;

                    bodies.Add(body);
                    nextId++;
                    usedClusterArea += body.Cells.Count;
                    MarkOccupied(mask, occupied, body.Cells, padding: 1, wrapX);
                }
            }
        }

        private void PlaceSolitaryLakes(
            MapMask mask,
            ElevationMap terrain,
            ElevationGenerationOptions options,
            IReadOnlyList<GridPoint> component,
            bool[] eligible,
            double[] scores,
            double[] localRelief,
            bool[] occupied,
            List<GeneratedLakeBody> bodies,
            ref int nextId,
            int maxBodies,
            double countMultiplier,
            double sizeMultiplier,
            bool wrapX)
        {
            var targetCount = Math.Clamp((int)Math.Round(component.Count / 1400.0 * countMultiplier), 0, 8);
            if (component.Count >= 80 && _random.NextDouble() < Math.Clamp(component.Count / 1000.0, 0.10, 0.65))
                targetCount += Math.Max(1, (int)Math.Round(countMultiplier));

            foreach (var anchor in component.OrderByDescending(p => scores[p.Y * mask.Width + p.X]).Take(Math.Min(component.Count, 36)))
            {
                if (targetCount <= 0 || bodies.Count >= maxBodies)
                    break;
                if (_random.NextDouble() > scores[anchor.Y * mask.Width + anchor.X])
                    continue;

                var body = TryCreateLake(mask, terrain, options, component.Count, anchor, eligible, scores, localRelief, occupied, isCluster: false, nextId, sizeMultiplier, wrapX);
                if (body is null)
                    continue;

                bodies.Add(body);
                nextId++;
                targetCount--;
                MarkOccupied(mask, occupied, body.Cells, padding: 3, wrapX);
            }
        }

        private void PlaceScatteredLakes(
            MapMask mask,
            ElevationMap terrain,
            ElevationGenerationOptions options,
            bool[] eligible,
            double[] scores,
            double[] localRelief,
            bool[] occupied,
            List<GeneratedLakeBody> bodies,
            ref int nextId,
            int maxBodies,
            int targetCount,
            double sizeMultiplier,
            bool wrapX)
        {
            if (targetCount <= 0 || bodies.Count >= maxBodies)
                return;

            var candidateLimit = Math.Min(eligible.Length, Math.Max(96, targetCount * 24));
            var anchors = Enumerable.Range(0, eligible.Length)
                .Where(i => eligible[i] && !occupied[i] && scores[i] >= 0.50)
                .OrderByDescending(i => scores[i] + _random.NextDouble() * 0.12)
                .Take(candidateLimit)
                .OrderBy(_ => _random.NextDouble())
                .ToList();

            foreach (var index in anchors)
            {
                if (targetCount <= 0 || bodies.Count >= maxBodies)
                    break;
                if (occupied[index])
                    continue;

                var score = scores[index];
                var chance = Math.Clamp((score - 0.42) / 0.50, 0.12, 0.86);
                if (_random.NextDouble() > chance)
                    continue;

                var anchor = new GridPoint(index % mask.Width, index / mask.Width);
                var body = TryCreateLake(mask, terrain, options, 260, anchor, eligible, scores, localRelief, occupied, isCluster: false, nextId, sizeMultiplier, wrapX);
                if (body is null)
                    continue;

                bodies.Add(body);
                nextId++;
                targetCount--;
                MarkOccupied(mask, occupied, body.Cells, padding: 5, wrapX);
            }
        }

        private GeneratedLakeBody? TryCreateLake(
            MapMask mask,
            ElevationMap terrain,
            ElevationGenerationOptions options,
            int componentArea,
            GridPoint center,
            bool[] eligible,
            double[] scores,
            double[] localRelief,
            bool[] occupied,
            bool isCluster,
            int id,
            double sizeMultiplier,
            bool wrapX)
        {
            var centerIndex = center.Y * mask.Width + center.X;
            if (!eligible[centerIndex] || occupied[centerIndex])
                return null;

            var maxArea = Math.Max(3.0, componentArea * Lerp(0.010, 0.030, _random.NextDouble()) * sizeMultiplier);
            var radiusScale = Math.Sqrt(sizeMultiplier);
            var maxRadius = Math.Clamp(Math.Sqrt(maxArea / Math.PI), 1.2, (isCluster ? 3.8 : 5.2) * radiusScale);
            var radiusX = Math.Max(1.2, maxRadius * Lerp(0.78, 1.42, _random.NextDouble()));
            var radiusY = Math.Max(1.2, maxRadius * Lerp(0.72, 1.34, _random.NextDouble()));
            var angle = _random.NextDouble() * Math.PI;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var cells = new List<GridPoint>();
            var scanRadius = (int)Math.Ceiling(Math.Max(radiusX, radiusY) + 1.5);

            for (var dy = -scanRadius; dy <= scanRadius; dy++)
            {
                var y = center.Y + dy;
                if (y < 0 || y >= mask.Height)
                    continue;

                for (var dx = -scanRadius; dx <= scanRadius; dx++)
                {
                    var x = center.X + dx;
                    if (wrapX)
                        x = WrapX(x, mask.Width);
                    else if (x < 0 || x >= mask.Width)
                        continue;

                    var point = new GridPoint(x, y);
                    var index = y * mask.Width + x;
                    if (!eligible[index] || occupied[index])
                        continue;

                    var rx = dx * cos + dy * sin;
                    var ry = -dx * sin + dy * cos;
                    var normalized = (rx * rx) / (radiusX * radiusX) + (ry * ry) / (radiusY * radiusY);
                    var edgeNoise = 0.82 + SmoothNoise(point.X, point.Y, id * 31 + 19, 3.8) * 0.34;
                    if (normalized <= edgeNoise && scores[index] >= 0.40)
                        cells.Add(point);
                }
            }

            var areaLimit = Math.Max(3, (int)Math.Round(componentArea * 0.03 * sizeMultiplier));
            if (cells.Count > areaLimit)
            {
                cells = cells
                    .OrderBy(p => Distance(center, p, mask.Width, wrapX))
                    .Take(areaLimit)
                    .ToList();
            }

            if (cells.Count < 3 || !IsConnected(cells, mask.Width, mask.Height, wrapX))
                return null;

            var relief = Math.Max(localRelief[centerIndex], cells.Max(p => terrain.GetElevation(p)) - cells.Min(p => terrain.GetElevation(p)));
            var capRatio = Lerp(0.05, 0.15, _random.NextDouble());
            var depthCap = Math.Clamp(relief * capRatio, options.MinLakeDepthMeters, Math.Min(options.MaxLakeDepthMeters, 18.0));
            var centroid = ComputeCentroid(cells);
            return new GeneratedLakeBody(new WaterBodyId(id), cells, centroid, isCluster, relief, depthCap);
        }

        private static double ComputeElevationRange(MapMask mask, ElevationMap terrain)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var point = new GridPoint(x, y);
                    if (!mask.IsLand(point))
                        continue;

                    var elevation = terrain.GetElevation(point);
                    min = Math.Min(min, elevation);
                    max = Math.Max(max, elevation);
                }
            }

            return max - min;
        }

        private static bool IsLocalMinimum(ElevationMap terrain, GridPoint point, bool wrapX, double tolerance)
        {
            var value = terrain.GetElevation(point);
            foreach (var neighbor in Neighbors8(point, terrain.Width, terrain.Height, wrapX))
            {
                if (terrain.GetElevation(neighbor) + tolerance < value)
                    return false;
            }

            return true;
        }

        private static double LocalRelief(ElevationMap terrain, GridPoint center, int radius, bool wrapX)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var dy = -radius; dy <= radius; dy++)
            {
                var y = center.Y + dy;
                if (y < 0 || y >= terrain.Height)
                    continue;

                for (var dx = -radius; dx <= radius; dx++)
                {
                    var x = center.X + dx;
                    if (wrapX)
                        x = WrapX(x, terrain.Width);
                    else if (x < 0 || x >= terrain.Width)
                        continue;

                    var value = terrain.GetElevation(x, y);
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            return max - min;
        }

        private static double[] BuildExistingLakeInfluence(MapMask mask, WaterBodyTopology topology, bool wrapX)
        {
            var influence = new double[mask.Width * mask.Height];
            var sums = new Dictionary<int, (double X, double Y, int Count)>();
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var id = topology.GetWaterBodyId(x, y);
                    if (!id.HasValue)
                        continue;

                    var classification = topology.GetClassification(id.Value);
                    if (classification?.Kind is not (WaterBodyKind.InlandLake or WaterBodyKind.InlandSea))
                        continue;

                    var current = sums.GetValueOrDefault(id.Value.Value);
                    sums[id.Value.Value] = (current.X + x, current.Y + y, current.Count + 1);
                }
            }

            foreach (var (idValue, sum) in sums)
            {
                var classification = topology.GetClassification(new WaterBodyId(idValue));
                if (classification is null || classification.CellCount < 24)
                    continue;

                var center = new GridPoint((int)Math.Round(sum.X / sum.Count), (int)Math.Round(sum.Y / sum.Count));
                var diameter = Math.Max(2.0, Math.Sqrt(classification.CellCount / Math.PI) * 2.0);
                var minRadius = diameter * 0.8;
                var maxRadius = diameter * 4.2;

                for (var y = 0; y < mask.Height; y++)
                {
                    for (var x = 0; x < mask.Width; x++)
                    {
                        var point = new GridPoint(x, y);
                        if (!mask.IsLand(point))
                            continue;

                        var distance = Distance(center, point, mask.Width, wrapX);
                        if (distance < minRadius || distance > maxRadius)
                            continue;

                        var t = 1.0 - Math.Abs(distance - diameter * 2.1) / Math.Max(1.0, maxRadius - minRadius);
                        var index = y * mask.Width + x;
                        influence[index] = Math.Max(influence[index], Math.Clamp(t, 0, 1));
                    }
                }
            }

            return influence;
        }

        private static double[] ComputeDistanceToWater(MapMask mask, WaterBodyTopology topology, bool wrapX)
        {
            var length = mask.Width * mask.Height;
            var distances = Enumerable.Repeat(double.PositiveInfinity, length).ToArray();
            var queue = new Queue<GridPoint>();

            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var point = new GridPoint(x, y);
                    if (mask.IsLand(point) && topology.GetWaterBodyId(point) is null)
                        continue;

                    distances[y * mask.Width + x] = 0;
                    queue.Enqueue(point);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var nextDistance = distances[current.Y * mask.Width + current.X] + 1;
                foreach (var neighbor in Neighbors4(current, mask.Width, mask.Height, wrapX))
                {
                    var index = neighbor.Y * mask.Width + neighbor.X;
                    if (nextDistance >= distances[index])
                        continue;

                    distances[index] = nextDistance;
                    queue.Enqueue(neighbor);
                }
            }

            return distances;
        }

        private static List<IReadOnlyList<GridPoint>> FindComponents(bool[] eligible, int width, int height, bool wrapX)
        {
            var visited = new bool[eligible.Length];
            var components = new List<IReadOnlyList<GridPoint>>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var start = new GridPoint(x, y);
                    var startIndex = y * width + x;
                    if (!eligible[startIndex] || visited[startIndex])
                        continue;

                    var queue = new Queue<GridPoint>();
                    var component = new List<GridPoint>();
                    visited[startIndex] = true;
                    queue.Enqueue(start);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        component.Add(current);
                        foreach (var neighbor in Neighbors4(current, width, height, wrapX))
                        {
                            var index = neighbor.Y * width + neighbor.X;
                            if (!eligible[index] || visited[index])
                                continue;

                            visited[index] = true;
                            queue.Enqueue(neighbor);
                        }
                    }

                    components.Add(component);
                }
            }

            return components;
        }

        private static bool[] BuildOccupied(MapMask mask, WaterBodyTopology topology)
        {
            var occupied = new bool[mask.Width * mask.Height];
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    if (!mask.IsLand(new GridPoint(x, y)) || topology.GetWaterBodyId(x, y).HasValue)
                        occupied[y * mask.Width + x] = true;
                }
            }

            return occupied;
        }

        private static void MarkOccupied(MapMask mask, bool[] occupied, IReadOnlyList<GridPoint> cells, int padding, bool wrapX)
        {
            foreach (var cell in cells)
            {
                foreach (var point in PointsInRadius(mask.Width, mask.Height, cell, padding, wrapX))
                    occupied[point.Y * mask.Width + point.X] = true;
            }
        }

        private static bool IsConnected(IReadOnlyList<GridPoint> cells, int width, int height, bool wrapX)
        {
            var set = cells.ToHashSet();
            var visited = new HashSet<GridPoint>();
            var queue = new Queue<GridPoint>();
            queue.Enqueue(cells[0]);
            visited.Add(cells[0]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in Neighbors4(current, width, height, wrapX))
                {
                    if (!set.Contains(neighbor) || !visited.Add(neighbor))
                        continue;

                    queue.Enqueue(neighbor);
                }
            }

            return visited.Count == cells.Count;
        }

        private static GridPoint ComputeCentroid(IReadOnlyList<GridPoint> cells)
        {
            var x = (int)Math.Round(cells.Average(p => p.X));
            var y = (int)Math.Round(cells.Average(p => p.Y));
            return new GridPoint(x, y);
        }

        private static IEnumerable<GridPoint> PointsInRadius(int width, int height, GridPoint center, int radius, bool wrapX)
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

                    var x = center.X + dx;
                    if (wrapX)
                        x = WrapX(x, width);
                    else if (x < 0 || x >= width)
                        continue;

                    yield return new GridPoint(x, y);
                }
            }
        }

        private static IEnumerable<GridPoint> Neighbors4(GridPoint point, int width, int height, bool wrapX)
        {
            if (point.X > 0)
                yield return new GridPoint(point.X - 1, point.Y);
            else if (wrapX)
                yield return new GridPoint(width - 1, point.Y);

            if (point.X < width - 1)
                yield return new GridPoint(point.X + 1, point.Y);
            else if (wrapX)
                yield return new GridPoint(0, point.Y);

            if (point.Y > 0) yield return new GridPoint(point.X, point.Y - 1);
            if (point.Y < height - 1) yield return new GridPoint(point.X, point.Y + 1);
        }

        private static IEnumerable<GridPoint> Neighbors8(GridPoint point, int width, int height, bool wrapX)
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

                    var x = point.X + dx;
                    if (wrapX)
                        x = WrapX(x, width);
                    else if (x < 0 || x >= width)
                        continue;

                    yield return new GridPoint(x, y);
                }
            }
        }

        private static double Distance(GridPoint a, GridPoint b, int width, bool wrapX)
        {
            var dx = Math.Abs(a.X - b.X);
            if (wrapX)
                dx = Math.Min(dx, Math.Max(0, width - dx));

            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
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

        private static double Hash01(int x, int y, int seed)
        {
            unchecked
            {
                var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
                value = (value << 13) ^ value;
                return 1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
            }
        }

        private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static int WrapX(int x, int width) => (x % width + width) % width;
    }
}
