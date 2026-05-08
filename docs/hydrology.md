# Hydrology

This document records hydrology-facing terrain semantics. The current implementation only covers water-body classification and lake levels; river routing and full hydro-surface generation remain future stages.

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

The elevation generator first builds potential terrain and bathymetry. Inland water cells are treated as basin cells rather than as global sea, so they are not clamped by `MaxSeaElevationMeters`.

For each inland lake or inland sea:

1. Find shoreline cells: land cells adjacent to lake cells.
2. Estimate the spill point from shoreline elevations using `LakeSurfacePercentile` instead of a single minimum cell.
3. Set `lakeSurface = spillElevation - margin`.
4. Lift shoreline rim cells below `lakeSurface + margin` when the inland-water mask is preserved.
5. Shape the lake bed as `lakeSurface - depth`, with depth increasing from shore toward the center.

Current defaults preserve the source mask:

```text
PreserveOceanCoastline = true
PreserveInlandWaterMask = true
AllowLakeExpansion = false
AllowLakeDrainage = false
```

`AllowLakeExpansion` and `AllowLakeDrainage` are options for later hydrology work. The current implementation keeps the mask stable and fixes small shoreline inconsistencies by lifting the rim.
