using MapRegionizer.Core.Domain;

namespace MapRegionizer.ImageSharp;

/// <summary>
/// File-backed world mask source for automatic world-context requests. The image is loaded
/// once and each requested/working window is returned as a local mask while
/// retaining its world-grid origin. This adapter deliberately contains no
/// generation or projection policy.
/// </summary>
public sealed class ImageMapMaskSource : IMapMaskSource
{
    private readonly MapMask _worldMask;

    public ImageMapMaskSource(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("World mask file was not found.", filePath);

        FilePath = filePath;
        _worldMask = ImageMaskReader.Read(filePath);
    }

    public string FilePath { get; }
    public GridWindow WorldWindow => _worldMask.Window;

    public MapMask GetMask(GridWindow window)
    {
        if (!_worldMask.Window.Contains(window))
        {
            throw new ArgumentException(
                $"World mask {_worldMask.Window} does not cover requested working window {window}.",
                nameof(window));
        }

        return _worldMask.Crop(window);
    }
}
