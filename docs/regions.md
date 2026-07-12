# Region Geometry Contract

`RawRegions` is the direct output of automatic region generation. `Regions` is the editable final region set after boundary post-processing. Both data keys obey the same geometric contract; a later manual editor must validate its result against the same contract before it replaces `Regions`.

## Invariants

For every valid map state:

- every region is a non-empty valid `Polygon`;
- each positive `RegionId` is unique; automatic generation assigns ids in deterministic landmass and polygon order, so the same input, options, seed, and pipeline path produce the same ids and geometry;
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

This is a topology rule, not a display preference. It prevents differences around `1e-10` from becoming artificial gaps, unmatched shared edges, or false non-neighbours. The pipeline preserves the exact source coastline instead of independently rounding intermediate points: on a sloped coast, independently rounded points cannot remain exactly collinear with the original coast. Code that creates or edits a polygon must use `RegionGeometryPrecision` for coordinate and topology comparisons, and must validate the completed collection with `RegionGeometryContract.Validate` (or throw through `EnsureSatisfied`). The validator is deliberately explicit rather than an unconditional pipeline pass, because union validation of a large map is an expensive editing/save-time operation.

## Validation

The Core test suite covers a deterministic map with a water hole and a separate island both before and after distortion. It verifies the full contract, deterministic ids/geometry for a fixed seed, the disabled-distortion path, and the point-touch adjacency rule.
