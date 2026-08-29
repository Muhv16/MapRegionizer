# Generation Pipeline

MapRegionizer uses a data-driven generation pipeline. A generation stage declares which data it requires and which data it produces. The pipeline uses these declarations to run only the necessary stages and to mark dependent results as dirty when earlier data is regenerated.

## Core Concepts

The current pipeline is built around these public concepts:

- `MapDataKey`: identifies a piece of generated data.
- `MapDataKeys`: standard data keys used by the core pipeline.
- `IMapGenerationStage`: contract for generation stages.
- `MapGenerationPipeline`: executes stages according to their dependencies.
- `MapGenerationPipelineBuilder`: builds and customizes pipelines.
- `MapGenerationSession`: keeps generation state and supports partial generation/regeneration.
- `MapGenerator`: convenience wrapper for full generation.

## Data Keys

Current core data keys:

```text
Mask
SpatialContext
Landmasses
WaterBodies
WaterBodyTopology
TectonicHistory
CrustFields
PlateDomains
TectonicBoundaries
OrogenProvinces
RiftProvinces
TectonicFeatures
BaseTerrain
GeneratedLakes
Elevation
WaterSurfaces
Hydrology
Climate
TectonicPlates
RegionDraft
RawRegions
Regions
RegionRaster
```

`SpatialContext` is an immutable initial input, available before the first
stage; it is not produced by a stage. Every spatially dependent stage declares
it in `Requires`, so changing generation coverage, mapping, topology, or map
units invalidates the appropriate downstream branch while keeping the input
available. Output coordinate choices are deliberately absent from this graph.

`RegionDraft`, `RawRegions`, and `Regions` are intentionally separate:

- `RegionDraft` is acquired from automatic generation or one externally supplied draft source.
- `RawRegions` are produced only by canonicalizing that draft.
- `Regions` are final regions after post-processing, currently boundary distortion.

This separation allows users to keep region generation and replace or disable later region post-processing without regenerating the raw region layout.

Both sets obey the [region geometry contract](regions.md). In particular, the final set remains an exact partition of every landmass even after boundary distortion.

`RegionRaster` is an optional raster view of the final `Regions`. It stores one `int32` region id per source mask cell, using `0` for water/outside cells and final `RegionId.Value` values for land pixels.

## Default Stages

The default pipeline contains these stages:

```text
ExtractLandmassesStage
 -> ExtractWaterBodiesStage
 -> ClassifyWaterBodiesStage
 -> GenerateTectonicHistoryStage
 -> GenerateCrustFieldsStage
 -> GeneratePlateDomainsStage
 -> GenerateTectonicBoundariesStage
 -> GenerateOrogenProvincesStage
 -> GenerateRiftProvincesStage
 -> GenerateTectonicFeaturesStage
 -> GenerateElevationStage
 -> GenerateSmallLakesStage
 -> GenerateLakeLevelsStage
 -> GenerateHydrologyStage
 -> GenerateClimateStage
 -> AssembleTectonicPlateMapStage
 -> GenerateRegionsStage
 -> CanonicalizeRegionDraftStage
 -> DistortRegionBoundariesStage
```

`RasterizeRegionsStage` is available as an opt-in stage after `DistortRegionBoundariesStage`; it is not part of the default pipeline.

Their dependencies are:

```text
ExtractLandmassesStage
  requires: Mask, SpatialContext
  produces: Landmasses

ExtractWaterBodiesStage
  requires: Landmasses, SpatialContext
  produces: WaterBodies

ClassifyWaterBodiesStage
  requires: Mask, Landmasses, WaterBodies, SpatialContext
  produces: WaterBodyTopology

GenerateTectonicHistoryStage
  requires: Mask, Landmasses, WaterBodies, SpatialContext
  produces: TectonicHistory

GenerateCrustFieldsStage
  requires: Mask, TectonicHistory, SpatialContext
  produces: CrustFields

GeneratePlateDomainsStage
  requires: Mask, CrustFields, TectonicHistory, SpatialContext
  produces: PlateDomains

GenerateTectonicBoundariesStage
  requires: PlateDomains, CrustFields, SpatialContext
  produces: TectonicBoundaries

GenerateOrogenProvincesStage
  requires: Mask, TectonicHistory, CrustFields, TectonicBoundaries, SpatialContext
  produces: OrogenProvinces

GenerateRiftProvincesStage
  requires: Mask, TectonicHistory, CrustFields, TectonicBoundaries, SpatialContext
  produces: RiftProvinces

GenerateTectonicFeaturesStage
  requires: Mask, Landmasses, TectonicHistory, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, SpatialContext
  produces: TectonicFeatures

GenerateElevationStage
  requires: Mask, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, TectonicFeatures, WaterBodyTopology, SpatialContext
  produces: BaseTerrain

GenerateSmallLakesStage
  requires: Mask, WaterBodyTopology, BaseTerrain, SpatialContext
  produces: GeneratedLakes

GenerateLakeLevelsStage
  requires: BaseTerrain, GeneratedLakes, WaterBodies, WaterBodyTopology, CrustFields, TectonicBoundaries, RiftProvinces, TectonicFeatures, SpatialContext
  produces: Elevation, WaterSurfaces

GenerateHydrologyStage
  requires: Elevation, WaterSurfaces, WaterBodyTopology, GeneratedLakes, SpatialContext
  produces: Hydrology

GenerateClimateStage
  requires: Elevation, WaterSurfaces, WaterBodyTopology, Hydrology, SpatialContext
  produces: Climate

AssembleTectonicPlateMapStage
  requires: TectonicHistory, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, TectonicFeatures
  produces: TectonicPlates

GenerateRegionsStage
  requires: Landmasses
  produces: RegionDraft

CanonicalizeRegionDraftStage
  requires: Landmasses, RegionDraft
  produces: RawRegions

DistortRegionBoundariesStage
  requires: Landmasses, RawRegions
  produces: Regions

RasterizeRegionsStage
  requires: Mask, Regions, SpatialContext
  produces: RegionRaster
```

If boundary distortion is disabled in options, `DistortRegionBoundariesStage` copies `RawRegions` to `Regions`.

`RasterizeRegionsStage` samples each source mask cell and writes the final region id for land pixels. Water and outside-mask pixels are written as `0`. Because this stage is optional, `RunFull()` on the default pipeline does not produce `RegionRaster`; add the stage only for workflows that need a dense raster lookup or CLI binary artifact.

## Stage Contract

A generation stage implements `IMapGenerationStage`:

```csharp
public interface IMapGenerationStage
{
    string Id { get; }
    IReadOnlySet<MapDataKey> Requires { get; }
    IReadOnlySet<MapDataKey> Produces { get; }
    void Execute(MapGenerationContext context);
}
```

Rules for stages:

- `Requires` must list every data key read by the stage.
- `Produces` must list every data key written by the stage.
- A stage should write only the data it declares in `Produces`.
- A pipeline cannot contain multiple stages producing the same data key.
- Stage IDs should be stable because they are used for replacement and customization.

## Full Generation

Use `MapGenerator` when the user only needs a complete generated map:

```csharp
var generator = new MapGenerator();
GeneratedMap map = generator.Generate(mask, options);
```

Internally, `MapGenerator` creates a `MapGenerationSession`, runs the default pipeline, and returns `GeneratedMap`.

## Partial Generation

Use `MapGenerationSession` when the user wants to inspect or regenerate specific stages:

```csharp
var session = MapGenerationSession.Create(mask, options);

session.RunUntil(MapDataKeys.RawRegions);
var rawRegions = session.RawRegions;

session.RunUntil(MapDataKeys.Regions);
var finalMap = session.CurrentMap;
```

`RunUntil(target)` recursively runs all missing or dirty dependencies required to produce `target`.

For example:

```csharp
session.RunUntil(MapDataKeys.Regions);
```

This ensures:

```text
Mask -> Landmasses -> RegionDraft -> RawRegions -> Regions
```

`WaterBodies` and tectonic data are not required for `Regions`, so tectonic layers are not generated unless requested by `RunFull()`, `RunUntil(MapDataKeys.TectonicHistory)`, `RunUntil(MapDataKeys.CrustFields)`, `RunUntil(MapDataKeys.PlateDomains)`, `RunUntil(MapDataKeys.TectonicBoundaries)`, `RunUntil(MapDataKeys.OrogenProvinces)`, `RunUntil(MapDataKeys.RiftProvinces)`, `RunUntil(MapDataKeys.TectonicFeatures)`, `RunUntil(MapDataKeys.BaseTerrain)`, `RunUntil(MapDataKeys.Elevation)`, `RunUntil(MapDataKeys.WaterSurfaces)`, `RunUntil(MapDataKeys.Hydrology)`, `RunUntil(MapDataKeys.Climate)`, or `RunUntil(MapDataKeys.TectonicPlates)`.

## Regeneration

Use `Regenerate(target)` when existing data is acceptable up to some point, but a later result should be recalculated:

```csharp
session.RunUntil(MapDataKeys.Regions);

// Raw regions are not acceptable. Regenerate only them and invalidate final regions.
session.Regenerate(MapDataKeys.RawRegions);

// This reruns only what is needed after RawRegions changed.
session.RunUntil(MapDataKeys.Regions);
```

When data is regenerated, the pipeline marks downstream data as dirty.

Example:

```text
Regenerate RawRegions
  -> RawRegions becomes clean
  -> Regions becomes dirty
```

Then:

```csharp
session.RunUntil(MapDataKeys.Regions);
```

will rerun only `DistortRegionBoundariesStage`, assuming `Landmasses` and `RawRegions` are already clean.

To generate the optional region raster in a custom workflow, add the stage and request `RegionRaster`:

```csharp
var pipeline = MapGenerationPipelineBuilder.CreateDefault()
    .AddRegionRasterization()
    .Build();

var session = MapGenerationSession.Create(mask, options, pipeline);
session.RunUntil(MapDataKeys.RegionRaster);
var raster = session.RegionRaster;
```

The CLI enables the same opt-in stage with `--rasterize-regions`. When enabled, artifact export writes `regions.bin` as little-endian row-major `int32` cells and `regions.summary.json` with dimensions, counts, format metadata, and the region ids present in the raster.

## Custom Pipeline

Create a custom pipeline by replacing a default stage:

```csharp
var pipeline = MapGenerationPipelineBuilder.CreateDefault()
    .ReplaceStage(MapStageIds.GenerateRegions, new MyRegionGenerationStage())
    .Build();

var session = MapGenerationSession.Create(mask, options, pipeline);
session.RunUntil(MapDataKeys.Regions);
```

Or build a pipeline manually:

```csharp
var pipeline = new MapGenerationPipelineBuilder()
    .AddStage(new ExtractLandmassesStage())
    .AddStage(new GenerateRegionsStage())
    .AddStage(new CanonicalizeRegionDraftStage())
    .AddStage(new DistortRegionBoundariesStage())
    .AddStage(new RasterizeRegionsStage())
    .Build();
```

Manual pipelines are useful when a workflow does not need all data. For example, a pipeline can omit `ExtractWaterBodiesStage` if water bodies are never requested.

## Replacing One Stage

A custom stage should preserve the same produced data key if it replaces a default stage.

Example custom region stage:

```csharp
public sealed class MyRegionGenerationStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateRegions;

    public IReadOnlySet<MapDataKey> Requires { get; } =
        new HashSet<MapDataKey> { MapDataKeys.Landmasses };

    public IReadOnlySet<MapDataKey> Produces { get; } =
        new HashSet<MapDataKey> { MapDataKeys.RegionDraft };

    public void Execute(MapGenerationContext context)
    {
        // Set context.RegionDraft. CanonicalizeRegionDraftStage remains the only RawRegions producer.
    }
}
```

If the custom stage produces a different key, dependent default stages will not be able to find their required data.

## Future Extension

World-generation features should be added as new data keys and stages. Tectonics is generated as layered equirectangular world data: history, local crust fields, plate domains, boundary segments, orogen provinces, rift provinces, derived features, and a compatible assembled `TectonicPlates` view. Base terrain is generated as a standalone pre-hydrology bed-height/bathymetry raster after tectonic feature, orogen-province, rift-province, and water-topology fields. Small generated lakes are selected from that base terrain before lake levels produce final `Elevation` and `WaterSurfaces`; hydrology then produces `Hydrology` from final terrain and lake surfaces; climate then produces temperature, moisture, biome, habitability, agriculture, monsoon, rain-shadow, and ice rasters. See [tectonics.md](tectonics.md), [elevation.md](elevation.md), [hydrology.md](hydrology.md), and [climate.md](climate.md) for the current domain models, options, algorithms, exports, and output map legends.

`WaterSurfaces` now carries both water-level records and inland lake metadata. Inland lake/sea records include location class, origin class, depth profile, maximum depth, centroid, shoreline relief, and tectonic/volcanic influence. Artifact export writes those records to `lakes.json`; raster water levels remain in `Elevation.WaterSurfaceMeters`. `Hydrology` carries hydro surface, D8 flow, accumulation, drainage basin ids, a canonical integer visible-river topology, render polylines derived from that topology, forced long mainstem candidates, major-river side tributary expansion, guaranteed inland-sea inflows where feasible, lake outlets, and river mouths. Artifact export writes those records to `rivers.json` and `elevation-rivers.png`; river mouths are exported as visible segment endpoints, `Cells` records canonical integer paths, `Polyline` records visual geometry, and `DrainageTerminal` records the final ocean, lake, or dry-basin target.

Generation owns one immutable `MapSpatialContext` per session. Its
`MapSpatialReference` describes grid size, map units per cell, world model,
geographic coverage, grid mapping, and edge topology; the canonical geometry
space is always `GridMapUnits`. Generation stages consume the context's
topology/mapping and do not depend on output coordinate choices. GeoJSON and
river exporters accept `MapOutputOptions` and transform cloned geometry at the
output boundary. Geographic exports include the full spatial-reference
descriptor, including the legacy compatibility profile when applicable. Web
Mercator output uses the official EPSG:3857 spherical-Mercator constants
(WGS84 semi-major radius, metres) and is also a presentation-only operation.
`LatitudeOverflowPolicy.Reject` is the default and rejects geographic
coordinates (or inverse projected Y values) outside the mathematical tile
latitude (±85.0511287798066°); `Clip` clamps them to that boundary. Output
metadata records the selected policy. `WebMercator3857` is the canonical output
name and `WebMercator` remains a compatibility spelling.

Projected vector output is copied from the canonical NTS geometry. Long
segments are adaptively subdivided using `ProjectionErrorTolerance`; midpoint
and quarter-point samples are checked so symmetric projection curves cannot
pass an estimator that only samples the midpoint. Recursion is bounded by
`MaxDensificationDepth`: if the tolerance is still unmet at that bound, the
transform fails explicitly. `MinDensificationSegmentLength` is the documented
safety override for accepting very short output segments without further
subdivision. This is an export policy: it never repairs, unions, or distorts
the generated geometry.
`AntimeridianOutputPolicy.Auto` unwraps geographic output for compatibility and
splits Web Mercator paths at the world seam; callers can choose `Unwrap` or
`Split` explicitly. Split line output can be a `MultiLineString`, and split
polygon output can be a `MultiPolygon`, with each part normalized to one world
copy and no artificial world-spanning edge. Point and `MultiPoint` output has
no segment to cut, so `Auto`/`Split` normalize each point immediately; an
`Auto`/`Unwrap` geographic point retains its unwrapped longitude. The same
policies apply to river
polylines; `RiverSegment.Polyline` remains fractional continuous-grid geometry
and is scaled by `UnitsPerCell` only by the output adapter.

For generation sampling, `GridMappingKind.WebMercator` maps a grid row linearly
in projected Mercator Y and applies inverse Mercator to obtain latitude. The
grid remains a raster metric (X/Y cell distances are used by generation
algorithms), while climate reads the resulting geographic latitude. When
`PreserveProjectedCellAspectRatio` is enabled (the default), context creation
requires projected X and Y cell sizes to match; applications using a deliberate
non-square raster can disable that check explicitly. The dimensions are
validated where the spatial context knows both coverage and grid size.

The Milestone 1 legacy baseline is intentionally explicit. The default
`EquirectangularWorld` profile keeps cylindrical X topology for tectonics,
elevation, hydrology, and climate, while land/water shape extraction remains
an open-edge vectorization policy: components at the two X edges are not
joined. The default climate profile also retains its historical polar margin;
new spatial configurations use their declared latitude coverage directly.
The pinned raster and canonical-geometry fixtures in
`LegacySpatialRegressionTests` record these compatibility choices, including
holes/islands and both-X-edge seam cases. Any later change to those semantics
must update the fixture with an explicit migration decision.

Grid coordinates use the documented raster orientation `(0,0)` at the
top-left, X to the right, and Y downward. Continuous cell centers therefore
use `(x + 0.5, y + 0.5)`; mapping code owns the inversion to north-positive
geographic latitude. The old width/height overloads retained by a few Core
helpers are compatibility adapters that construct the legacy cylindrical
topology. Generation stages pass the session topology explicitly.

Tectonic GeoJSON export uses `Summary` mode by default. Summary output keeps runtime-friendly plate, boundary, crust, coastal, age, feature, and island metadata, but omits large diagnostic point clouds and writes compact JSON. Use `CompactDiagnostic` to include segment points without duplicate aggregate point lists, or `Diagnostic` for dense age rows and full feature point output.

Current and future terrain-oriented data keys include:

```text
BaseTerrain
GeneratedLakes
Elevation
WaterSurfaces
Hydrology
Climate
```

Potential dependencies:

The spatially dependent stages listed below also declare the initial
`SpatialContext` input, even where it is omitted from the abbreviated diagram.

```text
GenerateElevationStage
  requires: Mask, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, TectonicFeatures, WaterBodyTopology
  produces: BaseTerrain

GenerateSmallLakesStage
  requires: Mask, WaterBodyTopology, BaseTerrain
  produces: GeneratedLakes

GenerateLakeLevelsStage
  requires: BaseTerrain, GeneratedLakes, WaterBodies, WaterBodyTopology, CrustFields, TectonicBoundaries, RiftProvinces, TectonicFeatures
  produces: Elevation, WaterSurfaces

GenerateHydrologyStage
  requires: Elevation, WaterSurfaces, WaterBodyTopology, GeneratedLakes
  produces: Hydrology

GenerateClimateStage
  requires: Elevation, WaterSurfaces, WaterBodyTopology, Hydrology
  produces: Climate
```

With this model, if a user likes generated regions but dislikes generated tectonics or terrain, only tectonic generation and downstream elevation/compatibility data need to be regenerated. Region data remains clean and reusable.
