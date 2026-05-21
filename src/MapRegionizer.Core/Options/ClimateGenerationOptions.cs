namespace MapRegionizer.Core.Options;

public sealed class ClimateGenerationOptions
{
    public double PolarLatitudeMargin { get; init; } = 0.05;
    public double EquatorTemperatureCelsius { get; init; } = 28.0;
    public double PoleCoolingCelsius { get; init; } = 55.0;
    public double LatitudeCurveExponent { get; init; } = 1.35;
    public double LapseRateCelsiusPerMeter { get; init; } = 0.0045;
    public double BaseSeasonalityCelsius { get; init; } = 6.0;
    public double LatitudeSeasonalityCelsius { get; init; } = 18.0;
    public double ContinentalSeasonalityCelsius { get; init; } = 13.0;
    public double ContinentalSummerBoostCelsius { get; init; } = 5.0;
    public double ContinentalWinterPenaltyCelsius { get; init; } = 8.0;
    public int ContinentalityDistanceCells { get; init; } = 96;
    public int LargeLakeMinCellCount { get; init; } = 220;
    public double OceanEvaporation { get; init; } = 1.30;
    public double LakeEvaporation { get; init; } = 0.68;
    public double LandEvapotranspiration { get; init; } = 0.08;
    public double MoistureRetention { get; init; } = 0.86;
    public double BaseRainfallEfficiency { get; init; } = 0.22;
    public double OrographicStrength { get; init; } = 0.84;
    public double DescentDrying { get; init; } = 0.27;
    public double ContinentalDrying { get; init; } = 0.17;
    public double RiverMoistureBonus { get; init; } = 0.26;
    public double RiverAgricultureBonus { get; init; } = 0.54;
    public double MonsoonRainStrength { get; init; } = 0.38;
    public double DrySeasonStrength { get; init; } = 0.22;
    public int MonsoonOceanDistanceCells { get; init; } = 30;
    public int MonsoonCoastProbeCells { get; init; } = 10;
    public double SnowMeltThresholdCelsius { get; init; } = 2.0;
    public double SnowPrecipitationScale { get; init; } = 0.42;

    public void Validate()
    {
        if (PolarLatitudeMargin < 0 || PolarLatitudeMargin >= 1) throw new ArgumentOutOfRangeException(nameof(PolarLatitudeMargin), "Polar latitude margin must be in [0, 1).");
        if (LatitudeCurveExponent <= 0) throw new ArgumentOutOfRangeException(nameof(LatitudeCurveExponent), "Latitude curve exponent must be greater than zero.");
        if (LapseRateCelsiusPerMeter < 0) throw new ArgumentOutOfRangeException(nameof(LapseRateCelsiusPerMeter), "Lapse rate cannot be negative.");
        if (ContinentalityDistanceCells <= 0) throw new ArgumentOutOfRangeException(nameof(ContinentalityDistanceCells), "Continentality distance must be greater than zero.");
        if (LargeLakeMinCellCount < 0) throw new ArgumentOutOfRangeException(nameof(LargeLakeMinCellCount), "Large lake threshold cannot be negative.");
        if (OceanEvaporation < 0) throw new ArgumentOutOfRangeException(nameof(OceanEvaporation), "Ocean evaporation cannot be negative.");
        if (LakeEvaporation < 0) throw new ArgumentOutOfRangeException(nameof(LakeEvaporation), "Lake evaporation cannot be negative.");
        if (LandEvapotranspiration < 0) throw new ArgumentOutOfRangeException(nameof(LandEvapotranspiration), "Land evapotranspiration cannot be negative.");
        if (MoistureRetention < 0 || MoistureRetention > 1) throw new ArgumentOutOfRangeException(nameof(MoistureRetention), "Moisture retention must be in [0, 1].");
        if (BaseRainfallEfficiency < 0) throw new ArgumentOutOfRangeException(nameof(BaseRainfallEfficiency), "Base rainfall efficiency cannot be negative.");
        if (OrographicStrength < 0) throw new ArgumentOutOfRangeException(nameof(OrographicStrength), "Orographic strength cannot be negative.");
        if (DescentDrying < 0) throw new ArgumentOutOfRangeException(nameof(DescentDrying), "Descent drying cannot be negative.");
        if (ContinentalDrying < 0) throw new ArgumentOutOfRangeException(nameof(ContinentalDrying), "Continental drying cannot be negative.");
        if (RiverMoistureBonus < 0) throw new ArgumentOutOfRangeException(nameof(RiverMoistureBonus), "River moisture bonus cannot be negative.");
        if (RiverAgricultureBonus < 0) throw new ArgumentOutOfRangeException(nameof(RiverAgricultureBonus), "River agriculture bonus cannot be negative.");
        if (MonsoonRainStrength < 0) throw new ArgumentOutOfRangeException(nameof(MonsoonRainStrength), "Monsoon rain strength cannot be negative.");
        if (DrySeasonStrength < 0) throw new ArgumentOutOfRangeException(nameof(DrySeasonStrength), "Dry-season strength cannot be negative.");
        if (MonsoonOceanDistanceCells <= 0) throw new ArgumentOutOfRangeException(nameof(MonsoonOceanDistanceCells), "Monsoon ocean distance must be greater than zero.");
        if (MonsoonCoastProbeCells < 0) throw new ArgumentOutOfRangeException(nameof(MonsoonCoastProbeCells), "Monsoon coast probe cannot be negative.");
        if (SnowPrecipitationScale <= 0) throw new ArgumentOutOfRangeException(nameof(SnowPrecipitationScale), "Snow precipitation scale must be greater than zero.");
    }
}
