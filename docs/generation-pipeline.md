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
  requires: Mask
  produces: Landmasses

ExtractWaterBodiesStage
  requires: Landmasses
  produces: WaterBodies

ClassifyWaterBodiesStage
  requires: Mask, Landmasses, WaterBodies
  produces: WaterBodyTopology

GenerateTectonicHistoryStage
  requires: Mask, Landmasses, WaterBodies
  produces: TectonicHistory

GenerateCrustFieldsStage
  requires: Mask, TectonicHistory
  produces: CrustFields

GeneratePlateDomainsStage
  requires: Mask, CrustFields, TectonicHistory
  produces: PlateDomains

GenerateTectonicBoundariesStage
  requires: PlateDomains, CrustFields
  produces: TectonicBoundaries

GenerateOrogenProvincesStage
  requires: Mask, TectonicHistory, CrustFields, TectonicBoundaries
  produces: OrogenProvinces

GenerateRiftProvincesStage
  requires: Mask, TectonicHistory, CrustFields, TectonicBoundaries
  produces: RiftProvinces

GenerateTectonicFeaturesStage
  requires: Mask, Landmasses, TectonicHistory, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces
  produces: TectonicFeatures

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
  requires: Mask, Regions
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
