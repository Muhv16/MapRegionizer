# Elevation Generation

This document describes the first terrain-generation stage. The model is designed for strategy-game worlds: it favors readable, useful landforms over physical simulation, while keeping an earth-like relationship between tectonics, coastlines, mountains, shelves, and ocean basins.

## Pipeline Position

Elevation is generated as a standalone raster layer after tectonic feature fields:

```text
Mask
 -> Landmasses
 -> WaterBodies
 -> WaterBodyTopology
 -> TectonicHistory
 -> CrustFields
 -> PlateDomains
 -> TectonicBoundaries
 -> OrogenProvinces
 -> RiftProvinces
 -> TectonicFeatures
 -> BaseTerrain
 -> GeneratedLakes
 -> Elevation
 -> WaterSurfaces
 -> Hydrology
```

`BaseTerrain` is the pre-hydrology terrain raster. `GenerateSmallLakesStage` can add small terrain-derived lake cells on top of this raster without changing the source mask, landmasses, or regions. `GenerateLakeLevelsStage` then turns base terrain plus user and generated lakes into final `Elevation` by applying local water surfaces, shoreline rim fixes, and lake-bed shaping. Final `Elevation` does not modify the land/water mask. By default, land cells stay above ocean sea level, ocean cells stay at or below sea level, and inland water cells receive a local water surface above sea level with a bed below that surface. `GenerateHydrologyStage` then builds a separate `HydrologyMap` with `HydroSurfaceMeters`, flow routing, drainage basins, and visible rivers. River and climate stages should depend on final terrain, water-surface data, and hydrology rasters, not on rendered terrain images. See [hydrology.md](hydrology.md) for lake-level and river-routing semantics.

## Domain Model

`ElevationMap` stores raster data with the same width and height as `MapMask`.

Main field:

- `ElevationMeters`: compatible final bed-height field in meters relative to sea level. On land it is exposed land elevation; under water it is the bed/bathymetry.
- `BedElevationMeters`: explicit ground elevation. This currently matches `ElevationMeters` and exists so future hydrology does not have to infer whether a water cell stores bed or surface.
- `WaterSurfaceMeters`: water level for water cells. Ocean is `0`, `OceanSea` is below `0`, and inland lakes/seas are above `0`. Land cells have no water surface.
- `WaterSurfaces`: per-water-body surface records. Inland lake/sea records include lake location, origin, profile, depth, centroid, shoreline relief, and tectonic/volcanic influence metadata.
- `HydrologyMap.HydroSurfaceMeters`: post-lake routing surface. It uses bed elevation on land, `0` for oceanic water, and water surface elevation for inland lakes and seas. It is intentionally outside `ElevationMap`.

Diagnostic fields:

- `BaseElevationMeters`: broad land/ocean shape derived from mask distance, shelf context, crust type, and coastal zone.
- `TectonicElevationMeters`: uplift, subsidence, ridge, rift, volcanic, passive-margin, and collision contribution.
- `Roughness`: local detail amplitude in `[0, 1]`.
- `ErosionMask`: final smoothing blend used by the erosion pass.
- `TerrainClass`: derived terrain class for rendering and future hydrology/climate use.
- `MountainPassPotential`: likelihood that a mountain-axis cell should behave as a pass or weak section rather than a wall.
- `RidgeContinuity`: strength and continuity of the skeletal mountain ridge network.
- `FoothillInfluence`: broad lower-relief belt around major orogens and massifs.
- `BasinInfluence`: broad low-relief basin signal used for plains, sedimentary basins, and dry basins.

`ElevationZoneKind` is a derived classification for exports and rendering:

- `DeepOcean`
- `ShelfSea`
- `CoastalLowland`
- `Lowland`
- `Highland`
- `Mountain`
- `IceCapCandidate`

The zone is not the source of truth. It is derived from final elevation.

`TerrainClassKind` is a more detailed derived terrain class. It is used by the default renderer and should be preferred by future river, erosion, and climate stages when they need lowland semantics:

- `Ocean`
- `ShelfSea`
- `DeepChannel`
- `ShallowBank`
- `AbyssalBasin`
- `SubmarineRidge`
- `Trench`
- `StraitDepth`
- `InlandSeaDepth`
- `Beach`
- `CoastalPlain`
- `AlluvialPlain`
- `InteriorLowland`
- `SedimentaryBasin`
- `DryBasin`
- `DeltaCandidate`
- `DesertPlateauCandidate`
- `Highland`
- `Mountain`

## Options

Elevation is configured through `ElevationGenerationOptions`.

- `ReliefScale`: global height multiplier.
- `Mountaininess`: scales collision, uplift, and mountain-building effects.
- `Erosion`: smoothing strength. Higher values soften broad terrain while preserving major ridges.
- `Roughness`: local noise/detail strength.
- `SeaDepthScale`: bathymetry multiplier.
- `ShelfWidthFactor`: terrain-specific shelf width scale.
- `VolcanismInfluence`: strength of volcanic islands, arcs, and seamount-like uplift.
- `SmallIslandReliefFactor`: relief multiplier for classified small islands. Default value reduces excessive volcanic and ridge relief on very small islands while allowing larger island landmasses to keep stronger mountains. Values near `1` also relax the small-island relief ceiling; lower values make the ceiling stricter.
- `RiftInfluence`: strength of rift valleys and back-arc lowering.
- `GenerateSmallLakes`: enables small terrain-derived lakes after `BaseTerrain` generation. Enabled by default.
- `SmallLakeCountMultiplier`: scales the number of generated small lakes. `1` keeps default density; `0` suppresses generated lakes while leaving the stage available.
- `SmallLakeScatterMultiplier`: scales the extra budget for scattered standalone small lakes. `1` keeps default scatter density; `0` keeps clustered generation but disables the global standalone pass.
- `SmallLakeSizeMultiplier`: scales generated small-lake footprint area. `1` keeps default sizes; values above `1` make generated lakes larger.
- `PreserveMaskCoastline`: legacy compatibility option retained for existing callers; new terrain constraints are controlled by the more specific options below.
- `PreserveOceanCoastline`: keeps source land above ocean sea level and ocean water below sea level.
- `PreserveInlandWaterMask`: keeps source inland lakes and seas as water bodies.
- `AllowLakeExpansion`: reserved for later hydrology; current defaults keep lake masks stable.
- `AllowLakeDrainage`: reserved for later hydrology; current defaults keep lake masks stable.
- `MaxElevationMeters`: upper clamp for final terrain.
- `MinOceanDepthMeters`: lower clamp for final bathymetry.
- `MinLandElevationMeters`: minimum final height for land when coastline preservation is enabled.
- `MaxSeaElevationMeters`: maximum final height for oceanic water when coastline preservation is enabled.
- `LakeSurfacePercentile`: shoreline elevation percentile used as the stable spill-point estimate.
- `MinLakeSurfaceMarginMeters` / `MaxLakeSurfaceMarginMeters`: margin subtracted from spill elevation.
- `MinLakeDepthMeters`, `MaxLakeDepthMeters`, `MaxRiftLakeDepthMeters`, `MaxInlandSeaDepthMeters`: lake and inland-sea depth bounds.
- `MountainLakeElevationMeters`, `PlateauLakeElevationMeters`, `MountainLakeReliefMeters`: thresholds used to classify lake location as mountain, plateau, or plain.
- `LakeTectonicFaultThreshold`, `LakeVolcanicInfluenceThreshold`, `PlainLakeKarstChance`: origin-classification controls for tectonic, volcanic/karst, glacial, and erosional lakes.
- `LakeDepthRandomnessMin` / `LakeDepthRandomnessMax`: deterministic per-lake depth variation around the profile's base depth.
- `LargeLakeDepressionMinCellCount`: minimum cell count for adding multiple local bottom depressions to large lakes.

## Generation Algorithm

### 1. Base Shape

The generator computes distance-to-land and distance-to-water rasters with horizontal wrapping. Land starts low near coasts and rises inland. Oceanic water starts as shallow shelf near land and deepens into ocean basins. Inland water starts as a basin candidate rather than global sea: it uses land-like base terrain during the main terrain pass and is converted into a lake or inland-sea bed after the local water surface is known. Shelf distance is locally warped by smooth multi-scale noise, and the tectonic crust stage uses variable shelf, inner-shelf, and shallow-sea widths. This prevents isolated islands from receiving perfect circular shallow-water halos while preserving the original land/water mask. Crust and coastal-zone data nudge this base:

- shelf and passive margins stay lower and smoother;
- active margins and arcs start higher;
- oceanic age deepens old ocean floor with a deliberately small range so ridge age helps broad basin tone without drawing bright lines across the ocean;
- rift crust lowers local terrain.

### 2. Tectonic Contribution

Tectonic features, orogen provinces, and boundary segments are converted into local height influence. Raw diagnostic tectonic rasters are not used directly as height lines: the terrain stage smooths and thresholds them first so historical craton, suture, ridge, trench, and orogen traces become broad regional influence instead of visible map scratches.

- collision and transpression produce major mountains;
- `OrogenProvinceMap` produces broad highland, foothill, and roughness influence from validated local province masks instead of long historical lines;
- `RiftProvinceMap` produces broad extensional province influence, local grabens, shoulder uplift, heat-flow patches, and breakup masks. `RiftAxis` is diagnostic only and is not used directly as a height/depth stroke;
- subduction produces trenches offshore and broad uplift near active margins/arcs; its land contribution is deliberately diffused so it does not draw narrow non-mountain elevation lines across continents;
- ridges subtly uplift ocean floor, but oceanic ridge/heat-flow signals are heavily smoothed and damped for the final playable map;
- rifts and back-arc spreading lower land or ocean basins through province masks: grabens drive local subsidence, shoulders add mild uplift/roughness, heat-flow is broad and weakly volcanic, and breakup zones weaken continuity. Boundary-line rift masks remain only a subdued fallback/diagnostic context;
- volcanism raises arcs, islands, and hotspot terrain;
- passive margins and sediment supply create lower, smoother lowlands and shelves.

Collision and transpression boundaries are not treated as continuous mountain walls. The tectonic stage already stores local `PlateBoundarySegment` records by boundary mode; the terrain stage then gates individual segment points with broad noise. Boundary stamps also vary local radius, strength, and edge falloff, so ridges, rifts, passive margins, and basin-prone belts widen, narrow, and fade along their length instead of looking like constant-width brush strokes. Non-mountain boundary masks are diffused again before they affect height, while collision and massif masks retain more local strength. Weak gated stretches become passes or subdued uplands, while strong gated stretches expand into wider massif masks. A second broader foreland signal adds foothill belts around the strongest massifs, also with local breakup noise.

Orogen provinces are applied separately from the active mountain axis. Their influence and strength rasters raise broad highlands, increase foothill influence, and add moderate roughness. They only add a small amount to ridge continuity, so old provinces read as upland belts and terrane memory rather than straight mountain roads. Young active provinces can reinforce nearby collision ranges, while old historical provinces decay into wider, weaker highland tendency.

Rift provinces are applied separately from raw rift lineaments. Continental rifts become chains of offset graben basins with raised shoulders and gaps, so terrain reads as an extensional province rather than a drawn incision. Back-arc extension is lower contrast and broader: it contributes heat-flow and basin tendency over lens-shaped patches behind arc systems, with much less shoulder uplift than continental rifts. Raw extensional boundary masks no longer feed tectonic height directly; terrain uses only the province influence/graben/shoulder/heat/breakup masks.

After raw tectonic uplift, the terrain stage builds a skeletal mountain network:

- orogen axis: collision/transpression influence after segment gating;
- main ridge: high `RidgeContinuity`;
- side ridges and massifs: widened high-gate mountain masks;
- foothill belt: broad `FoothillInfluence`;
- foreland basin: basin-prone belt outside foothills;
- passes: high `MountainPassPotential`, which damps local mountain uplift.

This keeps long ranges readable while avoiding uniform walls along every active boundary.

The final mountain shaping pass adds a cross-section profile over that network:

- main ridge: strengthened high `RidgeContinuity` axis;
- steep slope: a shoulder around the axis, still rugged but lower than the crest;
- foothill belt: broad `FoothillInfluence` that raises and smooths terrain into playable uplands;
- foreland basin: a basin-prone lowland outside the foothill belt where `BasinInfluence` is high and ridge continuity is low.

This gives mountain systems a more legible transition from crest to slope to foothills to plains, instead of isolated ridge lines sitting directly beside flat land.

### 3. Continental Basins and Lowlands

Large continental plains are generated from broad low-relief signals instead of isolated spots. The basin pass combines subsidence, sediment supply, passive-margin context, rift province influence, local grabens, distance from coast, low ridge continuity, and broad noise. It modulates tectonic-basin width before smoothing, blends medium and broad smoothing passes with local noise, and damps overly narrow peaks back into the broad field. Basin edges therefore fade into surrounding terrain and basin belts can thicken, pinch, or break naturally instead of forming constant-width brush patches. Strong basins softly flatten terrain toward low interior targets, producing large plains on big continents without forcing every basin to sea level.

### 4. Island Profiles

Small landmasses use `TectonicIsland` classification from the tectonic feature layer:

- `VolcanicArc`: higher central cones and rougher slopes;
- `ShelfArchipelago`: low islands with broad shallow shelves whose extent is anisotropic and lobe-shaped, avoiding perfectly circular reef halos around isolated islands;
- `Microcontinent`: mixed mini-relief;
- `UpliftedRidge`: elongated ridge-like islands;
- `Hotspot`: volcanic high points with a chain-like profile.

Island profile relief is scaled by `SmallIslandReliefFactor`, blended back toward full strength as island area grows. The same pass applies a soft broad ceiling only to the truly small end of classified island landmasses: the ceiling is strongest on the smallest islands, fades out across the lower-middle size range, and is skipped for larger classified islands. Its strength also depends on `SmallIslandReliefFactor`, so a value near `1` leaves excess relief mostly unrestricted while lower values compress oversized peaks more aggressively. Local cone, hotspot-chain, or ridge cells can still receive extra peak allowance through a narrow size/shape/noise gate, so high points can form as local landmarks without occupying the whole island. This prevents tiny volcanic arcs or hotspot islands from turning into near-maximum-elevation rock walls after the general tectonic and volcanic uplift passes have already raised them, while larger islands remain capable of stronger mountains.

### 5. Bathymetric Structure

Oceanic water keeps final bed elevation in meters, but derived `TerrainClassKind` values describe underwater roles:

- `DeepChannel`: narrow deeper passages, usually related to rifts or constrained seas;
- `ShallowBank`: raised shallow banks on shelves and around archipelagos;
- `AbyssalBasin`: broad deep-ocean lows;
- `SubmarineRidge`: underwater ridge and spreading-center influence;
- `Trench`: subduction-related deep troughs;
- `StraitDepth`: navigable constrained channels between nearby landmasses;
- `InlandSeaDepth`: shallower enclosed or semi-enclosed seas.

These roles are generated from final depth, distance to land, local land enclosure, crust/coastal zones, and diffused ridge/subduction/rift masks. Their height adjustments are intentionally moderate so seas gain strategic structure without becoming visually overloaded.

### 6. Generated Small Lakes

After `BaseTerrain` is available, the small-lake stage scans source land cells for low, flat local minima. Candidate cells must be local 3x3 depressions, have low 5x5 relief, avoid mountain/high-plateau terrain, and stay away from steep roughness, ridge, foothill, ocean, and existing-water edges. Existing inland lakes add a moderate satellite-lake influence in nearby lowlands, so small lakes can appear as compact companions around larger user-provided water bodies.

The stage groups eligible lowlands, then places a conservative mix of clusters, solitary lakes inside those lowlands, and standalone scattered lakes chosen from high-scoring candidates across the whole map. Cluster placement uses a coarse local grid-like selection and a shared area budget; local solitary lakes use larger spacing; scattered lakes reserve an even wider exclusion radius so they read as isolated basins. `SmallLakeCountMultiplier` scales body budgets and placement attempts, `SmallLakeScatterMultiplier` scales the standalone scatter pass, and `SmallLakeSizeMultiplier` scales footprint area budgets. Each generated lake receives a small oval, lightly noisy footprint and a local depth cap based on roughly `5..15%` of nearby relief, bounded by normal lake depth options. Generated lakes are hydrology-only: they contribute water surfaces, bed shaping, rendering, and `lakes.json`, but do not cut holes into exported landmass or region geometry.

### 7. Lake Levels

After the main terrain and ocean bathymetry passes, inland water bodies use `WaterBodyTopology` to compute a local water surface. The shoreline ring is all land adjacent to a lake. The lake spill point is estimated from the configured shoreline percentile, then the surface is set slightly below it. With the default preservation settings, shoreline cells below the rim are lifted to keep the original mask stable instead of expanding or draining the lake.

Before bed shaping, each user-provided inland lake/sea and generated small lake is classified by location and origin. Location uses shoreline elevation, shoreline relief, roughness, ridge/foothill influence, and volcanic context to distinguish mountain, plain, and plateau basins. Origin then combines local tectonic boundary/rift/graben influence, volcanic/heat-flow influence, roundness, size, and deterministic per-lake variation:

- tectonic lakes become deep elongated troughs, preferentially aligned to local boundary/fault direction;
- glacial lakes become steep mountain bowls;
- erosional lakes become shallow lowland basins with Gaussian-like depth distribution;
- volcanic/karst lakes become compact cone-like basins and can be deep for their size.

Maximum depth is derived from size, origin, shoreline relief, tectonic/volcanic influence, and a deterministic `0.8..1.2` default depth coefficient. Generated small lakes additionally clamp maximum depth to their local relief-derived cap. Large lakes can receive several local depressions so the bottom is not a single perfect basin. `InlandSea` bodies use broader sea-like depth ranges while still keeping a positive surface.

### 8. Procedural Detail

Multi-octave value noise adds local terrain variation without external dependencies. Noise amplitude is controlled by `Roughness`, local tectonic activity, crust type, and distance from the coastline. Ocean noise is lower than land noise so underwater relief remains a background signal. Ocean and shelf distances use weighted 8-neighbor distance so bathymetry changes in smoother rings rather than sharp Manhattan-distance steps.

### 9. Smoothing and Constraints

The erosion pass blends each cell toward nearby cells on the same land/water surface. Water receives stronger smoothing than land to remove hard ocean lines. Ridge and collision masks reduce smoothing so mountain belts remain legible. A final interior-lowland lift suppresses isolated beach-colored spots far from coasts while preserving coastal lowlands. The final pass clamps heights and, by default, re-enforces ocean coastline constraints without forcing inland lakes below zero.

## Exports and Rendering

`ElevationJsonWriter` writes compact run-length encoded rows. Summary export includes final elevation rows, derived zone rows, and terrain-class rows. Diagnostic export also includes bed elevation, water-surface elevation, base elevation, tectonic elevation, roughness, erosion mask, mountain-pass potential, ridge continuity, foothill influence, and basin influence rows. `LakeJsonWriter` writes `lakes.json`, a readable per-lake metadata export with classification, profile, surface, spill, margin, and maximum depth. `RiverJsonWriter` writes `rivers.json`, and `MapImageRenderer.RenderElevationRivers` writes `elevation-rivers.png`.

`MapImageRenderer.RenderElevation` renders a hypsometric PNG with optional hillshade. Ocean hillshade is intentionally weaker than land hillshade so underwater tectonic structure stays readable but does not dominate the map. Inland lake depth now adds a subtle per-lake depth tint normalized against that lake's own maximum depth, so deep lake basins are visible on `elevation-final.png` without making oceans noisy. Final land color blends the derived terrain class with a continuous elevation gradient; this preserves terrain identity while preventing hard class borders from drawing artificial uplift stripes. `ElevationRenderOptions.Mode` can switch the renderer to diagnostic modes. `elevation-rivers.png` is a presentation overlay: it draws generated dry-cell river paths as anti-aliased smoothed curves and extends visible segments only to their receiving water or confluence. Hydrology uses terrain class, basin influence, roughness, pass potential, and ridge continuity to route denser, less-linear river networks, but it still does not edit `ElevationMap`.

### `elevation.png`

Default legend:

- dark blue: deep ocean;
- medium blue: ocean basin;
- cyan / turquoise: shallow sea and continental shelf;
- subtle darker/lighter blue-cyan tints: `DeepChannel`, `ShallowBank`, `AbyssalBasin`, `SubmarineRidge`, `Trench`, `StraitDepth`, and `InlandSeaDepth`;
- pale warm green: `Beach`, meaning very low land rather than desert;
- light green: `CoastalPlain` and `DeltaCandidate`;
- richer green: `AlluvialPlain` and `InteriorLowland`;
- muted yellow-green: `SedimentaryBasin`, including broad quiet continental basins;
- visible subdued tan: `DryBasin` and `DesertPlateauCandidate`;
- green-olive: moderate `Highland` terrain and old uplands;
- olive / ochre-gray: upper highlands approaching true mountain terrain;
- gray: `Mountain` terrain, usually above 1750 meters, or lower where ridge continuity is strong;
- near-white: very high mountains / snow-cap candidates, fading in from about 2100 meters and strongest around 3200 meters and above;
- lighting: hillshade, stronger on land and subtle underwater.

Very low land is intentionally rendered as pale green rather than sand so players do not read ordinary lowlands as deserts. Moderate interior highlands are kept greenish in the default render. Brown and ochre tones are reserved for dry basins, desert plateau candidates, upper highlands, and ridge-driven mountains so ordinary playable continents do not read as continuous mountain country while major relief still remains visible.

Plate boundaries are not drawn by default on the elevation map. They can be enabled with `ElevationRenderOptions.DrawPlateBoundaries` for debugging.

The Avalonia app writes:

```text
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
elevation.json
lakes.json
rivers.json
```

next to the existing region, tectonic, crust, and feature outputs.

Debug maps:

- `elevation-final.png`: default playable terrain view.
- `elevation-base.png`: mask/coast/crust-derived base shape.
- `elevation-tectonic.png`: tectonic contribution, with uplift and subsidence separated by color.
- `elevation-roughness.png`: final roughness field.
- `elevation-erosion.png`: smoothing blend used during erosion.
- `elevation-terrain-zones.png`: discrete `TerrainClassKind` colors.
- `elevation-mountain.png`: ridge continuity, foothills, and pass potential.
- `elevation-basin.png`: broad basin influence.
- `elevation-rivers.png`: final elevation with generated rivers drawn as smoothed presentation curves. Outlet and mouth markers are debug-only and are not drawn on the default presentation render.

## Known Constraints

- This is not hydraulic erosion. River carving remains disabled by default in the current `Hydrology` stage.
- Elevation preserves the mask by default, so generated terrain will not create new seas or land bridges.
- Below-zero water is no longer synonymous with all water. Only oceanic water is globally constrained below sea level; inland lakes and seas use local positive water surfaces.
- Diagnostic fields are generation aids. Climate and river stages should primarily use final elevation plus selected tectonic fields where needed.
