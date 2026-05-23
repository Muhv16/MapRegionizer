using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using static MapRegionizer.Core.Terrain.ElevationGridMath;
using static MapRegionizer.Core.Terrain.ElevationNoise;
using static MapRegionizer.Core.Terrain.ElevationSignalMath;

namespace MapRegionizer.Core.Terrain;

internal sealed class TerrainClassifier
{
    public void Classify(ElevationRasterSet rasters, ElevationInput context, TectonicFields tectonicFields, BasinFields basinFields, MountainFields mountainFields)
    {
        ClassifyTerrain(
            context.Mask,
            context.CrustFields,
            rasters.Elevation,
            rasters.Roughness,
            context.DistanceToLand,
            context.DistanceToWater,
            context.LandEnclosure,
            context.ShelfWidth,
            tectonicFields.SedimentSupply,
            tectonicFields.HeatFlow,
            basinFields.BasinInfluence,
            mountainFields.FoothillInfluence,
            mountainFields.RidgeContinuity,
            tectonicFields.RidgeMask,
            tectonicFields.SubductionMask,
            tectonicFields.RiftProvince,
            tectonicFields.RiftGraben,
            rasters.TerrainClasses);
    }

    internal static void ClassifyTerrain(
        MapMask mask,
        CrustFieldMap crustFields,
        double[] elevation,
        double[] roughness,
        double[] distanceToLand,
        double[] distanceToWater,
        double[] landEnclosure,
        double shelfWidth,
        double[] sedimentSupply,
        double[] heatFlow,
        double[] basinInfluence,
        double[] foothillInfluence,
        double[] ridgeContinuity,
        double[] ridgeMask,
        double[] subductionMask,
        double[] riftProvince,
        double[] riftGraben,
        byte[] terrainClasses)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var point = new GridPoint(x, y);
                var index = y * mask.Width + x;
                if (!mask.IsLand(point))
                {
                    terrainClasses[index] = (byte)ClassifyBathymetry(
                        mask,
                        crustFields,
                        elevation[index],
                        x,
                        y,
                        distanceToLand[index],
                        landEnclosure[index],
                        shelfWidth,
                        ridgeMask[index],
                        subductionMask[index],
                        riftProvince[index] * 0.42 + riftGraben[index] * 0.72);
                    continue;
                }

                var value = elevation[index];
                var coastalInfluence = Math.Clamp(1.0 - distanceToWater[index] / Math.Max(1.0, shelfWidth * 3.0), 0, 1);
                var interior = Math.Clamp((distanceToWater[index] - shelfWidth * 1.2) / Math.Max(1.0, shelfWidth * 5.0), 0, 1);
                var sediment = sedimentSupply[index];
                var basin = basinInfluence[index];
                var foothill = foothillInfluence[index];
                var ridge = ridgeContinuity[index];
                var heat = heatFlow[index];
                var aridityNoise = SmoothNoise(x, y, 1801, 54.0);
                var plateauNoise = SmoothNoise(x, y, 1807, 46.0);

                var terrainClass = value switch
                {
                    < 45 => TerrainClassKind.Beach,
                    < 170 when coastalInfluence > 0.55 && (sediment > 0.22 || basin > 0.24) => TerrainClassKind.DeltaCandidate,
                    < 260 when coastalInfluence > 0.34 => TerrainClassKind.CoastalPlain,
                    < 720 when foothill > 0.14 && (sediment > 0.10 || basin > 0.16) => TerrainClassKind.AlluvialPlain,
                    < 560 when sediment > 0.20 && basin > 0.18 && coastalInfluence < 0.45 => TerrainClassKind.AlluvialPlain,
                    < 820 when basin > 0.22 && coastalInfluence < 0.68 && sediment < 0.50 && aridityNoise > 0.60 => TerrainClassKind.DryBasin,
                    < 820 when basin > 0.28 => TerrainClassKind.SedimentaryBasin,
                    < 760 when ridge < 0.35 => TerrainClassKind.InteriorLowland,
                    < 1700 when ridge > 0.58 && value > 1050 => TerrainClassKind.Mountain,
                    < 1600 when value >= 780 && coastalInfluence < 0.64 && ridge < 0.50 && roughness[index] < 0.72 && basin < 0.76 && plateauNoise > 0.54 => TerrainClassKind.DesertPlateauCandidate,
                    < 1750 => TerrainClassKind.Highland,
                    _ => TerrainClassKind.Mountain
                };

                terrainClasses[index] = (byte)terrainClass;
                roughness[index] = AdjustRoughnessForTerrainClass(roughness[index], terrainClass, ridge);
            }
        }
    }

    internal static TerrainClassKind ClassifyBathymetry(
        MapMask mask,
        CrustFieldMap crustFields,
        double elevation,
        int x,
        int y,
        double distanceToLand,
        double enclosure,
        double shelfWidth,
        double ridge,
        double subduction,
        double rift)
    {
        var coastal = crustFields.GetCoastalZone(x, y);
        var shelf = Math.Clamp(1.0 - distanceToLand / Math.Max(1.0, shelfWidth * 2.3), 0, 1);
        var bankNoise = SmoothNoise(x - 47, y + 83, 2107, 34.0);
        var channelNoise = SmoothNoise(x + 109, y - 73, 2101, 28.0);
        var basinNoise = SmoothNoise(x + 13, y + 29, 2111, 88.0);

        if (subduction > 0.38 && elevation < -1050)
            return TerrainClassKind.Trench;
        if (ridge > 0.38 && elevation > -2450)
            return TerrainClassKind.SubmarineRidge;
        if (enclosure > 0.64 && distanceToLand <= shelfWidth * 2.8)
            return TerrainClassKind.InlandSeaDepth;
        if (enclosure > 0.42 && rift + Math.Clamp(channelNoise - 0.54, 0, 1) > 0.42 && elevation < -420)
            return TerrainClassKind.StraitDepth;
        if (shelf > 0.34 && elevation > -620 && bankNoise > 0.66)
            return TerrainClassKind.ShallowBank;
        if ((rift > 0.42 || channelNoise > 0.82) && elevation < -850)
            return TerrainClassKind.DeepChannel;
        if (elevation < -3300 && basinNoise > 0.66)
            return TerrainClassKind.AbyssalBasin;
        if (elevation > -900 || coastal is CoastalZoneKind.Shelf or CoastalZoneKind.ShallowSea)
            return TerrainClassKind.ShelfSea;

        return TerrainClassKind.Ocean;
    }

    internal static double AdjustRoughnessForTerrainClass(double roughness, TerrainClassKind terrainClass, double ridgeContinuity)
    {
        var target = terrainClass switch
        {
            TerrainClassKind.Beach => 0.07,
            TerrainClassKind.CoastalPlain => 0.14,
            TerrainClassKind.AlluvialPlain => 0.16,
            TerrainClassKind.InteriorLowland => 0.17,
            TerrainClassKind.SedimentaryBasin => 0.10,
            TerrainClassKind.DryBasin => 0.19,
            TerrainClassKind.DeltaCandidate => 0.10,
            TerrainClassKind.DesertPlateauCandidate => 0.22,
            TerrainClassKind.Highland => 0.30,
            TerrainClassKind.Mountain => 0.56 + ridgeContinuity * 0.24,
            _ => roughness
        };

        return Math.Clamp(roughness * 0.45 + target * 0.55, 0.03, 1.0);
    }

}
