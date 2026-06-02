namespace MapRegionizer.Core.Options;

public sealed class HydrologyGenerationOptions
{
    public double RiverDensity { get; init; } = 1;
    public double MountainRiverDensity { get; init; } = 0.58;
    public int MaxMountainSourcesPerCluster { get; init; } = 0;
    public int MinMountainSourceSpacing { get; init; } = 0;
    public double MajorRiverCountMultiplier { get; init; } = 1.5;
    public double LongRiverCountMultiplier { get; init; } = 1.3;
    public double TributaryDensity { get; init; } = 1.0;
    public double MajorRiverTributaryMultiplier { get; init; } = 1.0;
    public double LakeOutletInflowForceMultiplier { get; init; } = 0.45;
    public double EndorheicBasinChance { get; init; } = 0.22;
    public int MaxEndorheicBasins { get; init; } = 3;
    public double DeltaFrequency { get; init; } = 0.8;
    public double MeanderStrength { get; init; } = 0.65;
    public double LakeOutletStrictness { get; init; } = 0.35;
    public bool PreserveCoastline { get; init; } = true;
    public bool AllowRiverCarving { get; init; } = false;

    public void Validate()
    {
        if (RiverDensity < 0) throw new ArgumentOutOfRangeException(nameof(RiverDensity), "River density cannot be negative.");
        if (MountainRiverDensity < 0) throw new ArgumentOutOfRangeException(nameof(MountainRiverDensity), "Mountain river density cannot be negative.");
        if (MaxMountainSourcesPerCluster < 0) throw new ArgumentOutOfRangeException(nameof(MaxMountainSourcesPerCluster), "Mountain source cluster cap cannot be negative.");
        if (MinMountainSourceSpacing < 0) throw new ArgumentOutOfRangeException(nameof(MinMountainSourceSpacing), "Mountain source spacing cannot be negative.");
        if (MajorRiverCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(MajorRiverCountMultiplier), "Major river count multiplier cannot be negative.");
        if (LongRiverCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(LongRiverCountMultiplier), "Long river count multiplier cannot be negative.");
        if (TributaryDensity < 0) throw new ArgumentOutOfRangeException(nameof(TributaryDensity), "Tributary density cannot be negative.");
        if (MajorRiverTributaryMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(MajorRiverTributaryMultiplier), "Major river tributary multiplier cannot be negative.");
        if (LakeOutletInflowForceMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(LakeOutletInflowForceMultiplier), "Lake outlet inflow force multiplier cannot be negative.");
        if (EndorheicBasinChance < 0 || EndorheicBasinChance > 1) throw new ArgumentOutOfRangeException(nameof(EndorheicBasinChance), "Endorheic basin chance must be in [0, 1].");
        if (MaxEndorheicBasins < 0) throw new ArgumentOutOfRangeException(nameof(MaxEndorheicBasins), "Max endorheic basins cannot be negative.");
        if (DeltaFrequency < 0) throw new ArgumentOutOfRangeException(nameof(DeltaFrequency), "Delta frequency cannot be negative.");
        if (MeanderStrength < 0 || MeanderStrength > 1) throw new ArgumentOutOfRangeException(nameof(MeanderStrength), "Meander strength must be in [0, 1].");
        if (LakeOutletStrictness < 0 || LakeOutletStrictness > 1) throw new ArgumentOutOfRangeException(nameof(LakeOutletStrictness), "Lake outlet strictness must be in [0, 1].");
    }
}
