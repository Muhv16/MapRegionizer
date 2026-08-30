#pragma warning disable CS0618

namespace MapRegionizer.Core.Domain;

/// <summary>
/// A world-aligned, integer raster window.  Coordinates identify cells in the
/// stable world grid; the mask handed to legacy algorithms remains local to
/// the returned window.
/// </summary>
public readonly record struct GridWindow
{
    public GridWindow(int x, int y, int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Window width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Window height must be greater than zero.");
        if ((long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(x), "Window coordinates overflow the integer world grid.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int OriginX => X;
    public int OriginY => Y;
    public int MaxXExclusive => checked(X + Width);
    public int MaxYExclusive => checked(Y + Height);
    public int RightExclusive => MaxXExclusive;
    public int BottomExclusive => MaxYExclusive;

    public bool Contains(int x, int y) => x >= X && x < MaxXExclusive && y >= Y && y < MaxYExclusive;
    public bool Contains(GridWindow other) =>
        other.X >= X && other.Y >= Y && other.MaxXExclusive <= MaxXExclusive && other.MaxYExclusive <= MaxYExclusive;

    public GridWindow Expand(int halo)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halo);
        return new GridWindow(checked(X - halo), checked(Y - halo), checked(Width + halo * 2), checked(Height + halo * 2));
    }

    public GridWindow Offset(int dx, int dy) => new(checked(X + dx), checked(Y + dy), Width, Height);

    public static GridWindow FromSize(int width, int height) => new(0, 0, width, height);

    public override string ToString() => $"[{X},{Y} .. {MaxXExclusive},{MaxYExclusive})";
}

/// <summary>Requested output domain.  It is deliberately not interchangeable with a working domain.</summary>
public sealed record RequestedDomain
{
    public RequestedDomain(GridWindow Window)
    {
        this.Window = Window;
    }

    public RequestedDomain(int x, int y, int width, int height)
        : this(new GridWindow(x, y, width, height))
    {
    }

    public GridWindow Window { get; }
    public int X => Window.X;
    public int Y => Window.Y;
    public int Width => Window.Width;
    public int Height => Window.Height;

    public bool Contains(int x, int y) => Window.Contains(x, y);
}

/// <summary>
/// Domain on which spatial stages execute.  A working domain must contain the
/// requested domain; its extra cells are intentionally removed only at the
/// final output boundary.
/// </summary>
public sealed record WorkingDomain
{
    public WorkingDomain(GridWindow Window)
    {
        this.Window = Window;
    }

    public WorkingDomain(int x, int y, int width, int height)
        : this(new GridWindow(x, y, width, height))
    {
    }

    public GridWindow Window { get; }
    public int X => Window.X;
    public int Y => Window.Y;
    public int Width => Window.Width;
    public int Height => Window.Height;

    public int HaloCells(RequestedDomain requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!Contains(requested))
            throw new ArgumentException("Requested domain must be contained by the working domain.", nameof(requested));
        // A finite radius is safe only when every side of the requested
        // window has that much context.  Returning the smallest side margin
        // prevents an asymmetric working window from masquerading as a full
        // halo and leaving one interior edge under-supported.
        var left = requested.X - X;
        var right = Window.MaxXExclusive - requested.Window.MaxXExclusive;
        var top = requested.Y - Y;
        var bottom = Window.MaxYExclusive - requested.Window.MaxYExclusive;
        return Math.Min(Math.Min(left, right), Math.Min(top, bottom));
    }

    public bool Contains(RequestedDomain requested) => Window.Contains(requested.Window);

    public static WorkingDomain ForRequested(RequestedDomain requested, int finiteHaloCells = 0)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return new WorkingDomain(requested.Window.Expand(finiteHaloCells));
    }

    public (int X, int Y) OffsetOf(RequestedDomain requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!Contains(requested))
            throw new ArgumentException("Requested domain must be contained by the working domain.", nameof(requested));
        return (requested.X - X, requested.Y - Y);
    }
}

/// <summary>
/// Describes how a generation request obtains world and boundary context.
/// Coverage remains an independent spatial option.
/// </summary>
public enum WorldContextMode
{
    Isolated,
    Automatic,
    Custom
}

/// <summary>
/// Obsolete compatibility enum for callers of the original request API.
/// <see cref="Legacy"/> is an adapter concern; new code should use
/// <see cref="WorldContextMode"/> and <see cref="LegacyCompatibilityProfile"/>
/// independently.
/// </summary>
[Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
public enum RegionalGenerationMode
{
    Legacy,
    Isolated,
    Automatic,
    /// <summary>
    /// The caller supplies the world/mask and boundary contexts explicitly.
    /// Core does not invent a custom context for this mode.
    /// </summary>
    Custom
}

/// <summary>Obsolete short alias retained for source compatibility.</summary>
[Obsolete("Use WorldContextMode. Legacy maps to Isolated plus legacy compatibility semantics.")]
public enum GenerationMode
{
    Legacy = RegionalGenerationMode.Legacy,
    Isolated = RegionalGenerationMode.Isolated,
    Automatic = RegionalGenerationMode.Automatic,
    Custom = RegionalGenerationMode.Custom
}

/// <summary>Conversions used only at compatibility boundaries.</summary>
public static class WorldContextModeCompatibility
{
    public static WorldContextMode ToWorldContextMode(this RegionalGenerationMode mode) => mode switch
    {
        RegionalGenerationMode.Legacy => WorldContextMode.Isolated,
        RegionalGenerationMode.Isolated => WorldContextMode.Isolated,
        RegionalGenerationMode.Automatic => WorldContextMode.Automatic,
        RegionalGenerationMode.Custom => WorldContextMode.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown generation mode.")
    };

    public static WorldContextMode ToWorldContextMode(this GenerationMode mode) => ((RegionalGenerationMode)mode).ToWorldContextMode();

    public static RegionalGenerationMode ToRegionalGenerationMode(this WorldContextMode mode) => mode switch
    {
        WorldContextMode.Isolated => RegionalGenerationMode.Isolated,
        WorldContextMode.Automatic => RegionalGenerationMode.Automatic,
        WorldContextMode.Custom => RegionalGenerationMode.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown world context mode.")
    };

    public static GenerationMode ToGenerationMode(this WorldContextMode mode) => (GenerationMode)mode.ToRegionalGenerationMode();
}

/// <summary>Source for world-aligned mask windows used by automatic world-context generation.</summary>
public interface IMapMaskSource
{
    MapMask GetMask(GridWindow window);
}

/// <summary>Adapter for callers that already have a deterministic window sampler.</summary>
public sealed class DelegateMapMaskSource : IMapMaskSource
{
    private readonly Func<GridWindow, MapMask> _getMask;

    public DelegateMapMaskSource(Func<GridWindow, MapMask> getMask)
    {
        _getMask = getMask ?? throw new ArgumentNullException(nameof(getMask));
    }

    public MapMask GetMask(GridWindow window)
    {
        var mask = _getMask(window) ?? throw new InvalidOperationException("The mask source returned null.");
        if (!mask.Window.Equals(window))
            throw new ArgumentException($"Mask source returned {mask.Window}; expected {window}.", nameof(window));
        return mask;
    }
}
