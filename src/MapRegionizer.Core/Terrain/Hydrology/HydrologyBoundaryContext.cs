using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Terrain;

/// <summary>Boundary values needed when a drainage solve covers only a world window.</summary>
public interface IHydrologyBoundaryContext
{
    /// <summary>
    /// Whether an edge with no explicit downstream target is considered an
    /// outgoing world path. Isolated boundaries set this to false.
    /// </summary>
    bool TreatUnspecifiedBoundaryAsExternal => false;
    double GetIncomingFlow(int worldX, int worldY) => 0.0;
    HydrologyExternalTarget? GetExternalDownstreamTarget(int worldX, int worldY) => null;
    double GetExternalElevation(int worldX, int worldY) => 0.0;
    bool GetExternalWaterInfluence(int worldX, int worldY) => false;
}

public sealed record HydrologyExternalTarget(
    DrainageTargetKind Kind,
    int? TargetId = null,
    double ElevationMeters = 0.0);

/// <summary>Explicit local policy: requested edges are ordinary isolated terminals.</summary>
public sealed class IsolatedHydrologyBoundaryContext : IHydrologyBoundaryContext
{
    public static IsolatedHydrologyBoundaryContext Instance { get; } = new();

    private IsolatedHydrologyBoundaryContext()
    {
    }

    public double GetIncomingFlow(int worldX, int worldY) => 0.0;
    public HydrologyExternalTarget? GetExternalDownstreamTarget(int worldX, int worldY) => null;
    public double GetExternalElevation(int worldX, int worldY) => 0.0;
    public bool GetExternalWaterInfluence(int worldX, int worldY) => false;
}

/// <summary>
/// A small immutable boundary table useful for coarse-world adapters and
/// deterministic synthetic fixtures.  Missing cells use the neutral policy.
/// </summary>
public sealed class HydrologyBoundaryContext : IHydrologyBoundaryContext
{
    private readonly IReadOnlyDictionary<(int X, int Y), double> _incoming;
    private readonly IReadOnlyDictionary<(int X, int Y), HydrologyExternalTarget> _targets;
    private readonly IReadOnlyDictionary<(int X, int Y), double> _elevations;
    private readonly IReadOnlySet<(int X, int Y)> _water;
    public bool TreatUnspecifiedBoundaryAsExternal { get; }

    public HydrologyBoundaryContext(
        IReadOnlyDictionary<(int X, int Y), double>? incomingFlow = null,
        IReadOnlyDictionary<(int X, int Y), HydrologyExternalTarget>? downstreamTargets = null,
        IReadOnlyDictionary<(int X, int Y), double>? externalElevation = null,
        IReadOnlySet<(int X, int Y)>? externalWater = null,
        bool treatUnspecifiedBoundaryAsExternal = true)
    {
        // Copy caller collections so the boundary snapshot remains immutable
        // for the lifetime of a generation request.
        _incoming = incomingFlow is null
            ? new Dictionary<(int X, int Y), double>()
            : new Dictionary<(int X, int Y), double>(incomingFlow);
        _targets = downstreamTargets is null
            ? new Dictionary<(int X, int Y), HydrologyExternalTarget>()
            : new Dictionary<(int X, int Y), HydrologyExternalTarget>(downstreamTargets);
        _elevations = externalElevation is null
            ? new Dictionary<(int X, int Y), double>()
            : new Dictionary<(int X, int Y), double>(externalElevation);
        _water = externalWater is null
            ? new HashSet<(int X, int Y)>()
            : new HashSet<(int X, int Y)>(externalWater);
        TreatUnspecifiedBoundaryAsExternal = treatUnspecifiedBoundaryAsExternal;
        if (_incoming.Any(pair => !double.IsFinite(pair.Value) || pair.Value < 0))
            throw new ArgumentException("Incoming flow values must be finite and non-negative.", nameof(incomingFlow));
    }

    public double GetIncomingFlow(int worldX, int worldY) =>
        _incoming.TryGetValue((worldX, worldY), out var value) ? value : 0.0;

    public HydrologyExternalTarget? GetExternalDownstreamTarget(int worldX, int worldY) =>
        _targets.TryGetValue((worldX, worldY), out var target) ? target : null;

    public double GetExternalElevation(int worldX, int worldY) =>
        _elevations.TryGetValue((worldX, worldY), out var value) ? value : 0.0;

    public bool GetExternalWaterInfluence(int worldX, int worldY) => _water.Contains((worldX, worldY));
}

/// <summary>Convenience provider that forwards a fixed external target to edge cells.</summary>
public sealed class WorkingDomainHydrologyBoundaryContext : IHydrologyBoundaryContext
{
    private readonly IHydrologyBoundaryContext _inner;

    public WorkingDomainHydrologyBoundaryContext(IHydrologyBoundaryContext inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool TreatUnspecifiedBoundaryAsExternal => _inner.TreatUnspecifiedBoundaryAsExternal;

    public double GetIncomingFlow(int worldX, int worldY) => _inner.GetIncomingFlow(worldX, worldY);
    public HydrologyExternalTarget? GetExternalDownstreamTarget(int worldX, int worldY) => _inner.GetExternalDownstreamTarget(worldX, worldY);
    public double GetExternalElevation(int worldX, int worldY) => _inner.GetExternalElevation(worldX, worldY);
    public bool GetExternalWaterInfluence(int worldX, int worldY) => _inner.GetExternalWaterInfluence(worldX, worldY);
}
