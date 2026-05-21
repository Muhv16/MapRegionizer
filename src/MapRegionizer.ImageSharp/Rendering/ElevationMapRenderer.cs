using MapRegionizer.Core.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapRegionizer.ImageSharp;

internal static class ElevationMapRenderer
{
    internal static void RenderElevationToFile(GeneratedMap map, string filePath, ElevationRenderOptions? options = null)
    {
        using var image = RenderElevation(map, options);
        image.SaveAsPng(filePath);
    }

    internal static Image<Rgba32> RenderElevation(GeneratedMap map, ElevationRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var elevation = map.Elevation ?? throw new InvalidOperationException("The map does not contain elevation data.");

        options ??= new ElevationRenderOptions();

        var width = Math.Max(1, (int)Math.Ceiling(map.Bounds.Width * options.Scale));
        var height = Math.Max(1, (int)Math.Ceiling(map.Bounds.Height * options.Scale));
        var image = new Image<Rgba32>(width, height);
        var pixelSize = Math.Max(double.Epsilon, map.Bounds.PixelSize * options.Scale);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = Math.Clamp((int)(y / pixelSize), 0, elevation.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = Math.Clamp((int)(x / pixelSize), 0, elevation.Width - 1);
                    var shade = options.DrawHillshade && options.Mode == ElevationRenderMode.FinalElevation
                        ? ComputeHillshade(elevation, sourceX, sourceY, options)
                        : 1.0;
                    row[x] = GetElevationColor(elevation, map.WaterSurfaces, map.WaterBodyTopology, sourceX, sourceY, shade, options);
                }
            }
        });

        if (options.DrawPlateBoundaries && map.TectonicPlates is not null)
            TectonicPlateRenderer.DrawPlateBoundaries(image, map.TectonicPlates, map.Bounds.PixelSize, options);

        return image;
    }

    internal static void RenderElevationDebugToFiles(
        GeneratedMap map,
        string outputDirectory,
        string prefix = "elevation",
        ElevationRenderOptions? options = null,
        RiverRenderOptions? riverOptions = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        Directory.CreateDirectory(outputDirectory);
        options ??= new ElevationRenderOptions();
        riverOptions ??= new RiverRenderOptions { Scale = options.Scale };

        var modes = new[]
        {
            (ElevationRenderMode.FinalElevation, "final"),
            (ElevationRenderMode.BaseElevation, "base"),
            (ElevationRenderMode.TectonicContribution, "tectonic"),
            (ElevationRenderMode.Roughness, "roughness"),
            (ElevationRenderMode.ErosionMask, "erosion"),
            (ElevationRenderMode.TerrainZones, "terrain-zones"),
            (ElevationRenderMode.MountainInfluence, "mountain"),
            (ElevationRenderMode.BasinInfluence, "basin")
        };

        foreach (var (mode, suffix) in modes)
            RenderElevationToFile(map, System.IO.Path.Combine(outputDirectory, $"{prefix}-{suffix}.png"), new ElevationRenderOptions
            {
                Scale = options.Scale,
                Mode = mode,
                DrawHillshade = options.DrawHillshade,
                DrawPlateBoundaries = options.DrawPlateBoundaries,
                PlateBoundaryWidth = options.PlateBoundaryWidth
            });

        if (map.Hydrology is not null)
            RiverOverlayRenderer.RenderElevationRiversToFile(map, System.IO.Path.Combine(outputDirectory, $"{prefix}-rivers.png"), riverOptions);
    }

    internal static Rgba32 GetElevationColor(
        ElevationMap elevation,
        WaterSurfaceMap? waterSurfaces,
        WaterBodyTopology? waterBodyTopology,
        int x,
        int y,
        double shade,
        ElevationRenderOptions options)
    {
        return options.Mode switch
        {
            ElevationRenderMode.BaseElevation => ColorBlending.GetDivergingHeightColor(elevation.GetBaseElevation(x, y), -4600, 2600),
            ElevationRenderMode.TectonicContribution => ColorBlending.GetDivergingContributionColor(elevation.GetTectonicElevation(x, y), -900, 2800),
            ElevationRenderMode.Roughness => ColorBlending.GetUnitColor(elevation.GetRoughness(x, y), Color.FromRgb(36, 73, 82), Color.FromRgb(226, 206, 142)),
            ElevationRenderMode.ErosionMask => ColorBlending.GetUnitColor(elevation.GetErosionMask(x, y), Color.FromRgb(40, 46, 73), Color.FromRgb(128, 219, 196)),
            ElevationRenderMode.TerrainZones => GetTerrainClassColor(elevation.GetTerrainClass(x, y), 1.0, options),
            ElevationRenderMode.MountainInfluence => GetMountainInfluenceColor(elevation, x, y),
            ElevationRenderMode.BasinInfluence => ColorBlending.GetUnitColor(elevation.GetBasinInfluence(x, y), Color.FromRgb(58, 93, 84), Color.FromRgb(207, 184, 108)),
            _ => GetFinalElevationColor(elevation, waterSurfaces, waterBodyTopology, x, y, shade, options)
        };
    }

    internal static Rgba32 GetFinalElevationColor(
        ElevationMap elevationMap,
        WaterSurfaceMap? waterSurfaces,
        WaterBodyTopology? waterBodyTopology,
        int x,
        int y,
        double shade,
        ElevationRenderOptions options)
    {
        var elevation = elevationMap.GetElevation(x, y);
        Rgba32 color;
        if (elevationMap.HasWaterSurface(x, y))
        {
            var surface = elevationMap.GetWaterSurface(x, y);
            var bed = elevationMap.GetBedElevation(x, y);
            var depthMeters = surface <= 0 ? Math.Max(-bed, surface - bed) : Math.Max(0.0, surface - bed);
            var depth = Math.Clamp(depthMeters / Math.Max(1.0, Math.Abs(options.DeepOceanDepthMeters)), 0, 1);
            var depthColor = depth < 0.35
                ? ColorBlending.LerpColor(options.ShallowWaterColor, options.ShelfWaterColor, depth / 0.35)
                : ColorBlending.LerpColor(options.ShelfWaterColor, options.DeepWaterColor, (depth - 0.35) / 0.65);
            var terrainColor = GetTerrainClassColor(elevationMap.GetTerrainClass(x, y), 1.0, options);
            color = ColorBlending.Blend(depthColor, terrainColor, 0.18);
            var bodyId = waterBodyTopology?.GetWaterBodyId(x, y);
            var body = bodyId.HasValue ? waterSurfaces?.GetBodySurface(bodyId.Value) : null;
            if (body?.Kind is WaterBodyKind.InlandLake or WaterBodyKind.InlandSea && body.MaxDepthMeters > 0)
            {
                var lakeDepth = Math.Clamp(depthMeters / Math.Max(1.0, body.MaxDepthMeters), 0, 1);
                var lakeShade = Math.Pow(lakeDepth, 0.78);
                var deepLakeColor = body.LakeOrigin switch
                {
                    LakeOriginKind.Tectonic => options.TectonicLakeDepthColor,
                    LakeOriginKind.VolcanicKarst => options.VolcanicLakeDepthColor,
                    _ => options.LakeDepthColor
                };
                color = ColorBlending.Blend(color, deepLakeColor.ToPixel<Rgba32>(), lakeShade * options.LakeDepthTintStrength);
            }
        }
        else
        {
            var terrainClass = elevationMap.GetTerrainClass(x, y);
            var terrainColor = GetTerrainClassColor(terrainClass, 1.0, options);
            var elevationColor = GetContinuousLandColor(elevation, options);
            var terrainWeight = terrainClass switch
            {
                TerrainClassKind.Mountain => 0.78,
                TerrainClassKind.Highland => 0.62,
                TerrainClassKind.DesertPlateauCandidate => 0.58,
                TerrainClassKind.DryBasin => 0.55,
                TerrainClassKind.Beach => 0.48,
                TerrainClassKind.DeltaCandidate => 0.48,
                TerrainClassKind.SedimentaryBasin => 0.42,
                TerrainClassKind.AlluvialPlain => 0.40,
                TerrainClassKind.CoastalPlain => 0.38,
                TerrainClassKind.InteriorLowland => 0.34,
                _ => 0.45
            };
            color = ColorBlending.Blend(elevationColor, terrainColor, terrainWeight);
            if (terrainClass == TerrainClassKind.Highland)
            {
                var highlandColor = elevation switch
                {
                    < 820 => ColorBlending.LerpColor(options.InteriorLowlandColor, options.HighlandColor, Math.Clamp((elevation - 500.0) / 320.0, 0, 1)),
                    < 1250 => ColorBlending.LerpColor(options.HighlandColor, options.UplandColor, Math.Clamp((elevation - 820.0) / 430.0, 0, 1)),
                    _ => ColorBlending.LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1250.0) / 520.0, 0, 1))
                };
                color = ColorBlending.Blend(color, highlandColor, 0.58);
            }
            else if (terrainClass == TerrainClassKind.Mountain)
            {
                var mountainColor = elevation switch
                {
                    < 2100 => ColorBlending.LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1300.0) / 800.0, 0, 1)),
                    _ => ColorBlending.LerpColor(options.MountainColor, options.SnowColor, Math.Clamp((elevation - 2100.0) / Math.Max(1.0, options.SnowElevationMeters - 2100.0), 0, 1))
                };
                color = ColorBlending.Blend(color, mountainColor, 0.72);
            }
        }

        var shadeStrength = elevation < 0 ? options.OceanHillshadeStrength : options.HillshadeStrength;
        return ColorBlending.ApplyShade(color, shade, shadeStrength);
    }

    internal static Rgba32 GetContinuousLandColor(double elevation, ElevationRenderOptions options)
    {
        return elevation switch
        {
            < 90 => ColorBlending.LerpColor(options.BeachColor, options.CoastalPlainColor, Math.Clamp(elevation / 90.0, 0, 1)),
            < 420 => ColorBlending.LerpColor(options.CoastalPlainColor, options.InteriorLowlandColor, Math.Clamp((elevation - 90.0) / 330.0, 0, 1)),
            < 900 => ColorBlending.LerpColor(options.InteriorLowlandColor, options.HighlandColor, Math.Clamp((elevation - 420.0) / 480.0, 0, 1)),
            < 1450 => ColorBlending.LerpColor(options.HighlandColor, options.UplandColor, Math.Clamp((elevation - 900.0) / 550.0, 0, 1)),
            < 2200 => ColorBlending.LerpColor(options.UplandColor, options.MountainColor, Math.Clamp((elevation - 1450.0) / 750.0, 0, 1)),
            _ => ColorBlending.LerpColor(options.MountainColor, options.SnowColor, Math.Clamp((elevation - 2200.0) / Math.Max(1.0, options.SnowElevationMeters - 2200.0), 0, 1))
        };
    }

    internal static Rgba32 GetTerrainClassColor(TerrainClassKind terrainClass, double shade, ElevationRenderOptions options)
    {
        var color = terrainClass switch
        {
            TerrainClassKind.Ocean => options.DeepWaterColor.ToPixel<Rgba32>(),
            TerrainClassKind.ShelfSea => options.ShallowWaterColor.ToPixel<Rgba32>(),
            TerrainClassKind.DeepChannel => options.DeepChannelColor.ToPixel<Rgba32>(),
            TerrainClassKind.ShallowBank => options.ShallowBankColor.ToPixel<Rgba32>(),
            TerrainClassKind.AbyssalBasin => options.AbyssalBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.SubmarineRidge => options.SubmarineRidgeColor.ToPixel<Rgba32>(),
            TerrainClassKind.Trench => options.TrenchColor.ToPixel<Rgba32>(),
            TerrainClassKind.StraitDepth => options.StraitDepthColor.ToPixel<Rgba32>(),
            TerrainClassKind.InlandSeaDepth => options.InlandSeaDepthColor.ToPixel<Rgba32>(),
            TerrainClassKind.Beach => options.BeachColor.ToPixel<Rgba32>(),
            TerrainClassKind.CoastalPlain => options.CoastalPlainColor.ToPixel<Rgba32>(),
            TerrainClassKind.AlluvialPlain => options.AlluvialPlainColor.ToPixel<Rgba32>(),
            TerrainClassKind.InteriorLowland => options.InteriorLowlandColor.ToPixel<Rgba32>(),
            TerrainClassKind.SedimentaryBasin => options.SedimentaryBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.DryBasin => options.DryBasinColor.ToPixel<Rgba32>(),
            TerrainClassKind.DeltaCandidate => options.DeltaCandidateColor.ToPixel<Rgba32>(),
            TerrainClassKind.DesertPlateauCandidate => options.DesertPlateauCandidateColor.ToPixel<Rgba32>(),
            TerrainClassKind.Highland => options.HighlandColor.ToPixel<Rgba32>(),
            TerrainClassKind.Mountain => options.MountainColor.ToPixel<Rgba32>(),
            _ => options.LowlandColor.ToPixel<Rgba32>()
        };

        return ColorBlending.ApplyShade(color, shade, terrainClass is TerrainClassKind.Ocean or TerrainClassKind.ShelfSea ? options.OceanHillshadeStrength : options.HillshadeStrength);
    }

    internal static Rgba32 GetMountainInfluenceColor(ElevationMap elevation, int x, int y)
    {
        var ridge = elevation.GetRidgeContinuity(x, y);
        var foothill = elevation.GetFoothillInfluence(x, y);
        var pass = elevation.GetMountainPassPotential(x, y);
        var color = ColorBlending.Blend(Color.FromRgb(43, 74, 70).ToPixel<Rgba32>(), Color.FromRgb(116, 104, 91).ToPixel<Rgba32>(), foothill);
        color = ColorBlending.Blend(color, Color.FromRgb(226, 219, 197).ToPixel<Rgba32>(), ridge);
        return ColorBlending.Blend(color, Color.FromRgb(92, 168, 196).ToPixel<Rgba32>(), pass * 0.85);
    }

    internal static double ComputeHillshade(ElevationMap elevation, int x, int y, ElevationRenderOptions options)
    {
        var left = elevation.GetElevation(RenderingGeometry.WrapX(x - 1, elevation.Width), y);
        var right = elevation.GetElevation(RenderingGeometry.WrapX(x + 1, elevation.Width), y);
        var up = elevation.GetElevation(x, Math.Max(0, y - 1));
        var down = elevation.GetElevation(x, Math.Min(elevation.Height - 1, y + 1));
        var eastSlope = (left - right) / Math.Max(1.0, options.HillshadeElevationScale);
        var southSlope = (up - down) / Math.Max(1.0, options.HillshadeElevationScale);
        return Math.Clamp(0.9 + eastSlope * 0.18 + southSlope * 0.24, 0.58, 1.22);
    }
}
