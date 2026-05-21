namespace MapRegionizer.Core.Domain;

public sealed class ClimateMap
{
    private readonly double[] _latitudeNorm;
    private readonly double[] _meanAnnualTemperature;
    private readonly double[] _summerTemperature;
    private readonly double[] _winterTemperature;
    private readonly double[] _seasonality;
    private readonly double[] _atmosphericMoisture;
    private readonly double[] _precipitation;
    private readonly double[] _moisture;
    private readonly double[] _biomeMoisture;
    private readonly double[] _rainShadow;
    private readonly double[] _monsoonInfluence;
    private readonly double[] _riverValleyInfluence;
    private readonly double[] _wetlandInfluence;
    private readonly double[] _snowOverlay;
    private readonly double[] _mountainOverlay;
    private readonly double[] _iceScore;
    private readonly double[] _habitability;
    private readonly double[] _agriculturalPotential;
    private readonly byte[] _climateClasses;
    private readonly byte[] _biomes;

    public int Width { get; }
    public int Height { get; }

    public ClimateMap(
        int width,
        int height,
        double[] latitudeNorm,
        double[] meanAnnualTemperature,
        double[] summerTemperature,
        double[] winterTemperature,
        double[] seasonality,
        double[] atmosphericMoisture,
        double[] precipitation,
        double[] moisture,
        double[] biomeMoisture,
        double[] rainShadow,
        double[] monsoonInfluence,
        double[] riverValleyInfluence,
        double[] wetlandInfluence,
        double[] snowOverlay,
        double[] mountainOverlay,
        double[] iceScore,
        double[] habitability,
        double[] agriculturalPotential,
        byte[] climateClasses,
        byte[] biomes)
    {
        var expectedLength = width * height;
        ValidateLength(latitudeNorm, expectedLength, nameof(latitudeNorm));
        ValidateLength(meanAnnualTemperature, expectedLength, nameof(meanAnnualTemperature));
        ValidateLength(summerTemperature, expectedLength, nameof(summerTemperature));
        ValidateLength(winterTemperature, expectedLength, nameof(winterTemperature));
        ValidateLength(seasonality, expectedLength, nameof(seasonality));
        ValidateLength(atmosphericMoisture, expectedLength, nameof(atmosphericMoisture));
        ValidateLength(precipitation, expectedLength, nameof(precipitation));
        ValidateLength(moisture, expectedLength, nameof(moisture));
        ValidateLength(biomeMoisture, expectedLength, nameof(biomeMoisture));
        ValidateLength(rainShadow, expectedLength, nameof(rainShadow));
        ValidateLength(monsoonInfluence, expectedLength, nameof(monsoonInfluence));
        ValidateLength(riverValleyInfluence, expectedLength, nameof(riverValleyInfluence));
        ValidateLength(wetlandInfluence, expectedLength, nameof(wetlandInfluence));
        ValidateLength(snowOverlay, expectedLength, nameof(snowOverlay));
        ValidateLength(mountainOverlay, expectedLength, nameof(mountainOverlay));
        ValidateLength(iceScore, expectedLength, nameof(iceScore));
        ValidateLength(habitability, expectedLength, nameof(habitability));
        ValidateLength(agriculturalPotential, expectedLength, nameof(agriculturalPotential));
        ValidateLength(climateClasses, expectedLength, nameof(climateClasses));
        ValidateLength(biomes, expectedLength, nameof(biomes));

        Width = width;
        Height = height;
        _latitudeNorm = latitudeNorm;
        _meanAnnualTemperature = meanAnnualTemperature;
        _summerTemperature = summerTemperature;
        _winterTemperature = winterTemperature;
        _seasonality = seasonality;
        _atmosphericMoisture = atmosphericMoisture;
        _precipitation = precipitation;
        _moisture = moisture;
        _biomeMoisture = biomeMoisture;
        _rainShadow = rainShadow;
        _monsoonInfluence = monsoonInfluence;
        _riverValleyInfluence = riverValleyInfluence;
        _wetlandInfluence = wetlandInfluence;
        _snowOverlay = snowOverlay;
        _mountainOverlay = mountainOverlay;
        _iceScore = iceScore;
        _habitability = habitability;
        _agriculturalPotential = agriculturalPotential;
        _climateClasses = climateClasses;
        _biomes = biomes;
    }

    public double GetLatitudeNorm(int x, int y) => _latitudeNorm[y * Width + x];

    public double GetMeanAnnualTemperature(int x, int y) => _meanAnnualTemperature[y * Width + x];

    public double GetSummerTemperature(int x, int y) => _summerTemperature[y * Width + x];

    public double GetWinterTemperature(int x, int y) => _winterTemperature[y * Width + x];

    public double GetSeasonality(int x, int y) => _seasonality[y * Width + x];

    public double GetAtmosphericMoisture(int x, int y) => _atmosphericMoisture[y * Width + x];

    public double GetPrecipitation(int x, int y) => _precipitation[y * Width + x];

    public double GetMoisture(int x, int y) => _moisture[y * Width + x];

    public double GetBiomeMoisture(int x, int y) => _biomeMoisture[y * Width + x];

    public double GetRainShadow(int x, int y) => _rainShadow[y * Width + x];

    public double GetMonsoonInfluence(int x, int y) => _monsoonInfluence[y * Width + x];

    public double GetRiverValleyInfluence(int x, int y) => _riverValleyInfluence[y * Width + x];

    public double GetWetlandInfluence(int x, int y) => _wetlandInfluence[y * Width + x];

    public double GetSnowOverlay(int x, int y) => _snowOverlay[y * Width + x];

    public double GetMountainOverlay(int x, int y) => _mountainOverlay[y * Width + x];

    public double GetIceScore(int x, int y) => _iceScore[y * Width + x];

    public double GetHabitability(int x, int y) => _habitability[y * Width + x];

    public double GetAgriculturalPotential(int x, int y) => _agriculturalPotential[y * Width + x];

    public ClimateClassKind GetClimateClass(int x, int y) => (ClimateClassKind)_climateClasses[y * Width + x];

    public BiomeKind GetBiome(int x, int y) => (BiomeKind)_biomes[y * Width + x];

    public ReadOnlySpan<double> LatitudeNormSpan => _latitudeNorm;
    public ReadOnlySpan<double> MeanAnnualTemperatureSpan => _meanAnnualTemperature;
    public ReadOnlySpan<double> SummerTemperatureSpan => _summerTemperature;
    public ReadOnlySpan<double> WinterTemperatureSpan => _winterTemperature;
    public ReadOnlySpan<double> SeasonalitySpan => _seasonality;
    public ReadOnlySpan<double> AtmosphericMoistureSpan => _atmosphericMoisture;
    public ReadOnlySpan<double> PrecipitationSpan => _precipitation;
    public ReadOnlySpan<double> MoistureSpan => _moisture;
    public ReadOnlySpan<double> BiomeMoistureSpan => _biomeMoisture;
    public ReadOnlySpan<double> RainShadowSpan => _rainShadow;
    public ReadOnlySpan<double> MonsoonInfluenceSpan => _monsoonInfluence;
    public ReadOnlySpan<double> RiverValleyInfluenceSpan => _riverValleyInfluence;
    public ReadOnlySpan<double> WetlandInfluenceSpan => _wetlandInfluence;
    public ReadOnlySpan<double> SnowOverlaySpan => _snowOverlay;
    public ReadOnlySpan<double> MountainOverlaySpan => _mountainOverlay;
    public ReadOnlySpan<double> IceScoreSpan => _iceScore;
    public ReadOnlySpan<double> HabitabilitySpan => _habitability;
    public ReadOnlySpan<double> AgriculturalPotentialSpan => _agriculturalPotential;
    public ReadOnlySpan<byte> ClimateClassSpan => _climateClasses;
    public ReadOnlySpan<byte> BiomeSpan => _biomes;

    private static void ValidateLength<T>(T[] array, int expectedLength, string parameterName)
    {
        if (array.Length != expectedLength)
            throw new ArgumentException($"Array length must be {expectedLength}", parameterName);
    }
}

public enum ClimateClassKind
{
    Ocean,
    TropicalWet,
    TropicalSeasonal,
    HotArid,
    SemiArid,
    WarmTemperate,
    TemperateWet,
    Continental,
    Boreal,
    Tundra,
    PolarDesert,
    IceCap,
    Alpine
}

public enum BiomeKind
{
    Ocean,
    TropicalRainforest,
    MonsoonForest,
    DryTropicalForest,
    TropicalSeasonalForest,
    Savanna,
    OpenWoodland,
    HotDesert,
    SemiDesert,
    RockyDesert,
    SaltFlat,
    ColdDesert,
    Steppe,
    XericShrubland,
    MediterraneanShrubland,
    TemperateGrassland,
    TemperateForest,
    TemperateRainforest,
    BorealForest,
    Tundra,
    PolarDesert,
    IceSheet,
    AlpineTundra,
    Wetland,
    Floodplain,
    Marsh,
    Mangrove,
    MontaneForest,
    CloudForest,
    SnowyMountain,
    VolcanicBadlands
}
