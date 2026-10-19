# Elsa 4 Native Compensation and Saga Recovery

- **Status:** Active for program establishment and planning, by owner authorization on 19 October 2026.
- **Area:** Native workflow runtime/activities and Elsa Studio, together in this repository.
- **Steward:** Sipke Schoorstra; the establishing Codex workroom maintains the shared delivery records.
- **Program:** [#3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035).
- **Canonical requirements and decision register:** [PRD #3036](https://github.com/elsa-workflows/elsa-foundation/issues/3036).

## Purpose and boundaries

A user can visually author, publish, execute, inspect and recover a compensation-enabled native workflow in
Elsa Studio. Recovery must remain correct and observable across retries, cancellation and restarts, with
uncertain external outcomes represented explicitly. Studio authoring and occurrence inspection are part of
the first durable delivery slice, not a later addition.

BPMN is semantic inspiration only. This program includes no BPMN implementation, adapters, shared
compensation abstractions or BPMN dependency. Elsa 3 implementation is a later effort; this program produces
a follow-on assessment linked to [Core #4015](https://github.com/elsa-workflows/elsa-core/issues/4015).

This is a standalone program. Coordinate narrowly overlapping execution mechanisms with
[Unified Execution #2892](unified-execution.md), retaining one owner for shared changes, without depending
on that entire program completing or disturbing its active queue. Reuse proven current runtime contracts;
do not introduce a second scheduler or universal semantic engine.

## Delivery sequence

| Milestone | Integrated outcome | Owning records |
|---|---|---|
| M0 | Native semantics and Studio authoring/inspection design contract | [Contract #3041](https://github.com/elsa-workflows/elsa-foundation/issues/3041), [design task #3043](https://github.com/elsa-workflows/elsa-foundation/issues/3043) |
| M1 | Visually authored reservation journey that survives restart during recovery | [Epic #3037](https://github.com/elsa-workflows/elsa-foundation/issues/3037), [feature #3044](https://github.com/elsa-workflows/elsa-foundation/issues/3044) |
| M2 | Uncertain effects, retries and intervention | [Epic #3038](https://github.com/elsa-workflows/elsa-foundation/issues/3038), [feature #3045](https://github.com/elsa-workflows/elsa-foundation/issues/3045) |
| M3 | Nested/repeated occurrences, confirmation and native composition | [Epic #3039](https://github.com/elsa-workflows/elsa-foundation/issues/3039), [feature #3046](https://github.com/elsa-workflows/elsa-foundation/issues/3046) |
| M4 | Accessible Studio recovery inspection and authorized operator controls | [Epic #3040](https://github.com/elsa-workflows/elsa-foundation/issues/3040), [feature #3047](https://github.com/elsa-workflows/elsa-foundation/issues/3047) |
| M5 | Integrated qualification, documentation, samples and Elsa 3 follow-on assessment | [Feature #3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) |

## Current work and readiness

[Establishment #3042](https://github.com/elsa-workflows/elsa-foundation/issues/3042) owns the program records
and this repository entrypoint. The first prepared successor is design task #3043, which remains
`needs-triage` until maintainer promotion. Later features are outlines with native blocking relationships;
refine their implementation leaves only after the preceding design/evidence gates. Use one active
implementation leaf and one integration lane, with claims and transitions on the relevant issues.

The establishment baseline is `e4a48e3bdfd8a7cf58a83fd4d5c760e01b93baad` (19 October 2026), with runtime
under `Essentials/` and `Extensions/`, and consolidated Studio under `Studio/`. Refresh source, claims and
open PRs before design work. The PRD records the current persistence modes and recovery requirements;
exact contracts, public names, state schema and visual notation remain design outputs.

GitHub issue bodies, native sub-issues and blocking relationships are the current control surface.
Organization Project creation was attempted as `elsa-workflows-codex[bot]` and rejected for missing Projects
permission. No board exists for this program yet. Creating it requires that permission or explicit owner
authorization for another identity; this does not prevent the bounded design work once its prerequisites
are met. Track resolution on #3035 rather than creating a second queue.

## Authorization and completion

The owner approved creation on 19 October: "This sounds great. Let's proceed and create a program."
The [named ADR 0080 D0 amendment](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) records this
establishment scope. It does not reactivate other parked programs or authorize production implementation,
releases or deployment as part of the setup task.

The PRD owns requirements and unsettled decisions. Record consequential architecture choices in ADRs,
semantic meanings in the glossary and feature specifications through the repository's normal spec flow;
do not duplicate those sources here. Completion requires the integrated milestone demonstrations,
including a real Studio-to-runtime-to-persistence restart journey, honest recovery outcomes and explicit
compatibility limits. Program setup, issue closure or in-memory tests alone do not prove delivery.
