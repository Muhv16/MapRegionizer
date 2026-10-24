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

Full map validation runs finalization and water-body counting on a background
thread. The editor shows that the check is running, stays interactive, and
applies the result only if the draft and spatial settings are unchanged. Water
bodies are counted once per check. Rasterization checks cell centers only
inside each landmass envelope using an indexed point-in-area locator, preserving
the rule that centers on a boundary count as land. Shared-boundary
canonicalization uses a spatial index to find nearby vertex candidates.
Closing the editor cancels an active validation. Geometry, rasterization, and
water-body extraction check for cancellation between their expensive operations
so the background work can stop after the editor closes.

## Persistence and App state

`ManualMapJson` writes version 3.0 `*.manual-map.json` documents containing the
grid, shared vertices, faces, the `MapSpatialReference` used to interpret their
coordinates, geographic control points, calibration parameters, and the last
calibration error report. Versions 1.0 and 2.0 remain readable. Version 1.0
projects contain no spatial reference; the editor asks before loading one with
the current spatial settings because its original geographic coordinates
cannot be recovered. Cancelling that prompt leaves the current draft and
spatial settings unchanged.

The Avalonia editor stores background visibility, lock, opacity, a map-unit
rectangle, rotation, a relative image path, and any source-to-target raster
reprojection recipe in the `*.manual-map.json.editor.json` sidecar. The recipe
keeps the original image path, source placement, source and target spatial
references, longitude offset, and optional calibration model. Opening the
project rebuilds the derived raster from the source image; it does not apply a
saved correction to the manual geometry again. Older sidecars with canvas-pixel
offsets are migrated once using those values as map-unit offsets. Background
state is presentation-only and never enters Core finalization.

## Reprojection and geographic calibration

The editor has separate **Reproject map…** and **Geographic calibration…**
actions. A direct reprojection explicitly defines how to interpret the current
vertices and how to store them, including a new grid size and `UnitsPerCell`.
It converts map units to grid cells, maps each vertex through geographic
coordinates, then converts into target map units. Shared edges are adaptively
densified once and reused by every adjacent face. The default deviation limit
is 0.25 target cell, with a bounded vertex budget and recursion depth. A
conversion that leaves the target extent or exceeds the Web Mercator latitude
limit is rejected with diagnostics. A region crossing the target canvas cut
offers an explicit cut-relocation action for full-world reprojection, or a
contour-preserving calibrated reference in geographic calibration.

Before applying, the editor validates and finalizes the output. It offers
separate choices to reproject geometry, the background image, or both. Raster
reprojection uses inverse mapping and bilinear sampling; transparent pixels
represent areas outside the source image. Applying geometry is one undoable
operation that updates the draft and spatial reference together. A background
only operation leaves region geometry and its spatial reference untouched.
Direct spatial setting changes are blocked while regions or vertices exist;
use explicit reprojection to move a populated map.

Geographic control points can be anchored to map units or normalized positions
on a source image and assigned the `Calibration` or `Validation` role. The MVP
solver fits a circular longitude offset and a linear north/south bound in the
selected Equirectangular or Web Mercator mapping. Validation points are not
used in the fit; the report shows each point's expected, before, and after
coordinates, separate calibration and validation errors, mean absolute longitude
and latitude error, and P95 and maximum angular error. All known points may
be used for fitting; independent validation points are optional.
The current model does not fit affine or local nonlinear warps.

### Working with the transformation dialogs

Geographic calibration has three tabs: **Control points**, **Fit and check**,
and **Before and after**. Each point has separately labelled latitude and
longitude fields, a name, an enabled flag, and a localized fit/check role.
Both comma and period are accepted as decimal separators; missing or invalid
active coordinates prevent calculation rather than reusing a previous value.
Disabled points do not participate in fitting or accuracy metrics. To add a
new anchor, return to the editor's **Add control point** tool. Saving points
alone does not transform either layer; removing all points can also be saved.

The fit/check tab separates model fitting, independent accuracy assessment,
and geometry applicability. A fitted model does not imply confirmed accuracy
or that every vertex fits the target extent. Unavailable metrics are shown
as missing data. Residuals, in latitude/longitude order, and copyable technical
diagnostics are collapsed separately from the concise result and next action.
Geometry and background buttons have independent availability; a failed
background preview does not invalidate a valid geometry result. Application
confirmation reports the measured fit error.

The main result shows RMS and maximum error on the points used for fitting.
Without validation points the report neutrally states that independent checking
was not performed; it does not request more points or label the fit unconfirmed.
Optional validation points report their own errors without any minimum count,
uniqueness, or spatial-spread requirement. The existing `ValidationTargetReached`
field now indicates only whether the available validation residuals meet P95 ≤ 5°
and maximum ≤ 10° (or the thresholds supplied in the request); it is false when
no validation points exist. It does not certify accuracy across the whole map.
Validation points are still excluded from fitting, and their presence, absence,
and threshold results never determine application availability. The serialized
model shape is unchanged. Latitude fitting minimizes squared residuals in
the selected projection subject to its physical bounds: ±90° for
Equirectangular and ±85.0511287798066° for Web Mercator. If ordinary least
squares extrapolates a canvas edge beyond a pole, the solver refits on the
allowed bounds using all enabled calibration points. It solves the free
edge again rather than independently clamping the old solution. A nonblocking
`latitude-fit-constrained` diagnostic and a concise UI notice explain this
case; the residual report shows the resulting errors for every point.
This avoids requiring users to disable an entire hemisphere just to obtain
a valid model. A flat or vertically inverted fit remains unsupported, and
invalid input coordinates (including calibration latitudes outside Mercator's
range when Mercator is selected) still produce explicit errors. No points
are silently removed and validation points still cannot affect the fit.
Existing feasible fits and the serialized parameter format are unchanged;
previously rejected extrapolations can now produce a bounded best fit.
To repeat the reported 16 calibration/1 validation point case through fitting,
contour preservation, application and finalization, set
`MAPREGIONIZER_CALIBRATION_SOURCE` to the supplied project's path before
`./scripts/verify.ps1 -Full`; the source is checked unchanged.

Direct reprojection similarly separates the source interpretation, target
grid, and result into tabs. The left-edge longitude is available for global
and regional maps; global longitude span stays fixed at 360°. A background-only
operation uses the current map grid and disables
the irrelevant target controls. Selecting Mercator limits initial pole
defaults to its valid latitude range; invalid manual input is rejected.
The before/after preview uses the currently displayed raster for the before
panel. Reprojecting an already warped background into another target grid
reuses its saved original-image recipe and calibration, avoiding repeated
resampling or reinterpretation under the current grid. Changing the source
interpretation of such a background requires undoing its previous warp first.

### Resolving the world cut

The antimeridian is a geographic meridian; the editable canvas cut can be
placed at a different longitude. A fitted longitude correction can move an
otherwise valid, continuous region across the existing canvas edge. Direct
reprojection then shows **Move the map cut and check**. This aligns the target's
full 360° interval with the transformed source's left edge and rebuilds the
validated preview. For a source starting at −180° with a
+12.0219° longitude correction, the proposed cut is −167.9781°. The geographic
correction is unchanged; in direct reprojection only the target longitude
interval changes.

Geographic calibration offers **Keep contours and check the reference**. This
uses the fitted projection, latitude bounds and corrected longitude interval
on the original authoring grid and map-unit scale. The same geographic
correction is therefore represented without changing the authored contours.
The standard reprojection pipeline still computes and validates every vertex
against that reference. This also avoids compressing nearly touching edges
across the six-decimal geometry precision lattice (reduced regression from
region 1360 of the supplied project). The new reference is stated in the
result and application confirmation. Separate direct reprojection remains
available when a different target projection is desired.

No region is discarded, clipped, split, or assigned a new region ID;
shared vertex IDs and edges remain shared. The actual target cut is shown in
the result and the editor's spatial summary before/after application.

Cut relocation is an explicit preview action, not an automatic change to an
existing map. Cancel leaves the editor unchanged; application is undoable with
the spatial reference, points and background together. A relocated raster can
be applied together with geometry; background-only application continues to
use the editor's current spatial reference and is disabled when the target
reference changes. Regional extents do not offer a full-world cut relocation. Faces that
still cross a chosen cut remain rejected rather than silently corrupted; this
workflow does not add multipart editable regions.

Full-world longitude continuity retains the exact western/eastern endpoint
distinction, including an eastern endpoint equivalent to the western meridian
modulo 360°. Calibration, canvas resize, spatial controls, manual-map JSON and
nested background recipes retain the non-default cut. The existing schema 3.0
and `longitudeStartDegrees`/`longitudeSpanDegrees` fields are used; old projects
with a −180° cut are unchanged. Projects saved with a relocated global cut
require the updated reader: previous application builds discarded that field
on global-map load. MapPackage v1 already stores the full longitude interval.

For an optional reproduction with a supplied v2 project and the reported
calibration parameters, set `MAPREGIONIZER_SEAM_SOURCE` to the project path
before `./scripts/verify.ps1 -Full`. Setting `MAPREGIONIZER_SEAM_OUTPUT` to an
artifact directory also writes a calibrated copy and a verification summary;
the source file is checked unchanged. This reproduction uses the supplied
parameters (+12.0219°, −28.8145°…83.7987°) and does not invent missing GCP data.

Long nonlinear edges are checked at their midpoint and quarter points before
subdivision, including symmetric edges crossing the equator. The vertex
budget is enforced during subdivision, before allocating the whole result.

Changing any point or spatial parameter cancels the running preview and
invalidates application. Closing a dialog cancels its calculation; closing
an application confirmation leaves the editor unchanged. During atomic
application the calibration dialog temporarily disables editing and closing.
Applied models retain their applied flags when points are added or edited.
An already applied calibration cannot be fitted/applied again in that dialog:
undo the application in the editor before fitting another correction.

Both dialogs support a minimum 740 × 480 logical-pixel window, scrolling
within tabs, and wrapping footer actions. Automated tests exercise 740 × 480,
1080 × 760, and 1460 × 900 at 100%, 150%, and 200% render scaling with the
Fluent theme and Skia rendering. To save the actual test-rendered tabs for
visual inspection, set `MAPREGIONIZER_UI_SNAPSHOTS` to an absolute artifact
directory before running `./scripts/verify.ps1 -Full`. Snapshots and generated
CLI runs belong under `artifacts/` and are not committed. The manual-map
schema remains 3.0 and the MapPackage v1 output contract is unchanged.

The calibration preview shows transformed outlines, a geographic graticule,
and the resampled background where available. Numbered point markers and
lines to known locations make residuals visible on the preview; numeric
residuals remain authoritative in the point report. If geometry fails but
the background is ready, the after panel is labelled as background-only.
The user can explicitly apply
the model to geometry, only to the background, or to both. Applying the model
to geometry marks it as applied in the project; reopening the project does not
transform those vertices again. The background recipe is stored separately
and regenerates a derived raster without modifying the source image.

The editor can add or remove empty edge strips from the canvas. Existing shared
vertices are translated once and retain their IDs; the spatial coverage is
updated using equirectangular degrees per cell or Web Mercator projected units
per cell so their geographic coordinates remain fixed. A shrink is rejected
if any saved or in-progress vertex would lie outside the new canvas. Global
maps cannot change width because their longitude span is already 360 degrees.
Resize is limited to 100,000 cells on either side and 100,000,000 cells total.
The background rectangle moves with the map vertices and keeps its map-unit
size, so it does not stretch when the canvas changes. **Fit background** remains
an explicit action that fills the current canvas.

Resize preview work is debounced and cancellable, and runs off the UI thread.
Applying a resize reuses the calculated draft; geometry validation and spatial
index preparation also run in the background while the dialog shows a spinner.
The resize preview caches the region outlines and translates them during
rendering instead of rebuilding coordinate lists on every frame.

Selecting the manual map source does not show an invalid-map warning by itself.
The warning appears when generation or export is requested without a finalized
manual map. Closing the editor without creating a map refreshes validation and
returns the main window without leaving that warning visible.

Modal results are accepted on the UI dispatcher after the closing callback
has unwound. Avalonia completes `ShowDialog` before removing the child from
its owner, so accepting a result or disposing the editor inline can run while
the native main window is still disabled. The App uses `OwnedWindowDialog`
for the manual and region editors, resize, reprojection, calibration and its
confirmation. It reactivates a visible owner after ownership cleanup, preserves
its prior enabled state, and does not interrupt another open modal window.
The headless UI regression exercises validation, finalization, result handoff,
cancellation and reopening, and observes native platform enablement separately
from `Window.IsEnabled`. To repeat this workflow with a local calibrated map,
set `MAPREGIONIZER_FINALIZE_SOURCE` to its absolute path before verification.
This lifecycle change does not alter map geometry or project formats.

Global coverage exposes independent south and north latitude bounds. Regional
coverage also uses explicit west/east and south/north bounds. Accepted spatial
settings update the App configuration and are used to finalize and generate the
map; cancelling the editor discards those edits. The package output contract
does not change.

## Current limitations and future work

The editor supports creating, selecting, renaming, deleting, snapping, edge
splitting, undo/redo, background reference images, reprojection, geographic
control points, validation, and finalization. It does not provide a complete
GIS vertex-move tool, arbitrary GIS projections, affine calibration, or local
nonlinear calibration. Tectonics, elevation, hydrology, and climate are not
special manual stages; the derived mask and spatial reference are normal
pipeline inputs, so a finalized manual map uses the existing stages.
