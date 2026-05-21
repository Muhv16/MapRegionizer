using SixLabors.ImageSharp;

namespace MapRegionizer.ImageSharp;

public sealed class MapRenderOptions
{
    public float Scale { get; init; } = 1;
    public float BorderWidth { get; init; } = 2;
    public Color LandColor { get; init; } = Color.White;
    public Color WaterColor { get; init; } = Color.Blue;
    public Color RegionBorderColor { get; init; } = Color.Black;
}

public class TectonicPlateRenderOptions
{
    public float Scale { get; init; } = 1;
    public float PlateBoundaryWidth { get; init; } = 1;
    public int PlateIdDigitScale { get; init; } = 3;
    public Color LandColor { get; init; } = Color.White;
    public Color WaterColor { get; init; } = Color.DeepSkyBlue;
    public Color PlateBoundaryColor { get; init; } = Color.Red;
    public Color PlateIdColor { get; init; } = Color.Black;
    public Color PlateIdBackgroundColor { get; init; } = Color.FromRgba(255, 255, 255, 180);
}

public sealed class CrustRenderOptions : TectonicPlateRenderOptions
{
    public CrustRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgb(190, 30, 42);
        PlateBoundaryWidth = 0.75f;
    }

    public bool DrawPlateBoundaries { get; init; }
    public int CrustSmoothingRadius { get; init; } = 1;
    public Color ContinentalColor { get; init; } = Color.FromRgb(213, 190, 142);
    public Color OceanicColor { get; init; } = Color.FromRgb(25, 93, 154);
    public Color ShelfColor { get; init; } = Color.FromRgb(83, 171, 185);
    public Color ArcColor { get; init; } = Color.FromRgb(225, 113, 74);
    public Color RiftColor { get; init; } = Color.FromRgb(202, 79, 132);
    public Color TerraneColor { get; init; } = Color.FromRgb(159, 139, 198);
    public Color UnknownColor { get; init; } = Color.DarkGray;
    public Color ShelfTintColor { get; init; } = Color.FromRgb(162, 224, 218);
    public Color SlopeTintColor { get; init; } = Color.FromRgb(49, 115, 158);
    public Color PassiveMarginTintColor { get; init; } = Color.FromRgb(128, 190, 151);
    public Color ActiveMarginTintColor { get; init; } = Color.FromRgb(232, 82, 77);
    public Color ShallowSeaTintColor { get; init; } = Color.FromRgb(99, 213, 224);
    public double CoastalZoneTintStrength { get; init; } = 0;
}

public sealed class ElevationRenderOptions : TectonicPlateRenderOptions
{
    public ElevationRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgba(40, 30, 25, 120);
        PlateBoundaryWidth = 0.65f;
    }

    public bool DrawHillshade { get; init; } = true;
    public bool DrawPlateBoundaries { get; init; }
    public ElevationRenderMode Mode { get; init; } = ElevationRenderMode.FinalElevation;
    public double HillshadeStrength { get; init; } = 0.42;
    public double OceanHillshadeStrength { get; init; } = 0.035;
    public double HillshadeElevationScale { get; init; } = 3200;
    public double DeepOceanDepthMeters { get; init; } = -6500;
    public double SnowElevationMeters { get; init; } = 3200;
    public Color DeepWaterColor { get; init; } = Color.FromRgb(16, 53, 105);
    public Color ShelfWaterColor { get; init; } = Color.FromRgb(47, 133, 169);
    public Color ShallowWaterColor { get; init; } = Color.FromRgb(113, 198, 196);
    public Color LakeDepthColor { get; init; } = Color.FromRgb(27, 98, 136);
    public Color TectonicLakeDepthColor { get; init; } = Color.FromRgb(20, 81, 132);
    public Color VolcanicLakeDepthColor { get; init; } = Color.FromRgb(31, 88, 121);
    public double LakeDepthTintStrength { get; init; } = 0.26;
    public Color DeepChannelColor { get; init; } = Color.FromRgb(24, 78, 128);
    public Color ShallowBankColor { get; init; } = Color.FromRgb(136, 213, 194);
    public Color AbyssalBasinColor { get; init; } = Color.FromRgb(12, 43, 92);
    public Color SubmarineRidgeColor { get; init; } = Color.FromRgb(76, 168, 180);
    public Color TrenchColor { get; init; } = Color.FromRgb(8, 35, 82);
    public Color StraitDepthColor { get; init; } = Color.FromRgb(28, 105, 151);
    public Color InlandSeaDepthColor { get; init; } = Color.FromRgb(73, 166, 181);
    public Color BeachColor { get; init; } = Color.FromRgb(172, 202, 132);
    public Color LowlandColor { get; init; } = Color.FromRgb(96, 156, 88);
    public Color CoastalPlainColor { get; init; } = Color.FromRgb(137, 190, 117);
    public Color AlluvialPlainColor { get; init; } = Color.FromRgb(116, 168, 92);
    public Color InteriorLowlandColor { get; init; } = Color.FromRgb(91, 145, 83);
    public Color SedimentaryBasinColor { get; init; } = Color.FromRgb(151, 178, 116);
    public Color DryBasinColor { get; init; } = Color.FromRgb(174, 157, 105);
    public Color DeltaCandidateColor { get; init; } = Color.FromRgb(119, 198, 139);
    public Color DesertPlateauCandidateColor { get; init; } = Color.FromRgb(168, 151, 94);
    public Color HighlandColor { get; init; } = Color.FromRgb(116, 145, 80);
    public Color UplandColor { get; init; } = Color.FromRgb(160, 144, 86);
    public Color MountainColor { get; init; } = Color.FromRgb(126, 118, 111);
    public Color SnowColor { get; init; } = Color.FromRgb(238, 241, 235);
}

public sealed class RiverRenderOptions
{
    public float Scale { get; init; } = 1;
    public bool DrawDebugMarkers { get; init; }
    public double Opacity { get; init; } = 1.0;
    public double MinRiverWidth { get; init; } = 0.35;
    public double MaxRiverWidth { get; init; } = 3.2;
    public double WidthLowPercentile { get; init; } = 0.10;
    public double WidthHighPercentile { get; init; } = 0.98;
    public double WidthGamma { get; init; } = 1.7;
    public double OutletMarkerRadius { get; init; } = 1.6;
    public Color PlainRiverColor { get; init; } = Color.FromRgba(35, 113, 188, 180);
    public Color MountainRiverColor { get; init; } = Color.FromRgba(90, 183, 220, 180);
    public Color RiftRiverColor { get; init; } = Color.FromRgba(43, 104, 174, 180);
    public Color DeltaRiverColor { get; init; } = Color.FromRgba(42, 151, 177, 180);
    public Color EndorheicRiverColor { get; init; } = Color.FromRgba(58, 137, 163, 180);
    public Color DeltaColor { get; init; } = Color.FromRgba(61, 180, 166, 180);
    public Color MarshDeltaColor { get; init; } = Color.FromRgba(72, 170, 126, 180);
    public Color InlandDeltaColor { get; init; } = Color.FromRgba(85, 150, 142, 180);
    public Color OutletColor { get; init; } = Color.FromRgba(230, 247, 255, 160);
}

public sealed class ClimateRenderOptions
{
    public float Scale { get; init; } = 1;
    public ClimateRenderMode Mode { get; init; } = ClimateRenderMode.Biomes;
    public bool DrawHillshade { get; init; } = true;
    public bool DrawRivers { get; init; } = true;
    public bool DrawRiverValleyAccents { get; init; } = true;
    public double LandReliefBlend { get; init; } = 0.24;
    public double HighlandReliefBlend { get; init; } = 0.34;
    public double MountainReliefBlend { get; init; } = 0.52;
    public double PlateauReliefBlend { get; init; } = 0.42;
    public double BasinReliefBlend { get; init; } = 0.32;
    public double MountainTintStrength { get; init; } = 0.42;
    public double IceOverlayStrength { get; init; } = 0.84;
    public double SnowOverlayThreshold { get; init; } = 0.08;
    public double RiverValleyOverlayStrength { get; init; } = 0.22;
    public double RiverValleyOverlayThreshold { get; init; } = 0.48;
    public double RiverValleyOverlayGamma { get; init; } = 1.85;
    public double WetlandOverlayStrength { get; init; } = 0.42;
    public double BiomeHillshadeStrength { get; init; } = 0.48;
    public double BiomeBoundaryBlend { get; init; } = 0.16;
    public double BiomeCenterSaturationBoost { get; init; } = 1.16;
    public double BiomeCenterValueBoost { get; init; } = 0.015;
    public double TextureStrength { get; init; } = 0.12;
    public double DesertWarmthStrength { get; init; } = 0.20;
    public double DesertDuneStrength { get; init; } = 0.18;
    public double DesertRockStrength { get; init; } = 0.22;
    public double MinRiverWidth { get; init; } = 0.38;
    public double MaxRiverWidth { get; init; } = 2.25;
    public double RiverValleyAccentMinWidth { get; init; } = 0.45;
    public double RiverValleyAccentMaxWidth { get; init; } = 1.65;
    public double RiverValleyAccentGamma { get; init; } = 1.35;
    public double RiverWidthLowPercentile { get; init; } = 0.06;
    public double RiverWidthHighPercentile { get; init; } = 0.98;
    public double RiverWidthGamma { get; init; } = 1.55;
    public double PresentationRiverOpacity { get; init; } = 0.82;
    public ElevationRenderOptions Elevation { get; init; } = new()
    {
        DrawHillshade = true,
        HillshadeStrength = 0.50,
        OceanHillshadeStrength = 0.06
    };
    public Color OceanColor { get; init; } = Color.FromRgb(42, 113, 162);
    public Color TropicalRainforestColor { get; init; } = Color.FromRgb(20, 117, 63);
    public Color MonsoonForestColor { get; init; } = Color.FromRgb(58, 151, 77);
    public Color DryTropicalForestColor { get; init; } = Color.FromRgb(113, 166, 68);
    public Color TropicalSeasonalForestColor { get; init; } = Color.FromRgb(96, 163, 82);
    public Color SavannaColor { get; init; } = Color.FromRgb(190, 186, 54);
    public Color OpenWoodlandColor { get; init; } = Color.FromRgb(132, 168, 67);
    public Color HotDesertColor { get; init; } = Color.FromRgb(229, 161, 70);
    public Color SemiDesertColor { get; init; } = Color.FromRgb(224, 196, 119);
    public Color RockyDesertColor { get; init; } = Color.FromRgb(150, 126, 105);
    public Color SaltFlatColor { get; init; } = Color.FromRgb(218, 214, 188);
    public Color ColdDesertColor { get; init; } = Color.FromRgb(155, 158, 145);
    public Color SteppeColor { get; init; } = Color.FromRgb(197, 170, 66);
    public Color XericShrublandColor { get; init; } = Color.FromRgb(163, 146, 105);
    public Color MediterraneanShrublandColor { get; init; } = Color.FromRgb(101, 132, 61);
    public Color TemperateGrasslandColor { get; init; } = Color.FromRgb(139, 198, 84);
    public Color TemperateForestColor { get; init; } = Color.FromRgb(70, 143, 83);
    public Color TemperateRainforestColor { get; init; } = Color.FromRgb(40, 128, 106);
    public Color BorealForestColor { get; init; } = Color.FromRgb(61, 111, 86);
    public Color TundraColor { get; init; } = Color.FromRgb(151, 161, 133);
    public Color PolarDesertColor { get; init; } = Color.FromRgb(197, 201, 190);
    public Color IceColor { get; init; } = Color.FromRgb(235, 242, 243);
    public Color AlpineTundraColor { get; init; } = Color.FromRgb(144, 137, 126);
    public Color WetlandColor { get; init; } = Color.FromRgb(72, 143, 128);
    public Color FloodplainColor { get; init; } = Color.FromRgb(94, 163, 112);
    public Color MarshColor { get; init; } = Color.FromRgb(54, 137, 121);
    public Color MangroveColor { get; init; } = Color.FromRgb(25, 111, 87);
    public Color MontaneForestColor { get; init; } = Color.FromRgb(67, 126, 84);
    public Color CloudForestColor { get; init; } = Color.FromRgb(42, 130, 104);
    public Color SnowyMountainColor { get; init; } = Color.FromRgb(202, 210, 205);
    public Color VolcanicBadlandsColor { get; init; } = Color.FromRgb(121, 102, 91);
    public Color MountainColor { get; init; } = Color.FromRgb(128, 124, 112);
    public Color DryMountainColor { get; init; } = Color.FromRgb(158, 139, 106);
    public Color ColdMountainColor { get; init; } = Color.FromRgb(142, 145, 139);
    public Color RiverValleyOverlayColor { get; init; } = Color.FromRgb(62, 184, 104);
    public Color WetlandOverlayColor { get; init; } = Color.FromRgb(43, 139, 132);
    public Color RiverValleyAccentColor { get; init; } = Color.FromRgb(75, 214, 119);
    public Color RiverValleyMajorAccentColor { get; init; } = Color.FromRgb(67, 188, 169);
    public Color PresentationRiverColor { get; init; } = Color.FromRgb(36, 119, 205);
    public Color PresentationMountainRiverColor { get; init; } = Color.FromRgb(82, 184, 226);
    public Color PresentationDeltaRiverColor { get; init; } = Color.FromRgb(31, 158, 179);
    public Color DesertSunColor { get; init; } = Color.FromRgb(246, 188, 76);
    public Color DesertDuneColor { get; init; } = Color.FromRgb(255, 222, 130);
    public Color DesertRockColor { get; init; } = Color.FromRgb(111, 95, 88);
    public Color TextureLightColor { get; init; } = Color.FromRgb(255, 246, 198);
    public Color TextureDarkColor { get; init; } = Color.FromRgb(34, 50, 38);
    public Color WetlandTextureLightColor { get; init; } = Color.FromRgb(110, 192, 166);
    public Color WetlandTextureDarkColor { get; init; } = Color.FromRgb(22, 76, 91);
    public Color UnknownBiomeColor { get; init; } = Color.DarkGray;
    public Color ExtremeColdColor { get; init; } = Color.FromRgb(51, 85, 155);
    public Color ColdColor { get; init; } = Color.FromRgb(106, 169, 203);
    public Color CoolColor { get; init; } = Color.FromRgb(121, 177, 128);
    public Color WarmColor { get; init; } = Color.FromRgb(218, 180, 91);
    public Color HotColor { get; init; } = Color.FromRgb(191, 76, 58);
    public Color DryColor { get; init; } = Color.FromRgb(210, 184, 112);
    public Color WetColor { get; init; } = Color.FromRgb(42, 124, 112);
    public Color RainColor { get; init; } = Color.FromRgb(47, 116, 187);
    public Color LowSeasonalityColor { get; init; } = Color.FromRgb(82, 158, 153);
    public Color HighSeasonalityColor { get; init; } = Color.FromRgb(176, 91, 75);
    public Color LowHabitabilityColor { get; init; } = Color.FromRgb(70, 75, 78);
    public Color HighHabitabilityColor { get; init; } = Color.FromRgb(107, 184, 111);
    public Color LowAgricultureColor { get; init; } = Color.FromRgb(92, 83, 70);
    public Color HighAgricultureColor { get; init; } = Color.FromRgb(194, 187, 83);
    public Color NoIceColor { get; init; } = Color.FromRgb(58, 111, 134);
}

public sealed class TectonicFeatureRenderOptions : TectonicPlateRenderOptions
{
    public TectonicFeatureRenderOptions()
    {
        PlateBoundaryColor = Color.FromRgba(255, 80, 70, 150);
        PlateBoundaryWidth = 0.75f;
    }

    public TectonicFeatureRenderMode Mode { get; init; } = TectonicFeatureRenderMode.Summary;
    public bool DrawPlateBoundaries { get; init; }
    public int MaxConnectedFeatureStep { get; init; } = 6;
    public int MaxBoundaryDerivedPointMarkers { get; init; } = 1600;
    public int MaxBoundaryDerivedSummaryMarkers { get; init; } = 90;
    public int MaxHistoricalSummaryMarkers { get; init; } = 260;
    public int MaxHotspotSummaryMarkers { get; init; } = 1;
    public int MinBoundaryDerivedLinePoints { get; init; } = 24;
    public int MinHistoricalLinePoints { get; init; } = 8;
    public int MinOrogenLinePoints { get; init; } = 28;
    public double MinConnectedStepRatio { get; init; } = 0.75;
    public bool DrawFeatureFields { get; init; } = true;
    public double MinimumVisibleFieldStrength { get; init; } = 0.08;
    public double UpliftFieldThreshold { get; init; } = 0.42;
    public double SubsidenceFieldThreshold { get; init; } = 0.32;
    public double VolcanismFieldThreshold { get; init; } = 0.28;
    public double HeatFlowFieldThreshold { get; init; } = 0.34;
    public double SeismicityFieldThreshold { get; init; } = 0.68;
    public double FieldSaturation { get; init; } = 1.0;
    public double FieldGamma { get; init; } = 1.35;
    public double FieldIntensity { get; init; } = 0.34;
    public double SeismicityFieldIntensity { get; init; } = 0.12;
    public float FeatureWidth { get; init; } = 1.25f;
    public float MajorFeatureWidth { get; init; } = 1.75f;
    public float OrogenFeatureWidth { get; init; } = 1.1f;
    public float PointFeatureWidth { get; init; } = 3;
    public bool DrawUpliftFieldInSummary { get; init; } = false;
    public bool DrawSeismicityFieldInSummary { get; init; } = false;
    public Color BackgroundColor { get; init; } = Color.FromRgb(23, 36, 47);
    public Color UpliftColor { get; init; } = Color.FromRgb(226, 164, 72);
    public Color SubsidenceColor { get; init; } = Color.FromRgb(53, 113, 181);
    public Color VolcanismColor { get; init; } = Color.FromRgb(235, 69, 67);
    public Color SeismicityColor { get; init; } = Color.FromRgb(204, 176, 91);
    public Color HeatFlowColor { get; init; } = Color.FromRgb(213, 102, 183);
    public Color RidgeColor { get; init; } = Color.FromRgb(88, 214, 226);
    public Color TrenchColor { get; init; } = Color.FromRgb(35, 24, 30);
    public Color ArcColor { get; init; } = Color.FromRgb(255, 140, 74);
    public Color RiftColor { get; init; } = Color.FromRgb(241, 77, 143);
    public Color SutureColor { get; init; } = Color.FromRgb(126, 113, 88);
    public Color OrogenColor { get; init; } = Color.FromRgb(219, 158, 74);
    public Color CratonColor { get; init; } = Color.FromRgb(132, 176, 118);
    public Color PassiveMarginColor { get; init; } = Color.FromRgb(142, 206, 171);
    public Color HotspotColor { get; init; } = Color.FromRgb(255, 243, 119);
    public Color SedimentaryBasinColor { get; init; } = Color.FromRgb(77, 125, 151);
    public Color MicroplateColor { get; init; } = Color.FromRgb(255, 255, 255);
    public Color BackArcBasinColor { get; init; } = Color.FromRgb(83, 152, 213);
    public Color IslandColor { get; init; } = Color.White;
    public Color UnknownFeatureColor { get; init; } = Color.LightGray;
}

public readonly record struct RiverWidthScale(double Low, double High);

public enum ClimateRenderMode
{
    Biomes,
    DebugBiomes,
    Temperature,
    Moisture,
    BiomeMoisture,
    Precipitation,
    Seasonality,
    Habitability,
    Agriculture,
    Ice
}

public enum ElevationRenderMode
{
    FinalElevation,
    BaseElevation,
    TectonicContribution,
    Roughness,
    ErosionMask,
    TerrainZones,
    MountainInfluence,
    BasinInfluence
}

public enum TectonicFeatureRenderMode
{
    Summary,
    Diagnostic
}
