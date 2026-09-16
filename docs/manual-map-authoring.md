# Manual map authoring

Manual authoring creates geography from an empty canvas. It is a separate
editable source from `RegionDraft`: `RegionDraft` subdivides existing
landmasses, while `ManualMapDraft` defines both the coastline and the region
faces.

## Core model

`ManualMapDraft` contains a grid size, shared `ManualMapVertex` records, and
`ManualRegionFace` records that reference vertex IDs. Neighbouring faces must
reuse the same vertex IDs. A vertex inserted on an existing edge is inserted
into every face incident to that edge, so the shared boundary remains exact.

The draft may contain no regions, isolated islands, and uncovered map area.
Uncovered area is water in this workflow. A completed face must still have at
least three unique vertices, finite coordinates, positive area, valid polygon
topology, and no material overlap with another face. Coordinates are rounded
with the same six-decimal `RegionGeometryPrecision` policy used by ordinary
regions.

## Finalization

`ManualMapDraftFinalizer.FinalizeDraft` performs these steps:

1. validates faces and returns machine-readable `ManualMapDiagnostic` values;
2. unions valid face polygons into deterministic vector `Landmass` components;
3. derives each region's `LandmassId` and creates the normal `RegionDraft`;
4. verifies the draft through `RegionCoverageCanonicalizer`;
5. projects the authoritative vector landmasses into a `MapMask` by marking a
   cell land when its center is covered by a landmass polygon.

Vector landmass geometry is authoritative coastline geometry. The raster mask
is a simulation representation and is never used to reconstruct the manual
coastline. `ExtractWaterBodiesStage` continues to calculate water as map bounds
minus vector landmasses, which preserves holes and separate islands.

The resulting `MapGeometrySeed` is supplied to `MapGenerationSession`. The
seed marks `Landmasses` and `RegionDraft` as explicit available inputs, so the
normal dependency-driven pipeline skips the extraction/generation producers.
`CanonicalizeRegionDraftStage`, `DistortRegionBoundariesStage`, water
extraction, preview, and export remain the existing pipeline paths. Manual
maps set boundary distortion off by default; it can be enabled explicitly.

## Editor performance

The editor keeps a uniform-grid spatial index for vertices and unique shared
edges. Pointer snapping queries only nearby cells and adding a free vertex
updates the vertex index incrementally; the index is rebuilt only when face
topology changes or a project is restored. Completed-face validation is not
run for every pointer event or free-vertex click.

The canvas stores region and vertex marker geometry in map coordinates and
reuses it between frames. Screen transforms and stroke widths are applied at
render time, while pointer previews are sampled at approximately 60 Hz. Region
selection uses an envelope index before running the exact polygon containment
check.

## Persistence and App state

`ManualMapJson` writes a versioned `*.manual-map.json` document containing the
grid, shared vertices, and faces. The Avalonia editor stores background image
visibility, lock, opacity, transform, and a relative image path in the
`*.manual-map.json.editor.json` sidecar. Background state is presentation-only
and never enters Core finalization.

## Current limitations and future work

The first editor supports creating, selecting, renaming, deleting, snapping,
edge splitting, undo/redo, background reference images, validation, and
finalization. The current editor does not provide a complete GIS vertex-move
tool for manual geography. Tectonics, elevation, hydrology, and climate are
not special manual stages; the derived mask and spatial reference are already
normal pipeline inputs, so future simulation work can consume a manual map
through the existing stages.
