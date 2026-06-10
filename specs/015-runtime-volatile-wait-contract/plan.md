# Implementation Plan: Runtime Volatile Wait Contract

**Branch**: `codex/runtime-volatile-wait-contract` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next Runtime Execution Seam contract slice after the baseline state/checkpoint/bookmark/value/recovery/migration units. This slice codifies volatile wait as an in-memory activity/branch-scoped wait and represents completion as deterministic scheduler continuation work, not durable bookmark resume and not recursive bubbling.

## Technical Context

- Runtime state contracts already live under `src/Elsa/Workflows/Runtime/Core/Models/`.
- `SchedulerState` currently carries pending activity work and a placeholder volatile wait registration.
- Checkpoint commits already include typed scheduler state changes.
- Runtime extension-point docs are maintained through `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` and generated `docs/maps/extension-point-map.md`.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes only Runtime.Core models/contracts/tests. |
| Artifact-only runtime | PASS | Volatile waits reference activity execution and scheduler state, not authored documents. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated volatile wait contract tests. |
| Extension points documented | PASS | Add policy boundary to runtime extension-point catalog if introduced. |

## Scope

- Add typed volatile wait registration state.
- Add typed scheduler continuation work item state.
- Add volatile wait policy request/decision contract.
- Update `SchedulerState` to carry continuation work separately from activity scheduling.
- Add focused tests and refresh generated extension-point map.

## Out of Scope

- Executing waits.
- Durable suspension/bookmark implementation.
- Pause/unpause runtime control plane.
- Generator emissions.
- Activity completion propagation scheduler drain.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
