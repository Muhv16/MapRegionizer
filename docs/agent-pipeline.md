# Agent Generation Pipeline

Use `MapRegionizer.Cli` when an AI agent or script needs to generate map artifacts without opening the Avalonia UI.

## Generate Artifacts

```powershell
dotnet run --project src\MapRegionizer.Cli -- generate `
  --mask artifacts\test-source\s1.png `
  --out artifacts\agent-run `
  --seed 42
```

The output directory contains:

```text
result.png
tectonic-plates.png
tectonic-crust.png
tectonic-features.png
elevation.png
elevation-final.png
elevation-base.png
elevation-tectonic.png
elevation-roughness.png
elevation-erosion.png
elevation-terrain-zones.png
elevation-mountain.png
elevation-basin.png
elevation-rivers.png
regions.geojson
landmasses.geojson
water-bodies.geojson
tectonic-plates.json
elevation.json
lakes.json
rivers.json
summary.json
```

`lakes.json` records inland lake and inland-sea classification, profile, surface, spill, margin, shoreline metrics, tectonic/volcanic influence, and maximum depth. `rivers.json` records river summaries, visible river segments, lake outlets, mouths, and drainage basins. `summary.json` records the input mask, generation options, output paths, entity counts, elevation range, and river statistics. Prefer setting `--seed` for agent checks so repeated runs are comparable.

## Build Notes For Agents

In the Codex sandbox, set `DOTNET_CLI_HOME` inside the repository before invoking `dotnet`. Without it, first-time .NET setup may try to write under the sandbox user profile and fail with an access-denied error.

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
```

The SDK/workload resolver can also return exit code `1` while reporting `0` warnings and `0` errors during project-reference traversal. When that happens, build the projects in dependency order and disable project-reference traversal for dependent projects:

```powershell
dotnet build src\MapRegionizer.Core\MapRegionizer.Core.csproj --no-restore --disable-build-servers -m:1
dotnet build src\MapRegionizer.GeoJson\MapRegionizer.GeoJson.csproj --no-restore --disable-build-servers -m:1 -p:BuildProjectReferences=false
dotnet build src\MapRegionizer.ImageSharp\MapRegionizer.ImageSharp.csproj --no-restore --disable-build-servers -m:1 -p:BuildProjectReferences=false
dotnet build src\MapRegionizer.Runner\MapRegionizer.Runner.csproj --no-restore --disable-build-servers -m:1 -p:BuildProjectReferences=false
dotnet build src\MapRegionizer.Cli\MapRegionizer.Cli.csproj --no-restore --disable-build-servers -m:1 -p:BuildProjectReferences=false
```

After this fallback build, run the compiled CLI directly:

```powershell
src\MapRegionizer.Cli\bin\Debug\net10.0\MapRegionizer.Cli.exe generate `
  --mask artifacts\test-source\s1.png `
  --out artifacts\agent-run `
  --seed 42 `
  --elevation-json-mode Diagnostic `
  --tectonic-json-mode CompactDiagnostic
```

If you change a referenced project, rebuild that project first and verify the referenced DLL timestamp in `src\MapRegionizer.Cli\bin\Debug\net10.0`. With `BuildProjectReferences=false`, MSBuild may compile the dependent project without copying the latest referenced DLL into the CLI output. If timestamps do not update, copy the changed project output manually before running the compiled CLI:

```powershell
Copy-Item src\MapRegionizer.Core\bin\Debug\net10.0\MapRegionizer.Core.dll `
  src\MapRegionizer.Cli\bin\Debug\net10.0\MapRegionizer.Core.dll -Force
Copy-Item src\MapRegionizer.Core\bin\Debug\net10.0\MapRegionizer.Core.pdb `
  src\MapRegionizer.Cli\bin\Debug\net10.0\MapRegionizer.Core.pdb -Force
```

`.dotnet-home` is a local tool/runtime cache and should not be committed.

## Useful Options

```text
--target-area <uint>
--points-multiplier <number>
--min-area-ratio <number>
--max-area-ratio <number>
--simplify-tolerance <number>
--boundary-detail <number>
--max-offset <number>
--min-line-length-to-curve <number>
--projection equirectangular-world|flat|regional
--plate-count <int>
--hotspot-count <int>
--generate-small-lakes <bool>
--small-lake-count-multiplier <number>
--small-lake-scatter-multiplier <number>
--small-lake-size-multiplier <number>
--river-density <number>                 # default 1
--mountain-river-density <number>        # default 0.58
--max-mountain-sources-per-cluster <int> # default 0, automatic
--min-mountain-source-spacing <int>      # default 0, automatic
--major-river-count-multiplier <number>
--long-river-count-multiplier <number>
--tributary-density <number>             # default 1
--major-river-tributary-multiplier <number>
--lake-outlet-inflow-force-multiplier <number>
--endorheic-basin-chance <0..1>
--delta-frequency <number>
--meander-strength <0..1>
--lake-outlet-strictness <0..1>
--preserve-river-coastline <bool>
--allow-river-carving <bool>
--tectonic-json-mode Summary|CompactDiagnostic|Diagnostic
--elevation-json-mode Summary|Diagnostic
--debug                          # print per-stage & per-phase memory diagnostics (stderr)
```

## Debug Memory Diagnostics

Pass `--debug` to print per-stage and per-phase managed-heap and working-set deltas to stderr:

```powershell
dotnet run --project src\MapRegionizer.Cli -- generate `
  --mask artifacts\test-source\s1.png `
  --out artifacts\agent-run `
  --seed 42 `
  --debug
```

Output (stderr):

```
[MEM] baseline             |      +0,0M managed,     +0,0M WS  | total:    0,1M /   17,7M
[MEM] load mask            |      +3,1M managed,    +15,7M WS  | total:    3,2M /   35,1M
[MEM] Stage  1/18: extractLandmasses              |      +0,2M managed,    +13,7M WS  | total:    3,3M /   49,7M
...
[MEM] Stage  5/18: generateCrustFields            |     +32,8M managed,    +41,9M WS  | total:   41,2M /  129,4M
...
[MEM] Stage 11/18: generateElevation              |     +69,5M managed,   +389,0M WS  | total:  206,3M /  569,3M
[MEM] Stage 13/18: generateLakeLevels             |     +69,5M managed,    +27,4M WS  | total:  279,0M /  596,7M
[MEM] Stage 14/18: generateHydrology              |     +43,7M managed,   +358,4M WS  | total:  322,7M /  955,2M
[MEM] Stage 15/18: generateClimate                |    +114,1M managed,    +25,2M WS  | total:  436,7M /  980,4M
...
[MEM] generation           |    +363,7M managed,   +685,1M WS  | total:  366,8M /  720,1M
[MEM] artifacts            |     +17,9M managed,   -174,4M WS  | total:  384,7M /  545,8M
```

- **managed** — bytes allocated on the .NET managed heap (`GC.GetTotalMemory`). Persistent growth here indicates objects held across stages.
- **WS** — process working set (`Environment.WorkingSet`). OS-managed; negative values mean pages were trimmed/reclaimed.
- **total** — cumulative managed / WS after the stage. Use this to identify the peak memory footprint.
- **Phases**: `load mask` (image I/O), `generation` (the 18-stage pipeline), `artifacts` (image rendering + JSON/GeoJSON writes).

The most allocation-heavy stages are a good place to start optimising. On the `s1.png` smoke case at default resolution, `generateClimate`, `generateElevation`, `generateLakeLevels`, and `generateHydrology` typically account for the bulk of managed allocations.

The Avalonia client uses the same `MapGenerationRunner`, so CLI checks exercise the same generation and export path as the UI button.

Hydrology adds a post-elevation pass and can make full generation noticeably slower. For smoke tests, verify at minimum that `elevation-rivers.png`, `rivers.json`, and `summary.json` exist and that `summary.json` includes non-null `RiverCount`, `MajorRiverCount`, `EndorheicBasinCount`, and `DeltaCount`. For river-routing changes, also inspect `rivers.json`: river `Cells` should be present as canonical integer paths, river polylines should end at `Mouth`, child river polylines should terminate where they meet the parent river, short segments should not jump to a distant final outlet, `DrainageTerminal` should be present for each river, `Quality.CrossingRiverEdgeCount` and `Quality.PolylineCrossingCount` should be `0`, and at least a few rivers should exceed the long-river threshold on normal world-sized masks when `--long-river-count-multiplier` is above `0`. On the `s1.png` seed `42` smoke case, default hydrology should produce a broad network rather than a handful of outlet stubs; a useful check is roughly `60..120` rivers with most lengths at or above `6` cells.
