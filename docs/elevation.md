# Elevation Generation

This document describes the first terrain-generation stage. The model is designed for strategy-game worlds: it favors readable, useful landforms over physical simulation, while keeping an earth-like relationship between tectonics, coastlines, mountains, shelves, and ocean basins.

## Pipeline Position

Elevation is generated as a standalone raster layer after tectonic feature fields:

```text
Mask
 -> Landmasses
 -> WaterBodies
 -> TectonicHistory
 -> CrustFields
 -> PlateDomains
 -> TectonicBoundaries
 -> TectonicFeatures
 -> Elevation
```

`Elevation` does not modify the land/water mask. By default, land cells stay above sea level and water cells stay below sea level. Future river and climate stages should depend on `Elevation`, not on rendered terrain images.

## Domain Model

`ElevationMap` stores raster data with the same width and height as `MapMask`.

Main field:

- `ElevationMeters`: final height in meters relative to sea level. `0` is sea level, positive values are land elevation, and negative values are bathymetry.

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
- `RiftInfluence`: strength of rift valleys and back-arc lowering.
- `PreserveMaskCoastline`: keeps source land above sea level and source water below sea level.
- `MaxElevationMeters`: upper clamp for final terrain.
- `MinOceanDepthMeters`: lower clamp for final bathymetry.
- `MinLandElevationMeters`: minimum final height for land when coastline preservation is enabled.
- `MaxSeaElevationMeters`: maximum final height for water when coastline preservation is enabled.

## Generation Algorithm

### 1. Base Shape

The generator computes distance-to-land and distance-to-water rasters with horizontal wrapping. Land starts low near coasts and rises inland. Water starts as shallow shelf near land and deepens into ocean basins. Crust and coastal-zone data nudge this base:

- shelf and passive margins stay lower and smoother;
- active margins and arcs start higher;
- oceanic age deepens old ocean floor with a deliberately small range so ridge age helps broad basin tone without drawing bright lines across the ocean;
- rift crust lowers local terrain.

### 2. Tectonic Contribution

Tectonic features and boundary segments are converted into local height influence. Raw diagnostic tectonic rasters are not used directly as height lines: the terrain stage smooths and thresholds them first so historical craton, suture, ridge, trench, and orogen traces become broad regional influence instead of visible map scratches.

- collision and transpression produce major mountains;
- subduction produces trenches offshore and uplifts nearby active margins/arcs;
- ridges subtly uplift ocean floor, but oceanic ridge/heat-flow signals are heavily smoothed and damped for the final playable map;
- rifts and back-arc spreading lower land or ocean basins, with oceanic lowering kept subtle so rift traces do not dominate bathymetry;
- volcanism raises arcs, islands, and hotspot terrain;
- passive margins and sediment supply create lower, smoother lowlands and shelves.

Collision and transpression boundaries are not treated as continuous mountain walls. The tectonic stage already stores local `PlateBoundarySegment` records by boundary mode; the terrain stage then gates individual segment points with broad noise. Weak gated stretches become passes or subdued uplands, while strong gated stretches expand into wider massif masks. A second broader foreland signal adds foothill belts around the strongest massifs.

After raw tectonic uplift, the terrain stage builds a skeletal mountain network:

- orogen axis: collision/transpression influence after segment gating;
- main ridge: high `RidgeContinuity`;
- side ridges and massifs: widened high-gate mountain masks;
- foothill belt: broad `FoothillInfluence`;
- foreland basin: basin-prone belt outside foothills;
- passes: high `MountainPassPotential`, which damps local mountain uplift.

This keeps long ranges readable while avoiding uniform walls along every active boundary.

### 3. Continental Basins and Lowlands

Large continental plains are generated from broad low-relief signals instead of isolated spots. The basin pass combines subsidence, sediment supply, passive-margin context, rift influence, distance from coast, low ridge continuity, and broad noise. Strong basins softly flatten terrain toward low interior targets, producing large plains on big continents without forcing every basin to sea level.

### 4. Island Profiles

Small landmasses use `TectonicIsland` classification from the tectonic feature layer:

- `VolcanicArc`: higher central cones and rougher slopes;
- `ShelfArchipelago`: low islands with broad shallow shelves;
- `Microcontinent`: mixed mini-relief;
- `UpliftedRidge`: elongated ridge-like islands;
- `Hotspot`: volcanic high points with a chain-like profile.

### 5. Procedural Detail

Multi-octave value noise adds local terrain variation without external dependencies. Noise amplitude is controlled by `Roughness`, local tectonic activity, crust type, and distance from the coastline. Ocean noise is lower than land noise so underwater relief remains a background signal. Ocean and shelf distances use weighted 8-neighbor distance so bathymetry changes in smoother rings rather than sharp Manhattan-distance steps.

### 6. Smoothing and Constraints

The erosion pass blends each cell toward nearby cells on the same land/water surface. Water receives stronger smoothing than land to remove hard ocean lines. Ridge and collision masks reduce smoothing so mountain belts remain legible. A final interior-lowland lift suppresses isolated beach-colored spots far from coasts while preserving coastal lowlands. The final pass clamps heights and, by default, re-enforces the original land/water mask.

## Exports and Rendering

`ElevationJsonWriter` writes compact run-length encoded rows. Summary export includes final elevation rows, derived zone rows, and terrain-class rows. Diagnostic export also includes base elevation, tectonic elevation, roughness, erosion mask, mountain-pass potential, ridge continuity, foothill influence, and basin influence rows.

`MapImageRenderer.RenderElevation` renders a hypsometric PNG with optional hillshade. Ocean hillshade is intentionally weaker than land hillshade so underwater tectonic structure stays readable but does not dominate the map. `ElevationRenderOptions.Mode` can switch the renderer to diagnostic modes.

### `elevation.png`

Default legend:

- dark blue: deep ocean;
- medium blue: ocean basin;
- cyan / turquoise: shallow sea and continental shelf;
- sand: `Beach`;
- light green: `CoastalPlain` and `DeltaCandidate`;
- richer green: `AlluvialPlain` and `InteriorLowland`;
- muted yellow-green: `SedimentaryBasin`, including broad quiet continental basins;
- visible subdued tan: `DryBasin` and `DesertPlateauCandidate`;
- green-olive: moderate `Highland` terrain and old uplands;
- olive / ochre-gray: upper highlands approaching true mountain terrain;
- gray: `Mountain` terrain, usually above 1750 meters, or lower where ridge continuity is strong;
- near-white: very high mountains / snow-cap candidates, fading in from about 2100 meters and strongest around 3200 meters and above;
- lighting: hillshade, stronger on land and subtle underwater.

Moderate interior highlands are intentionally kept greenish in the default render. Brown and ochre tones are reserved for dry basins, desert plateau candidates, upper highlands, and ridge-driven mountains so ordinary playable continents do not read as continuous mountain country while major relief still remains visible.

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
elevation.json
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

## Known Constraints

- This is not hydraulic erosion. River carving should be added in a future `Rivers` stage.
- Elevation preserves the mask by default, so generated terrain will not create new seas or land bridges.
- Diagnostic fields are generation aids. Climate and river stages should primarily use final elevation plus selected tectonic fields where needed.
