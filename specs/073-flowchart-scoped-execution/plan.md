# Implementation Plan: Flowchart Scoped Execution

**Branch**: `073-flowchart-scoped-execution` | **Date**: 2026-06-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/073-flowchart-scoped-execution/spec.md`

## Summary

Replace the initial Flowchart direct-continuation dispatcher with a clean-slate, activity-owned scoped execution model. The Flowchart will own runtime control-flow state for execution scopes, execution paths, optional arrivals, diagnostics, and active child bindings. Routing and synchronization decisions will be made by public gateway policies that receive read-only state and return commands for the Flowchart engine to apply.

The first implementation slice delivers the state model, policy seam, direct continuation, implicit activation-aware joins, graph/reachability support, diagnostics, and tests. Additional gateway policies are added incrementally after the core engine proves deterministic and loop-safe.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: Existing Elsa activity/runtime contracts, dependency injection abstractions, and current Flowchart module contracts. No new external packages planned.

**Storage**: Flowchart-owned runtime state persisted through the parent activity execution state. The feature does not introduce a new runtime store or database dependency.

**Testing**: xUnit tests in `tests/Elsa/Activities/Flowchart/Tests`, covering unit-level engine behavior and runtime-level scheduling behavior.

**Target Platform**: Elsa server/runtime applications using the existing workflow runtime and composite-activity scheduling seam.

**Project Type**: Library feature module within the Elsa foundation repository.

**Performance Goals**: Deterministic branch/join decisions for small-to-medium authored Flowcharts; no measurable regression for linear and simple branching Flowcharts covered by existing tests.

**Constraints**:

- Runtime execution must remain artifact-only and must not load design-side workflow documents.
- Flowchart graph semantics must remain activity-owned structure, not generic core child-slot metadata.
- Public policies must not mutate runtime state directly or access raw stores/scheduler APIs.
- Branch/join state transitions must be idempotent and order-independent.

**Scale/Scope**:

- v1 scope covers Flowchart execution and public policy contracts only.
- v1 built-ins cover direct continuation, implicit activation-aware join, Decision, Parallel Fork/Join, Inclusive Fork/Join, First Wins, and Merge.
- True concurrent worker execution is out of scope; the Flowchart model must be safe for future concurrency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Elsa §E2.2 Workflows.Design ↔ Workflows.Runtime split**: PASS. Runtime execution must not depend on design documents. Flowchart runtime state and executable structure are artifact-owned; design-side authored structure handling remains separate from runtime execution behavior.
- **Elsa §E2.6 Artifact-only runtime / executable-always-runs**: PASS. The Flowchart engine reads the published executable node, Flowchart executable structure, and runtime activity state only.
- **Elsa §E2.9 Activity-owned composite structure**: PASS. Flowchart connections, start selection, join/routing policy metadata, and scoped execution state are Flowchart-owned concepts, not core-owned generic composition metadata.
- **Framework §2.1 Three-layer separation / dependency envelope**: PASS. Contracts stay in the Flowchart feature boundary; no new heavy dependencies are introduced.
- **Framework §2.6 Cross-feature extension mechanisms**: PASS. Gateway policies are explicit public extension points with contracts, not hidden service replacement.
- **Framework §2.21/§2.23 test discipline**: PASS with obligation. Feature-class registration tests, policy contract tests, engine unit tests, runtime scheduling tests, failure-path tests, and extension-point docs are required.
- **Extension-point catalog discipline**: PASS with obligation. `src/elsa/Activities/Flowchart/EXTENSION_POINTS.md` and root `EXTENSION_POINTS.md` must be updated, then generated maps refreshed.

## Project Structure

### Documentation (this feature)

```text
specs/073-flowchart-scoped-execution/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── flowchart-policy-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/elsa/Activities/Flowchart/
├── Activities/
│   └── Flowchart.cs
├── Contracts/
│   ├── IFlowchartPolicy.cs
│   ├── IFlowchartPolicyRegistry.cs
│   └── IFlowchartPolicyContext.cs
├── Internal/
│   ├── FlowchartExecutionEngine.cs
│   ├── FlowchartReachabilityAnalyzer.cs
│   ├── FlowchartPolicyRegistry.cs
│   └── Policies/
├── Models/
│   ├── FlowchartExecutionState.cs
│   ├── ExecutionScope.cs
│   ├── ExecutionPath.cs
│   ├── FlowchartArrival.cs
│   ├── FlowchartDiagnosticEvent.cs
│   ├── FlowchartNodeMetadata.cs
│   ├── FlowchartConnectionMetadata.cs
│   ├── FlowchartPolicyDecision.cs
│   └── FlowchartPolicyCommand.cs
├── ActivitiesFlowchartFeature.cs
├── EXTENSION_POINTS.md
└── README.md

tests/Elsa/Activities/Flowchart/Tests/
├── ActivitiesFlowchartFeatureTests.cs
├── FlowchartActivityTests.cs
├── FlowchartRuntimeTests.cs
├── FlowchartExecutionEngineTests.cs
├── FlowchartPolicyContractTests.cs
├── FlowchartImplicitJoinTests.cs
├── FlowchartLoopIterationTests.cs
└── FlowchartRacePolicyTests.cs
```

**Structure Decision**: Keep the feature inside the existing `Elsa.Activities.Flowchart` module and its current test project. Add public extension contracts under the Flowchart module because the policies are specific to Flowchart graph semantics. Do not create a shared runtime primitive package in this slice; use generic metadata names (`executionPathId`, `executionScopeId`) so future composite activities can adopt the pattern later.

## Complexity Tracking

No constitutional violations or complexity exceptions are required for the planned design.

## Phase 0: Research

Research is captured in [research.md](research.md). Key decisions:

- Use Flowchart-owned scoped execution state, not a new global runtime primitive.
- Use generic execution-path/scope correlation names for child scheduling metadata.
- Expose public policy contracts from v1.
- Keep policies deterministic and command-returning.
- Implement runtime-aware dead-path detection using graph topology plus active runtime work summaries.

## Phase 1: Design

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/flowchart-policy-contract.md](contracts/flowchart-policy-contract.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **Artifact-only runtime**: PASS. Design artifacts keep Flowchart runtime execution state on the parent activity execution and require only executable structure plus runtime state.
- **Activity-owned structure**: PASS. Graph policy metadata is part of Flowchart structure, not generic slot metadata.
- **Public extension-point discipline**: PASS with documented contract and follow-up docs obligations.
- **Test discipline**: PASS with explicit unit/runtime/failure-path validation scenarios in quickstart and future tasks.
- **Dependency envelope**: PASS. No new external dependency is planned.
