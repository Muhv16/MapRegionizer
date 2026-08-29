namespace MapRegionizer.Core.Options;

[Obsolete("Use MapGenerationOptions.Spatial. Projection mode is retained only for legacy compatibility.")]
public enum MapProjectionMode
{
    EquirectangularWorld,
    Flat,
    Regional
}
