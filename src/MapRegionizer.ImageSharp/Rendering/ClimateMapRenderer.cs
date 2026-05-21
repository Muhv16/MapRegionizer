using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class ClimateMapRenderer
{
    internal static void RenderClimateToFile(GeneratedMap map, string filePath, ClimateRenderOptions? options = null)
    {
        using var image = RenderClimate(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderClimate(GeneratedMap map, ClimateRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var climate = map.Climate ?? throw new InvalidOperationException("The map does not contain climate data.");

        options ??= new ClimateRenderOptions();
        var elevation = map.Elevation;
        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);
        var elevationOptions = options.Elevation;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, climate.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, climate.Width - 1);
                    var shade = options.DrawHillshade && elevation is not null && options.Mode == ClimateRenderMode.Biomes
                        ? ElevationMapRenderer.ComputeHillshade(elevation, Math.Min(sourceX, elevation.Width - 1), Math.Min(sourceY, elevation.Height - 1), elevationOptions)
                        : 1.0;
                    row[x] = GetClimateColor(map, climate, sourceX, sourceY, shade, options);
                }
            }
        });

        if (options.Mode == ClimateRenderMode.Biomes && map.Hydrology is not null)
        {
            if (options.DrawRiverValleyAccents)
                RiverOverlayRenderer.DrawRiverValleyAccents(image, map.Hydrology, map.Bounds.PixelSize, options);

            if (!options.DrawRivers)
                return image;

            RiverOverlayRenderer.DrawRivers(image, map.Hydrology, map.Bounds.PixelSize, new RiverRenderOptions
            {
                Scale = options.Scale,
                Opacity = options.PresentationRiverOpacity
            });
        }

        return image;
    }

    internal static Rgba32 GetClimateColor(GeneratedMap map, ClimateMap climate, int x, int y, double shade, ClimateRenderOptions options)
    {
        return options.Mode switch
        {
            ClimateRenderMode.DebugBiomes => GetBiomeColor(climate.GetBiome(x, y), options),
            ClimateRenderMode.Temperature => GetTemperatureColor(climate.GetMeanAnnualTemperature(x, y), options),
            ClimateRenderMode.Moisture => ColorBlending.GetUnitColor(climate.GetMoisture(x, y), options.DryColor, options.WetColor),
            ClimateRenderMode.BiomeMoisture => ColorBlending.GetUnitColor(climate.GetBiomeMoisture(x, y), options.DryColor, options.WetColor),
            ClimateRenderMode.Precipitation => ColorBlending.GetUnitColor(climate.GetPrecipitation(x, y), options.DryColor, options.RainColor),
            ClimateRenderMode.Seasonality => ColorBlending.GetUnitColor(Math.Clamp(climate.GetSeasonality(x, y) / 36.0, 0, 1), options.LowSeasonalityColor, options.HighSeasonalityColor),
            ClimateRenderMode.Habitability => ColorBlending.GetUnitColor(climate.GetHabitability(x, y), options.LowHabitabilityColor, options.HighHabitabilityColor),
            ClimateRenderMode.Agriculture => ColorBlending.GetUnitColor(climate.GetAgriculturalPotential(x, y), options.LowAgricultureColor, options.HighAgricultureColor),
            ClimateRenderMode.Ice => ColorBlending.GetUnitColor(climate.GetIceScore(x, y), options.NoIceColor, options.IceColor),
            _ => GetBiomeReliefColor(map, climate, x, y, shade, options)
        };
    }

    internal static Rgba32 GetBiomeReliefColor(GeneratedMap map, ClimateMap climate, int x, int y, double shade, ClimateRenderOptions options)
    {
        var biome = climate.GetBiome(x, y);
        var elevation = map.Elevation;
        if (elevation is null)
            return GetBiomeColor(biome, options);

        var sourceX = Math.Min(x, elevation.Width - 1);
        var sourceY = Math.Min(y, elevation.Height - 1);
        var elevationOptions = options.Elevation;
        if (elevation.HasWaterSurface(sourceX, sourceY) || biome == BiomeKind.Ocean)
            return ElevationMapRenderer.GetFinalElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, shade, elevationOptions);

        var biomeColor = GetBoundaryAwareBiomeColor(climate, x, y, options);
        biomeColor = ApplyBiomeTexture(biomeColor, biome, x, y, climate, options);
        var terrainColor = ElevationMapRenderer.GetFinalElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, 1.0, elevationOptions);
        var terrainClass = elevation.GetTerrainClass(sourceX, sourceY);
        var reliefBlend = terrainClass switch
        {
            TerrainClassKind.Mountain => options.MountainReliefBlend,
            TerrainClassKind.Highland => options.HighlandReliefBlend,
            TerrainClassKind.DesertPlateauCandidate => options.PlateauReliefBlend,
            TerrainClassKind.DryBasin => options.BasinReliefBlend,
            _ => options.LandReliefBlend
        };

        var color = ColorBlending.Blend(biomeColor, terrainColor, reliefBlend);
        var ridge = elevation.GetRidgeContinuity(sourceX, sourceY);
        var foothill = elevation.GetFoothillInfluence(sourceX, sourceY);
        var mountainOverlay = climate.GetMountainOverlay(x, y);
        if (ridge > 0.18 || terrainClass == TerrainClassKind.Mountain || mountainOverlay > 0.34)
            color = ColorBlending.Blend(color, GetBiomeMountainTint(biome, options), Math.Clamp(0.16 + Math.Max(ridge, mountainOverlay) * 0.36, 0, options.MountainTintStrength));
        else if (foothill > 0.15)
            color = ColorBlending.Blend(color, terrainColor, Math.Clamp(foothill * 0.18, 0, 0.2));

        if (IsDesertBiome(biome))
            color = ApplyDesertPresentationModifiers(color, biome, x, y, options);

        var river = Math.Pow(climate.GetRiverValleyInfluence(x, y), options.RiverValleyOverlayGamma);
        if (river > options.RiverValleyOverlayThreshold)
            color = ColorBlending.Blend(color, options.RiverValleyOverlayColor.ToPixel<Rgba32>(), Math.Clamp((river - options.RiverValleyOverlayThreshold) / (1.0 - options.RiverValleyOverlayThreshold) * options.RiverValleyOverlayStrength, 0, options.RiverValleyOverlayStrength));

        var wetland = climate.GetWetlandInfluence(x, y);
        if (wetland > 0.12)
            color = ColorBlending.Blend(color, options.WetlandOverlayColor.ToPixel<Rgba32>(), Math.Clamp((wetland - 0.12) / 0.88 * options.WetlandOverlayStrength, 0, options.WetlandOverlayStrength));

        var snow = Math.Max(climate.GetSnowOverlay(x, y), climate.GetIceScore(x, y) * 0.72);
        if (snow > options.SnowOverlayThreshold)
            color = ColorBlending.Blend(color, options.IceColor.ToPixel<Rgba32>(), Math.Clamp((snow - options.SnowOverlayThreshold) / (1.0 - options.SnowOverlayThreshold) * options.IceOverlayStrength, 0, options.IceOverlayStrength));

        return ColorBlending.ApplyShade(color, shade, options.BiomeHillshadeStrength);
    }

    internal static Rgba32 GetBoundaryAwareBiomeColor(ClimateMap climate, int x, int y, ClimateRenderOptions options)
    {
        var center = climate.GetBiome(x, y);
        var color = GetBiomeColor(center, options);
        var neighborColor = new Rgba32(0, 0, 0, 255);
        var neighborCount = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            var yy = y + dy;
            if (yy < 0 || yy >= climate.Height)
                continue;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var xx = RenderingGeometry.WrapX(x + dx, climate.Width);
                var neighbor = climate.GetBiome(xx, yy);
                if (neighbor == center)
                    continue;

                neighborColor = ColorBlending.Blend(neighborColor, GetBiomeColor(neighbor, options), 1.0 / (neighborCount + 1));
                neighborCount++;
            }
        }

        if (neighborCount == 0)
            return ColorBlending.BoostSaturation(color, options.BiomeCenterSaturationBoost, options.BiomeCenterValueBoost);

        var boundaryAmount = Math.Clamp((neighborCount - 1.0) / 7.0 * options.BiomeBoundaryBlend, 0, options.BiomeBoundaryBlend);
        return ColorBlending.Blend(color, neighborColor, boundaryAmount);
    }

    internal static Rgba32 ApplyBiomeTexture(Rgba32 color, BiomeKind biome, int x, int y, ClimateMap climate, ClimateRenderOptions options)
    {
        if (options.TextureStrength <= 0 || biome == BiomeKind.Ocean)
            return color;

        var fine = ColorBlending.HashNoise(x, y, 17);
        var broad = ColorBlending.HashNoise(x / 7, y / 7, 53);
        var value = biome switch
        {
            BiomeKind.TropicalRainforest or BiomeKind.TemperateRainforest or BiomeKind.BorealForest => fine * 0.65 + broad * 0.35 - 0.55,
            BiomeKind.Savanna => broad * 0.75 + fine * 0.25 - 0.42,
            BiomeKind.OpenWoodland => broad * 0.68 + fine * 0.32 - 0.48,
            BiomeKind.HotDesert or BiomeKind.SemiDesert => broad * 0.78 + fine * 0.22 - 0.34,
            BiomeKind.RockyDesert => broad * 0.46 + fine * 0.54 - 0.42,
            BiomeKind.Tundra or BiomeKind.PolarDesert => fine * 0.35 + broad * 0.65 - 0.50,
            BiomeKind.Marsh or BiomeKind.Floodplain or BiomeKind.Mangrove => fine - 0.42,
            _ => fine * 0.45 + broad * 0.55 - 0.50
        };

        var strength = Math.Clamp(Math.Abs(value) * options.TextureStrength, 0, options.TextureStrength);
        var target = value >= 0
            ? options.TextureLightColor.ToPixel<Rgba32>()
            : options.TextureDarkColor.ToPixel<Rgba32>();
        if (biome is BiomeKind.Marsh or BiomeKind.Floodplain or BiomeKind.Mangrove)
            target = value >= 0 ? options.WetlandTextureLightColor.ToPixel<Rgba32>() : options.WetlandTextureDarkColor.ToPixel<Rgba32>();

        return ColorBlending.Blend(color, target, strength);
    }

    internal static Rgba32 ApplyDesertPresentationModifiers(Rgba32 color, BiomeKind biome, int x, int y, ClimateRenderOptions options)
    {
        var broad = ColorBlending.HashNoise(x / 9, y / 9, 211);
        var streak = ColorBlending.HashNoise((x + y) / 11, (x - y) / 11, 307);
        var rocky = ColorBlending.HashNoise(x / 3, y / 3, 401);
        var warmAmount = Math.Clamp((broad - 0.28) * options.DesertWarmthStrength, 0, options.DesertWarmthStrength);
        var result = ColorBlending.Blend(color, options.DesertSunColor.ToPixel<Rgba32>(), warmAmount);

        if (biome is BiomeKind.RockyDesert or BiomeKind.ColdDesert)
            result = ColorBlending.Blend(result, options.DesertRockColor.ToPixel<Rgba32>(), Math.Clamp((rocky - 0.35) * options.DesertRockStrength, 0, options.DesertRockStrength));
        else
            result = ColorBlending.Blend(result, options.DesertDuneColor.ToPixel<Rgba32>(), Math.Clamp((streak - 0.38) * options.DesertDuneStrength, 0, options.DesertDuneStrength));

        return result;
    }

    internal static bool IsDesertBiome(BiomeKind biome) => biome is
        BiomeKind.HotDesert or
        BiomeKind.SemiDesert or
        BiomeKind.RockyDesert or
        BiomeKind.ColdDesert or
        BiomeKind.SaltFlat;

    internal static Rgba32 GetBiomeMountainTint(BiomeKind biome, ClimateRenderOptions options)
    {
        return biome switch
        {
            BiomeKind.HotDesert or BiomeKind.ColdDesert => options.DryMountainColor.ToPixel<Rgba32>(),
            BiomeKind.Tundra or BiomeKind.AlpineTundra or BiomeKind.PolarDesert => options.ColdMountainColor.ToPixel<Rgba32>(),
            BiomeKind.IceSheet => options.IceColor.ToPixel<Rgba32>(),
            _ => options.MountainColor.ToPixel<Rgba32>()
        };
    }

    internal static Rgba32 GetTemperatureColor(double temperature, ClimateRenderOptions options)
    {
        if (temperature < -10)
            return ColorBlending.LerpColor(options.ExtremeColdColor, options.ColdColor, Math.Clamp((temperature + 35.0) / 25.0, 0, 1));
        if (temperature < 8)
            return ColorBlending.LerpColor(options.ColdColor, options.CoolColor, Math.Clamp((temperature + 10.0) / 18.0, 0, 1));
        if (temperature < 22)
            return ColorBlending.LerpColor(options.CoolColor, options.WarmColor, Math.Clamp((temperature - 8.0) / 14.0, 0, 1));

        return ColorBlending.LerpColor(options.WarmColor, options.HotColor, Math.Clamp((temperature - 22.0) / 18.0, 0, 1));
    }

    internal static Rgba32 GetBiomeColor(BiomeKind biome, ClimateRenderOptions options)
    {
        return biome switch
        {
            BiomeKind.Ocean => options.OceanColor.ToPixel<Rgba32>(),
            BiomeKind.TropicalRainforest => options.TropicalRainforestColor.ToPixel<Rgba32>(),
            BiomeKind.MonsoonForest => options.MonsoonForestColor.ToPixel<Rgba32>(),
            BiomeKind.DryTropicalForest => options.DryTropicalForestColor.ToPixel<Rgba32>(),
            BiomeKind.TropicalSeasonalForest => options.TropicalSeasonalForestColor.ToPixel<Rgba32>(),
            BiomeKind.Savanna => options.SavannaColor.ToPixel<Rgba32>(),
            BiomeKind.OpenWoodland => options.OpenWoodlandColor.ToPixel<Rgba32>(),
            BiomeKind.HotDesert => options.HotDesertColor.ToPixel<Rgba32>(),
            BiomeKind.SemiDesert => options.SemiDesertColor.ToPixel<Rgba32>(),
            BiomeKind.RockyDesert => options.RockyDesertColor.ToPixel<Rgba32>(),
            BiomeKind.SaltFlat => options.SaltFlatColor.ToPixel<Rgba32>(),
            BiomeKind.ColdDesert => options.ColdDesertColor.ToPixel<Rgba32>(),
            BiomeKind.Steppe => options.SteppeColor.ToPixel<Rgba32>(),
            BiomeKind.XericShrubland => options.XericShrublandColor.ToPixel<Rgba32>(),
            BiomeKind.MediterraneanShrubland => options.MediterraneanShrublandColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateGrassland => options.TemperateGrasslandColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateForest => options.TemperateForestColor.ToPixel<Rgba32>(),
            BiomeKind.TemperateRainforest => options.TemperateRainforestColor.ToPixel<Rgba32>(),
            BiomeKind.BorealForest => options.BorealForestColor.ToPixel<Rgba32>(),
            BiomeKind.Tundra => options.TundraColor.ToPixel<Rgba32>(),
            BiomeKind.PolarDesert => options.PolarDesertColor.ToPixel<Rgba32>(),
            BiomeKind.IceSheet => options.IceColor.ToPixel<Rgba32>(),
            BiomeKind.AlpineTundra => options.AlpineTundraColor.ToPixel<Rgba32>(),
            BiomeKind.Wetland => options.WetlandColor.ToPixel<Rgba32>(),
            BiomeKind.Floodplain => options.FloodplainColor.ToPixel<Rgba32>(),
            BiomeKind.Marsh => options.MarshColor.ToPixel<Rgba32>(),
            BiomeKind.Mangrove => options.MangroveColor.ToPixel<Rgba32>(),
            BiomeKind.MontaneForest => options.MontaneForestColor.ToPixel<Rgba32>(),
            BiomeKind.CloudForest => options.CloudForestColor.ToPixel<Rgba32>(),
            BiomeKind.SnowyMountain => options.SnowyMountainColor.ToPixel<Rgba32>(),
            BiomeKind.VolcanicBadlands => options.VolcanicBadlandsColor.ToPixel<Rgba32>(),
            _ => options.UnknownBiomeColor.ToPixel<Rgba32>()
        };
    }
}
