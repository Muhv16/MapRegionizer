# Map package (`MapRegionizer.MapPackage.v1`)

The map package is the portable representation of a **finalized** MapRegionizer
map. It stores the minimum information a third-party application needs to
restore the map geometry: the versioned schema identity, generator metadata,
the canonical spatial reference, map bounds, landmasses, and regions.

It is an interchange format owned by MapRegionizer. It does not adapt to any
specific downstream project; consumers import the package and map it onto
their own models.

## Purpose and non-goals

The package is **not**:

* a manual editor project — unfinished, editable manual drafts stay in
  `*.manual-map.json` (`ManualMapJson`);
* a generation configuration;
* a raster simulation snapshot;
* a consumer-specific format;
* a presentation export — separate GeoJSON exports
  (`regions.geojson`, `landmasses.geojson`, `water-bodies.geojson`) keep
  their output-coordinate transforms.

The export path is origin-independent. A map generated from a mask and a
manually authored map both flow through the standard generation pipeline into
`GeneratedMap`, and the same writer serializes both:

```text
mask / manual authoring
        |
generation pipeline
        |
GeneratedMap
        |
MapPackageWriter -> *.map.json
```

## v1 contents

```json
{
  "schema": "MapRegionizer.MapPackage.v1",
  "generator": { "name": "MapRegionizer", "version": "1.0.0.0" },
  "spatialReference": {
    "worldModel": { "kind": "Spherical", "planetRadius": null },
    "gridMapping": "Equirectangular",
    "topology": "CylindricalX",
    "coverage": {
      "kind": "Global",
      "longitudeStart": -180,
      "longitudeSpan": 360,
      "southLatitude": -90,
      "northLatitude": 90
    },
    "gridWidth": 2048,
    "gridHeight": 1024,
    "unitsPerCell": 1.0,
    "canonicalCoordinates": "GridMapUnits",
    "preserveProjectedCellAspectRatio": true,
    "legacyCompatibility": "None"
  },
  "bounds": { "width": 2048, "height": 1024, "unitsPerCell": 1.0 },
  "landmasses": [
    { "id": 1, "shape": { "type": "Polygon", "coordinates": [] } }
  ],
  "regions": [
    { "id": 1, "landmassId": 1, "shape": { "type": "Polygon", "coordinates": [] } }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `schema` | Stable versioned identifier, exactly `MapRegionizer.MapPackage.v1`. |
| `generator.name` | Always `MapRegionizer`. |
| `generator.version` | Exporting library assembly version; informational only. |
| `spatialReference` | The complete canonical `MapSpatialReference` descriptor in the shared spatial JSON format (the same shape used by region drafts). |
| `bounds` | Map extents in canonical map units: `width`, `height`, and `unitsPerCell`. |
| `landmasses[].id` | Stable `LandmassId`. |
| `landmasses[].shape` | GeoJSON `Polygon` (exterior ring first, interior rings are water holes). |
| `regions[].id` | Stable `RegionId`, unique across the package. |
| `regions[].landmassId` | Reference to an exported landmass. |
| `regions[].shape` | GeoJSON `Polygon`. |

## Canonical coordinates

Geometry is stored in the canonical MapRegionizer coordinate space
(`GridMapUnits`). The writer never re-projects geometry into
`GeographicLongitudeLatitude`, `WebMercator`, or any other output coordinate
system. The stored `spatialReference` is complete enough (grid dimensions,
units per cell, world model, coverage, grid mapping, topology, canonical
coordinate space, aspect-ratio policy, and legacy compatibility profile) for a
consumer to place the grid on the world surface and derive any presentation
projection itself.

## Derived data

The following is intentionally not stored in v1 and can be reconstructed:

* **Water bodies**: `map bounds - union(landmasses)`. Landmass polygons keep
  interior rings, so inland lakes inside a landmass are preserved, and
  separate islands stay separate landmasses.
* **Region adjacency**: shared polygon edges between regions of the same
  landmass. A vertex-only touch is not adjacency.
* Region raster, spatial indexes, internal topology structures, and editor
  vertex ids are not exported.

## Determinism

For the same finalized map the export is byte-for-byte deterministic:

* landmasses are ordered by `LandmassId`, regions by `RegionId`;
* object properties are written in a fixed order;
* numbers use invariant-culture round-trip formatting;
* no timestamps or other run-dependent values are emitted.

## Validation

The writer performs a light structural check before serializing — the map
must be non-null, landmass and region polygons must be non-empty and
topologically valid with finite coordinates, region ids must be unique, and
every region must reference an exported landmass. Heavier invariants
(coverage, overlap, shared-edge identity) are guaranteed by the region
geometry contract of the pipeline and are not rechecked.

## Library API

```csharp
string json = MapPackageWriter.Write(map);
MapPackageWriter.WriteToFile(map, "map-package.map.json");

MapPackageDocument document = MapPackageReader.Read(json);
MapPackageDocument loaded = MapPackageReader.ReadFromFile("map-package.map.json");
```

The reader returns the geometry-only `MapPackageDocument`
(`SpatialReference`, `Bounds`, `Landmasses`, `Regions`, `Generator`).
It deliberately does not construct a `GeneratedMap`: a geometry-only package
does not carry tectonics, elevation, hydrology, climate, or region-raster
state, so a full map cannot be restored faithfully. Consumers should adapt
`MapPackageDocument` to their own models.

## Versioning

The `schema` string is the only version identity:
`MapRegionizer.MapPackage.v1`. Future incompatible or additive changes must
introduce a new identifier (for example `MapRegionizer.MapPackage.v2`) instead
of silently changing the meaning of v1 documents. Readers reject unknown
schema identifiers with a clear error; there is no implicit migration.

## Where it fits

```text
*.manual-map.json        editable manual project/source data
region-draft.geojson     editable region subdivision draft
regions.geojson etc.     individual GIS/debug exports
*.map.json               whole finalized map as a portable package
```
