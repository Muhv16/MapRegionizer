# Hydrology

This document records hydrology-facing terrain semantics. The current implementation covers water-body classification, lake levels, lake origin/profile metadata, and lake-bed shaping; river routing and full hydro-surface generation remain future stages.

## Elevation Concepts

Terrain generation separates three related concepts:

- `BedElevationMeters`: ground elevation. On land this is the exposed land surface; under water this is the lake or sea floor.
- `WaterSurfaceMeters`: water level. Ocean water uses `0`, ocean-sea water uses a value below `0`, and inland lakes/seas use a value above `0`.
- `HydroSurfaceMeters`: future routing surface for moving water. It should be `BedElevationMeters` on land, `0` on ocean cells, and `WaterSurfaceMeters` on lake cells.

`HydroSurfaceMeters` is not generated yet. Future river stages should route over this hydro surface instead of inferring flow from rendered relief colors.

## Water Body Classes

`ClassifyWaterBodiesStage` runs after `ExtractWaterBodiesStage` and produces `WaterBodyTopology`.

Current classes:

- `Ocean`: water connected to the outside sea or global ocean through the source mask edge. Surface is `0`.
- `OceanSea`: a very large closed sea that is not topologically connected to the ocean, or an `InlandSea`-sized body separated from the ocean by only a very small land distance. Surface is below `0`.
- `InlandLake`: a closed inland water body. Surface is above `0`.
- `InlandSea`: a large inland lake or enclosed sea. Surface is above `0`, but depth shaping uses larger, sea-like ranges.

The classifier uses raster water components from the mask, then associates them with extracted water-body polygons. Edge-connected water is `Ocean`. Closed water above `OceanSeaMinAreaRatio` is `OceanSea`. Closed water above `InlandSeaMinAreaRatio` is normally `InlandSea`, but it is promoted to `OceanSea` when it is within `OceanSeaNearOceanMaxDistanceCells` grid cells of edge-connected ocean water. The rest is `InlandLake`.

## Lake Levels

The elevation generator first builds pre-hydrology terrain and bathymetry as `BaseTerrain`. Inland water cells are treated as basin cells rather than as global sea, so they are not clamped by `MaxSeaElevationMeters`. `GenerateLakeLevelsStage` then applies lake surfaces and writes the final `Elevation` plus `WaterSurfaces`.

For each inland lake or inland sea:

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

Current defaults preserve the source mask:

```text
PreserveOceanCoastline = true
PreserveInlandWaterMask = true
AllowLakeExpansion = false
AllowLakeDrainage = false
```

`AllowLakeExpansion` and `AllowLakeDrainage` are options for later hydrology work. The current implementation keeps the mask stable and fixes small shoreline inconsistencies by lifting the rim.

## Lake Export

Generated artifacts include `lakes.json` when lake-surface data is available. It exports one record per inland lake or inland sea with surface elevation, spill elevation, margin, maximum depth, shoreline metrics, classification, and profile metadata. Raster water levels remain in `ElevationMap.WaterSurfaceMeters`; future routing should still use the eventual `HydroSurfaceMeters` field rather than `lakes.json`.
