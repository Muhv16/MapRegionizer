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

`WaterBodies` are not required for `Regions`, so they are not generated unless requested by `RunFull()` or `RunUntil(MapDataKeys.WaterBodies)`.

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

Future world-generation features should be added as new data keys and stages, for example:

```text
TectonicPlates
Elevation
Rivers
Climate
```

Potential dependencies:

```text
GenerateTectonicPlatesStage
  requires: Landmasses, Regions
  produces: TectonicPlates

GenerateElevationStage
  requires: Landmasses, WaterBodies, TectonicPlates
  produces: Elevation

GenerateRiversStage
  requires: Elevation, WaterBodies
  produces: Rivers

GenerateClimateStage
  requires: Elevation, Rivers
  produces: Climate
```

With this model, if a user likes generated regions but dislikes generated tectonic plates, only plate generation and downstream data need to be regenerated. Region data remains clean and reusable.
