using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using AvaloniaRegionizer.ViewModels;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.ImageSharp;
using MapRegionizer.Runner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AvaloniaRegionizer.Services;

public sealed class MapPreviewService
{
    public Bitmap? RenderMask(string maskPath)
    {
        if (string.IsNullOrWhiteSpace(maskPath) || !File.Exists(maskPath))
            return null;

        using var stream = File.OpenRead(maskPath);
        return new Bitmap(stream);
    }

    public Bitmap? Render(MapGenerationSession? session, PreviewLayerViewModel? layer)
    {
        if (session is null || layer is null || !session.Has(layer.RequiredKey))
            return null;

        using var image = RenderImage(session, layer);
        return ToBitmap(image);
    }

    public string GetLegend(LocalizationService localization, PreviewLayerViewModel? layer, bool available)
    {
        if (layer is null || !available)
            return localization["LegendNotAvailable"];

        return layer.Kind switch
        {
            PreviewLayerKind.TectonicPlates or PreviewLayerKind.Crust or PreviewLayerKind.TectonicFeatures => localization["LegendTectonics"],
            PreviewLayerKind.Elevation or PreviewLayerKind.ElevationBase or PreviewLayerKind.ElevationTectonic or
                PreviewLayerKind.ElevationRoughness or PreviewLayerKind.ElevationErosion or PreviewLayerKind.ElevationZones or
                PreviewLayerKind.ElevationMountain or PreviewLayerKind.ElevationBasin or PreviewLayerKind.ElevationRivers => localization["LegendElevation"],
            PreviewLayerKind.ClimateBiomesDebug or PreviewLayerKind.ClimateBiomesPresentation or PreviewLayerKind.ClimateTemperature or PreviewLayerKind.ClimateMoisture or
                PreviewLayerKind.ClimatePrecipitation or PreviewLayerKind.ClimateSeasonality or PreviewLayerKind.ClimateHabitability or
                PreviewLayerKind.ClimateAgriculture or PreviewLayerKind.ClimateIce => localization["LegendClimate"],
            _ => localization["LegendOverview"]
        };
    }

    private static Image<Rgba32> RenderImage(MapGenerationSession session, PreviewLayerViewModel layer)
    {
        var map = session.CurrentMap;
        return layer.Kind switch
        {
            PreviewLayerKind.TectonicPlates => map.TectonicPlates is not null
                ? MapImageRenderer.RenderTectonicPlates(map)
                : RenderPlateDomains(session.PlateDomains ?? throw new InvalidOperationException("Plate domains are not available.")),
            PreviewLayerKind.Crust => map.TectonicPlates is not null
                ? MapImageRenderer.RenderCrust(map)
                : RenderCrustFields(session.CrustFields ?? throw new InvalidOperationException("Crust fields are not available.")),
            PreviewLayerKind.TectonicFeatures => map.TectonicPlates is not null
                ? MapImageRenderer.RenderTectonicFeatures(map)
                : RenderFeatureFields(session.TectonicFeatures ?? throw new InvalidOperationException("Tectonic features are not available.")),
            PreviewLayerKind.Elevation or PreviewLayerKind.ElevationBase or PreviewLayerKind.ElevationTectonic or
                PreviewLayerKind.ElevationRoughness or PreviewLayerKind.ElevationErosion or PreviewLayerKind.ElevationZones or
                PreviewLayerKind.ElevationMountain or PreviewLayerKind.ElevationBasin =>
                MapImageRenderer.RenderElevation(map, new ElevationRenderOptions { Mode = layer.ElevationMode!.Value }),
            PreviewLayerKind.ElevationRivers => MapImageRenderer.RenderElevationRivers(map),
            PreviewLayerKind.ClimateBiomesDebug =>
                MapImageRenderer.RenderClimate(map, new ClimateRenderOptions { Mode = ClimateRenderMode.DebugBiomes, DrawRivers = false, DrawHillshade = false }),
            PreviewLayerKind.ClimateBiomesPresentation or PreviewLayerKind.ClimateTemperature or PreviewLayerKind.ClimateMoisture or
                PreviewLayerKind.ClimatePrecipitation or PreviewLayerKind.ClimateSeasonality or PreviewLayerKind.ClimateHabitability or
                PreviewLayerKind.ClimateAgriculture or PreviewLayerKind.ClimateIce =>
                MapImageRenderer.RenderClimate(map, new ClimateRenderOptions { Mode = layer.ClimateMode!.Value }),
            _ => MapImageRenderer.Render(map)
        };
    }

    private static Image<Rgba32> RenderCrustFields(CrustFieldMap crustFields)
    {
        var image = new Image<Rgba32>(crustFields.Width, crustFields.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = GetCrustColor(crustFields.GetCrust(x, y));
            }
        });
        return image;
    }

    private static Image<Rgba32> RenderPlateDomains(PlateDomainMap domains)
    {
        var image = new Image<Rgba32>(domains.Width, domains.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = GetPlateColor(domains.GetPlate(x, y).Value);
            }
        });
        return image;
    }

    private static Image<Rgba32> RenderFeatureFields(TectonicFeatureMap features)
    {
        var image = new Image<Rgba32>(features.Width, features.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var uplift = Math.Clamp(features.GetUplift(x, y), 0, 1);
                    var subsidence = Math.Clamp(features.GetSubsidence(x, y), 0, 1);
                    var volcanism = Math.Clamp(features.GetVolcanism(x, y), 0, 1);
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(35 + volcanism * 220 + uplift * 90, 0, 255),
                        (byte)Math.Clamp(45 + uplift * 180, 0, 255),
                        (byte)Math.Clamp(55 + subsidence * 210, 0, 255),
                        255);
                }
            }
        });
        return image;
    }

    private static Rgba32 GetCrustColor(CrustKind kind) => kind switch
    {
        CrustKind.Continental => new Rgba32(213, 190, 142),
        CrustKind.Oceanic => new Rgba32(25, 93, 154),
        CrustKind.Shelf => new Rgba32(83, 171, 185),
        CrustKind.Arc => new Rgba32(225, 113, 74),
        CrustKind.Rift => new Rgba32(202, 79, 132),
        CrustKind.Terrane => new Rgba32(159, 139, 198),
        _ => new Rgba32(96, 96, 96)
    };

    private static Rgba32 GetPlateColor(int id)
    {
        unchecked
        {
            var hash = id * 1103515245 + 12345;
            return new Rgba32(
                (byte)(80 + Math.Abs(hash & 0x7F)),
                (byte)(80 + Math.Abs((hash >> 8) & 0x7F)),
                (byte)(80 + Math.Abs((hash >> 16) & 0x7F)),
                255);
        }
    }

    public async Task SavePreviewToFileAsync(MapGenerationSession session, PreviewLayerViewModel layer, string filePath, MapArtifactRenderOptions renderOptions)
    {
        var map = session.CurrentMap;
        var isJpeg = Path.GetExtension(filePath).ToLowerInvariant() is ".jpg" or ".jpeg";

        Image<Rgba32> image;
        switch (layer.Kind)
        {
            case PreviewLayerKind.TectonicPlates:
                image = MapImageRenderer.RenderTectonicPlates(map, new TectonicPlateRenderOptions
                {
                    Scale = renderOptions.Scale,
                    PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
                });
                break;
            case PreviewLayerKind.Crust:
                image = MapImageRenderer.RenderCrust(map, new CrustRenderOptions
                {
                    Scale = renderOptions.Scale,
                    DrawPlateBoundaries = renderOptions.DrawCrustPlateBoundaries,
                    PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
                });
                break;
            case PreviewLayerKind.TectonicFeatures:
            {
                var plateMap = map.TectonicPlates;
                image = plateMap is not null
                    ? MapImageRenderer.RenderTectonicFeatures(map, new TectonicFeatureRenderOptions
                    {
                        Scale = renderOptions.Scale,
                        DrawPlateBoundaries = renderOptions.DrawFeaturePlateBoundaries,
                        PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
                    })
                    : RenderFeatureFields(session.TectonicFeatures ?? throw new InvalidOperationException("Tectonic features are not available."));
                break;
            }
            case PreviewLayerKind.Elevation or PreviewLayerKind.ElevationBase or PreviewLayerKind.ElevationTectonic or
                PreviewLayerKind.ElevationRoughness or PreviewLayerKind.ElevationErosion or PreviewLayerKind.ElevationZones or
                PreviewLayerKind.ElevationMountain or PreviewLayerKind.ElevationBasin:
                image = MapImageRenderer.RenderElevation(map, new ElevationRenderOptions
                {
                    Scale = renderOptions.Scale,
                    Mode = layer.ElevationMode!.Value,
                    DrawHillshade = renderOptions.DrawElevationHillshade,
                    DrawPlateBoundaries = renderOptions.DrawElevationPlateBoundaries,
                    PlateBoundaryWidth = renderOptions.TectonicBoundaryWidth
                });
                break;
            case PreviewLayerKind.ElevationRivers:
                image = MapImageRenderer.RenderElevationRivers(map, new RiverRenderOptions
                {
                    Scale = renderOptions.Scale
                });
                break;
            case PreviewLayerKind.ClimateBiomesDebug:
                image = MapImageRenderer.RenderClimate(map, new ClimateRenderOptions
                {
                    Scale = renderOptions.Scale,
                    Mode = ClimateRenderMode.DebugBiomes,
                    DrawRivers = false,
                    DrawHillshade = false
                });
                break;
            case PreviewLayerKind.ClimateBiomesPresentation or PreviewLayerKind.ClimateTemperature or PreviewLayerKind.ClimateMoisture or
                PreviewLayerKind.ClimatePrecipitation or PreviewLayerKind.ClimateSeasonality or PreviewLayerKind.ClimateHabitability or
                PreviewLayerKind.ClimateAgriculture or PreviewLayerKind.ClimateIce:
                image = MapImageRenderer.RenderClimate(map, new ClimateRenderOptions
                {
                    Scale = renderOptions.Scale,
                    Mode = layer.ClimateMode!.Value,
                    DrawHillshade = renderOptions.DrawClimateHillshade,
                    DrawRivers = renderOptions.DrawClimateRivers,
                    DrawRiverValleyAccents = renderOptions.DrawClimateRiverValleyAccents,
                    Elevation = new ElevationRenderOptions
                    {
                        Scale = renderOptions.Scale,
                        DrawHillshade = renderOptions.DrawClimateHillshade
                    }
                });
                break;
            default:
                image = MapImageRenderer.Render(map, new MapRenderOptions
                {
                    Scale = renderOptions.Scale,
                    BorderWidth = renderOptions.RegionBorderWidth
                });
                break;
        }

        using (image)
        {
            if (isJpeg)
                await image.SaveAsJpegAsync(filePath);
            else
                await image.SaveAsPngAsync(filePath);
        }
    }

    private static Bitmap ToBitmap(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
