using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Tectonics;

internal sealed class RiftProvinceGenerator
{
    public RiftProvinceMap Generate(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, TectonicBoundaryMap boundaries)
    {
        var length = mask.Width * mask.Height;
        var influence = new double[length];
        var axis = new double[length];
        var graben = new double[length];
        var shoulders = new double[length];
        var heatFlow = new double[length];
        var breakup = new double[length];
        var support = BuildBoundarySupport(mask, boundaries);
        var provinces = new List<RiftProvince>();
        var nextId = 1;

        foreach (var candidate in CreateCandidates(history, boundaries))
        {
            var ordered = OrderAxisPoints(candidate.Points, mask.Width, candidate.IsHistorical);
            if (ordered.Count < 4)
                continue;

            foreach (var run in ValidateCandidate(mask, history, crustFields, support, candidate, ordered))
            {
                var degradation = AnalyzeLinearity(mask, candidate, run.Points, run.Kind);
                var segments = BuildEnEchelonSegments(mask, candidate, run.Points, run.BaseWidth, run.Activity, run.Kind, degradation);
                if (segments.Count == 0)
                    continue;

                var province = new RiftProvince(
                    nextId++,
                    run.Kind,
                    run.Points,
                    segments,
                    candidate.Age,
                    Math.Clamp(run.Activity * degradation.StrengthScale, 0.08, 1.25),
                    run.MeanScore,
                    run.BaseWidth * degradation.WidthScale,
                    candidate.SourceLineamentId,
                    candidate.SourceBoundarySegmentId);

                provinces.Add(province);
                StampProvince(mask, province, influence, axis, graben, shoulders, heatFlow, breakup);
            }
        }

        return new RiftProvinceMap(mask.Width, mask.Height, provinces, influence, axis, graben, shoulders, heatFlow, breakup);
    }

    private static IEnumerable<CandidateAxis> CreateCandidates(TectonicHistory history, TectonicBoundaryMap boundaries)
    {
        foreach (var segment in boundaries.Segments.Where(s => IsExtensionalMode(s.BoundaryMode)))
            yield return new CandidateAxis(segment.Points, 0, Math.Clamp(segment.Activity, 0.15, 1.35), null, segment.Id, false, segment.BoundaryMode);

        foreach (var lineament in history.Lineaments.Where(l => l.Kind == TectonicFeatureKind.Rift))
            yield return new CandidateAxis(lineament.Points, lineament.Age, Math.Clamp(lineament.Intensity * 0.55, 0.08, 0.65), lineament.Id, null, true, BoundaryMode.ContinentalRift);
    }

    private static IEnumerable<ValidatedRiftRun> ValidateCandidate(
        MapMask mask,
        TectonicHistory history,
        CrustFieldMap crustFields,
        BoundarySupportMap support,
        CandidateAxis candidate,
        IReadOnlyList<GridPoint> points)
    {
        var width = mask.Width;
        var supportRadius = SupportRadius(mask);
        var minRunLength = Math.Max(4, (int)Math.Round(width * 0.014));
        var chunkLength = Math.Clamp((int)Math.Round(width * (0.045 + Hash01(candidate.Seed, points[0].Y, 3101) * 0.07)), minRunLength, Math.Max(minRunLength + 1, (int)Math.Round(width * 0.13)));
        var threshold = candidate.IsHistorical ? 0.10 : 0.15;
        var start = 0;

        while (start < points.Count)
        {
            var end = Math.Min(points.Count, start + chunkLength);
            var run = new List<ScoredPoint>();

            for (var i = start; i < end; i++)
            {
                var point = points[i];
                var score = ScorePoint(mask, history, crustFields, support, point, candidate, supportRadius, out var boundaryDistance);
                var noiseGate = SmoothNoise(point.X, point.Y, candidate.Seed + 3121, Math.Max(8.0, width * 0.045));
                var valid = score >= threshold && noiseGate > (candidate.IsHistorical ? 0.16 : 0.12);

                if (candidate.IsHistorical && boundaryDistance > supportRadius * 1.15 && !HasYoungRiftMemory(crustFields, point))
                    valid = false;

                if (valid)
                {
                    run.Add(new ScoredPoint(point, score, boundaryDistance));
                    continue;
                }

                foreach (var finished in FinishRun(mask, candidate, run, minRunLength, supportRadius))
                    yield return finished;
                run.Clear();
            }

            foreach (var finished in FinishRun(mask, candidate, run, minRunLength, supportRadius))
                yield return finished;

            var gap = Math.Clamp((int)Math.Round(width * (0.006 + Hash01(points[start].X, points[start].Y, candidate.Seed + 3137) * 0.024)), 1, Math.Max(2, width / 32));
            start = end + gap;
        }
    }

    private static IEnumerable<ValidatedRiftRun> FinishRun(MapMask mask, CandidateAxis candidate, List<ScoredPoint> run, int minRunLength, double supportRadius)
    {
        if (run.Count < minRunLength)
            yield break;

        var first = run[0].Point;
        var breakupChance = candidate.IsHistorical ? 0.28 : 0.14;
        if (Hash01(first.X, first.Y, candidate.Seed + 3163) < breakupChance)
            yield break;

        var meanScore = run.Average(p => p.Score);
        var meanDistance = run.Average(p => p.BoundaryDistance);
        if (candidate.IsHistorical && meanDistance > supportRadius * 0.9)
            meanScore *= 0.62;

        var activity = Math.Clamp(candidate.Activity * (0.66 + meanScore * 0.82), 0.10, 1.25);
        var kind = candidate.Mode == BoundaryMode.BackArcSpreading
            ? RiftProvinceKind.BackArcExtension
            : candidate.Mode == BoundaryMode.Transtension
                ? RiftProvinceKind.DiffuseExtension
                : RiftProvinceKind.ContinentalRift;
        var widthFactor = kind == RiftProvinceKind.BackArcExtension ? 1.9 : kind == RiftProvinceKind.DiffuseExtension ? 1.35 : 1.0;
        var baseWidth = Math.Clamp(
            mask.Width * (0.012 + meanScore * 0.018) * widthFactor * (0.72 + activity * 0.45),
            kind == RiftProvinceKind.BackArcExtension ? 4.0 : 2.5,
            kind == RiftProvinceKind.BackArcExtension ? Math.Max(8.0, mask.Width * 0.085) : Math.Max(5.0, mask.Width * 0.055));

        yield return new ValidatedRiftRun(run.Select(p => p.Point).ToArray(), kind, meanScore, activity, baseWidth);
    }

    private static LineDegradation AnalyzeLinearity(MapMask mask, CandidateAxis candidate, IReadOnlyList<GridPoint> points, RiftProvinceKind kind)
    {
        if (points.Count < 8)
            return LineDegradation.None;

        var pathLength = 0.0;
        var longSteps = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var step = Math.Sqrt(WrappedDistanceSquared(points[i - 1], points[i], mask.Width));
            pathLength += step;
            if (step > Math.Max(3.0, mask.Width * 0.012))
                longSteps++;
        }

        if (pathLength <= 0)
            return LineDegradation.None;

        var start = points[0];
        var end = points[^1];
        var endDx = WrappedDeltaX(end.X - start.X, mask.Width);
        var endDy = end.Y - start.Y;
        var endpointDistance = Math.Sqrt(endDx * endDx + endDy * endDy);
        var componentLength = Math.Max(1.0, endpointDistance);
        var straightness = Math.Clamp(endpointDistance / pathLength, 0, 1);
        var ux = endDx / componentLength;
        var uy = endDy / componentLength;
        var residualSum = 0.0;

        foreach (var point in points)
        {
            var dx = WrappedDeltaX(point.X - start.X, mask.Width);
            var dy = point.Y - start.Y;
            residualSum += Math.Abs(dx * uy - dy * ux);
        }

        var thinness = residualSum / points.Count / componentLength;
        var turnCount = 0;
        var turnSamples = 0;
        for (var i = 2; i < points.Count - 2; i += 2)
        {
            var a = Normalize(new GridVector(WrappedDeltaX(points[i].X - points[i - 2].X, mask.Width), points[i].Y - points[i - 2].Y));
            var b = Normalize(new GridVector(WrappedDeltaX(points[i + 2].X - points[i].X, mask.Width), points[i + 2].Y - points[i].Y));
            var dot = Math.Clamp(a.X * b.X + a.Y * b.Y, -1, 1);
            var angle = Math.Acos(dot) * 180.0 / Math.PI;
            if (angle > 14.0)
                turnCount++;
            turnSamples++;
        }

        var turnRatio = turnSamples == 0 ? 0.0 : turnCount / (double)turnSamples;
        var longEnough = endpointDistance >= Math.Max(12.0, mask.Width * 0.025);
        var breakPenalty = Math.Clamp(longSteps / (double)Math.Max(1, points.Count), 0, 0.35);
        var isLineLike = longEnough &&
                         straightness >= 0.68 &&
                         thinness <= 0.16 &&
                         turnRatio <= 0.50;
        if (!isLineLike)
            return LineDegradation.None;

        var severity = Math.Clamp(
            (straightness - 0.68) / 0.32 * 0.48 +
            (0.16 - thinness) / 0.16 * 0.34 +
            (0.50 - turnRatio) / 0.50 * 0.18 -
            breakPenalty * 0.25,
            0.35,
            1.0);
        var backArc = kind == RiftProvinceKind.BackArcExtension || candidate.Mode == BoundaryMode.BackArcSpreading;
        return new LineDegradation(
            true,
            Lerp(backArc ? 0.42 : 0.48, backArc ? 0.60 : 0.68, 1.0 - severity),
            Lerp(1.7, 3.0, severity),
            Lerp(backArc ? 1.55 : 1.22, backArc ? 2.35 : 1.80, severity),
            Lerp(1.65, 3.25, severity),
            Lerp(backArc ? 0.48 : 0.58, backArc ? 0.70 : 0.78, 1.0 - severity),
            backArc ? Lerp(0.04, 0.12, severity) : Lerp(0.18, 0.34, severity),
            Lerp(0.22, 0.45, severity));
    }

    private static double ScorePoint(
        MapMask mask,
        TectonicHistory history,
        CrustFieldMap crustFields,
        BoundarySupportMap support,
        GridPoint point,
        CandidateAxis candidate,
        double supportRadius,
        out double boundaryDistance)
    {
        var index = point.Y * mask.Width + point.X;
        var crust = crustFields.GetCrust(point);
        var coastal = crustFields.GetCoastalZone(point);
        boundaryDistance = support.Distance[index];
        var mode = support.Mode[index] == BoundaryMode.MixedSegmentBoundary ? candidate.Mode : support.Mode[index];
        var boundaryProximity = Math.Clamp(1.0 - boundaryDistance / Math.Max(1.0, supportRadius), 0, 1);
        var boundarySupport = candidate.IsHistorical
            ? Math.Clamp(boundaryProximity * 0.72 + RiftMemoryScore(crustFields, point) * 0.45, 0.05, 1.0)
            : Math.Clamp(0.22 + boundaryProximity * 0.92, 0.05, 1.0);
        var extensionalMotion = ExtensionalMotionScore(mode) * Math.Clamp(support.Activity[index] > 0 ? support.Activity[index] : candidate.Activity, 0.12, 1.35);
        var crustSuitability = CrustSuitability(crust);
        var context = LandOrShelfContext(mask, crust, coastal, point, mode);
        var nonCraton = NonCratonPreference(mask, history, crustFields, point, boundaryDistance, supportRadius);
        var basin = BasinAffinity(crust, coastal, crustFields.GetLastRiftingAge(point));
        var noise = SmoothNoise(point.X - 37, point.Y + 53, candidate.Seed + 3181, Math.Max(10.0, mask.Width * 0.07));
        var noiseGate = noise < 0.20 ? 0.20 + noise * 1.15 : 0.58 + noise * 0.52;

        return Math.Clamp(
            extensionalMotion *
            crustSuitability *
            boundarySupport *
            context *
            nonCraton *
            basin *
            noiseGate,
            0,
            1.5);
    }

    private static IReadOnlyList<RiftProvinceSegment> BuildEnEchelonSegments(
        MapMask mask,
        CandidateAxis candidate,
        IReadOnlyList<GridPoint> points,
        double baseWidth,
        double activity,
        RiftProvinceKind kind,
        LineDegradation degradation)
    {
        var result = new List<RiftProvinceSegment>();
        var i = 0;
        var minStep = Math.Max(3, (int)Math.Round(mask.Width * 0.012));

        while (i < points.Count)
        {
            var point = points[Math.Clamp(i + (int)Math.Round(minStep * 0.5), 0, points.Count - 1)];
            var tangent = Tangent(points, Math.Clamp(i, 0, points.Count - 1), mask.Width);
            var normal = new GridVector(-tangent.Y, tangent.X);
            var length = kind == RiftProvinceKind.BackArcExtension
                ? mask.Width * (0.045 + Hash01(point.X, point.Y, candidate.Seed + 3203) * 0.075)
                : mask.Width * (0.025 + Hash01(point.X, point.Y, candidate.Seed + 3209) * 0.055);
            length *= degradation.LengthScale;
            var width = baseWidth * degradation.WidthScale * (0.72 + Hash01(point.X, point.Y, candidate.Seed + 3217) * (kind == RiftProvinceKind.BackArcExtension ? 1.55 : 1.05));
            var offset = (Hash01(point.X, point.Y, candidate.Seed + 3221) * 2.0 - 1.0) * width * (kind == RiftProvinceKind.BackArcExtension ? 1.25 : 0.8) * degradation.OffsetScale;
            var jitterDegrees = degradation.IsLineLike ? 84.0 : 50.0;
            var jitter = (Hash01(point.X, point.Y, candidate.Seed + 3229) * jitterDegrees - jitterDegrees * 0.5) * Math.PI / 180.0;
            var direction = Normalize(Rotate(tangent, jitter));
            var center = new GridPoint(WrapX((int)Math.Round(point.X + normal.X * offset), mask.Width), Math.Clamp((int)Math.Round(point.Y + normal.Y * offset), 0, mask.Height - 1));
            var strength = Math.Clamp(activity * degradation.StrengthScale * (0.64 + Hash01(point.X, point.Y, candidate.Seed + 3251) * 0.56), 0.06, 1.25);

            result.Add(new RiftProvinceSegment(center, direction, length, width, strength, false));

            var branchChance = (kind == RiftProvinceKind.BackArcExtension ? 0.12 : 0.15 + Hash01(point.X, point.Y, candidate.Seed + 3257) * 0.20) + degradation.BranchChanceBonus;
            if (Hash01(point.X, point.Y, candidate.Seed + 3259) < branchChance)
            {
                var side = Hash01(point.X, point.Y, candidate.Seed + 3263) < 0.5 ? -1.0 : 1.0;
                var branchDirection = Normalize(Rotate(direction, side * (0.58 + Hash01(point.X, point.Y, candidate.Seed + 3269) * 0.72)));
                var branchOffset = width * degradation.OffsetScale * (0.65 + Hash01(point.X, point.Y, candidate.Seed + 3271) * 0.75);
                var branchCenter = new GridPoint(WrapX((int)Math.Round(center.X + normal.X * branchOffset * side), mask.Width), Math.Clamp((int)Math.Round(center.Y + normal.Y * branchOffset * side), 0, mask.Height - 1));
                result.Add(new RiftProvinceSegment(branchCenter, branchDirection, length * 0.42, width * 0.58, strength * 0.48, true));
            }

            var gap = mask.Width * (0.005 + Hash01(point.X, point.Y, candidate.Seed + 3279) * 0.020) * degradation.GapScale;
            var advanceScale = degradation.IsLineLike ? 0.30 : 0.62;
            i += Math.Max(minStep, (int)Math.Round((length + gap) * advanceScale));
        }

        if (degradation.IsLineLike && result.Count == 1 && points.Count >= minStep * 3)
        {
            var point = points[Math.Clamp(points.Count * 2 / 3, 0, points.Count - 1)];
            var tangent = Tangent(points, Math.Clamp(points.Count * 2 / 3, 0, points.Count - 1), mask.Width);
            var normal = new GridVector(-tangent.Y, tangent.X);
            var side = Hash01(point.X, point.Y, candidate.Seed + 3283) < 0.5 ? -1.0 : 1.0;
            var width = baseWidth * degradation.WidthScale * 1.15;
            var center = new GridPoint(WrapX((int)Math.Round(point.X + normal.X * width * degradation.OffsetScale * side), mask.Width), Math.Clamp((int)Math.Round(point.Y + normal.Y * width * degradation.OffsetScale * side), 0, mask.Height - 1));
            result.Add(new RiftProvinceSegment(center, Normalize(Rotate(tangent, side * 0.62)), Math.Max(mask.Width * 0.018, result[0].Length * 0.55), width, result[0].Strength * 0.55, true));
        }

        return result;
    }

    private static void StampProvince(
        MapMask mask,
        RiftProvince province,
        double[] riftInfluence,
        double[] riftAxis,
        double[] grabenMask,
        double[] shoulderUpliftMask,
        double[] heatFlowMask,
        double[] breakupMask)
    {
        for (var i = 0; i < province.Segments.Count; i++)
        {
            var segment = province.Segments[i];
            StampSegment(mask, province.Id, province.Kind, segment, riftInfluence, riftAxis, grabenMask, shoulderUpliftMask, heatFlowMask, breakupMask);

            if (segment.IsFailedArm || i == 0)
                continue;

            var previous = province.Segments[i - 1];
            if (!previous.IsFailedArm)
                StampTransferZone(mask, province.Id, province.Kind, previous, segment, riftInfluence, heatFlowMask, breakupMask);
        }
    }

    private static void StampSegment(
        MapMask mask,
        int provinceId,
        RiftProvinceKind kind,
        RiftProvinceSegment segment,
        double[] riftInfluence,
        double[] riftAxis,
        double[] grabenMask,
        double[] shoulderUpliftMask,
        double[] heatFlowMask,
        double[] breakupMask)
    {
        var halfLength = Math.Max(1.0, segment.Length * 0.5);
        var radius = Math.Clamp((int)Math.Ceiling(Math.Max(halfLength, segment.Width * 2.5)), 2, Math.Max(4, (int)Math.Round(mask.Width * 0.11)));
        var normal = new GridVector(-segment.Direction.Y, segment.Direction.X);
        var heatWidth = segment.Width * (kind == RiftProvinceKind.BackArcExtension ? 3.0 : 2.0);
        var grabenWidth = segment.Width * (kind == RiftProvinceKind.BackArcExtension ? 0.62 : 0.34);
        var heatStrength = kind == RiftProvinceKind.BackArcExtension ? 0.72 : 0.92;
        var grabenStrength = kind == RiftProvinceKind.BackArcExtension ? 0.24 : segment.IsFailedArm ? 0.42 : 1.0;

        foreach (var point in PointsInRadius(mask.Width, mask.Height, segment.Center, radius))
        {
            var dx = WrappedDeltaX(point.X - segment.Center.X, mask.Width);
            var dy = point.Y - segment.Center.Y;
            var along = dx * segment.Direction.X + dy * segment.Direction.Y;
            var across = dx * normal.X + dy * normal.Y;
            var axial = SmoothFalloff(Math.Abs(along) / halfLength);
            var cross = SmoothFalloff(Math.Abs(across) / Math.Max(1.0, segment.Width));
            var broadCross = SmoothFalloff(Math.Abs(across) / Math.Max(1.0, heatWidth));
            if (axial <= 0 || broadCross <= 0)
                continue;

            var index = point.Y * mask.Width + point.X;
            var patchNoise = SmoothNoise(point.X, point.Y, provinceId * 79 + 3301, Math.Max(6.0, segment.Width * 1.8));
            var localGate = patchNoise < 0.18 ? 0.34 + patchNoise * 1.6 : 0.72 + patchNoise * 0.36;
            var influence = axial * cross * segment.Strength * localGate;
            var broadInfluence = axial * broadCross * segment.Strength * (0.70 + patchNoise * 0.35);
            var center = SmoothFalloff(Math.Abs(across) / Math.Max(1.0, grabenWidth));
            var shoulderPosition = Math.Abs(across) / Math.Max(1.0, segment.Width);
            var shoulder = SmoothFalloff(Math.Abs(shoulderPosition - 0.86) / 0.42) * axial * segment.Strength * (segment.IsFailedArm ? 0.45 : 1.0);

            riftInfluence[index] = Math.Max(riftInfluence[index], broadInfluence);
            grabenMask[index] = Math.Max(grabenMask[index], influence * center * grabenStrength);
            shoulderUpliftMask[index] = Math.Max(shoulderUpliftMask[index], shoulder * (kind == RiftProvinceKind.BackArcExtension ? 0.28 : 0.82));
            heatFlowMask[index] = Math.Max(heatFlowMask[index], broadInfluence * heatStrength);
            breakupMask[index] = Math.Max(breakupMask[index], broadInfluence * Math.Clamp(0.34 - patchNoise, 0, 0.34) / 0.34);

            if (Math.Abs(across) <= Math.Max(0.75, segment.Width * 0.12) && Math.Abs(along) <= halfLength)
                riftAxis[index] = Math.Max(riftAxis[index], axial * segment.Strength);
        }
    }

    private static void StampTransferZone(
        MapMask mask,
        int provinceId,
        RiftProvinceKind kind,
        RiftProvinceSegment first,
        RiftProvinceSegment second,
        double[] riftInfluence,
        double[] heatFlowMask,
        double[] breakupMask)
    {
        var dx = WrappedDeltaX(second.Center.X - first.Center.X, mask.Width);
        var dy = second.Center.Y - first.Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= 1.0 || distance > Math.Max(first.Length, second.Length) * 1.4)
            return;

        var direction = Normalize(new GridVector(dx, dy));
        var center = new GridPoint(WrapX((int)Math.Round(first.Center.X + dx * 0.5), mask.Width), Math.Clamp((int)Math.Round(first.Center.Y + dy * 0.5), 0, mask.Height - 1));
        var transferStrength = kind == RiftProvinceKind.BackArcExtension ? 0.18 : 0.30;
        var segment = new RiftProvinceSegment(center, direction, distance, Math.Max(1.5, Math.Min(first.Width, second.Width) * 0.55), Math.Min(first.Strength, second.Strength) * transferStrength, true);
        var halfLength = Math.Max(1.0, segment.Length * 0.5);
        var radius = Math.Clamp((int)Math.Ceiling(Math.Max(halfLength, segment.Width * 1.8)), 2, Math.Max(4, (int)Math.Round(mask.Width * 0.08)));
        var normal = new GridVector(-segment.Direction.Y, segment.Direction.X);

        foreach (var point in PointsInRadius(mask.Width, mask.Height, segment.Center, radius))
        {
            var localDx = WrappedDeltaX(point.X - segment.Center.X, mask.Width);
            var localDy = point.Y - segment.Center.Y;
            var along = localDx * segment.Direction.X + localDy * segment.Direction.Y;
            var across = localDx * normal.X + localDy * normal.Y;
            var influence = SmoothFalloff(Math.Abs(along) / halfLength) * SmoothFalloff(Math.Abs(across) / Math.Max(1.0, segment.Width)) * segment.Strength;
            if (influence <= 0)
                continue;

            var index = point.Y * mask.Width + point.X;
            var noise = SmoothNoise(point.X, point.Y, provinceId * 83 + 3319, 9.0);
            riftInfluence[index] = Math.Max(riftInfluence[index], influence * 0.38);
            heatFlowMask[index] = Math.Max(heatFlowMask[index], influence * 0.30);
            breakupMask[index] = Math.Max(breakupMask[index], influence * (0.42 + noise * 0.30));
        }
    }

    private static BoundarySupportMap BuildBoundarySupport(MapMask mask, TectonicBoundaryMap boundaries)
    {
        var length = mask.Width * mask.Height;
        var distance = Enumerable.Repeat(double.PositiveInfinity, length).ToArray();
        var activity = new double[length];
        var mode = Enumerable.Repeat(BoundaryMode.MixedSegmentBoundary, length).ToArray();
        var queue = new PriorityQueue<BoundaryQueueItem, double>();
        var maxDistance = SupportRadius(mask) * 1.35;

        foreach (var segment in boundaries.Segments.Where(s => IsExtensionalMode(s.BoundaryMode)))
        {
            foreach (var point in segment.Points)
            {
                var index = point.Y * mask.Width + point.X;
                if (distance[index] == 0 && activity[index] >= segment.Activity)
                    continue;

                distance[index] = 0;
                activity[index] = segment.Activity;
                mode[index] = segment.BoundaryMode;
                queue.Enqueue(new BoundaryQueueItem(point, 0, segment.BoundaryMode, segment.Activity), 0);
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
                mode[index] = current.Mode;
                queue.Enqueue(new BoundaryQueueItem(neighbor, nextDistance, current.Mode, current.Activity), nextDistance);
            }
        }

        return new BoundarySupportMap(distance, activity, mode);
    }

    private static IReadOnlyList<GridPoint> OrderAxisPoints(IReadOnlyList<GridPoint> points, int width, bool preserveOrder)
    {
        var distinct = points.Distinct().ToArray();
        if (preserveOrder || distinct.Length <= 2)
            return distinct;

        var remaining = distinct.ToHashSet();
        var start = distinct.OrderBy(p => NeighborCount(p, remaining, width)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
        var ordered = new List<GridPoint>(distinct.Length) { start };
        remaining.Remove(start);

        while (remaining.Count > 0)
        {
            var current = ordered[^1];
            var next = remaining.OrderBy(p => WrappedDistanceSquared(current, p, width)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    private static GridVector Tangent(IReadOnlyList<GridPoint> points, int index, int width)
    {
        var previous = points[Math.Max(0, index - 2)];
        var next = points[Math.Min(points.Count - 1, index + 2)];
        return Normalize(new GridVector(WrappedDeltaX(next.X - previous.X, width), next.Y - previous.Y));
    }

    private static bool HasYoungRiftMemory(CrustFieldMap crustFields, GridPoint point)
    {
        var age = crustFields.GetLastRiftingAge(point);
        return crustFields.GetCrust(point) == CrustKind.Rift || (!double.IsNaN(age) && age <= 220);
    }

    private static double RiftMemoryScore(CrustFieldMap crustFields, GridPoint point)
    {
        var age = crustFields.GetLastRiftingAge(point);
        if (crustFields.GetCrust(point) == CrustKind.Rift)
            return 0.85;
        if (double.IsNaN(age))
            return 0;

        return Math.Clamp(1.0 - age / 260.0, 0, 0.72);
    }

    private static double CrustSuitability(CrustKind crust) => crust switch
    {
        CrustKind.Continental => 1.0,
        CrustKind.Shelf => 0.82,
        CrustKind.Arc => 0.76,
        CrustKind.Terrane => 0.86,
        CrustKind.Rift => 0.94,
        CrustKind.Oceanic => 0.20,
        _ => 0.35
    };

    private static double LandOrShelfContext(MapMask mask, CrustKind crust, CoastalZoneKind coastal, GridPoint point, BoundaryMode mode)
    {
        if (mode == BoundaryMode.BackArcSpreading)
            return crust is CrustKind.Arc or CrustKind.Shelf or CrustKind.Oceanic ? 0.92 : 0.62;
        if (mask.IsLand(point))
            return coastal == CoastalZoneKind.ActiveMargin ? 0.70 : 1.0;
        if (crust == CrustKind.Shelf || coastal is CoastalZoneKind.Shelf or CoastalZoneKind.Slope or CoastalZoneKind.ShallowSea)
            return 0.74;

        return 0.22;
    }

    private static double NonCratonPreference(MapMask mask, TectonicHistory history, CrustFieldMap crustFields, GridPoint point, double boundaryDistance, double supportRadius)
    {
        var age = crustFields.GetContinentalAge(point);
        var ageScore = double.IsNaN(age) ? 0 : Math.Clamp((age - 1800.0) / 1600.0, 0, 1);
        var centerScore = 0.0;
        var radius = Math.Max(8.0, mask.Width * 0.13);
        foreach (var center in history.CratonCenters)
        {
            var distance = Math.Sqrt(WrappedDistanceSquared(point, center, mask.Width));
            centerScore = Math.Max(centerScore, Math.Clamp(1.0 - distance / radius, 0, 1));
        }

        var craton = Math.Max(ageScore, centerScore);
        var supportRelief = boundaryDistance <= supportRadius * 0.45 ? 0.40 : 0.0;
        return Math.Clamp(1.0 - craton * (0.72 - supportRelief), 0.16, 1.0);
    }

    private static double BasinAffinity(CrustKind crust, CoastalZoneKind coastal, double lastRiftingAge)
    {
        var affinity = crust switch
        {
            CrustKind.Rift => 1.0,
            CrustKind.Shelf => 0.82,
            CrustKind.Arc => 0.70,
            CrustKind.Terrane => 0.64,
            CrustKind.Continental => 0.58,
            CrustKind.Oceanic => 0.36,
            _ => 0.45
        };

        if (coastal is CoastalZoneKind.Shelf or CoastalZoneKind.Slope or CoastalZoneKind.PassiveMargin or CoastalZoneKind.ShallowSea)
            affinity += 0.16;
        if (!double.IsNaN(lastRiftingAge))
            affinity += Math.Clamp(1.0 - lastRiftingAge / 260.0, 0, 1) * 0.20;

        return Math.Clamp(affinity, 0.20, 1.15);
    }

    private static double ExtensionalMotionScore(BoundaryMode mode) => mode switch
    {
        BoundaryMode.ContinentalRift => 1.0,
        BoundaryMode.Transtension => 0.82,
        BoundaryMode.BackArcSpreading => 0.92,
        _ => 0.18
    };

    private static bool IsExtensionalMode(BoundaryMode mode) =>
        mode is BoundaryMode.ContinentalRift or BoundaryMode.Transtension or BoundaryMode.BackArcSpreading;

    private static double SupportRadius(MapMask mask) => Math.Clamp(mask.Width * 0.07, 6.0, 30.0);

    private static double SmoothFalloff(double normalizedDistance)
    {
        var value = Math.Clamp(1.0 - normalizedDistance, 0, 1);
        return value * value * (3.0 - 2.0 * value);
    }

    private static int NeighborCount(GridPoint point, IReadOnlySet<GridPoint> points, int width) =>
        Neighbors8(point, width, int.MaxValue).Count(points.Contains);

    private static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            value = (value << 13) ^ value;
            return Math.Clamp((1.0 - ((value * (value * value * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0 + 1.0) * 0.5, 0, 1);
        }
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

    private static GridVector Rotate(GridVector vector, double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new GridVector(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private static GridVector Normalize(GridVector vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return length <= 0.0001 ? new GridVector(1, 0) : new GridVector(vector.X / length, vector.Y / length);
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

    private static double WrappedDeltaX(double dx, int width)
    {
        if (Math.Abs(dx) <= width / 2.0)
            return dx;

        return dx > 0 ? dx - width : dx + width;
    }

    private static int WrapX(int x, int width) => (x % width + width) % width;

    private sealed record CandidateAxis(
        IReadOnlyList<GridPoint> Points,
        double Age,
        double Activity,
        int? SourceLineamentId,
        int? SourceBoundarySegmentId,
        bool IsHistorical,
        BoundaryMode Mode)
    {
        public int Seed => SourceBoundarySegmentId ?? SourceLineamentId ?? 0;
    }

    private sealed record ScoredPoint(GridPoint Point, double Score, double BoundaryDistance);

    private sealed record ValidatedRiftRun(IReadOnlyList<GridPoint> Points, RiftProvinceKind Kind, double MeanScore, double Activity, double BaseWidth);

    private sealed record LineDegradation(
        bool IsLineLike,
        double LengthScale,
        double GapScale,
        double OffsetScale,
        double WidthScale,
        double StrengthScale,
        double BranchChanceBonus,
        double TransferStrengthScale)
    {
        public static LineDegradation None { get; } = new(false, 1.0, 1.0, 1.0, 1.0, 1.0, 0.0, 1.0);
    }

    private sealed record BoundaryQueueItem(GridPoint Point, double Distance, BoundaryMode Mode, double Activity);

    private sealed record BoundarySupportMap(double[] Distance, double[] Activity, BoundaryMode[] Mode);
}
