using MapRegionizer.Core.Climate;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.Core.Tectonics;

/// <summary>Stable latent identity of one automatic tectonic world.</summary>
public sealed record TectonicWorldPlate(
    TectonicPlateId Id,
    int ObjectSeed,
    GridPoint WorldSeedPoint,
    GridVector Motion,
    TectonicPlateKind Kind,
    double Activity,
    double Density,
    double Thickness,
    double? MeanOceanicAge,
    bool IsMicroplate = false);

public sealed record TectonicWorldLineament(
    int Id,
    TectonicFeatureKind Kind,
    IReadOnlyList<GridPoint> WorldPoints,
    double Age,
    double Intensity);

/// <summary>
/// A deterministic world-coordinate source for automatic tectonics.  The
/// latent world uses a fixed coarse lattice, so sampling a crop or a full
/// window cannot change plate identity or object seeds.
/// </summary>
public sealed class TectonicWorldContext
{
    public const int WorldWidth = 4096;
    public const int WorldHeight = 2048;

    // The latent tectonic world has one explicit cylindrical topology. All
    // world-edge normalization stays behind that topology object; consumers
    // sample it in world coordinates and never implement a second wrap rule.
    private static readonly IGridTopology WorldTopology = new CylindricalXTopology(WorldWidth, WorldHeight);
    private readonly IReadOnlyList<TectonicWorldPlate> _plates;

    private TectonicWorldContext(
        int worldSeed,
        IReadOnlyList<TectonicWorldPlate> plates,
        IReadOnlyList<GridPoint> hotspots,
        IReadOnlyList<TectonicWorldLineament> lineaments,
        IReadOnlyList<TectonicWorldLineament> riftSystems)
    {
        WorldSeed = worldSeed;
        _plates = plates;
        Hotspots = hotspots;
        MacroLineaments = lineaments;
        RiftSystems = riftSystems;
    }

    public TectonicWorldContext(int worldSeed, TectonicPlateGenerationOptions options)
    {
        var generated = Create(worldSeed, options);
        WorldSeed = generated.WorldSeed;
        _plates = generated.Plates;
        Hotspots = generated.Hotspots;
        MacroLineaments = generated.MacroLineaments;
        RiftSystems = generated.RiftSystems;
    }

    public int WorldSeed { get; }
    public IReadOnlyList<TectonicWorldPlate> Plates => _plates;
    public IReadOnlyList<GridPoint> Hotspots { get; }
    public IReadOnlyList<TectonicWorldLineament> MacroLineaments { get; }
    public IReadOnlyList<TectonicWorldLineament> RiftSystems { get; }
    public int PlateCount => _plates.Count;

    public static TectonicWorldContext Create(int worldSeed, TectonicPlateGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plateCount = Math.Clamp(options.PlateCount ?? 12, 1, 128);
        var plates = new List<TectonicWorldPlate>(plateCount);
        for (var i = 0; i < plateCount; i++)
        {
            var x = (int)(StableHash.Hash(worldSeed, i, 0, 0x501) % WorldWidth);
            var y = (int)(StableHash.Hash(worldSeed, i, 0, 0x502) % WorldHeight);
            var angle = StableHash.Unit(worldSeed, i, 0, 0x503) * Math.PI * 2.0;
            var speed = 0.45 + StableHash.Unit(worldSeed, i, 0, 0x504) * 1.25;
            var kindRoll = StableHash.Unit(worldSeed, i, 0, 0x505);
            var kind = kindRoll < 0.37 ? TectonicPlateKind.Continental : kindRoll > 0.71 ? TectonicPlateKind.Oceanic : TectonicPlateKind.Mixed;
            plates.Add(new TectonicWorldPlate(
                new TectonicPlateId(i + 1),
                StableHash.Int32(worldSeed, i, 0, 0x506),
                new GridPoint(x, y),
                new GridVector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                kind,
                0.65 + StableHash.Unit(worldSeed, i, 0, 0x507) * 0.7,
                kind == TectonicPlateKind.Oceanic ? 1.0 + StableHash.Unit(worldSeed, i, 0, 0x508) * 0.2 : 0.7 + StableHash.Unit(worldSeed, i, 0, 0x508) * 0.3,
                kind == TectonicPlateKind.Oceanic ? 0.45 + StableHash.Unit(worldSeed, i, 0, 0x509) * 0.25 : 0.75 + StableHash.Unit(worldSeed, i, 0, 0x509) * 0.4,
                kind == TectonicPlateKind.Continental ? null : 20.0 + StableHash.Unit(worldSeed, i, 0, 0x50A) * 180.0));
        }

        var hotspotCount = Math.Clamp(options.HotspotCount ?? 6, 0, 64);
        var hotspots = Enumerable.Range(0, hotspotCount)
            .Select(i => new GridPoint(
                (int)(StableHash.Hash(worldSeed, i, 0, 0x601) % WorldWidth),
                (int)(StableHash.Hash(worldSeed, i, 0, 0x602) % WorldHeight)))
            .ToArray();

        var lineaments = BuildLineaments(worldSeed, 0x700, TectonicFeatureKind.Ridge, Math.Max(1, plateCount / 3));
        var rifts = BuildLineaments(worldSeed, 0x710, TectonicFeatureKind.Rift, Math.Max(1, plateCount / 5));
        return new TectonicWorldContext(worldSeed, plates, hotspots, lineaments, rifts);
    }

    public TectonicPlateId SamplePlateId(int worldX, int worldY)
    {
        var x = GridTopologyMath.NormalizeX(WorldTopology, worldX);
        var y = Math.Clamp(worldY, 0, WorldHeight - 1);
        var best = _plates[0];
        var bestDistance = double.MaxValue;
        foreach (var plate in _plates)
        {
            var dx = Math.Abs(x - plate.WorldSeedPoint.X);
            dx = Math.Min(dx, WorldWidth - dx);
            var dy = y - plate.WorldSeedPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance.Equals(bestDistance) && plate.Id.Value < best.Id.Value))
            {
                best = plate;
                bestDistance = distance;
            }
        }

        return best.Id;
    }

    public TectonicPlateId SamplePlateId(GridPoint worldPoint) => SamplePlateId(worldPoint.X, worldPoint.Y);

    public TectonicHistory CreateHistory(GridWindow window)
    {
        var lineaments = MacroLineaments.Concat(RiftSystems)
            .Select(lineament => ProjectLineament(lineament, window))
            .Where(lineament => lineament.Points.Count > 0)
            .ToArray();
        var hotspots = Hotspots
            .Where(point => window.Contains(point.X, point.Y))
            .Select(point => new GridPoint(point.X - window.X, point.Y - window.Y))
            .ToArray();
        var cratons = _plates
            .Where(plate => plate.Kind == TectonicPlateKind.Continental && window.Contains(plate.WorldSeedPoint.X, plate.WorldSeedPoint.Y))
            .Select(plate => new GridPoint(plate.WorldSeedPoint.X - window.X, plate.WorldSeedPoint.Y - window.Y))
            .ToArray();
        var events = lineaments
            .Select((lineament, index) => new TectonicEvent(index + 1, EventFor(lineament.Kind), lineament.Points[lineament.Points.Count / 2], lineament.Age, Math.Max(window.Width, window.Height) * 0.08, lineament.Intensity))
            .ToArray();
        return new TectonicHistory(window.Width, window.Height, lineaments, events, cratons, hotspots);
    }

    private static IReadOnlyList<TectonicWorldLineament> BuildLineaments(int seed, int salt, TectonicFeatureKind kind, int count)
    {
        var result = new List<TectonicWorldLineament>(count);
        for (var i = 0; i < count; i++)
        {
            var startX = (int)(StableHash.Hash(seed, i, 0, salt) % WorldWidth);
            var amplitude = 40 + (int)(StableHash.Hash(seed, i, 0, salt + 1) % 240);
            var points = Enumerable.Range(0, 48)
                .Select(step => new GridPoint(
                    GridTopologyMath.NormalizeX(
                        WorldTopology,
                        startX + (int)Math.Round(Math.Sin(step * 0.32 + StableHash.Unit(seed, i, 0, salt + 2) * 6.2) * amplitude)),
                    Math.Clamp((int)((step + 0.5) * WorldHeight / 48.0), 0, WorldHeight - 1)))
                .Distinct()
                .ToArray();
            result.Add(new TectonicWorldLineament(i + 1 + salt * 100, kind, points, StableHash.Unit(seed, i, 0, salt + 3) * 2400.0, 0.45 + StableHash.Unit(seed, i, 0, salt + 4) * 0.55));
        }

        return result;
    }

    private static TectonicLineament ProjectLineament(TectonicWorldLineament lineament, GridWindow window)
    {
        var points = lineament.WorldPoints
            .Where(point => window.Contains(point.X, point.Y))
            .Select(point => new GridPoint(point.X - window.X, point.Y - window.Y))
            .ToArray();
        return new TectonicLineament(lineament.Id, lineament.Kind, points, lineament.Age, lineament.Intensity);
    }

    private static TectonicEventKind EventFor(TectonicFeatureKind kind) => kind switch
    {
        TectonicFeatureKind.Ridge => TectonicEventKind.OceanOpening,
        TectonicFeatureKind.Rift => TectonicEventKind.ContinentalRifting,
        TectonicFeatureKind.Trench => TectonicEventKind.OceanClosing,
        _ => TectonicEventKind.Volcanism
    };
}
