# Region Geometry Contract

`RawRegions` is the canonical output of a selected region draft source. The source is either automatic generation or an externally supplied `RegionDraft`; it is never a final `MapRegion` list supplied directly by an editor. `Regions` is the editable final region set after boundary post-processing. Both data keys obey the same geometric contract.

An editor can run the session through `Landmasses`, call `MapGenerationSession.SetRegionDraft`, and then run the normal region branch. Replacing a draft invalidates `RawRegions`, `Regions`, and `RegionRaster`, but not tectonics, elevation, hydrology, or climate. Passing `null` selects the automatic source again.

## Invariants

For every valid map state:

- every region is a non-empty valid `Polygon`;
- each positive `RegionId` is unique; automatic generation assigns ids in deterministic landmass and polygon order, so the same input, options, and seed produce the same ids and geometry, including when only the region branch is run;
- every region references exactly one existing `LandmassId`;
- for each landmass, the union of its regions equals the landmass polygon within the coordinate-precision model;
- regions for the same landmass have no material area overlap and leave no material gaps;
- an internal border is stored by both neighbouring polygons with the same endpoint coordinates in reverse order;
- every unpaired region edge lies on the corresponding landmass boundary within the coordinate-precision model; therefore the exterior coastline, islands, and interior water holes are retained;
- no region extends beyond its landmass, so water belongs to no region;
- two regions are neighbours only if their boundary intersection has non-zero length. A vertex-only touch is not adjacency.

`DistortRegionBoundariesStage` must preserve all these invariants. It changes only shared internal borders; the landmass boundary is never distorted.

## Coordinate precision

Landmass and region topology uses one fixed precision: six decimal places (`1e-6` map units). Reverse-coordinate comparison, directed replacement lookup, adjacency, coverage checks, and validation all use this precision.

This is a topology rule, not a display preference. It prevents differences around `1e-10` from becoming artificial gaps, unmatched shared edges, or false non-neighbours. Source coast geometry remains the authoritative clipping boundary; all comparisons against it use the same precision model. Code that creates or edits a polygon must use `RegionGeometryPrecision` for coordinate and topology comparisons, and must validate the completed collection with `RegionGeometryContract.Validate` (or throw through `EnsureSatisfied`). The validator is deliberately explicit rather than an unconditional pipeline pass, because union validation of a large map is an expensive editing/save-time operation.

## Drafts, canonicalization, and diagnostics

`RegionDraft` intentionally permits a missing ID, an invalid geometry, an incomplete coverage, and a temporary overlap. It is not pipeline output. `RegionCoverageCanonicalizer` is the only Core API that can turn it into `RawRegions`; error diagnostics make the result ineligible for finalization. Diagnostics have stable machine-readable codes, severity, and, where applicable, a region and landmass ID.

Canonicalization performs only deterministic, unambiguous repairs:

- snap-rounding to the shared `1e-6` grid and removal of components that collapse on that grid;
- insertion of existing endpoints and segment crossings into both sides of a shared boundary, so different segmentation of the same edge becomes one shared edge sequence;
- clipping a valid polygon to its explicitly assigned landmass when that clipping yields exactly one non-empty polygon;
- merging a microscopic face into the neighbour with the longest shared edge (with ID as the deterministic tie-breaker).

It does not guess at a material overlap, gap, an invalid/self-intersecting ring, an unknown landmass, a non-finite coordinate, or clipping that produces multiple polygons. Those conditions are error diagnostics and the caller must ask the user to resolve them. A *gap* is landmass area absent from the union of its regions beyond area tolerance; an *overlap* is pairwise region intersection above that tolerance; a *sliver* is a face at or below `max(1, perimeter) * 1e-6` square map units. A point-only contact is neither an edge nor adjacency.

## Editable topology

`RegionTopology` is the Core representation used by an editor after a draft has been canonicalized. It exposes faces, shared vertices, and edges. An edge with one incident face is a coast edge; its vertices are protected. Moving an interior topology vertex emits a new draft and changes every incident face, never just one neighbouring polygon. The draft must still return through the canonicalizer, which is where invalid moves, coverage errors, and all repair diagnostics are reported.

## Region IDs

Positive, unique IDs supplied by a draft are retained. Missing IDs are allocated in draft order using the smallest available positive integer, making manual input deterministic. Normal vertex/edge edits retain face IDs. Split and merge policies are editor operations to be added later; until then they must supply already-valid IDs in the draft. `RegionRaster` consumes the final IDs unchanged.

## Boundary distortion safety

`DistortRegionBoundariesStage` accepts only canonical `RawRegions`. It validates the candidate result for every landmass with the same geometry contract used by drafts. `MaxOffset` is a strict upper bound on the distance an inserted boundary point can move; a value of `0` preserves raw geometry exactly. If the requested deformation violates the contract, the stage retries with deterministically reduced offset and detail, adding a `distortion-reduced` diagnostic if that succeeds. If every safe attempt causes an invalid polygon, an overlap, a gap, a broken shared edge, or a coastline violation, that landmass keeps its original raw boundaries and a `distortion-reverted` warning is added to `RegionDiagnostics`. The final `Regions` collection is always validated before it is exposed to rasterization or export.

## Portable editable GeoJSON

`RegionDraftGeoJson` is the portable, versioned authoring format; `regions.geojson` remains the unchanged final-output format. The document is a GeoJSON `FeatureCollection` with extension members `schemaVersion`, `projectionMode`, `bounds`, `maskFingerprint`, `landmassFingerprint`, and `applyBoundaryDistortion`. Each feature keeps `regionId`, `landmassId`, `origin`, geometry, and optional `name` and string `metadata`.

Version `1.0` accepts imports only when schema version, projection mode, bounds/pixel size, logical source-mask fingerprint, and landmass-geometry fingerprint all match. A failed check stops the import before the draft is applied; transferring edits to a changed coastline is deliberately a later explicit operation.

## Desktop editor

The desktop App opens manual editing in a separate large `RegionEditorWindow`, keeping the main generation workspace available behind it. The editor operates on `RegionTopology`/`RegionDraft`, not independent neighbouring polygons. A split uses two points as a direction: the editor extends that line and derives the actual cut from its intersections with the selected region boundary, so neither click has to land on the boundary. After the first point, the provisional line follows the cursor. Merge is deliberately two-step: select the retained region, press **Merge**, then select its neighbouring target. The editor also provides face rename, shared-vertex move, shared-edge vertex insertion, delete-with-reassignment to the first deterministic neighbour, undo/redo, structured diagnostics, optional distortion preview, and independent draft save/load.

There are two entry points in the Regions settings:

- **Open region editor** starts from automatic canonical `RawRegions`; the user may choose whether the completed draft will be distorted.
- **Edit visible result** starts from current final `Regions`, marks them `GeneratedAndEdited`, and forces distortion off, preventing a second pass over a user-confirmed outline.

Automatic region settings (`Seed`, target area, point multiplier, and area limits) invalidate the automatic `RegionDraft` source, rather than only its canonical `RawRegions` result. The region generator and boundary-distortion stages use independent deterministic random streams, so running just the region branch produces the same automatic regions as a fresh map run with the same mask, options, and seed. A draft applied from the editor remains the active source until the user selects **Return to automatic regions**; while it is active, automatic region settings do not overwrite the confirmed edit.

A background image is an editor-only visual layer. Its visibility, lock, opacity, scale, offset, and rotation are editor state and never alter the mask, landmasses, or portable draft document. Saving a draft also writes a `<draft>.editor.json` sidecar with these values and a relative background-image path when possible. When any landmass receives a `distortion-reverted` warning, the desktop App shows that the distortion settings are too strong and provides an expandable list of the affected landmasses.

## Validation

The Core test suite covers a deterministic map with a water hole and a separate island both before and after distortion. It verifies the full contract, deterministic ids/geometry for a fixed seed, the disabled-distortion path, and the point-touch adjacency rule.
