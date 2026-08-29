using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

internal static class WorldPlateDomainSampler
{
    public static PlateDomainMap Sample(
        MapMask mask,
        CrustFieldMap crustFields,
        TectonicWorldContext world,
        TectonicPlateGenerationOptions options)
    {
        var plates = new short[mask.Width * mask.Height];
        var counts = new Dictionary<int, int>();
        var sumX = new Dictionary<int, long>();
        var sumY = new Dictionary<int, long>();
        var oceanicAgeSums = new Dictionary<int, double>();
        var oceanicAgeCounts = new Dictionary<int, int>();

        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var id = world.SamplePlateId(mask.OriginX + x, mask.OriginY + y).Value;
                var index = y * mask.Width + x;
                plates[index] = checked((short)id);
                counts[id] = counts.GetValueOrDefault(id) + 1;
                sumX[id] = sumX.GetValueOrDefault(id) + x;
                sumY[id] = sumY.GetValueOrDefault(id) + y;
                var crust = crustFields.GetCrust(x, y);
                if (crust is CrustKind.Oceanic or CrustKind.Arc)
                {
                    var age = crustFields.GetOceanicAge(x, y);
                    if (!double.IsNaN(age))
                    {
                        oceanicAgeSums[id] = oceanicAgeSums.GetValueOrDefault(id) + age;
                        oceanicAgeCounts[id] = oceanicAgeCounts.GetValueOrDefault(id) + 1;
                    }
                }
            }
        }

        var domains = new List<PlateDomain>();
        foreach (var worldPlate in world.Plates)
        {
            if (!counts.TryGetValue(worldPlate.Id.Value, out var count))
                continue;

            var meanAge = oceanicAgeCounts.GetValueOrDefault(worldPlate.Id.Value) == 0
                ? worldPlate.MeanOceanicAge
                : oceanicAgeSums[worldPlate.Id.Value] / oceanicAgeCounts[worldPlate.Id.Value];
            domains.Add(new PlateDomain(
                worldPlate.Id,
                worldPlate.Kind,
                count,
                new GridPoint((int)Math.Round(sumX[worldPlate.Id.Value] / (double)count), (int)Math.Round(sumY[worldPlate.Id.Value] / (double)count)),
                worldPlate.Motion,
                worldPlate.Activity * options.Activity,
                worldPlate.Density,
                worldPlate.Thickness,
                meanAge,
                worldPlate.IsMicroplate));
        }

        return new PlateDomainMap(mask.Width, mask.Height, plates, domains);
    }
}
