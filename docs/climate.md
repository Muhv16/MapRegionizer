# Climate Generation

Climate is generated as a raster stage after hydrology. The model is intentionally heuristic: it is designed to produce readable strategy-map climate belts, wet coasts, dry interiors, rain shadows, river corridors, polar ice, highland snow, and biome output without requiring a full atmospheric simulation.

## Pipeline Position

```text
Elevation
 -> WaterSurfaces
 -> Hydrology
 -> Climate
```

`GenerateClimateStage` depends on final terrain, water-body classification, water-surface metadata, and hydrology. It produces `ClimateMap`, which is exposed on `GeneratedMap.Climate` and `MapGenerationSession.Climate`.

## Domain Model

`ClimateMap` has the same raster size as `ElevationMap`.

Primary fields:

- `MeanAnnualTemperature`
- `SummerTemperature`
- `WinterTemperature`
- `Seasonality`
- `AtmosphericMoisture`
- `Precipitation`
- `Moisture`

Derived fields:

- `ClimateClass`
- `Biome`
- `Habitability`
- `AgriculturalPotential`
- `RainShadow`
- `MonsoonInfluence`
- `IceScore`
- `LatitudeNorm`

`LatitudeNorm` is `0` at the equator and approaches the pole toward the top and bottom edges. The edge does not have to be exactly `1`; `ClimateGenerationOptions.PolarLatitudeMargin` defaults to `0.05`, so the outermost rows behave like about `0.95` latitude-normalized polar proximity instead of a mathematical pole.

## Temperature

Base temperature follows a curved latitude model:

```text
baseTemp = EquatorTemperatureCelsius
         - pow(latitudeNorm, LatitudeCurveExponent) * PoleCoolingCelsius
```

Defaults:

```text
EquatorTemperatureCelsius = 28
PoleCoolingCelsius = 55
LatitudeCurveExponent = 1.35
LapseRateCelsiusPerMeter = 0.0045
```

Land temperature is cooled by elevation:

```text
temp -= elevationMeters * LapseRateCelsiusPerMeter
```

Terrain classes then add small local modifiers. Mountains and highlands cool the cell; dry basins and desert plateau candidates warm it slightly. Large water bodies reduce seasonality, while inland distance increases seasonal range:

```text
seasonality = BaseSeasonality
            + latitudeNorm * LatitudeSeasonality
            + continentality * ContinentalSeasonality

summerTemp += continentality * ContinentalSummerBoost
winterTemp -= continentality * ContinentalWinterPenalty
```

`continentality` is based on distance to ocean, ocean-sea, inland sea, or a lake above `LargeLakeMinCellCount`.

## Moisture And Winds

The generator builds moisture from sources and a wind march rather than pure noise.

Large sources:

- ocean;
- ocean seas;
- inland seas;
- large lakes.

Local sources:

- smaller lakes;
- visible rivers;
- deltas and river mouths;
- modest land evapotranspiration in warm cells.

For each latitude row, the stage selects an earth-like prevailing wind:

```text
Tropics:        east -> west
Midlatitudes:   west -> east
Polar:          east -> west
```

It also adds a small north-south component:

- Hadley belt moves toward the equator;
- midlatitudes drift weakly poleward;
- polar air drifts toward lower latitudes.

Cells are processed from upwind edge to downwind edge. Incoming moisture combines the previous upwind cell, a small vertical wind component, and local evaporation. Rainfall removes moisture from the air parcel.

## Orography And Rain Shadow

Orographic rainfall is based on upwind slope:

```text
slopeUpwind = elevation[cell] - elevation[upwindCell]
orographicRain += max(0, slopeUpwind) * OrographicStrength
```

The effect is strengthened by `Mountain`, `Highland`, `RidgeContinuity`, and `FoothillInfluence`. When air descends, the stage adds drying:

```text
descentDrying += max(0, elevation[upwindCell] - elevation[cell]) * DescentDrying
```

This feeds `RainShadow`, which reduces later precipitation and final moisture. Dry terrain classes such as `DryBasin` and `DesertPlateauCandidate` add a smaller local drying signal.

## Continental Drying

Interior land becomes drier even without mountains:

```text
moisture -= distanceToLargeWater * ContinentalDrying
```

Rivers and deltas add local moisture and a stronger agriculture bonus, but they do not fully erase global aridity. This is intended to create fertile corridors through dry biomes instead of turning every river basin into forest.

## Monsoons

`MonsoonInfluence` is an additional tropical/subtropical seasonal layer. It is strongest where warm large water is nearby, the cell is on land, there is nearby continental interior, and the coast has eastward or equator-facing water exposure.

Effects:

```text
precipitation += monsoon * MonsoonRainStrength
seasonality += monsoon
winterTemp -= monsoon * DrySeasonStrength
```

This helps produce savannas, monsoon forests, wet tropical coasts, and dry-winter/wet-summer climates.

## Snow And Ice

Ice is not temperature-only. The stage combines cold summer temperatures with snow availability:

```text
coldness = clamp((SnowMeltThresholdCelsius - summerTemp) / 14)
snowAvailability = clamp(precipitation / SnowPrecipitationScale)
iceScore = coldness * (0.32 + snowAvailability * 0.68)
```

This allows:

- wet glaciated coasts;
- dry polar deserts;
- snowy mountain chains;
- cold but low-snow plateaus.

## Classification

`ClimateClassKind` is a broad climate class:

- `Ocean`
- `TropicalWet`
- `TropicalSeasonal`
- `HotArid`
- `SemiArid`
- `WarmTemperate`
- `TemperateWet`
- `Continental`
- `Boreal`
- `Tundra`
- `PolarDesert`
- `IceCap`
- `Alpine`

`BiomeKind` is meant for map rendering:

- `Ocean`
- `TropicalRainforest`
- `MonsoonForest`
- `DryTropicalForest`
- `TropicalSeasonalForest`
- `Savanna`
- `OpenWoodland`
- `HotDesert`
- `SemiDesert`
- `RockyDesert`
- `SaltFlat`
- `ColdDesert`
- `Steppe`
- `XericShrubland`
- `MediterraneanShrubland`
- `TemperateGrassland`
- `TemperateForest`
- `TemperateRainforest`
- `BorealForest`
- `Tundra`
- `PolarDesert`
- `IceSheet`
- `AlpineTundra`
- `Wetland`
- `Floodplain`
- `Marsh`
- `Mangrove`
- `MontaneForest`
- `CloudForest`
- `SnowyMountain`
- `VolcanicBadlands`

Habitability and agriculture are normalized `0..1` fields. Habitability favors moderate temperature, moderate moisture, rivers, and lower elevations. Agriculture favors warm temperatures, sufficient but not excessive moisture, and river corridors more strongly than biome moisture.

`Moisture` remains the physical-ish moisture field. `BiomeMoisture` is a presentation/classification field. It is land-normalized separately inside broad temperature bands, then nudged by wet coasts, windward rainfall, and small river-valley bonuses. This keeps a dry world from losing all rainforests and wet forests, while still letting physical moisture stay available for diagnostics.

Overlay fields are not base biomes:

- `RiverValleyInfluence`: green corridor signal along rivers and deltas.
- `WetlandInfluence`: marsh/floodplain/lake-lowland signal.
- `SnowOverlay`: snow tint for cold mountains and high-latitude ice.
- `MountainOverlay`: rocky/highland tint and relief emphasis.

These overlays let a desert remain `HotDesert` while still showing a Nile-like river corridor, or let a forested range show snowy summits without erasing the underlying forest biome.

## Options

Climate options live in `ClimateGenerationOptions`.

Important controls:

- `PolarLatitudeMargin`
- `EquatorTemperatureCelsius`
- `PoleCoolingCelsius`
- `LatitudeCurveExponent`
- `LapseRateCelsiusPerMeter`
- `ContinentalityDistanceCells`
- `ContinentalDrying`
- `OrographicStrength`
- `DescentDrying`
- `RiverMoistureBonus`
- `RiverAgricultureBonus`
- `MonsoonRainStrength`
- `DrySeasonStrength`
- `SnowMeltThresholdCelsius`
- `SnowPrecipitationScale`

The CLI exposes the main latitude and temperature controls:

```text
--climate-polar-latitude-margin
--climate-equator-temperature
--climate-pole-cooling
--climate-lapse-rate
--climate-json-mode
```

## Rendering And Export

Artifact export writes:

```text
climate-biomes-debug.png
climate-biomes-presentation.png
climate-temperature.png
climate-moisture.png
climate-precipitation.png
climate-habitability.png
climate-agriculture.png
climate-ice.png
climate.json
```

`climate-biomes-debug.png` is a flat class mask. `climate-biomes-presentation.png` is the game-facing map. Water uses the final elevation renderer so oceans, seas, and lakes keep bathymetry and lake-depth tone. Land blends biome color with final terrain color, hillshade, ridge/foothill influence, mountain tint, river-valley tint, wetland tint, texture, and snow overlay. Visible hydrology rivers are drawn over the biome-relief map so the layer can be used directly as a strategy-game world map.

The presentation palette deliberately separates transitional dry biomes by value and saturation: grassland is brighter green, steppe is golden, savanna is yellow-green, Mediterranean shrubland is darker olive, xeric shrubland is gray-ochre, semi-desert is pale sand, rocky desert is gray-brown, hot desert is warmer orange, and cold desert is cooler gray. Desert biomes also receive dune/rock texture modifiers so large arid regions read as strategic obstacles rather than generic tan land.

Biome edges are lightly blended with neighboring biome colors, while uninterrupted biome interiors get a small saturation/value lift. River-valley accents are rendered separately from broad floodplain data: floodplain and wetland biomes may occupy lowlands, but the presentation layer draws a thinner, brighter green/blue-green accent directly along river polylines based on discharge.

`climate.json` includes compact run-length encoded rows for climate class, biome, mean annual temperature, physical moisture, biome moisture, habitability, and agricultural potential. Diagnostic mode adds summer and winter temperature, seasonality, latitude, atmospheric moisture, precipitation, rain shadow, monsoon influence, river-valley influence, wetland influence, snow overlay, mountain overlay, and ice score rows.
