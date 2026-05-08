using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

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
