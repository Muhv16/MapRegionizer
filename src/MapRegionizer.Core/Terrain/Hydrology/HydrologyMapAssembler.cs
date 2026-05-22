using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.HydrologyGridMath;
using static MapRegionizer.Core.Terrain.HydrologyTerrainRules;
using static MapRegionizer.Core.Terrain.HydrologyRenderRules;
using static MapRegionizer.Core.Terrain.FlowAccumulationSolver;
using static MapRegionizer.Core.Terrain.FlowDirectionSolver;

namespace MapRegionizer.Core.Terrain;

internal sealed class HydrologyMapAssembler
{
    internal static double[] BuildHydroSurface(MapMask mask, ElevationMap elevation, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
    {
        var hydro = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (mask.IsLand(point) && !generatedLakes.Contains(point))
                {
                    hydro[index] = elevation.GetBedElevation(point);
                    continue;
                }

                if (!mask.IsLand(point) && topology.IsOceanicWater(point))
                {
                    hydro[index] = 0.0;
                    continue;
                }

                hydro[index] = elevation.HasWaterSurface(point)
                    ? elevation.GetWaterSurface(point)
                    : Math.Max(0.0, elevation.GetElevation(point));
            }
        }

        return hydro;
    }

    internal static int[] BuildLakeIdRaster(MapMask mask, WaterBodyTopology topology, GeneratedLakeMap generatedLakes)
    {
        var lakeIds = new int[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (generatedLakes.GetLakeId(point) is { } generatedId)
                {
                    lakeIds[index] = generatedId.Value;
                    continue;
                }

                var id = topology.GetWaterBodyId(point);
                if (!id.HasValue)
                    continue;

                var kind = topology.GetKind(point);
                if (kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea)
                    lakeIds[index] = id.Value.Value;
            }
        }

        return lakeIds;
    }

    internal static LandComponentMap BuildLandComponents(MapMask mask, GeneratedLakeMap generatedLakes)
    {
        var width = mask.Width;
        var height = mask.Height;
        var componentIds = new int[width * height];
        var components = new List<LandComponent>();
        var nextId = 1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = new GridPoint(x, y);
                var startIndex = y * width + x;
                if (!IsRiverSourceLand(mask, generatedLakes, start) || componentIds[startIndex] != 0)
                    continue;

                var id = nextId++;
                var queue = new Queue<GridPoint>();
                var cells = new List<GridPoint>();
                componentIds[startIndex] = id;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cells.Add(current);
                    foreach (var neighbor in Neighbors8(current, width, height))
                    {
                        var index = neighbor.Y * width + neighbor.X;
                        if (componentIds[index] != 0 || !IsRiverSourceLand(mask, generatedLakes, neighbor))
                            continue;

                        componentIds[index] = id;
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(new LandComponent(id, cells.Count));
            }
        }

        return new LandComponentMap(componentIds, components);
    }

    internal static byte[] BuildRiverCellRaster(int width, int height, IReadOnlyList<RiverSegment> rivers)
    {
        var riverCells = new byte[width * height];
        foreach (var river in rivers)
        {
            foreach (var cell in river.Cells)
            {
                if (cell.Y < 0 || cell.Y >= height)
                    continue;

                riverCells[cell.Y * width + WrapX(cell.X, width)] = 1;
            }
        }

        return riverCells;
    }

    internal static HydrologyMap Create(
        HydrologyGenerationContext context,
        double[] hydroSurface,
        int[] flowDirections,
        double[] accumulation,
        int[] basinIds,
        byte[] riverCells,
        IReadOnlyList<RiverSegment> rivers,
        IReadOnlyList<RiverMouth> mouths,
        IReadOnlyList<LakeOutlet> outlets,
        IReadOnlyList<DrainageBasin> basins) =>
        new(
            context.Width,
            context.Height,
            hydroSurface,
            flowDirections,
            accumulation,
            basinIds,
            riverCells,
            rivers,
            mouths,
            outlets,
            basins);
}
