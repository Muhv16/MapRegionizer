# AGENTS.md

## Project overview

MapRegionizer is a .NET procedural strategic-map generator.

The generation pipeline currently includes tectonics, elevation and terrain,
climate, hydrology, and region generation.

Prefer validating generation changes through `MapRegionizer.Cli`.

## Repository map

* `src/MapRegionizer.Core` — domain models and generation algorithms.
* `src/MapRegionizer.GeoJson` — GeoJSON serialization and export.
* `src/MapRegionizer.ImageSharp` — image loading and rendering adapters.
* `src/MapRegionizer.Runner` — orchestration and artifact generation.
* `src/MapRegionizer.Cli` — command-line interface.
* `src/MapRegionizer.App*` — Avalonia UI and platform hosts.
* `samples` — small deterministic masks, configurations, and expected metadata.
* `docs` — architecture, algorithms, development workflow, and decisions.
* `artifacts` — generated output; do not commit generated runs unless explicitly requested.

Read the relevant document under `docs/` before changing a generation stage.

## Architecture boundaries

Keep `MapRegionizer.Core` independent of UI, ImageSharp, filesystem-specific,
CLI, and GeoJSON concerns.

Allowed dependency direction:

```text
Core
├── GeoJson ──────┐
├── ImageSharp ───┼── Runner ─── CLI
└─────────────────┘       └────── App
```

Higher-level projects may depend on lower-level projects. Do not introduce a
dependency from `Core` to an adapter, runner, CLI, or UI project.

Place:

* generation algorithms and domain invariants in `Core`;
* serialization formats in their adapter projects;
* filesystem orchestration and artifact writing in `Runner`;
* argument parsing and console presentation in `Cli`;
* interactive state and presentation in the application projects.

Do not move domain logic into the CLI or UI to make a single feature easier.

## Generation requirements

Generation must be reproducible for the same input, configuration, and seed.

When adding randomness:

* use the random source supplied by the pipeline;
* do not instantiate `Random` inside generation algorithms;
* do not use current time, process IDs, unordered hash iteration, or environment state;
* preserve stable processing order where it affects output.

Prefer algorithms that expose explicit inputs and outputs. Avoid hidden mutable
global state.

Changes to one generation stage must not silently change the meaning of data
owned by another stage. Document any intentional contract change.

## Code conventions

Follow `.editorconfig` and existing C# conventions.

Prefer:

* small focused types;
* explicit names based on map-generation terminology;
* immutable result models where practical;
* pure calculation methods separated from filesystem and rendering code;
* comments that explain algorithmic intent or non-obvious constraints.

Do not add abstractions only to reduce the number of lines in one class.
Introduce an abstraction when it creates a real boundary, supports more than one
implementation, or makes an algorithm independently testable.

## Working procedure

Before editing:

1. Locate the owning project and generation stage.
2. Read its relevant documentation and nearby tests.
3. Identify invariants and downstream consumers.
4. For a behavioral change, establish a failing test or a reproducible CLI case.

Keep changes scoped to the task. Do not combine algorithm changes with broad
renaming or formatting unless required.

When changing public models, configuration, serialized output, CLI arguments,
or generation-stage behavior, update the corresponding documentation.

## Validation

Use the repository scripts rather than inventing alternate build commands.

For normal code changes:

```powershell
./scripts/verify.ps1 -Fast
```

For generation algorithms, serialization, pipeline orchestration, randomness,
or output artifacts:

```powershell
./scripts/verify.ps1 -Full
```

A full verification must include a deterministic CLI run using a fixed seed and
a small sample mask.

## Generated files

Keep test fixtures small. Do not add large generated maps when a reduced mask
can demonstrate the same behavior.

## Definition of done

A change is complete when:

* the implementation is in the correct architectural layer;
* required verification passes;
* deterministic generation remains deterministic;
* public behavior and formats are documented;
* no unrelated generated artifacts or formatting changes are included.

In the final report, state:

* what changed;
* which architectural area was affected;
* which validation commands were run;
* any intentional output or compatibility changes.