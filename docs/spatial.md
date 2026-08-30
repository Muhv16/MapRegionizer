# Spatial Model

MapRegionizer has two independent decisions: what surface the raster
represents, and what world context is available while generating it. Spatial
configuration affects generation. Output coordinate settings affect only
serialization and export.

## Quick Reference

### Default configuration

The modern explicit spatial descriptor defaults to:

| Setting | Default |
| --- | --- |
| `WorldModel` | `Spherical` |
| `Coverage` | `Global` (`-180..180`, `-90..90`) |
| `GridMapping` | `Equirectangular` |
| `Topology` | `CylindricalX` (horizontal wrap) |
| `UnitsPerCell` | `1.0` |
| `LegacyCompatibilityProfile` | `None` |
| world context | `WorldContextMode.Isolated` in the App and in explicit isolated requests |
| output coordinates | `GridMapUnits` |

`MapGenerationOptions` and `MapGenerator.Generate(MapMask, ...)` retain a
legacy default for compatibility. The legacy entry point is an adapter; it
uses an isolated context and records the historical spatial profile. New code
should provide `MapSpatialOptions` explicitly when it needs canonical spatial
semantics.

### Common configurations

```csharp
var spatial = new MapSpatialOptions
{
    WorldModel = WorldModelDescriptor.Spherical(),
    Coverage = MapCoverage.Global(),
    GridMapping = GridMappingKind.Equirectangular,
    Topology = GridTopologyKind.CylindricalX,
    UnitsPerCell = 1,
    LegacyCompatibility = LegacyCompatibilityProfile.None
};

var options = new MapGenerationOptions { Spatial = spatial, Seed = 42 };
var request = MapGenerationRequest.Isolated(requested, mask, options);
```

The normal modern whole-world request is `Global + CylindricalX + Isolated`.
The axes are independent: `Global + Automatic`, `Global + Custom`,
`Regional + Isolated`, `Regional + Automatic`, and `Regional + Custom` are
valid where their required sources and boundary contexts are available.

For a regional request with a world-aligned mask and a finite halo:

```csharp
var request = MapGenerationRequest.Automatic(
    requested,
    WorkingDomain.ForRequested(requested, finiteHaloCells: 2),
    worldMaskSource,
    options);
```

## Spatial Model Overview

`MapSpatialOptions` describes the represented surface and the mapping from
that surface to the generation raster:

```text
WorldModel
Coverage
GridMapping
Topology
UnitsPerCell
LegacyCompatibilityProfile
```

The descriptor is consumed by Core. It must not depend on UI, filesystem, or
an output adapter.

## Canonical Coordinate Space

Generation stages use one canonical raster coordinate space: integer grid
cells and `GridMapUnits` derived from `UnitsPerCell`. A `GridWindow` carries a
world-aligned origin, while a `MapMask` stores land points local to its own
window. `MapSpatialReference` records the dimensions and spatial descriptor
used by the generated result.

## Spatial Reference

### World Model

`WorldModel` describes the represented surface, not the output file format.
`Spherical` is the normal globe-like model. `Planar` is a flat surface for
compatibility and intentionally planar configurations.

### Coverage

`Coverage` states which geographic extent the generation grid represents.
`MapCoverage.Global` describes a full longitude span and normally reaches the
geographic poles. `MapCoverage.Regional` describes an explicit latitude range
and directed longitude interval; a longitude interval may cross the
antimeridian.

Coverage does not choose a world context. A regional map may be isolated, use
an automatically derived surrounding world, or use caller-supplied contexts;
the same is true of a global map.

### Grid Mapping

`GridMapping` describes how positions on the represented surface are assigned
to rows and columns of the raster. `Equirectangular` distributes the grid
uniformly in longitude and latitude. `WebMercator` uses the projected metric
Y coordinate and therefore cannot represent the mathematical poles.

Grid mapping is a generation setting. It determines the spatial context and
sampling used by generation even when the final output is exported in a
different coordinate system.

### Grid Topology

`Topology` describes how neighboring generation cells connect at raster
edges. `CylindricalX` wraps the left and right edges and leaves the north and
south edges open. `OpenRectangular` leaves all edges open.

Modern regional coverage normalizes a requested cylindrical X topology to
`OpenRectangular` when `LegacyCompatibilityProfile` is `None`. This prevents a
regional edge from silently connecting to the opposite side of the raster.
An explicit legacy profile may retain the historical cylindrical behavior.
Global cylindrical coverage remains cylindrical regardless of context mode.

### Units Per Cell

`UnitsPerCell` is the size of one generation cell in canonical map units.
`MapBounds.UnitsPerCell` and `MapSpatialReference.UnitsPerCell` expose the
same value. The obsolete `PixelSize` names are compatibility aliases only.

## Generation Domains

Generation can run on a larger world-aligned raster and return only the
requested area. This lets boundary-aware stages see context without changing
the public result dimensions.

### Requested Domain

`RequestedDomain` is the area the caller asks to receive. Its window defines
the public raster dimensions and the requested geographic extent.

### Working Domain

`WorkingDomain` is the world-aligned area on which stages execute. It must
contain the requested domain. A finite halo is the number of extra cells on
each side supplied by the caller; any mask source used by `Automatic` or
`Custom` must cover the complete working window.

The session crops generated rasters and geometry back to `RequestedDomain`.
World origins remain available through the request and result metadata.

## World Context Modes

`WorldContextMode` describes how generation obtains world and boundary
context. It is independent of `Coverage` and is not a projection or output
choice.

### Isolated

`Isolated` treats the working domain as self-contained. If no boundary
providers are supplied, Core uses isolated climate and hydrology boundaries.
This mode works for both global and regional coverage.

### Automatic

`Automatic` uses a world-aligned mask source and Core's deterministic world
context mechanisms. Climate and hydrology use analytical/default providers
when the caller does not provide explicit supported providers. A source must
cover the working domain, including any halo.

### Custom

`Custom` uses caller-owned world, climate, and hydrology context. Core requires
the world mask source plus both boundary contexts. Custom is available to
global and regional requests; the App intentionally offers only the simpler
isolated and surrounding-world choices.

## Output Coordinates

Output coordinates are selected with `MapOutputOptions`. They do not alter
the generation descriptor, topology, or stage inputs.

### GridMapUnits

`GridMapUnits` writes canonical cell/map coordinates. This is the lossless
choice for consumers that understand `UnitsPerCell` and `MapSpatialReference`.

### Geographic Longitude/Latitude

`GeographicLongitudeLatitude` writes longitude and latitude from the
generation spatial reference. Antimeridian output is controlled separately by
`AntimeridianOutputPolicy` (`Auto`, `Unwrap`, or `Split`).

### Web Mercator 3857

`WebMercator3857` projects output geometry to EPSG:3857. It is valid even when
generation used an equirectangular raster:

```text
GridMapping = Equirectangular
OutputCoordinateSystem = WebMercator3857
```

Output Web Mercator does not mean that generation used a Web Mercator grid.

## Projection and World-Edge Handling

### Antimeridian Output

Regional coverage stores a directed longitude interval, so a region from
170° to -170° is a 20° interval crossing the antimeridian. Cylindrical X
topology wraps raster neighbors for global maps. Output serialization chooses
whether to keep, unwrap, or split the seam independently.

### Web Mercator Latitude Overflow

Web Mercator generation supports latitudes only within
`±WebMercatorGridMapping.WebMercatorLatitudeLimit` (approximately 85.0511°).
The App replaces full-world ±90° defaults with this limit when Web Mercator is
selected, and restores ±90° only when switching back while those values are
still the untouched Mercator defaults. Manually entered invalid values are
reported at the App boundary with a localized validation message.

For output Web Mercator, `LatitudeOverflowPolicy` chooses `Reject` or `Clip`.
Generation and output policies are separate.

### Projected Geometry Densification

When output geometry is projected, adaptive densification can subdivide long
segments until the configured projection error tolerance is met. The output
settings control this representation detail; they do not change generation
cells or topology.

## Mapping, Topology, and Output Are Independent

The following settings answer different questions:

| Question | Setting |
| --- | --- |
| What surface is represented? | `WorldModel` |
| Which geographic area is generated? | `Coverage` |
| How are positions sampled into cells? | `GridMapping` |
| Which cells are neighbors at an edge? | `Topology` |
| How large is a cell? | `UnitsPerCell` |
| How does generation obtain outside context? | `WorldContextMode` |
| How is completed geometry serialized? | `MapOutputOptions.CoordinateSystem` |

Changing output coordinates must not dirty generation stages. Changing spatial
mapping or topology may change generation data and must invalidate their
consumers.

## Legacy Compatibility

`Legacy` is an adapter concern, not a world-context strategy. The obsolete
`RegionalGenerationMode` and `GenerationMode` APIs remain at compatibility
boundaries where practical. Their `Legacy` value maps to:

```text
WorldContextMode.Isolated
LegacyCompatibilityProfile = historical profile
```

`MapGenerationRequest.Legacy`, `MapGenerationSession.Create(MapMask, ...)`,
and `MapGenerator.Generate(MapMask, ...)` preserve the historical MapMask
behavior. New request/context/session/result state uses `WorldContextMode`.
Old settings and CLI `--generation-mode` values are read as compatibility
aliases; new App and CLI configuration uses world-context terminology.

## Guidelines for Spatially Aware Core Code

- Keep all stage coordinates in canonical grid/map units and use the shared
  `MapSpatialContext` for conversions.
- Read coverage and topology from the spatial descriptor; do not infer them
  from a world-context mode.
- Treat a global cylindrical seam as a real neighbor connection and a modern
  regional edge as open unless an explicit legacy profile says otherwise.
- Keep requested and working windows world-aligned. Crop only at the result
  boundary.
- Keep output coordinate conversion in output adapters. Do not use output
  projection settings to decide generation sampling.
- Use the Core Web Mercator latitude constant rather than duplicating
  projection mathematics in UI or orchestration code.
- Preserve deterministic seeds and explicit boundary providers across option
  updates.
