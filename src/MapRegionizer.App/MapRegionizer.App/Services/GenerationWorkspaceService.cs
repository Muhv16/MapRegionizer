using System;
using System.Collections.Generic;
using System.IO;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Generation;
using MapRegionizer.Core.Options;
using MapRegionizer.ImageSharp;

namespace MapRegionizer.App.Services;

public sealed class GenerationWorkspaceService
{
    private string _maskPath = string.Empty;
    private MapGenerationOptions? _options;
    private MapGenerationRequest? _request;

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
        _request = null;
        return true;
    }

    /// <summary>Creates a session from the spatial request assembled by the App.</summary>
    public bool EnsureSession(MapGenerationRequest request, bool forceReset)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceWindow = request.WorkingDomain.Window;
        var source = request.MaskSource?.GetMask(sourceWindow)
            ?? throw new InvalidOperationException("The generation request does not provide a mask source.");

        if (!forceReset && Session is not null && _request is not null &&
            HasSameExecutionDomain(_request, request))
        {
            return false;
        }

        Session = MapGenerationSession.Create(request);
        _request = request;
        _maskPath = string.Empty;
        _options = request.Options;
        _ = source; // Materialize/validate the source before replacing the session.
        return true;
    }

    private static bool HasSameExecutionDomain(MapGenerationRequest left, MapGenerationRequest right) =>
        left.WorldContextMode == right.WorldContextMode &&
        left.Options.EffectiveSpatial.LegacyCompatibility == right.Options.EffectiveSpatial.LegacyCompatibility &&
        left.RequestedDomain.Window.Equals(right.RequestedDomain.Window) &&
        left.WorkingDomain.Window.Equals(right.WorkingDomain.Window);

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
        _request = null;
    }
}
