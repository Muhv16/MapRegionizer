using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class MajorTributaryInjector
{
    internal static void AddMajorRiverTributaryCells(
        MapMask mask,
        WaterBodyTopology topology,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        HashSet<int> allowedRiverBasins,
        int[] lakeIds,
        byte[] riverCells,
        IReadOnlyDictionary<int, IReadOnlyList<int>> forcedLongPaths,
        HydrologyGenerationOptions options)
    {
        if (options.RiverDensity <= 0 || options.MajorRiverTributaryMultiplier <= 0 || forcedLongPaths.Count == 0)
            return;

        var width = mask.Width;
        var height = mask.Height;
        var upstream = BuildUpstreamLists(flowDirections, width, height);
        var upstreamDepths = BuildLongestUpstreamDepths(flowDirections, upstream, lakeIds, mask, topology, width, height);

        foreach (var path in forcedLongPaths.Values.OrderByDescending(p => p.Count))
        {
            if (path.Count < 18)
                continue;

            var mainstem = path.ToHashSet();
            var reservedMouths = new HashSet<int>();
            var targetCount = Math.Clamp((int)Math.Round(path.Count / 14.0 * options.MajorRiverTributaryMultiplier * Math.Max(0.2, options.RiverDensity)), 1, 22);
            var minAnchorGap = Math.Max(5, path.Count / Math.Max(2, targetCount + 1));
            var added = 0;

            foreach (var anchor in path
                         .Skip(Math.Max(4, path.Count / 10))
                         .Take(Math.Max(0, path.Count - Math.Max(8, path.Count / 5)))
                         .OrderByDescending(i => upstreamDepths[i])
                         .ThenByDescending(i => accumulation[i]))
            {
                if (added >= targetCount)
                    break;
                if (reservedMouths.Any(m => Math.Abs(PathIndexOf(path, m) - PathIndexOf(path, anchor)) < minAnchorGap))
                    continue;

                var tributaryCandidates = upstream[anchor]
                    .Where(i => !mainstem.Contains(i))
                    .Where(i => riverCells[i] == 0 && lakeIds[i] <= 0)
                    .Where(i => IsRenderableRiverLand(new GridPoint(i % width, i / width), mask, topology, lakeIds))
                    .Select(i => BuildLongestTributaryPath(i, upstream, upstreamDepths, accumulation, mainstem, riverCells, lakeIds, mask, topology, width))
                    .Where(p => p.Count >= 4)
                    .ToList();
                var tributary = SelectMajorRiverTributaryPath(tributaryCandidates, accumulation, anchor, width);

                if (tributary is null)
                    continue;
                if (tributary.Any(index => !allowedRiverBasins.Contains(basinIds[index])))
                    continue;

                foreach (var index in tributary)
                    riverCells[index] = 1;

                reservedMouths.Add(anchor);
                added++;
            }
        }
    }

    internal static int PathIndexOf(IReadOnlyList<int> path, int value)
    {
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i] == value)
                return i;
        }

        return -1;
    }

    internal static List<int>? SelectMajorRiverTributaryPath(
        IReadOnlyList<List<int>> candidates,
        double[] accumulation,
        int anchor,
        int width)
    {
        if (candidates.Count == 0)
            return null;

        var maxLength = candidates.Max(p => p.Count);
        var lengthPreference = HashUnit(anchor % width, anchor / width, 7883);
        var desiredLength = 4.0 + Math.Pow(lengthPreference, 1.65) * Math.Max(0, maxLength - 4);
        return candidates
            .OrderBy(p => Math.Abs(p.Count - desiredLength))
            .ThenByDescending(p => p.Count >= 8)
            .ThenByDescending(p => accumulation[p[0]])
            .First();
    }

    internal static List<int> BuildLongestTributaryPath(
        int mouthIndex,
        List<int>[] upstream,
        int[] upstreamDepths,
        double[] accumulation,
        HashSet<int> blockedMainstem,
        byte[] riverCells,
        int[] lakeIds,
        MapMask mask,
        WaterBodyTopology topology,
        int width)
    {
        var path = new List<int>();
        var current = mouthIndex;
        var seen = new HashSet<int>();
        while (current >= 0 && current < upstream.Length && seen.Add(current))
        {
            if (blockedMainstem.Contains(current) || lakeIds[current] > 0)
                break;

            var point = new GridPoint(current % width, current / width);
            if (!IsRenderableRiverLand(point, mask, topology, lakeIds))
                break;

            path.Add(current);
            var next = upstream[current]
                .Where(i => !blockedMainstem.Contains(i))
                .Where(i => riverCells[i] == 0 && lakeIds[i] <= 0)
                .Where(i =>
                {
                    var upstreamPoint = new GridPoint(i % width, i / width);
                    return IsRenderableRiverLand(upstreamPoint, mask, topology, lakeIds);
                })
                .OrderByDescending(i => upstreamDepths[i])
                .ThenByDescending(i => accumulation[i])
                .FirstOrDefault(-1);

            if (next < 0)
                break;
            current = next;
        }

        return path;
    }

}
