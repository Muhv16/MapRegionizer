using System;
using System.Collections.Generic;
using System.IO;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.App.Services;

public sealed class GenerationWorkspaceService
{
    private string _maskPath = string.Empty;
    private MapGenerationOptions? _options;

    public MapGenerationSession? Session { get; private set; }
    public string MaskPath => _maskPath;

    public bool EnsureSession(string maskPath, MapGenerationOptions options, bool forceReset)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(maskPath))
            throw new FileNotFoundException("Mask file was not found.", maskPath);

        if (!forceReset && Session is not null && string.Equals(_maskPath, maskPath, StringComparison.OrdinalIgnoreCase))
        {
            _options = options;
            return false;
        }

        var mask = ImageMaskReader.Read(maskPath);
        Session = MapGenerationSession.Create(mask, options);
        _maskPath = maskPath;
        _options = options;
        return true;
    }

    public void UpdateOptions(MapGenerationOptions options, IReadOnlyCollection<MapDataKey> dirtyRoots)
    {
        _options = options;
        Session?.UpdateOptions(options, dirtyRoots);
    }

    public void Reset()
    {
        Session = null;
        _maskPath = string.Empty;
        _options = null;
    }
}
