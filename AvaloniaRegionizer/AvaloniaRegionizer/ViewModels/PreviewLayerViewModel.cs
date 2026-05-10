using System;
using MapRegionizer.Core.Generation;
using MapRegionizer.ImageSharp;
using ReactiveUI;

namespace AvaloniaRegionizer.ViewModels;

public enum PreviewLayerKind
{
    Overview,
    Regions,
    TectonicPlates,
    Crust,
    TectonicFeatures,
    Elevation,
    ElevationBase,
    ElevationTectonic,
    ElevationRoughness,
    ElevationErosion,
    ElevationZones,
    ElevationMountain,
    ElevationBasin,
    ElevationRivers,
    ClimateBiomes,
    ClimateTemperature,
    ClimateMoisture,
    ClimatePrecipitation,
    ClimateSeasonality,
    ClimateHabitability,
    ClimateAgriculture,
    ClimateIce
}

public sealed class PreviewLayerViewModel : ReactiveObject
{
    private readonly Func<string, string> _localize;
    private bool _isAvailable;

    public PreviewLayerViewModel(PreviewLayerKind kind, string labelKey, MapDataKey requiredKey, Func<string, string> localize)
    {
        Kind = kind;
        LabelKey = labelKey;
        RequiredKey = requiredKey;
        _localize = localize;
    }

    public PreviewLayerKind Kind { get; }
    public string LabelKey { get; }
    public MapDataKey RequiredKey { get; }
    public string Name => _localize(LabelKey);

    public bool IsAvailable
    {
        get => _isAvailable;
        set => this.RaiseAndSetIfChanged(ref _isAvailable, value);
    }

    public ElevationRenderMode? ElevationMode => Kind switch
    {
        PreviewLayerKind.Elevation => ElevationRenderMode.FinalElevation,
        PreviewLayerKind.ElevationBase => ElevationRenderMode.BaseElevation,
        PreviewLayerKind.ElevationTectonic => ElevationRenderMode.TectonicContribution,
        PreviewLayerKind.ElevationRoughness => ElevationRenderMode.Roughness,
        PreviewLayerKind.ElevationErosion => ElevationRenderMode.ErosionMask,
        PreviewLayerKind.ElevationZones => ElevationRenderMode.TerrainZones,
        PreviewLayerKind.ElevationMountain => ElevationRenderMode.MountainInfluence,
        PreviewLayerKind.ElevationBasin => ElevationRenderMode.BasinInfluence,
        _ => null
    };

    public ClimateRenderMode? ClimateMode => Kind switch
    {
        PreviewLayerKind.ClimateBiomes => ClimateRenderMode.Biomes,
        PreviewLayerKind.ClimateTemperature => ClimateRenderMode.Temperature,
        PreviewLayerKind.ClimateMoisture => ClimateRenderMode.Moisture,
        PreviewLayerKind.ClimatePrecipitation => ClimateRenderMode.Precipitation,
        PreviewLayerKind.ClimateSeasonality => ClimateRenderMode.Seasonality,
        PreviewLayerKind.ClimateHabitability => ClimateRenderMode.Habitability,
        PreviewLayerKind.ClimateAgriculture => ClimateRenderMode.Agriculture,
        PreviewLayerKind.ClimateIce => ClimateRenderMode.Ice,
        _ => null
    };

    public void RefreshLocalization() => this.RaisePropertyChanged(nameof(Name));
}
