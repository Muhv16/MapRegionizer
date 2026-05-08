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
Elevation
WaterSurfaces
TectonicPlates
RawRegions
Regions
```

`RawRegions` and `Regions` are intentionally separate:

- `RawRegions` are produced by the region generation stage.
- `Regions` are final regions after post-processing, currently boundary distortion.

This separation allows users to keep region generation and replace or disable later region post-processing without regenerating the raw region layout.

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
 -> GenerateLakeLevelsStage
 -> AssembleTectonicPlateMapStage
 -> GenerateRegionsStage
 -> DistortRegionBoundariesStage
```

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
  produces: Elevation

GenerateLakeLevelsStage
  requires: Elevation, WaterBodies, WaterBodyTopology
  produces: WaterSurfaces

AssembleTectonicPlateMapStage
  requires: TectonicHistory, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, TectonicFeatures
  produces: TectonicPlates

GenerateRegionsStage
  requires: Landmasses
  produces: RawRegions

DistortRegionBoundariesStage
  requires: Landmasses, RawRegions
  produces: Regions
```

If boundary distortion is disabled in options, `DistortRegionBoundariesStage` copies `RawRegions` to `Regions`.

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
Mask -> Landmasses -> RawRegions -> Regions
```

`WaterBodies` and tectonic data are not required for `Regions`, so tectonic layers are not generated unless requested by `RunFull()`, `RunUntil(MapDataKeys.TectonicHistory)`, `RunUntil(MapDataKeys.CrustFields)`, `RunUntil(MapDataKeys.PlateDomains)`, `RunUntil(MapDataKeys.TectonicBoundaries)`, `RunUntil(MapDataKeys.OrogenProvinces)`, `RunUntil(MapDataKeys.RiftProvinces)`, `RunUntil(MapDataKeys.TectonicFeatures)`, `RunUntil(MapDataKeys.Elevation)`, `RunUntil(MapDataKeys.WaterSurfaces)`, or `RunUntil(MapDataKeys.TectonicPlates)`.

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
    .AddStage(new DistortRegionBoundariesStage())
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
        new HashSet<MapDataKey> { MapDataKeys.RawRegions };

    public void Execute(MapGenerationContext context)
    {
        // Fill context.RawRegions.
    }
}
```

If the custom stage produces a different key, dependent default stages will not be able to find their required data.

## Future Extension

World-generation features should be added as new data keys and stages. Tectonics is generated as layered equirectangular world data: history, local crust fields, plate domains, boundary segments, orogen provinces, rift provinces, derived features, and a compatible assembled `TectonicPlates` view. Elevation is generated as a standalone bed-height/bathymetry raster after tectonic feature, orogen-province, rift-province, and water-topology fields. Lake levels are exposed as `WaterSurfaces`. See [tectonics.md](tectonics.md), [elevation.md](elevation.md), and [hydrology.md](hydrology.md) for the current domain models, options, algorithms, exports, and output map legends.

Tectonic GeoJSON export uses `Summary` mode by default. Summary output keeps runtime-friendly plate, boundary, crust, coastal, age, feature, and island metadata, but omits large diagnostic point clouds and writes compact JSON. Use `CompactDiagnostic` to include segment points without duplicate aggregate point lists, or `Diagnostic` for dense age rows and full feature point output.

Current and future terrain-oriented data keys include:

```text
Elevation
WaterSurfaces
Rivers
Climate
```

Potential dependencies:

```text
GenerateElevationStage
  requires: Mask, CrustFields, PlateDomains, TectonicBoundaries, OrogenProvinces, RiftProvinces, TectonicFeatures, WaterBodyTopology
  produces: Elevation

GenerateLakeLevelsStage
  requires: Elevation, WaterBodies, WaterBodyTopology
  produces: WaterSurfaces

GenerateRiversStage
  requires: Elevation, WaterSurfaces
  produces: Rivers

GenerateClimateStage
  requires: Elevation, Rivers
  produces: Climate
```

With this model, if a user likes generated regions but dislikes generated tectonics or terrain, only tectonic generation and downstream elevation/compatibility data need to be regenerated. Region data remains clean and reusable.
