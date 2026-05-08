# Tectonics Generation

This document describes the current procedural tectonics pipeline: domain models, options, generation stages, exports, and diagnostic map legends.

The implementation is a procedural geology model, not a physical plate simulation through time. It generates a consistent set of geological traces, local crust fields, plate domains, segmented boundaries, and downstream feature fields.

## Pipeline Position

The default tectonic pipeline is layered:

```text
Mask
 -> Landmasses
 -> WaterBodies
 -> TectonicHistory
 -> CrustFields
 -> PlateDomains
 -> TectonicBoundaries
 -> OrogenProvinces
 -> RiftProvinces
 -> TectonicFeatures
 -> TectonicPlates
```

`TectonicPlates` is the compatibility view consumed by existing code and export tools. The richer downstream data lives inside it as optional layer references:

- `History`
- `CrustFields`
- `PlateDomains`
- `BoundaryMap`
- `OrogenProvinces`
- `RiftProvinces`
- `Features`

`RawRegions` and `Regions` do not depend on tectonics, so region regeneration can remain fast and independent.

## Domain Models

Main tectonic domain types:

- `TectonicHistory`: generated geological scenario. Contains `TectonicLineament`, `TectonicEvent`, craton centers, and hotspots.
- `CrustFieldMap`: raster fields for local crust type, coastal zone, oceanic age, continental age, last rifting age, last orogeny age, and last volcanism age.
- `PlateDomainMap`: raster plate id assignment plus `PlateDomain` metadata, including mean oceanic-crust age where available. It is the primary plate-domain layer before compatibility assembly.
- `TectonicBoundaryMap`: local segmented boundary model. Contains `PlateBoundarySegment` records with local type, precise boundary mode, segment activity, motion metrics, and oceanic-age metadata.
- `OrogenProvinceMap`: rasterized orogenic province layer generated from locally valid pieces of candidate axes. Stores province metadata plus influence, strength, and diagnostic axis rasters.
- `RiftProvinceMap`: rasterized extensional province layer generated from active extensional boundary modes plus weak historical hints. Stores province metadata plus `RiftInfluence`, diagnostic `RiftAxis`, `GrabenMask`, `ShoulderUpliftMask`, `HeatFlowMask`, and `BreakupMask` rasters.
- `TectonicFeatureMap`: derived downstream feature layer. Contains explicit `TectonicFeature` records, classified `TectonicIsland` records, and raster fields for uplift, subsidence, volcanism, seismicity, heat flow, and sediment supply.
- `TectonicPlateMap`: compatibility aggregate. Contains plate metadata, legacy aggregate boundaries, plate/crust raster, and references to all rich tectonic layers.

Important enums:

- `CrustKind`: `Continental`, `Oceanic`, `Shelf`, `Arc`, `Rift`, `Terrane`.
- `CoastalZoneKind`: `None`, `Shelf`, `Slope`, `PassiveMargin`, `ActiveMargin`, `ShallowSea`.
- `TectonicFeatureKind`: `Ridge`, `Trench`, `Arc`, `Rift`, `Suture`, `Orogen`, `Craton`, `PassiveMargin`, `Hotspot`, `SedimentaryBasin`, `Microplate`, `BackArcBasin`.
- `BoundarySegmentKind`: `Subduction`, `Collision`, `ContinentalRift`, `MidOceanRidge`, `Transform`, `BackArcBasin`, `PassiveMargin`.
- `BoundaryMode`: precise boundary behavior. Values are `PureTransform`, `Transpression`, `Transtension`, `ObliqueSubduction`, `MidOceanRidge`, `ContinentalRift`, `OceanOceanSubduction`, `OceanContinentSubduction`, `ContinentContinentCollision`, `PassiveMargin`, `DiffuseIntraplateBoundary`, `AccretionaryBoundary`, `BackArcSpreading`, `MixedSegmentBoundary`.
- `IslandKind`: `VolcanicArc`, `Hotspot`, `Microcontinent`, `UpliftedRidge`, `ShelfArchipelago`.

## Options

Tectonics is configured through `TectonicPlateGenerationOptions`.

- `PlateCount`: explicit total major plate count. If omitted, it is estimated from map size and `EarthLikeFactor`.
- `ContinentalSeedRatio`: legacy/seed-balance control for how many major seeds prefer continental-like crust.
- `BoundaryNoise`: noise added during plate-domain assignment. Higher values make borders less regular.
- `BoundaryNoiseScale`: legacy scale factor for boundary irregularity.
- `LandWaterTransitionPenalty`: cost penalty when a plate seed expands into a mismatched crust context. Higher values keep continental/oceanic domains more coherent.
- `Activity`: global motion/activity scale used by plate metadata and boundary classification.
- `EarthLikeFactor`: realism/coherence knob. Higher values increase structured behavior such as stronger lineament barriers and extra smoothing.
- `HistoryDepth`: scales geological ages, especially continental ages and old event traces.
- `MicroplateRatio`: target share of additional microplate candidates relative to major plate count.
- `MinMicroplateAreaRatio` / `MaxMicroplateAreaRatio`: bounded area range for valid microplates.
- `MinBoundarySegmentLength`: short boundary segments below this length are merged into nearby dominant segments.
- `ActiveMarginRatio`: controls how many active-margin trench/arc systems are seeded along coasts.
- `ShelfWidthFactor`: scales generated shelf and coastal-zone width.
- `HotspotCount`: explicit hotspot count. If omitted, it is estimated from map size.
- `RiftChance`: probability of generating continental rift systems.
- `ValidateGeometry`: enables post-validation for plate domains.
- `MaxValidationCycles`: maximum plate-domain cleanup passes.
- `MinPlateSize` / `MinPlateSizeRatio`: lower bound for non-microplate domains before merging.

Rendering options for diagnostic tectonic images live in `MapRegionizer.ImageSharp`:

- `CrustRenderOptions`: color palette, plate-boundary visibility, crust majority smoothing, optional coastal-zone tint.
- `TectonicFeatureRenderOptions`: summary/diagnostic mode, feature filtering, field thresholds, colors, and line/marker styling.
- `TectonicFeatureRenderMode.Summary`: default readable view. Hides noisy raw diagnostic layers.
- `TectonicFeatureRenderMode.Diagnostic`: full view for debugging; can show raw lineaments and raw raster fields.

## Generation Algorithm

### 1. Tectonic History

`TectonicHistoryGenerator` creates a plausible scenario:

- craton centers from landmass centroids;
- ocean-opening events and ridge lineaments as water-preferring curved meridians;
- active margins from traced coastline samples, producing trench and offset arc lineaments;
- continental rifts as land-preferring curved meridians;
- sutures and old orogens as historical candidate traces through craton zones;
- hotspot tracks from random hotspot points.

The output is intentionally synthetic. These lineaments are not final plate boundaries by themselves. Historical suture/orogen traces are especially treated as memory and candidates; they no longer become strong linear uplift on their own.

### 2. Crust Fields

`CrustFieldGenerator` starts from weighted land/water distance and tectonic history context:

- land becomes `Continental`;
- nearshore water becomes `Shelf`;
- deep water becomes `Oceanic`;
- coastal cells become `Shelf`, `Slope`, `PassiveMargin`, `ActiveMargin`, or `ShallowSea`;
- `Shelf` is the inner nearshore water belt, `Slope` is the outer shelf belt, `ShallowSea` is nearby ocean beyond the shelf, and `PassiveMargin` / `ActiveMargin` identify coastal land or water influenced by quiet margins versus trench/arc systems;
- `ShallowSea` is controlled by distance from land, with smooth local width variation, not by distant rift lineaments. This prevents open-ocean tectonic traces from becoming shallow-water bands in the elevation renderer;
- oceanic age increases with distance from ridge lineaments;
- continental age is randomized and scaled by `HistoryDepth`.

Land/water distance uses 8-neighbor weighted traversal with small smooth shelf-width variation. This keeps shelves and shallow seas from forming square buffers around small islands.

Lineaments then override narrow local zones:

- ridges force young oceanic crust;
- trenches mark active margins;
- arcs create `Arc` crust and recent volcanism ages;
- historical rifts create narrow `Rift` crust memory and last-rifting age, used as weak province hints rather than direct terrain strokes;
- sutures can locally create `Terrane` crust and last-orogeny age;
- orogens update last-orogeny age;
- hotspots update last-volcanism age.

The crust map is a local geological layer. It is not just a land/sea copy.

### 3. Plate Domains

`PlateDomainGenerator` creates major domains and then overlays validated microplates.

Major plate assignment:

- major seeds are spaced over the map and prefer local crust types;
- constrained priority flood fill assigns cells to seeds;
- crust mismatch, land/water transition cost, and lineament barrier influence shape the domains;
- ridge/trench/rift/suture lineaments act as soft boundary attractors;
- smoothing cleans obvious pixel noise.

Microplate assignment:

- candidates are grouped by tectonic system: arcs, trenches, rifts, sutures, and selected oceanic hotspots;
- candidates are spaced by a map-relative minimum distance and deduplicated by system key;
- microplates are generated locally as contextual or anisotropic masks, not as circular global Voronoi seeds;
- contextual masks prefer matching land/shelf/arc/terrane settings and the source plate pocket;
- oceanic masks use elongated noise-warped anisotropic scoring.

Validation then removes or merges bad domains:

- clustered microplates are merged;
- circular, single-neighbor, shredded, or too-small microplates are merged into the best neighbor;
- non-micro fragments are merged unless they are the main connected component;
- tiny non-micro pockets are merged if they look like accidental microplates.

### 4. Tectonic Boundaries

`TectonicBoundaryGenerator` samples adjacent plate ids in the plate-domain raster.

For each local boundary sample it computes relative motion:

- convergence;
- divergence;
- shear.

It classifies each local sample into both a compatibility `BoundarySegmentKind` and a precise `BoundaryMode`, not only the whole pair of plates:

- strong normal convergence or divergence takes precedence over shear, so oblique boundaries are not collapsed into pure transforms;
- convergence with oceanic/arc crust becomes `OceanOceanSubduction`, `OceanContinentSubduction`, or `ObliqueSubduction`;
- convergence between continental-like crust becomes `ContinentContinentCollision` or `Transpression`;
- divergence becomes `MidOceanRidge`, `ContinentalRift`, `BackArcSpreading`, or `Transtension` depending on crust and oceanic age;
- weak motion becomes `PassiveMargin` or `DiffuseIntraplateBoundary`;
- `SubductingPlate` and `SubductingOceanicAge` are set only for subduction-like modes.

Samples are grouped by plate pair and local mode into `PlateBoundarySegment` records. Short noisy segments are merged into longer dominant neighbors according to `MinBoundarySegmentLength`; if no mode dominates a merged segment, it becomes `MixedSegmentBoundary`. Segment `Activity` is derived from local normal/shear motion and is the preferred downstream intensity signal.

### 5. Orogen Provinces

`OrogenProvinceGenerator` builds uplift provinces before generic tectonic features are stamped. It uses candidate axes from:

- local `PlateBoundarySegment` records whose mode is `ContinentContinentCollision`, `Transpression`, or `AccretionaryBoundary`;
- historical `Orogen` and `Suture` lineaments, treated only as candidate axes.

Each candidate axis is ordered, split into short map-relative intervals, and scored point by point. The local score combines continental-like crust, convergence/transpression support from real boundary segments, boundary proximity, crust-age contrast, coastal penalties, craton-interior penalties, and low-frequency noise gates. Runs are cut when score falls below threshold, landmass id changes, stable craton interior continues unsupported, boundary support is too far away, or basin-like crust/coastal context dominates.

Valid runs become `OrogenProvince` records. The raster mask is stamped as a variable-width belt rather than a line:

- width varies with low-frequency noise and segment activity;
- strength is based on local score, activity, age decay, and breakup noise;
- segment ends taper smoothly so provinces fade instead of ending as blunt brush strokes;
- old historical provinces are wider and weaker, while young/active provinces are narrower and stronger.

This hard-limits the old "continental rail" failure mode: historical orogens can form local highland memory near plausible tectonic context, but unsupported lines through stable craton interiors are broken or reduced to near-zero influence.

### 6. Rift Provinces

`RiftProvinceGenerator` builds extensional provinces before generic tectonic features are stamped. It uses candidate axes from:

- local `PlateBoundarySegment` records whose mode is `ContinentalRift`, `Transtension`, or `BackArcSpreading`;
- historical `Rift` lineaments, treated only as weak hints near active support or young rift memory.

Each candidate point is scored from extensional motion, crust suitability, boundary support, land/shelf/back-arc context, non-craton preference, basin affinity, and low-frequency noise gates. Stable craton interiors weaken or break runs unless an active extensional boundary is close.

Valid runs are not stamped as a continuous line. They are converted into en-echelon `RiftProvinceSegment` records:

- main graben lenses have local offset, width, length, strength, and angle jitter;
- gaps are inserted between segments;
- some segments create short failed arms;
- neighboring offset segments can be connected by weak transfer zones that add heat/breakup context without drawing a strong depression.
- a line-likeness filter measures straightness, thinness, turn density, and path length; marker-stroke-like runs are degraded by shortening segment advance, widening lenses, increasing lateral offset, adding gaps/branches, and lowering strength.

The raster stamp is an elongated lens/capsule, not a stroke. It separates:

- `RiftInfluence`: broad province extent;
- `RiftAxis`: diagnostic center traces only;
- `GrabenMask`: local subsiding grabens and half-graben basins;
- `ShoulderUpliftMask`: raised rift shoulders;
- `HeatFlowMask`: broad thermal/volcanic influence;
- `BreakupMask`: weak, broken, or transfer-dominated patches.

`BackArcSpreading` produces wider, softer `BackArcExtension` provinces. These are lens-like heat/subsidence patches behind arc systems rather than narrow rift valleys, with weaker graben and shoulder expression.

### 7. Tectonic Features

`TectonicFeatureGenerator` converts history and boundary segments into downstream layers:

- explicit `TectonicFeature` records are created from history lineaments;
- orogen province axes are added as diagnostic `Orogen` features, while province rasters provide the broad uplift field;
- rift province segment centers are added as diagnostic `Rift` or `BackArcBasin` features, while province rasters provide the real subsidence, heat-flow, and shoulder-uplift fields;
- boundary modes are converted into feature kinds such as trench, ridge, rift, back-arc basin, or orogen;
- feature and segment points stamp raster fields: uplift, subsidence, volcanism, seismicity, heat flow, and sediment supply, but rift/back-arc lineaments and extensional boundary segments are metadata/diagnostic only; province rasters carry the terrain-relevant signal;
- hotspot tracks stamp sparse volcanic/heat-flow patches instead of continuous heat-flow strokes, so marker chains do not create straight pink diagnostic lines;
- boundary-derived feature intensity uses segment `Activity`;
- shelves, slopes, and passive margins add subsidence and sediment supply;
- rift crust adds only weak memory; active rift geometry comes from `RiftProvinceMap`;
- small landmasses are classified as volcanic arcs, hotspots, microcontinents, uplifted ridges, or shelf archipelagos. This classification is intentionally descriptive: the elevation stage can scale and cap island relief separately so a volcanic or arc-classified island does not automatically become a near-maximum-height mountain wall.

Historical sutures, old orogens, and old rifts now stamp only weak memory into feature rasters; strong orogenic uplift comes from `OrogenProvinceMap`, while rift subsidence/heat comes from `RiftProvinceMap`. The raw feature rasters are intentionally diagnostic and can contain line-shaped traces. The default feature PNG renderer filters these fields so the summary image stays readable.

### 8. Compatibility Assembly

`TectonicPlateAssembler` builds the existing `TectonicPlateMap` view:

- copies plate-domain metadata into `TectonicPlate` records;
- groups boundary segments into legacy `PlateBoundary` records;
- maps local boundary modes to legacy `PlateBoundaryKind` while preserving aggregate `BoundaryMode`;
- stores segment ids in aggregate boundaries to avoid duplicating point clouds in compact exports;
- attaches history, crust, domain, boundary, orogen-province, rift-province, and feature layers to the final map.

## Exports

`TectonicPlateJsonWriter` supports multiple export modes:

- `Summary`: default runtime-friendly JSON. Keeps plates, aggregate boundaries, compact raster rows, compact age encodings, feature metadata, and islands without large point-cloud duplication.
- `CompactDiagnostic`: keeps diagnostic meaning with compact JSON and no duplicated aggregate point lists. Orogen and rift province rows are included in compact scalar encoding.
- `Diagnostic`: full debug export with dense age rows, feature/segment points, orogen province axes, rift province axes, rift segments, and breakup masks.

Age rasters are quantized and encoded compactly in summary export. Dense raw age arrays are diagnostic data. Plate and boundary age summaries are exported as nullable numbers; unknown values are omitted instead of writing `NaN`.

## Output Map Legends

The Avalonia app writes three tectonic images next to the generated JSON.

### `tectonic-plates.png`

- white: land
- blue: water
- red lines: plate boundaries
- black labels on pale background: plate ids

This is the compatibility plate-domain view. It is intended for quickly reading plate layout, not local geology.

### `tectonic-crust.png`

- dark blue: oceanic crust
- sand: continental crust
- cyan/teal: shelf crust
- orange-red: arc crust
- pink: rift crust
- purple: terrane crust
- thin red lines: plate boundaries

The renderer uses a small majority filter by default so isolated single-cell crust noise does not dominate the map. Coastal-zone tint is available through options but disabled by default in the readable view.

### `tectonic-features.png`

Default mode is `Summary`.

Background:

- dark background: no visible summary-level tectonic field;
- blue tint: strong subsidence / basin influence;
- red-orange tint: strong volcanism;
- pink-purple tint: strong heat flow / rift-province influence;
- uplift and seismicity raw fields are disabled by default in summary because they carry dense line-shaped diagnostic traces. They remain available in `Diagnostic` mode or by enabling `DrawUpliftFieldInSummary` / `DrawSeismicityFieldInSummary`.

Line and marker overlays:

- cyan lines: ridges / spreading systems;
- dark lines: trenches / subduction systems;
- orange lines: volcanic arcs;
- pink/blue rift and back-arc axes plus plate-boundary overlays are hidden in summary mode; use `Diagnostic` mode to inspect `RiftAxis` and raw rift/back-arc feature traces;
- white markers or short lines: microplates;
- bright point markers: hotspots and classified island features.

Hidden from summary line rendering:

- sutures;
- orogens;
- cratons;
- passive margins;
- sedimentary basins;
- short or disconnected boundary-derived fragments.

Those layers are still present in domain data and JSON. They are hidden or converted to background/diagnostic data because drawing them directly creates unreadable line noise.

## Known Constraints

- The model is procedural and heuristic. It aims for plausible map-generation inputs, not geodynamic correctness.
- History lineaments can be long synthetic traces. They should be treated as influences, not always as visible map symbols.
- Raw `uplift` and `seismicity` rasters are useful for terrain/earthquake/resource generation, but they are not always suitable for direct summary rendering.
- Microplates are intentionally rare and validated aggressively. Small circular or isolated candidates are merged rather than shown as decorative plates.
- Equirectangular wrapping is supported horizontally; regional/flat maps may need different edge behavior later.
