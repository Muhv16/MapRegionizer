using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class RiverRendering
{
    internal static RiverWidthScale BuildRiverWidthScale(IReadOnlyList<RiverSegment> rivers, RiverRenderOptions options)
    {
        if (rivers.Count == 0)
            return new RiverWidthScale(0, 1);

        var sorted = rivers.Select(r => r.Discharge).Order().ToList();
        var low = PercentileSorted(sorted, options.WidthLowPercentile);
        var high = PercentileSorted(sorted, options.WidthHighPercentile);
        if (high <= low + 0.0001)
            high = sorted[^1] + 1.0;

        return new RiverWidthScale(low, high);
    }

    internal static float GetRiverWidth(RiverSegment river, RiverRenderOptions options, RiverWidthScale scale)
    {
        var normalized = Math.Clamp((river.Discharge - scale.Low) / Math.Max(0.0001, scale.High - scale.Low), 0, 1);
        normalized = Math.Pow(normalized, options.WidthGamma);
        var rank = river.VisibleRank > 0 ? river.VisibleRank : normalized;
        var orderFactor = river.Order switch
        {
            <= 1 => 0.58,
            2 => 0.74,
            _ => 1.0
        };
        var majorFactor = river.IsMajor ? 1.0 : 0.72;
        var width = options.MinRiverWidth + (options.MaxRiverWidth - options.MinRiverWidth) * Math.Max(normalized, rank * 0.82);
        var strokeScale = GetRiverStrokeScale(options.Scale);
        return (float)Math.Clamp(width * orderFactor * majorFactor * strokeScale, options.MinRiverWidth * 0.55 * strokeScale, options.MaxRiverWidth * strokeScale);
    }

    internal static double GetRiverStrokeScale(float scale)
    {
        if (scale <= 0)
            return 1.0;

        return scale <= 1 ? scale : Math.Sqrt(scale);
    }

    internal static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    internal static IReadOnlyList<PointF[]> BuildRiverRenderRuns(IReadOnlyList<MapPoint> polyline, int mapWidth, double pixelSize, float scale)
    {
        var runs = new List<PointF[]>();
        var current = new List<MapPoint>();
        foreach (var point in polyline)
        {
            if (current.Count > 0 && Math.Abs(current[^1].X - point.X) > mapWidth / 2.0)
            {
                AddSmoothedRun(current, runs, pixelSize, scale);
                current.Clear();
            }

            current.Add(point);
        }

        AddSmoothedRun(current, runs, pixelSize, scale);
        return runs;
    }

    internal static void AddSmoothedRun(IReadOnlyList<MapPoint> run, List<PointF[]> runs, double pixelSize, float scale)
    {
        if (run.Count < 2)
            return;

        if (run.Count == 2)
        {
            runs.Add(run.Select(p => RenderingGeometry.ToPixelPoint(p, pixelSize, scale)).ToArray());
            return;
        }

        var smoothedRun = PrepareRiverRunForRendering(run, scale);
        if (smoothedRun.Count == 2)
        {
            runs.Add(smoothedRun.Select(p => RenderingGeometry.ToPixelPoint(p, pixelSize, scale)).ToArray());
            return;
        }

        var points = new List<PointF> { RenderingGeometry.ToPixelPoint(smoothedRun[0], pixelSize, scale) };
        for (var index = 0; index < smoothedRun.Count - 1; index++)
        {
            var p1 = smoothedRun[index];
            var p2 = smoothedRun[index + 1];
            var p0 = index == 0 ? ReflectPoint(p2, p1) : smoothedRun[index - 1];
            var p3 = index + 2 >= smoothedRun.Count ? ReflectPoint(p1, p2) : smoothedRun[index + 2];
            var distance = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
            var samples = Math.Clamp((int)Math.Ceiling(distance * pixelSize * scale * 0.35), 2, 6);
            for (var sample = 1; sample <= samples; sample++)
            {
                var t = sample / (double)samples;
                points.Add(RenderingGeometry.ToPixelPoint(CatmullRomCentripetal(p0, p1, p2, p3, t), pixelSize, scale));
            }
        }

        runs.Add(points.ToArray());
    }

    internal static IReadOnlyList<MapPoint> PrepareRiverRunForRendering(IReadOnlyList<MapPoint> run, float scale)
    {
        if (run.Count <= 2)
            return run;

        var tolerance = GetRiverSimplificationTolerance(scale);
        var simplified = tolerance > 0
            ? SimplifyRiverRun(run, tolerance)
            : run.ToList();

        if (simplified.Count <= 2)
            return simplified;

        var smoothingStrength = GetRiverSmoothingStrength(scale);
        if (smoothingStrength <= 0)
            return simplified;

        var smoothed = new List<MapPoint>(simplified.Count) { simplified[0] };
        for (var i = 1; i < simplified.Count - 1; i++)
        {
            var previous = simplified[i - 1];
            var current = simplified[i];
            var next = simplified[i + 1];
            var target = new MapPoint(
                previous.X * 0.25 + current.X * 0.5 + next.X * 0.25,
                previous.Y * 0.25 + current.Y * 0.5 + next.Y * 0.25);
            smoothed.Add(Lerp(current, target, smoothingStrength));
        }

        smoothed.Add(simplified[^1]);
        return smoothed;
    }

    internal static double GetRiverSimplificationTolerance(float scale)
    {
        if (scale <= 1)
            return 0.02;

        var densityFactor = GetRiverMeanderDensityFactor(scale);
        return Math.Clamp(0.10 + (1.0 - densityFactor) * 1.05, 0.10, 0.95);
    }

    internal static double GetRiverSmoothingStrength(float scale)
    {
        if (scale <= 1)
            return 0.0;

        var densityFactor = GetRiverMeanderDensityFactor(scale);
        return Math.Clamp((1.0 - densityFactor) * 0.56, 0.0, 0.46);
    }

    internal static double GetRiverMeanderDensityFactor(float scale)
    {
        if (scale <= 1)
            return 1.0;

        var density = 1.0 / (1.0 + Math.Log2(scale) * 0.52);
        return Math.Clamp(density, 0.20, 1.0);
    }

    internal static List<MapPoint> SimplifyRiverRun(IReadOnlyList<MapPoint> run, double tolerance)
    {
        var keep = new bool[run.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRiverRun(run, 0, run.Count - 1, tolerance * tolerance, keep);

        var simplified = new List<MapPoint>();
        for (var i = 0; i < run.Count; i++)
        {
            if (keep[i])
                simplified.Add(run[i]);
        }

        return simplified.Count >= 2 ? simplified : [run[0], run[^1]];
    }

    internal static void SimplifyRiverRun(IReadOnlyList<MapPoint> run, int first, int last, double toleranceSquared, bool[] keep)
    {
        if (last <= first + 1)
            return;

        var maxDistanceSquared = 0.0;
        var farthestIndex = -1;
        for (var i = first + 1; i < last; i++)
        {
            var distanceSquared = DistanceToSegmentSquared(run[i], run[first], run[last]);
            if (distanceSquared <= maxDistanceSquared)
                continue;

            maxDistanceSquared = distanceSquared;
            farthestIndex = i;
        }

        if (farthestIndex < 0 || maxDistanceSquared <= toleranceSquared)
            return;

        keep[farthestIndex] = true;
        SimplifyRiverRun(run, first, farthestIndex, toleranceSquared, keep);
        SimplifyRiverRun(run, farthestIndex, last, toleranceSquared, keep);
    }

    internal static double DistanceToSegmentSquared(MapPoint point, MapPoint start, MapPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.0000001)
            return DistanceSquared(point, start);

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var projected = new MapPoint(start.X + dx * t, start.Y + dy * t);
        return DistanceSquared(point, projected);
    }

    internal static double DistanceSquared(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    internal static MapPoint Lerp(MapPoint from, MapPoint to, double amount) =>
        new(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);

    internal static MapPoint ReflectPoint(MapPoint point, MapPoint around) =>
        new(around.X * 2.0 - point.X, around.Y * 2.0 - point.Y);

    internal static MapPoint CatmullRomCentripetal(MapPoint p0, MapPoint p1, MapPoint p2, MapPoint p3, double t)
    {
        var t0 = 0.0;
        var t1 = GetCatmullRomKnot(t0, p0, p1);
        var t2 = GetCatmullRomKnot(t1, p1, p2);
        var t3 = GetCatmullRomKnot(t2, p2, p3);
        var target = t1 + (t2 - t1) * t;

        var a1 = InterpolateByKnot(p0, p1, t0, t1, target);
        var a2 = InterpolateByKnot(p1, p2, t1, t2, target);
        var a3 = InterpolateByKnot(p2, p3, t2, t3, target);
        var b1 = InterpolateByKnot(a1, a2, t0, t2, target);
        var b2 = InterpolateByKnot(a2, a3, t1, t3, target);
        return InterpolateByKnot(b1, b2, t1, t2, target);
    }

    internal static double GetCatmullRomKnot(double previous, MapPoint a, MapPoint b) =>
        previous + Math.Pow(Math.Max(0.000001, DistanceSquared(a, b)), 0.25);

    internal static MapPoint InterpolateByKnot(MapPoint a, MapPoint b, double knotA, double knotB, double target)
    {
        var span = knotB - knotA;
        if (Math.Abs(span) <= 0.000001)
            return a;

        var weightA = (knotB - target) / span;
        var weightB = (target - knotA) / span;
        return new MapPoint(a.X * weightA + b.X * weightB, a.Y * weightA + b.Y * weightB);
    }
}
