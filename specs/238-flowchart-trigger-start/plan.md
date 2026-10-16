# Implementation Plan: Flowchart Starts at the Trigger That Fired

**Branch**: `238-flowchart-trigger-start` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/238-flowchart-trigger-start/spec.md`

## Summary

Make a Flowchart start at the run's trigger node when that node is one of its direct children, and make the
design-time value-flow check treat every possible entry node as a root.

**Runtime** (Flowchart module only):
- `FlowchartGraph` gains `SelectEntryNode(triggerNodeId)`. It returns the direct child with that id, else
  `SelectStartNode()`.
- `FlowchartExecutionEngine` calls it, with `context.TriggerNodeId`, at its three initial-state sites
  ([R1](research.md#r1-where-the-flowchart-chooses-its-first-node)).
- Loop-edge classification roots trigger-marked children right after the start node
  ([R2](research.md#r2-loop-edge-classification-for-branches-reached-only-from-a-trigger)).
- No new state, contracts or metadata. Loops, nesting and redrive follow from the existing workflow-wide, persisted
  trigger id.

**Design** (`ValueFlowValidator` only):
- Entry points are `startNodeId` plus direct children with no inbound connection
  ([R3](research.md#r3-design-time-entry-points)).
- Dominance uses a virtual root over all entries ([R4](research.md#r4-multi-entry-dominance)).
- Writes that only different entries reach are exclusive ([R5](research.md#r5-exclusive-writes-across-trigger-branches)).
- With one entry, every result equals today's.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: The existing Flowchart activity module (`Elsa.Activities.Flowchart`), workflow runtime
core (`IRuntimeActivityExecutionContext`, `TriggerNodeMetadata`) and workflow design validations. No new package or
project reference in production code.

**Storage**: None changed. `FlowchartExecutionState` schema v1 is unchanged ([data-model](data-model.md)).

**Testing**:
- xUnit only, no FluentAssertions.
- Runtime and behavioural tests use `WorkflowExecutionHarness`; the end-to-end test uses `HttpEndpointHostFixture`.
- The validator is tested with `ValueFlowValidatorTests`' JSON Flowchart helper.

**Target Platform**: Cross-platform .NET server/runtime

**Project Type**: Modular libraries (Essentials domain tree)

**Performance Goals**:
- The runtime adds one dictionary lookup per Flowchart start.
- Validator cost stays linear in the number of entries times reachability, the same order as today's per-call
  `Dominates`.

**Constraints**:
- No Design reference from Runtime projects (§E2.2).
- No behaviour change for single-entry flowcharts.
- Edge classification stays a pure function of the graph, because it is cached.
- In-flight runs are unaffected, because only initial-state creation changes.

**Scale/Scope**:
- Two production files in Flowchart (`FlowchartGraph`, `FlowchartExecutionEngine`) and one in Design Validations
  (`ValueFlowValidator`).
- Tests in five test projects.
- The Flowchart README, and the validations README if it describes value flow.

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Ratification status**: Both constitutions are ratified (framework v6.1.0, Elsa v6.0.2). §2.24 sanctioned
  patterns, §E2.8 and §E2.9 are draft or provisional; this plan introduces no pattern and does not rely on them.
- **§E2.2, Runtime takes no Design reference**: PASS. The runtime change is inside `Elsa.Activities.Flowchart`,
  which already references `Workflows.Runtime.Core` for `TriggerNodeMetadata` and `IRuntimeActivityExecutionContext`.
  The design change is inside `Elsa.Workflows.Design.Validations` and computes entries from the draft alone
  (FR-013).
- **§E2.2.3, deployment shapes**: PASS. Design-only, Runtime-only and combined hosts are untouched; there is no
  cross-context call.
- **§E2.6, executable always runs**: PASS.
  - Published artifacts are not recompiled. The runtime reads only data the artifact already carries: child ids,
    the trigger marker and the seed's trigger id.
  - A multi-trigger artifact that used to park or run the wrong branch now runs the fired branch. That is the
    intended fix.
- **§2.1, §2.4, §2.17, `.Core` and helpers**: PASS. No `.Core` change and no new helper. The start-selection logic
  lives on `FlowchartGraph`, next to `SelectStartNode`.
- **§2.5, §2.6.x, features, events, contracts**: N/A. No registration, event, contributor or replacement contract
  changes. The `ValueFlowValidator` constructor is unchanged.
- **§2.21.1, existing tests**: PASS by construction (FR-012, SC-004). Any validator test whose result changes
  because a disconnected child becomes an entry point is named with its reason in the PR
  ([R6](research.md#r6-risk-to-existing-tests)).
- **§2.22, docs and `EXTENSION_POINTS.md`**: The Flowchart README gains a short "Where a run starts" section.
  `EXTENSION_POINTS.md` is unchanged, because no extension surface changes.
- **§2.23.1, §2.23.2, tests**: Risky paths get behaviour tests (FR-014 to FR-016). No test asserts on message text.
- **§2.23.3, visibility**: `FlowchartGraph` is already public in the `Internal` namespace. The new method follows
  `SelectStartNode` and adds no new public type. The module has no tracked public API files.
- **§4.1, §4.2, SemVer**: Additive method plus a bug fix: a minor or patch class. No removed or renamed member.
- **Review checklist**: no item is triggered beyond the above.
- **Post-design re-check**: unchanged. Phase 1 added no contract, entity or dependency.

## Project Structure

### Documentation (this feature)

```text
specs/238-flowchart-trigger-start/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

There is no `contracts/` folder: the unit changes no external interface (API, payload, descriptor or event).

### Source Code

```text
Essentials/src/Activities/Flowchart/
├── Internal/FlowchartGraph.cs              # SelectEntryNode; trigger-first DFS roots in ComputeBackwardEdges
├── Internal/FlowchartExecutionEngine.cs    # three call sites use SelectEntryNode(context.TriggerNodeId)
└── README.md                               # "Where a run starts"

Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/
└── Validators/ValueFlowValidator.cs        # entry set, multi-entry Dominates, EntriesReaching, exclusive writes

Essentials/tests/
├── Activities/Flowchart/Tests/             # FlowchartTriggerStartTests (+ FlowchartRuntimeFixture trigger overload)
├── Activities/Behavioral/Tests/            # FlowchartTriggerStartBehaviorTests: HTTP+HTTP, HTTP+Cron
├── Activities/Runtime/Tests/               # FlowchartTriggerStartLoopTests: For loop body restarts at trigger
├── Activities/Http/IntegrationTests/       # MultiTriggerFlowchartEndToEndTests (+ Flowchart ref and feature)
└── Workflows/Design/Tests/                 # ValueFlowValidatorTests: multi-entry cases + single-entry regressions
```

**Structure Decision**: Changes stay inside the two modules that own the behaviour: the Flowchart activity module
for runtime start selection, and Design Validations for value flow. The only new references are test-only: the HTTP
integration test project gains a Flowchart project reference, and its host gains the Flowchart feature.

## Complexity Tracking

No constitution violations to justify.
