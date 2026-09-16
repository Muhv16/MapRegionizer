using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>
/// Editable geography authored in map coordinates.  The draft deliberately
/// stores region faces separately from the final <see cref="MapRegionizer.Core.Regions.RegionDraft"/>
/// because it describes the coastline as well as the region subdivision.
/// </summary>
public sealed record ManualMapDraft
{
    public ManualMapDraft(
        int gridWidth,
        int gridHeight,
        IReadOnlyList<ManualMapVertex> vertices,
        IReadOnlyList<ManualRegionFace> regions)
    {
        if (gridWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridWidth), "Grid width must be greater than zero.");
        if (gridHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridHeight), "Grid height must be greater than zero.");

        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(regions);

        GridWidth = gridWidth;
        GridHeight = gridHeight;
        Vertices = vertices.ToArray();
        Regions = regions.ToArray();
    }

    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public IReadOnlyList<ManualMapVertex> Vertices { get; init; }
    public IReadOnlyList<ManualRegionFace> Regions { get; init; }

    public static ManualMapDraft Empty(int gridWidth, int gridHeight) =>
        new(gridWidth, gridHeight, Array.Empty<ManualMapVertex>(), Array.Empty<ManualRegionFace>());
}

/// <summary>A shared coordinate node used by one or more manual region faces.</summary>
public sealed record ManualMapVertex(int Id, MapPoint Position);

/// <summary>
/// A completed manual face.  The vertex list is an open ring; a repeated first
/// vertex is accepted by the validator for convenient import compatibility.
/// </summary>
public sealed record ManualRegionFace(
    int Id,
    IReadOnlyList<int> VertexIds,
    string? Name = null)
{
    public ManualRegionFace(int Id, IEnumerable<int> VertexIds, string? Name = null)
        : this(Id, VertexIds?.ToArray() ?? throw new ArgumentNullException(nameof(VertexIds)), Name)
    {
    }
}
