namespace MapRegionizer.Core.Options;

public sealed class ElevationGenerationOptions
{
    public double ReliefScale { get; init; } = 1.0;
    public double Mountaininess { get; init; } = 0.8;
    public double Erosion { get; init; } = 0.35;
    public double Roughness { get; init; } = 0.45;
    public double SeaDepthScale { get; init; } = 1.0;
    public double ShelfWidthFactor { get; init; } = 1.0;
    public double VolcanismInfluence { get; init; } = 0.6;
    public double SmallIslandReliefFactor { get; init; } = 0.55;
    public double RiftInfluence { get; init; } = 0.5;
    public bool GenerateSmallLakes { get; init; } = false;
    public double SmallLakeCountMultiplier { get; init; } = 0.3;
    public double SmallLakeScatterMultiplier { get; init; } = 1.0;
    public double SmallLakeSizeMultiplier { get; init; } = 0.5;
    public bool PreserveMaskCoastline { get; init; } = true;
    public bool PreserveOceanCoastline { get; init; } = true;
    public bool PreserveInlandWaterMask { get; init; } = true;
    public bool AllowLakeExpansion { get; init; } = false;
    public bool AllowLakeDrainage { get; init; } = false;
    public double MaxElevationMeters { get; init; } = 8500;
    public double MinOceanDepthMeters { get; init; } = -7000;
    public double MinLandElevationMeters { get; init; } = 1;
    public double MaxSeaElevationMeters { get; init; } = -1;
    public double LakeSurfacePercentile { get; init; } = 0.05;
    public double MinLakeSurfaceMarginMeters { get; init; } = 0.5;
    public double MaxLakeSurfaceMarginMeters { get; init; } = 8.0;
    public double MinLakeDepthMeters { get; init; } = 1.0;
    public double MaxLakeDepthMeters { get; init; } = 80.0;
    public double MaxRiftLakeDepthMeters { get; init; } = 320.0;
    public double MaxInlandSeaDepthMeters { get; init; } = 220.0;
    public double MountainLakeElevationMeters { get; init; } = 900.0;
    public double PlateauLakeElevationMeters { get; init; } = 1400.0;
    public double MountainLakeReliefMeters { get; init; } = 260.0;
    public double LakeTectonicFaultThreshold { get; init; } = 0.28;
    public double LakeVolcanicInfluenceThreshold { get; init; } = 0.34;
    public double PlainLakeKarstChance { get; init; } = 0.12;
    public double LakeDepthRandomnessMin { get; init; } = 0.8;
    public double LakeDepthRandomnessMax { get; init; } = 1.2;
    public int LargeLakeDepressionMinCellCount { get; init; } = 900;

    public void Validate()
    {
        if (ReliefScale < 0) throw new ArgumentOutOfRangeException(nameof(ReliefScale), "Relief scale cannot be negative.");
        if (Mountaininess < 0) throw new ArgumentOutOfRangeException(nameof(Mountaininess), "Mountaininess cannot be negative.");
        if (Erosion < 0 || Erosion > 1) throw new ArgumentOutOfRangeException(nameof(Erosion), "Erosion must be in [0, 1].");
        if (Roughness < 0 || Roughness > 1) throw new ArgumentOutOfRangeException(nameof(Roughness), "Roughness must be in [0, 1].");
        if (SeaDepthScale < 0) throw new ArgumentOutOfRangeException(nameof(SeaDepthScale), "Sea depth scale cannot be negative.");
        if (ShelfWidthFactor < 0) throw new ArgumentOutOfRangeException(nameof(ShelfWidthFactor), "Shelf width factor cannot be negative.");
        if (VolcanismInfluence < 0) throw new ArgumentOutOfRangeException(nameof(VolcanismInfluence), "Volcanism influence cannot be negative.");
        if (SmallIslandReliefFactor < 0) throw new ArgumentOutOfRangeException(nameof(SmallIslandReliefFactor), "Small island relief factor cannot be negative.");
        if (RiftInfluence < 0) throw new ArgumentOutOfRangeException(nameof(RiftInfluence), "Rift influence cannot be negative.");
        if (SmallLakeCountMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeCountMultiplier), "Small lake count multiplier cannot be negative.");
        if (SmallLakeScatterMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeScatterMultiplier), "Small lake scatter multiplier cannot be negative.");
        if (SmallLakeSizeMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(SmallLakeSizeMultiplier), "Small lake size multiplier must be greater than zero.");
        if (LakeSurfacePercentile < 0 || LakeSurfacePercentile > 1) throw new ArgumentOutOfRangeException(nameof(LakeSurfacePercentile), "Lake surface percentile must be in [0, 1].");
        if (MinLakeSurfaceMarginMeters < 0) throw new ArgumentOutOfRangeException(nameof(MinLakeSurfaceMarginMeters), "Minimum lake surface margin cannot be negative.");
        if (MaxLakeSurfaceMarginMeters < MinLakeSurfaceMarginMeters) throw new ArgumentOutOfRangeException(nameof(MaxLakeSurfaceMarginMeters), "Maximum lake surface margin cannot be below minimum margin.");
        if (MinLakeDepthMeters <= 0) throw new ArgumentOutOfRangeException(nameof(MinLakeDepthMeters), "Minimum lake depth must be greater than zero.");
        if (MaxLakeDepthMeters < MinLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxLakeDepthMeters), "Maximum lake depth cannot be below minimum lake depth.");
        if (MaxRiftLakeDepthMeters < MaxLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxRiftLakeDepthMeters), "Maximum rift lake depth cannot be below maximum lake depth.");
        if (MaxInlandSeaDepthMeters < MaxLakeDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxInlandSeaDepthMeters), "Maximum inland sea depth cannot be below maximum lake depth.");
        if (MountainLakeElevationMeters < 0) throw new ArgumentOutOfRangeException(nameof(MountainLakeElevationMeters), "Mountain lake elevation threshold cannot be negative.");
        if (PlateauLakeElevationMeters < MountainLakeElevationMeters) throw new ArgumentOutOfRangeException(nameof(PlateauLakeElevationMeters), "Plateau lake elevation threshold should be greater than or equal to mountain lake elevation threshold.");
        if (MountainLakeReliefMeters < 0) throw new ArgumentOutOfRangeException(nameof(MountainLakeReliefMeters), "Mountain lake relief threshold cannot be negative.");
        if (LakeTectonicFaultThreshold < 0 || LakeTectonicFaultThreshold > 1) throw new ArgumentOutOfRangeException(nameof(LakeTectonicFaultThreshold), "Lake tectonic fault threshold must be in [0, 1].");
        if (LakeVolcanicInfluenceThreshold < 0 || LakeVolcanicInfluenceThreshold > 1) throw new ArgumentOutOfRangeException(nameof(LakeVolcanicInfluenceThreshold), "Lake volcanic influence threshold must be in [0, 1].");
        if (PlainLakeKarstChance < 0 || PlainLakeKarstChance > 1) throw new ArgumentOutOfRangeException(nameof(PlainLakeKarstChance), "Plain lake karst chance must be in [0, 1].");
        if (LakeDepthRandomnessMin <= 0) throw new ArgumentOutOfRangeException(nameof(LakeDepthRandomnessMin), "Minimum lake depth randomness must be greater than zero.");
        if (LakeDepthRandomnessMax < LakeDepthRandomnessMin) throw new ArgumentOutOfRangeException(nameof(LakeDepthRandomnessMax), "Maximum lake depth randomness cannot be below minimum randomness.");
        if (LargeLakeDepressionMinCellCount < 0) throw new ArgumentOutOfRangeException(nameof(LargeLakeDepressionMinCellCount), "Large lake depression threshold cannot be negative.");
        if (MaxElevationMeters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "Maximum elevation must be greater than zero.");
        if (MinOceanDepthMeters >= 0) throw new ArgumentOutOfRangeException(nameof(MinOceanDepthMeters), "Minimum ocean depth must be below zero.");
        if (MinLandElevationMeters < 0) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot be negative.");
        if (MaxSeaElevationMeters > 0) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be above zero.");
        if (MinLandElevationMeters > MaxElevationMeters) throw new ArgumentOutOfRangeException(nameof(MinLandElevationMeters), "Minimum land elevation cannot exceed maximum elevation.");
        if (MaxSeaElevationMeters < MinOceanDepthMeters) throw new ArgumentOutOfRangeException(nameof(MaxSeaElevationMeters), "Maximum sea elevation cannot be below minimum ocean depth.");
    }
}
