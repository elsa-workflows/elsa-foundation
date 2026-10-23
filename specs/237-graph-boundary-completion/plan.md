# Implementation Plan: Graph Boundary Completion From Any Depth

**Branch**: `237-graph-boundary-completion` | **Date**: 2026-10-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/237-graph-boundary-completion/spec.md`

## Summary

Add a `Complete` catalog activity to the Graph Runtime module. When it runs at any depth inside a graph body, it
ends the nearest enclosing graph boundary with one literal boundary outcome and durably cancels the rest of the
body.

The design adds one generic runtime capability, an **ancestor-targeted notification** ([research R1](research.md#r1-how-the-exit-reaches-the-nearest-graph-boundary)):

1. The runtime walks the committed parent-execution chain to the nearest ancestor with a given consumer key.
2. It delivers the existing `NotifyParentActivity` work item to that ancestor.
3. That work item is ordered ahead of the notifier's own completion ([R2](research.md#r2-ordering-and-keeping-the-rest-of-the-body-from-running-in-between)).

`GraphActivity` handles the notification:

- It resolves the reference key through a new optional descriptor field, `CompleteOutcomes`.
- It requests subtree cancellation of its entry; the existing planner and cleanup store delete bookmarks, timers and
  queued work.
- It completes with the outcome. Outputs are captured in the same commit (R4).

At design time, the graph provider recognises `Complete` nodes. It validates their outcomes and relaxes schema-2
reachability to "a mapping or a `Complete` node", then emits `completeOutcomes` only for graphs that use the
activity (R5). A workflow draft validator rejects `Complete` outside a graph (R6). Intermediate composites, `Break`,
and the cancellation gaps tracked in #2752 are untouched.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Existing Elsa activity runtime, workflow runtime, activity design, workflow design
validation and publishing abstractions. No new package.

**Storage**: Existing runtime checkpoint commit stores (in-memory and EF Core). No schema or migration change.

**Testing**:
- xUnit only, with no FluentAssertions, using `WorkflowExecutionHarness` and the Behavioral drives.
- EF Sqlite, as in `DurableTimerRestartCrashTests`.
- The PowerShell e2e scripts under `e2e-tests/reusable-activities/`.

**Target Platform**: Cross-platform .NET server/runtime

**Project Type**: Modular libraries (Essentials domain tree)

**Performance Goals**:
- The ancestor walk is O(depth) state reads, once per `Complete` execution.
- Provider validation stays linear in node count, plus one catalog lookup per node at draft time.

**Constraints**:
- Runtime projects take no Design reference.
- Existing manifests and compiled payloads stay byte-identical.
- The boundary completion, output capture and subtree cancellation commit atomically.
- No change to composites or to `Break`.

**Scale/Scope**:
- Two runtime-core touches: the context member and the notification builder.
- One Graph Runtime activity plus `GraphActivity` changes.
- Graph Design provider rules plus one draft validator.
- Tests, docs and one e2e script.

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Ratification status**: Both constitutions are ratified (v6.0.1). §2.24 sanctioned patterns, §E2.8 activity
  versioning and §E2.9 are draft or provisional; this plan follows them without relying on their ratification.
- **§E2.2, Runtime takes no Design reference**: PASS.
  - `Complete` and the descriptor field live in Graph Runtime, which references only Activities.Runtime.Core and
    Workflows.Runtime.Core.
  - The ancestor capability lives in Workflows.Runtime.Core and the Activities.Runtime implementation.
  - The draft validator lives in Graph Design.
- **§2.1, `.Core` contents**: PASS. Workflows.Runtime.Core gains one interface member and one request model; there
  is no implementation logic in the core.
- **§2.6, §2.6.4, design and runtime are two contracts**: PASS. Reference keys stay design-side. The runtime sees
  the compiled `CompleteOutcomes` map plus a reference-key payload, and resolves names only from the pinned
  descriptor.
- **§2.6.1, contributor and fan-in shape**: PASS. `IDraftValidator` is an existing fan-in contract; one more
  implementation is added.
- **§E2.6, a published artifact always runs**: PASS. The runtime needs only the pinned descriptor.
- **§E2.8, catalog authority**: PASS. `Complete` reaches the picker only through the catalog scanner, and the
  contract baseline is updated.
- **§2.21.1, tests describing valid behaviour still pass**: PASS. No existing test is rewritten. The new
  `GraphActivity` branches apply only when `CompleteOutcomes` is not empty.
- **§2.22, `EXTENSION_POINTS.md` in the same unit**: PLANNED (R8). It covers the Graph Runtime, Graph Design and
  Workflows Runtime Core catalogs.
- **§2.23.1, §2.23.2, registration and behaviour tests**: PLANNED.
  - A registration test for the draft validator.
  - Behaviour tests for every new branch: routing, ordering, no target, unknown key, natural completion without a
    mapping, races, durable cancellation on two stores.
  - Tests assert on codes and states, not on message text.
- **§2.24 (draft), sanctioned patterns**: PASS. This extends the existing structural notification seam
  (`RequestParentNotification` and `IRuntimeActivityChildNotificationHandler`) with an addressing mode. The ancestor
  walk follows `RuntimeContainerScopeService`. No new pattern is introduced.
- **§2.6.6, failure policy**: PASS. Missing target, unknown key and natural completion without a mapping each fault
  with a stable code, and nothing falls back silently.
- **§4.1, §4.2, §E5, versioning**: NOTE. Adding a member to the public `IRuntimeActivityExecutionContext` is a
  required interface member, which is a major-class change under §4.2. The only implementer is internal
  (`SimpleActivityExecutionContext`), Elsa is on one pre-GA version line (§E5, ADR 0080), and the project does not
  track public API. Record it in the PR's release note; no separate bump is needed.
- **ADR 0080, internal by default**: Make `Complete`'s helpers and the notification builder change internal. Keep
  the `Complete` activity class and the descriptor field public, because the scanner and the payload need them.
- **§2.25.2, artifact retirement**: Not applicable.

**Post-design re-check**: PASS. Phase 1 added no new service lifetime, event, persistence abstraction, package or
constitutional exception.

## Project Structure

### Documentation (this feature)

```text
specs/237-graph-boundary-completion/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── complete-activity.md
├── checklists/requirements.md
└── tasks.md              (next: /speckit-tasks)
```

### Source Code (repository root)

```text
Essentials/src/Workflows/Runtime/Core/
├── Contracts/IRuntimeActivityExecutionContext.cs        # + RequestAncestorNotification; fix stale cancellation doc
└── Models/RuntimeAncestorNotificationRequest.cs         # new

Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/ActivityExecutions/
└── SimpleActivityExecutionContext.cs                    # stage ancestor requests

Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
├── ParentNotificationEvaluation.cs                      # resolve nearest ancestor by consumer key; typed no-target fault
├── WorkflowInvokeActivitySchedulerWorkHandler.cs        # order ancestor intents before own completion
└── StructuralParentEvaluationSupport.cs                 # same ordering for child-completion evaluations (if reachable)

Essentials/src/Activities/Graph/Runtime/
├── Activities/Complete.cs                               # new catalog activity
├── Activities/GraphActivity.cs                          # notification handler, outcome selection, empty-mapping fault
├── Models/GraphActivityDescriptor.cs                    # + CompleteOutcomes (extra JsonConstructor)
└── EXTENSION_POINTS.md

Essentials/src/Activities/Graph/Design/
├── Services/GraphActivityProvider.cs                    # Complete detection, rules, relaxed reachability, completeOutcomes
├── Services/CompleteOutsideGraphDraftValidator.cs       # new IDraftValidator
├── GraphActivitiesDesignFeature.cs                      # register validator
├── Elsa.Activities.Graph.Design.csproj                  # + Workflows.Design.Validations.Core reference
└── EXTENSION_POINTS.md

Essentials/tests/
├── Activities/Runtime/Tests/AncestorNotificationExecutionTests.cs                     # routing, nearest-first, no target, ordering
├── Activities/Graph/Tests/GraphActivityProviderTests.cs                                # + Complete validation/compile cases
├── Activities/Graph/Tests/GraphActivityExecutionTests.cs                               # + notification handling unit cases
├── Activities/Graph/Tests/CompleteOutsideGraphDraftValidatorTests.cs                   # new
├── Activities/Behavioral/Tests/GraphBoundaryCompleteTests.cs                           # composites, nested, entry, races
├── Activities/Behavioral/Tests/GraphBoundaryCompleteDurableCancellationTests.cs        # bookmark + timer, in-memory and EF Sqlite
├── Activities/Behavioral/Tests/Infrastructure/ActivityContractInventory.cs             # + Complete
└── Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json           # + Complete

e2e-tests/reusable-activities/Test-ReusableActivityComplete.ps1                      # new
docs/reference/activity-definition-authoring-composition.md                           # emitter contract (FR-021)
docs/glossary/elsa.md                                                                 # link from "Graph activity"
EXTENSION_POINTS.md, docs/maps/*                                                       # regenerated
```

**Structure Decision**: Each change goes into the existing project that owns it. There is no new project. The test
for durable cancellation goes in the Behavioral test project, because it already references Graph Runtime,
Flowchart, Scheduling and the Testing harness. That project gains a Sqlite runtime reference only if it lacks one.

## Implementation order and risk gates

1. **Gate R2 first.** Write the runtime ordering test before any feature code: the ancestor intent must dequeue
   before the notifier's completion on both queues. If it fails, switch to the fallback in R2 and amend this plan
   before continuing.
2. Runtime core: the ancestor notification, with a probe-based routing test suite.
3. Graph Runtime: the descriptor field, `Complete`, and the `GraphActivity` handler, with unit tests.
4. Graph Design: provider rules and compile output, the draft validator and its registration.
5. Engine-driven Behavioral tests (SC-001), then the durable cancellation test on both stores (SC-002).
6. Catalog baseline, docs, `EXTENSION_POINTS.md`, generated maps, the e2e script, and a full regression run of the
   existing reusable-activity e2e scripts (SC-003).

## Complexity Tracking

No constitutional violations need justification. The one major-class surface change (§4.2) is recorded in the
Constitution Check.
