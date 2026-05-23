namespace MapRegionizer.Core.Terrain;

internal sealed class CoastalFieldBuilder
{
    public CoastalFields Build(ElevationInput context) => new(context.ShelfWidth, context.InlandScale, context.DeepOceanScale);
}

internal sealed record CoastalFields(double ShelfWidth, double InlandScale, double DeepOceanScale);
