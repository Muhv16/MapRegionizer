namespace MapRegionizer.Core.Options;

public sealed class WaterBodyClassificationOptions
{
    public double OceanSeaMinAreaRatio { get; init; } = 0.12;
    public double InlandSeaMinAreaRatio { get; init; } = 0.015;
    public int OceanSeaNearOceanMaxDistanceCells { get; init; } = 13;

    public void Validate()
    {
        if (OceanSeaMinAreaRatio < 0 || OceanSeaMinAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(OceanSeaMinAreaRatio), "Ocean-sea minimum area ratio must be in [0, 1].");
        if (InlandSeaMinAreaRatio < 0 || InlandSeaMinAreaRatio > 1) throw new ArgumentOutOfRangeException(nameof(InlandSeaMinAreaRatio), "Inland-sea minimum area ratio must be in [0, 1].");
        if (OceanSeaMinAreaRatio < InlandSeaMinAreaRatio) throw new ArgumentOutOfRangeException(nameof(OceanSeaMinAreaRatio), "Ocean-sea minimum area ratio should be greater than or equal to inland-sea minimum area ratio.");
        if (OceanSeaNearOceanMaxDistanceCells < 0) throw new ArgumentOutOfRangeException(nameof(OceanSeaNearOceanMaxDistanceCells), "Ocean-sea near-ocean distance cannot be negative.");
    }
}
