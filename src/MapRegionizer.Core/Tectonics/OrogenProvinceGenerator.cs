using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

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
