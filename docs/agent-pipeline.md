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
regions.geojson
landmasses.geojson
water-bodies.geojson
tectonic-plates.json
elevation.json
lakes.json
summary.json
```

`lakes.json` records inland lake and inland-sea classification, profile, surface, spill, margin, shoreline metrics, tectonic/volcanic influence, and maximum depth. `summary.json` records the input mask, generation options, output paths, entity counts, and elevation range. Prefer setting `--seed` for agent checks so repeated runs are comparable.

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
--tectonic-json-mode Summary|CompactDiagnostic|Diagnostic
--elevation-json-mode Summary|Diagnostic
```

The Avalonia client uses the same `MapGenerationRunner`, so CLI checks exercise the same generation and export path as the UI button.
