# Agent Generation Pipeline

Use `MapRegionizer.Cli` when an AI agent or script needs to generate map artifacts without opening the Avalonia UI.

## Generate Artifacts

```powershell
dotnet run --project src\MapRegionizer.Cli -- generate `
  --mask ConsoleRegionizer\bin\source1.png `
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
regions.geojson
landmasses.geojson
water-bodies.geojson
tectonic-plates.json
elevation.json
summary.json
```

`summary.json` records the input mask, generation options, output paths, entity counts, and elevation range. Prefer setting `--seed` for agent checks so repeated runs are comparable.

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
--tectonic-json-mode Summary|CompactDiagnostic|Diagnostic
--elevation-json-mode Summary|Diagnostic
```

The Avalonia client uses the same `MapGenerationRunner`, so CLI checks exercise the same generation and export path as the UI button.
