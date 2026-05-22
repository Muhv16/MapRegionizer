# Hydrology

This document records hydrology-facing terrain semantics. The current implementation covers water-body classification, generated small lakes, lake levels, lake origin/profile metadata, lake-bed shaping, hydro-surface generation, river routing, drainage basins, river mouths, and river exports.

## Elevation Concepts

Terrain generation separates three related concepts:

- `BedElevationMeters`: ground elevation. On land this is the exposed land surface; under water this is the lake or sea floor.
- `WaterSurfaceMeters`: water level. Ocean water uses `0`, ocean-sea water uses a value below `0`, and inland lakes/seas use a value above `0`.
- `HydroSurfaceMeters`: generated routing surface for moving water. It is `BedElevationMeters` on land, `0` on ocean/ocean-sea cells, and `WaterSurfaceMeters` on inland lake/sea cells.

`HydroSurfaceMeters` is stored in `HydrologyMap`, not in `ElevationMap`, so river routing can use lake surfaces without changing the terrain/bed model.

## Water Body Classes

`ClassifyWaterBodiesStage` runs after `ExtractWaterBodiesStage` and produces `WaterBodyTopology`.

Current classes:

- `Ocean`: water connected to the outside sea or global ocean through the source mask edge. Surface is `0`.
- `OceanSea`: a very large closed sea that is not topologically connected to the ocean, or an `InlandSea`-sized body separated from the ocean by only a very small land distance. Surface is below `0`.
- `InlandLake`: a closed inland water body. Surface is above `0`.
- `InlandSea`: a large inland lake or enclosed sea. Surface is above `0`, but depth shaping uses larger, sea-like ranges.

The classifier uses raster water components from the mask, then associates them with extracted water-body polygons. Edge-connected water is `Ocean`. Closed water above `OceanSeaMinAreaRatio` is `OceanSea`. Closed water above `InlandSeaMinAreaRatio` is normally `InlandSea`, but it is promoted to `OceanSea` when it is within `OceanSeaNearOceanMaxDistanceCells` grid cells of edge-connected ocean water. The rest is `InlandLake`.

## Lake Levels

The elevation generator first builds pre-hydrology terrain and bathymetry as `BaseTerrain`. Inland water cells are treated as basin cells rather than as global sea, so they are not clamped by `MaxSeaElevationMeters`. `GenerateSmallLakesStage` may then add small generated lake cells on source land. `GenerateLakeLevelsStage` applies lake surfaces for both source-mask lakes and generated lakes, then writes the final `Elevation` plus `WaterSurfaces`.

For each inland lake, inland sea, or generated small lake:

1. Find shoreline cells: land cells adjacent to lake cells.
2. Estimate the spill point from shoreline elevations using `LakeSurfacePercentile` instead of a single minimum cell.
3. Classify the lake by location and origin.
4. Set `lakeSurface = spillElevation - margin`.
5. Lift shoreline rim cells below `lakeSurface + margin` when the inland-water mask is preserved.
6. Shape the lake bed as `lakeSurface - depth`, with depth increasing from shore toward the center according to the selected profile.

Lake location classes:

- `Mountain`: high or steep lakes in mountain belts, valleys, and rugged foothills.
- `Plain`: low-relief lowland lakes.
- `Plateau`: high basins with gentler local relief, including volcanic plateau settings.

Lake origin classes:

- `Tectonic`: lakes influenced by active boundary/fault, rift, or graben signals. These receive deeper, elongated trough profiles aligned to the local tectonic axis where available.
- `Glacial`: steep mountain lakes without dominant tectonic or volcanic signal. These receive steep-sided bowl profiles.
- `Erosional`: default lowland lakes. These are shallower and use smooth Gaussian-like profiles.
- `VolcanicKarst`: volcanic or small round karst-like basins. These use compact cone-like profiles and can be deep relative to area.

`WaterBodySurface` stores per-body lake metadata alongside the existing level fields: cell count, centroid, location, origin, profile, mean shoreline elevation, shoreline relief, tectonic influence, volcanic influence, and maximum depth. Ocean and ocean-sea records may leave lake-specific metadata unset.

## Generated Small Lakes

Generated small lakes are enabled by `ElevationGenerationOptions.GenerateSmallLakes`, which defaults to `true`. They are produced after `BaseTerrain` and before lake-level shaping. `SmallLakeCountMultiplier` scales the overall generated lake budget and placement attempts; `SmallLakeScatterMultiplier` scales the extra budget for standalone lakes scattered across separate suitable depressions; `SmallLakeSizeMultiplier` scales generated footprint area. Count and scatter multipliers default to `1`; the core footprint-size multiplier defaults to `0.25`.

The generator only considers source land cells. It looks for lowland local minima with low 5x5 relief, rejects high mountains and plateaus, avoids rough/ridged/foothill terrain, and keeps a minimum gap from oceans and existing water. Existing large inland lakes add a nearby satellite-lake bias so lowlands around user-provided lakes can gain smaller companion basins.

Placement is intentionally conservative:

- clusters are local and share a small area budget within one eligible lowland;
- solitary lakes inside an eligible lowland use larger spacing;
- scattered standalone lakes are selected from high-scoring candidates across the whole map and reserve a wider exclusion radius so they read as isolated basins rather than lake clusters;
- lake footprints are small noisy ovals;
- generated lake depth is capped by roughly `5..15%` of local relief and by the normal lake depth bounds.

Generated lakes are hydrology-only. They appear in final elevation, rendered images, `WaterSurfaces`, and `lakes.json`, but they do not alter `MapMask`, `Landmasses`, `Regions`, or `water-bodies.geojson`.

Current defaults preserve the source mask:

```text
PreserveOceanCoastline = true
PreserveInlandWaterMask = true
AllowLakeExpansion = false
AllowLakeDrainage = false
```

`AllowLakeExpansion` and `AllowLakeDrainage` remain reserved for later lake-mask edits. The current implementation keeps the mask stable and fixes small shoreline inconsistencies by lifting the rim.

## Rivers

`GenerateHydrologyStage` runs after `GenerateLakeLevelsStage` and produces `HydrologyMap`. It depends on final `Elevation`, `WaterSurfaces`, `WaterBodyTopology`, and generated lake footprints.

Internally, hydrology generation is split behind the stable `HydrologyGenerator.Generate(...)` facade. Stage implementation files live under `Terrain/Hydrology`: `HydrologyMapAssembler` prepares shared rasters and creates the final `HydrologyMap`; `LakeConnector` resolves inland lake/sea cells, outlets, and lake routing; `DrainageGraphBuilder` coordinates `FlowDirectionSolver` and `FlowAccumulationSolver`; `BasinDelineator` and `EndorheicOverflowConnector` own basin classification and forced dry-basin spill paths; `RiverNetworkExtractor` coordinates `RiverSourceSelector`, `ForcedLongRiverPlanner`, `MajorTributaryInjector`, `RiverTopologyGraph`, `RiverTopologyPlanarityResolver`, `RiverSegmentExtractor`, and `ChannelPathTracer`. Shared grid math, terrain rules, render rules, and generation state live in small internal helper types so these blocks can be exercised independently without changing the public hydrology API.

The stage first builds `HydroSurfaceMeters`:

```text
land: BedElevationMeters
ocean / ocean sea: 0
inland lake / inland sea / generated lake: WaterSurfaceMeters
```

It then selects lake outlets. Each inland lake or inland sea gets a shoreline candidate list, scored by shoreline height, local outward route cost, ridge/roughness penalties, basin/pass biases, lake origin, lake size, and deterministic noise. Tectonic, glacial, and erosional lakes are more likely to drain; volcanic/karst lakes and dry-basin plain lakes are more likely to remain endorheic. Large lakes may receive many incoming rivers but still expose one main outlet.

Flow routing still builds a D8-style `flowDirections` raster for hydrology, but visible rivers are no longer rendered directly from that greedy chain. The hydrology raster remains the source of accumulation, basins, lake targets, and terminal drainage. Visible river cells are then projected into a separate integer `RiverTopologyGraph`; after canonical river segments are extracted from that graph, each segment receives a smoothed polyline derived from its canonical cell path for rendering.

```text
cost = hydro height
     + uphill / roughness / ridge penalties
     - mountain-pass / basin / valley biases
     - lake/ocean target bias
     + deterministic noise
```

In low-slope plains, the hydrology routing cost also applies a deterministic multi-scale lateral bias and a small straight-axis penalty. Lake and ocean target attraction remains, but is softened in flat terrain so rivers do not always snap into the nearest water cell along long straight runs. Small dry depressions can be crossed by a limited logical breach when the uphill cost is low, especially near passes or basin outlets. Lakes without outlets are terminal drainage targets unless a shallow non-inland-sea lake receives many significant inflows; in that case a forced outlet is placed on shoreline far from the inflow cluster. `LakeOutletInflowForceMultiplier` scales the inflow-count threshold for this forced outlet.

The render geometry pass builds fractional points directly from the canonical river cells. It may add small bends and jitter for visual quality, but it no longer traces a separate integer channel that can diverge from the topology. This separates stable drainage accounting from cartographic river shape: `flowDirections` drives accumulation and basin logic, `RiverSegment.Cells` stores the canonical integer topology path, and `RiverSegment.Polyline` stores the smoothed render geometry. The render layer may smooth a river, but it must not introduce a new crossing or confluence outside the canonical topology.

After channel tracing, a diagnostic straight-run pass looks for repeated direction vectors of six or more cells. When it finds one, it attempts a local least-cost reroute between neighboring anchors in a 3..8 cell corridor, forbidding continuation of the same diagonal vector beyond the local limit and softly preferring valley/basin cells while allowing only a small breach.

Dry endorheic basins are classified before visible river selection:

- `Suppress`: implausible or tiny dry terminals are not drawn as rivers.
- `EphemeralSmall`: small plausible dry basins can keep short endorheic/seasonal river segments.
- `ForceOverflow`: large dry basins, or dry basins with high accumulated runoff, are treated as temporary depressions rather than final terminals.

`ForceOverflow` basins are connected back into normal drainage before accumulation is finalized for river extraction. The generator runs a bounded least-cost spill search from the dry terminal through nearby land and saddles until it reaches ocean water, a lake with an outlet, or any basin whose target is not `EndorheicDryBasin`. The spill path penalizes high barriers, rough/ridged terrain, and long detours while favoring mountain-pass and valley cells. This lets a closed basin cross a saddle several cells away instead of depending on a single neighboring-cell spill, while preserving small dry basins as local salt flats, dry washes, or seasonal channels.

Local runoff is an heuristic potential field. It is higher at elevation, foothills, passes, moderate roughness, alluvial plains, and basin edges. It is lower on extreme ridges, dry basins, desert plateau candidates, and ocean water. Accumulation is then propagated downstream through the flow graph after cycle breaking.

Visible river cells are selected from accumulation with terrain-dependent thresholds and distributed source selection. Hydrological flow remains dense, but the visible export is gated by accumulation, scale-dependent length, source spacing, basin/component budgets, mountain-cluster budgets, and proximity to stronger rivers. Instead of turning every above-threshold cell into a source, the stage finds local headwater candidates whose upstream cells are still below the local threshold, scores them by discharge and downstream dry run length, groups them by land component, basin, and coarse map bucket, then selects candidates round-robin across those groups. Candidate selection reserves chosen downstream corridors, so later sources need a short independent run before merging. A second sparse-bucket pass then looks for component/bucket areas that received no selected source and adds the best local candidates with softer spacing and downstream-run requirements.

Mountain and highland headwaters receive an additional visual budget. Connected mountain/highland clusters are found inside each land component, assigned a source cap derived from cluster area and `RiverDensity * MountainRiverDensity`, and split into north, south, west, and east slope budgets using the early downstream direction from the source. This prevents a single snowy massif from exporting every plausible hydrological trickle while still allowing one or more strong rivers on different sides of a range.

- mountains and delta candidates use lower thresholds;
- ordinary plains use medium thresholds;
- dry basins still use higher thresholds than ordinary plains, but less aggressively than before so accepted ephemeral basins can produce visible short channels.

After the distributed pass, a forced-long-river pass chooses the best ocean/lake/inland-sea basins by upstream path length and passes those paths into normal river extraction as priority mainstems. Before extraction, large inland seas receive at least one visible inflow of eight or more cells when river density permits and such a path exists. Major forced mainstems also seed additional side tributaries along their length; confluence points are spaced along the main river, and tributary lengths can range from short four-cell branches to the longest available upstream branch. `RiverDensity` scales forced mainstems, lake-inflow forcing, and guaranteed side tributaries; `LongRiverCountMultiplier` scales the forced-mainstem budget; `MajorRiverTributaryMultiplier` scales the number of guaranteed side tributaries.

Before river extraction, forced-long mainstems are marked into the visible river-cell raster, then the visible river network is materialized as `RiverTopologyGraph`: integer cells plus one downstream cell per visible cell. The graph runs a planarity repair pass that scans each 2x2 cell for opposing diagonal edges. When two river edges geometrically cross, the weaker edge by upstream accumulation is redirected into the stronger edge as a confluence instead of being left as a cartographic bridge. Candidate confluence targets include the stronger edge downstream cell, its upstream cell, and adjacent orthogonal river cells; selection uses hydro height, uphill penalty, attraction to accumulated flow, turn cost, basin-change penalty, and a large cycle penalty so non-cyclic confluences are preferred. If no safe confluence exists, the weak crossing edge is removed from the visible topology.

After river extraction and visible-network finalization, a second repair pass checks the exported render polylines themselves. This catches crossings or vertex touches introduced by meander bend points or tributary extraction after the integer topology has already reported a clean graph. When two final polylines cross or touch away from an existing confluence, the weaker river is shortened into the stronger river as a tributary: its mouth is moved to the nearest strong river cell, its final drainage target is inherited from the strong river, and its render polyline is snapped to the exact contact point on the stronger river. Existing parent/child river pairs are still checked; a confluence is valid only when the contact is the terminal render point of the child, so tributary tails cannot draw through the parent river. Very short remnants are removed. This keeps the render/export layer topologically consistent with the visual rule that rivers merge instead of passing through one another.

After extraction, a visible-network finalization pass suppresses weak near-parallel rivers when most of their length runs within a scale-dependent radius of a stronger river in the same land component and draining toward the same side. Remaining rivers are annotated with a simple network order, major/minor flag, visible rank, parent river id, and tributary ids. This lets downstream renderers and consumers draw major stems, tributaries, and minor mountain streams with different visual weights.

Extracted `RiverSegment` records include canonical dry-land cells, a smoothed render polyline, source, segment mouth, drainage terminal, discharge, length, mean slope, target basin, river kind, mouth kind, order, major flag, visible rank, parent river id, and tributary ids. `RiverSegment.Mouth` and `rivers.json.Rivers[].Mouth` are the end of the visible segment: receiving water, dry-basin endpoint, or confluence with an already extracted downstream segment. `DrainageTerminal` records the final ocean/lake/dry-basin target downstream. This keeps tributary polylines ending at confluences instead of drawing over the downstream river to the final ocean mouth. River kinds are `Mountain`, `Plain`, `Rift`, `Deltaic`, and `Endorheic`. A lake inflow is classified as endorheic only when the target lake or inland sea has no outlet; lakes with outlets receive normal river classification while still retaining a lake target in the drainage metadata. Mouth kinds are `SimpleMouth`, `Estuary`, `Delta`, `MarshDelta`, and `InlandDelta`.

Current river options live in `HydrologyGenerationOptions`:

- `RiverDensity` (default `1`)
- `MountainRiverDensity` (default `0.58`)
- `MaxMountainSourcesPerCluster` (default `0`, automatic)
- `MinMountainSourceSpacing` (default `0`, automatic)
- `MajorRiverCountMultiplier`
- `LongRiverCountMultiplier`
- `TributaryDensity` (default `1`)
- `MajorRiverTributaryMultiplier`
- `LakeOutletInflowForceMultiplier`
- `EndorheicBasinChance`
- `DeltaFrequency`
- `MeanderStrength`
- `LakeOutletStrictness`
- `PreserveCoastline`
- `AllowRiverCarving`

`AllowRiverCarving` defaults to `false`; the current stage may route through a low-cost breach logically, but it does not edit `ElevationMap`, `MapMask`, landmasses, regions, or coastlines.

## Lake Export

Generated artifacts include `lakes.json` when lake-surface data is available. It exports one record per inland lake or inland sea with surface elevation, spill elevation, margin, maximum depth, shoreline metrics, classification, and profile metadata.

Generated artifacts include `rivers.json` when hydrology data is available. It exports summary river statistics, quality diagnostics, river segments, canonical cell paths, render polylines, mouths, lake outlets, and drainage basins. Diagnostic rasters can be added through `RiverJsonExportOptions`; callers can opt out of `Cells` for a compact export, but the default river JSON includes both canonical `Cells` and visual `Polyline`.

The `Quality` block reports straight-run count and maximum run length, short-river count using a scale-dependent 5..8 cell limit, detached-river count, confluence count, mean tributaries per major river, endorheic river count, maximum alternating zig-zag run, mean curvature, sharp turns, backtrack-like turns, `CrossingRiverEdgeCount` for opposing visible diagonal D8 edges in 2x2 cells, and `PolylineCrossingCount` for remaining substantial crossings or invalid vertex touches in exported render polylines, including parent/child pairs whose child line does not terminate at the contact. `Summary.EndorheicRiverCount` is the count of exported rivers whose `Kind` is `Endorheic`. Separate summary counters report lake inflow rivers, closed-lake rivers, open-lake rivers, and dry-basin rivers so closed lakes and dry basins no longer have to be inferred from one overloaded count. `Summary.MajorRiverCount` uses the exported `IsMajor` classification so tiny creeks and steep low-discharge streams do not inflate the major-river total.

`elevation-rivers.png` renders `elevation-final.png` with presentation river overlays only. River width is percentile-scaled by discharge, color reflects river kind, and debug markers for outlets or mouths are hidden unless `RiverRenderOptions.DrawDebugMarkers` is enabled. The renderer samples river polylines into anti-aliased Catmull-Rom-like curves, while preserving wrap breaks so world-edge rivers do not draw across the full image.
