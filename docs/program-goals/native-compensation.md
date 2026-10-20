# Elsa 4 Native Compensation and Saga Recovery

- **Status:** Active for end-to-end delivery of M0–M5, by owner authorization on 19 October 2026.
- **Area:** Native workflow runtime/activities and Elsa Studio, together in this repository.
- **Steward:** Sipke Schoorstra. Delivery leaves are claimed per leaf under Issue Bus Lite.
- **Program:** [#3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035).
- **Canonical requirements and decision register:** [PRD #3036](https://github.com/elsa-workflows/elsa-foundation/issues/3036).

## Purpose and boundaries

A user can visually author, publish, execute, inspect and recover a compensation-enabled native workflow in
Elsa Studio. Recovery must remain correct and observable across retries, cancellation and restarts, with
uncertain external outcomes represented explicitly. Studio authoring and occurrence inspection are part of
the first durable delivery slice, not a later addition.

Releases and deployment are outside this program. Its PRs merge under
[ADR 0080 D7](../adr/0080-elsa-4-simplification-decisions.md#d7--recurring-review-and-fix-routines) as amended on
15 October: the Code Review app merges once a review round leaves nothing actionable and CI is green.

## Relationship to other programs

| Program | Relationship | Rule |
|---|---|---|
| [Unified Execution #2892](unified-execution.md) | Runs beside it; no dependency either way | Compensation consumes runtime contracts that predate #2892: `ActivityExecutionId` as invocation identity, `ActivityAttempt`, `ActivityInputSnapshot`, `SideEffectProfile.External`'s durable claim before the body runs, structural private state and fault absorption. It adds no scheduler, work ledger or lease. The **logical operation key** that external participants receive (stable across boundary retries, which mint a new `ActivityExecutionId`) is owned by this program; #2892 consumes it if its proposed "effect idempotency reference" needs one. |
| [BPMN Engine](bpmn-engine.md) | Separate semantics | [ADR 0086](../adr/0086-native-and-bpmn-compensation-stay-separate.md): no shared compensation abstraction, adapter or BPMN change. A BPMN handler may bind native work through the ordinary binding. |
| Incident retry [#2747](https://github.com/elsa-workflows/elsa-foundation/issues/2747) | Reuse, do not compete | #2747 owns generic retry of a faulted activity. Retrying a failed *handler* is compensation-scope semantics: the scope re-schedules the handler for the same entry, behind its own durable intervention wait ([spec 250](../../specs/250-native-compensation/contracts/semantics.md)). It does not use or duplicate incident retry. Retrying a faulted *forward* step goes through #2747. |
| Elsa 3 compensation, [elsa-core#8884](https://github.com/elsa-workflows/elsa-core/issues/8884) | Paused | Resumes after the #3043 design packet is reviewed, adapting the reviewed Elsa 4 contract or recording why not. |

## Delivery sequence

| Milestone | Integrated outcome | Owning records |
|---|---|---|
| M0 | Native semantics and Studio authoring/inspection design contract: [spec 250](../../specs/250-native-compensation/spec.md) | [Contract #3041](https://github.com/elsa-workflows/elsa-foundation/issues/3041), [design task #3043](https://github.com/elsa-workflows/elsa-foundation/issues/3043) |
| M1 | Visually authored reservation journey that survives restart during recovery | [Epic #3037](https://github.com/elsa-workflows/elsa-foundation/issues/3037), [feature #3044](https://github.com/elsa-workflows/elsa-foundation/issues/3044) |
| M2 | Uncertain effects, retries and intervention | [Epic #3038](https://github.com/elsa-workflows/elsa-foundation/issues/3038), [feature #3045](https://github.com/elsa-workflows/elsa-foundation/issues/3045) |
| M3 | Nested/repeated occurrences, confirmation and native composition | [Epic #3039](https://github.com/elsa-workflows/elsa-foundation/issues/3039), [feature #3046](https://github.com/elsa-workflows/elsa-foundation/issues/3046) |
| M4 | Accessible Studio recovery inspection and authorized operator controls | [Epic #3040](https://github.com/elsa-workflows/elsa-foundation/issues/3040), [feature #3047](https://github.com/elsa-workflows/elsa-foundation/issues/3047) |
| M5 | Integrated qualification, documentation, samples and Elsa 3 follow-on assessment; the replay-safe warning deferred from M1 (spec 250 FR-014) | [Feature #3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) |

The M0 design packet is a spec under `specs/` and settles the contract, state schema and visual notation.
Later features are refined from it; do not pre-create implementation leaves before it is reviewed.

## Control surface

GitHub issue bodies, native sub-issues and blocking relationships are the control surface; there is no
Project board. Board creation was refused for `elsa-workflows-codex[bot]` (missing organization Projects
permission). Creating one needs that permission or explicit owner authorization for another identity.

## Completion

The PRD owns requirements and open decisions. Consequential architecture choices go in ADRs, meanings in the
glossary, work-unit detail in specs. Completion requires the integrated milestone demonstrations, including a
real Studio-to-runtime-to-persistence restart journey, honest recovery outcomes and explicit compatibility
limits. Program setup, issue closure or in-memory tests alone do not prove delivery.
