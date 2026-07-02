# Runtime Execution Seam

Status: active.

Area: Workflows Runtime architecture / executable artifact seam.

Steward(s): Joey plus the incoming runtime architect.

## Purpose

Create a focused coordination bucket for the Workflows Runtime execution seam so the incoming architect can start from the current evidence, define the runnable artifact boundary, and move into Speckit without reopening the broader Elsa Foundation Operating Model work.

The goal is to specify the seam between Workflows Design and Workflows Runtime before runtime implementation begins. This bucket should keep runtime execution planning separate from operating-model grooming, CShells composition work, and broad constitution ratification.

## In Scope

- Runtime execution seam planning and Speckit preparation.
- The minimal runnable artifact currently named `WorkflowExecutable` or an approved equivalent, now constrained to carry one compiled root activity instead of a workflow-level flowchart graph or universal executable composition carrier.
- Compile/publish bridge inputs and outputs between Design-owned state and Runtime-owned runnable form.
- Runtime dependency envelope and Design-free execution-time boundary.
- Workflow execution context lifetime, DI scope, and concurrency model.
- Runtime-owned executable graph/node terminology if needed.
- Activity/workflow input-output boundary decisions where they affect runtime execution.
- Elsa 3 runtime import analysis, including known broken-window candidates that affect the Elsa 4 execution model.
- Workflow-as-activity consumer/pinning questions, nested execution, and cycle guards when they become part of the runtime seam.
- Test obligations that belong to the runtime seam, including structural dependency tests and focused unit tests.
- Correction of the provisional design/runtime graph shape that made `WorkflowDefinitionState`, `ActivityNode`, `WorkflowExecutable`, and `ExecutableNode` model flowcharts or generic compositions at the wrong boundary.

## Out Of Scope

- Broad constitution ratification unrelated to the runtime seam.
- CShells appsettings generation or feature-composition tooling.
- Runtime implementation before the seam has an approved spec/plan.
- TestContainers integration-testing policy as the first step; that should follow after the runtime seam has enough shape to test.
- Event dispatcher failure-strategy implementation, except where runtime execution explicitly depends on event semantics.

## Active Objectives

1. Use [runtime execution pre-spec handoff](../reports/runtime-execution-pre-spec-handoff.md) as the primary starting evidence.
2. Convert the handoff into an architect-owned Speckit work unit when the incoming architect is ready.
3. Keep Design reads confined to compile/publish bridge planning; execution-time Runtime must remain artifact-only.
4. Decide the runtime artifact, crossing points, context lifetime, graph terminology, and I/O boundary questions before coding.
5. Capture any approved spec, report, or implementation follow-up here instead of stretching the Elsa Foundation Operating Model bucket.
6. Supersede graph-shaped workflow boundary specs with the root-activity contract and implement that contract before adding flowchart/sequence/state-machine behavior.
7. Remove generic `ActivityComposition` / `ExecutableActivityComposition` assumptions. Composite child structure belongs to activity-specific contracts such as `Sequence.Activities`, `If.Then` / `If.Else`, `ForEach.Body`, `Composite.Root`, and `Flowchart` activities/connections/start/join state.
8. Keep `ActivityChildSlot` and `ExecutableChildSlot` as traversal projections only. Activity-owned relationship semantics belong to node/executable structure owned by the activity module, not slot metadata.
9. Plan and implement [Flowchart scoped execution](../../specs/073-flowchart-scoped-execution/spec.md) as the clean-slate activity-owned execution model for advanced Flowchart joins, loops, races, and public gateway policies.
10. Plan and implement [Activity execution inspection](../../specs/079-activity-execution-inspection/spec.md) as the checkpoint-gated runtime evidence model that supports repeated activity executions and workflow instance inspection.
11. Plan and implement [Runtime checkpoint commit](../../specs/080-runtime-checkpoint-commit/spec.md), based on [ADR 0020](../adr/0020-runtime-checkpoint-commit-post-commit-work.md): deepen runtime checkpoint commit so it records post-commit work without inline delivery, replaces `IRuntimeCheckpointWriter` with `IRuntimeCheckpointCommitStore`, and keeps post-commit delivery in the outbox processor.
12. ✅ **Decided** — the expression-execution-context propagation question is settled by [ADR 0030](../adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md): runtime expression evaluation uses a **parameter-threaded carrier** (never a DI-registered live context). D1 ratifies the carrier; Q2 confirms live mid-execution evaluation is in scope (Run-JavaScript-style activities), so a live execution-time carrier is added alongside the working `IMaterializationExpressionState`; D3 keeps all four dead accessor surfaces (identity funcs, named pascalized accessors, exec-time output accessors, JS variable write-back), re-pointed onto the carrier; Q3 keeps narrow markers over a general transient-properties bag; D4 adds an enable-feature resolve-and-evaluate guardrail. Implementation is objective 13.
13. ▶ **Next** — implement ADR 0030 (edge A). Speckit unit: live execution-time carrier + handler-side population; re-point the five `IWorkflowExecutionContext` processors onto it and strip their constructor deps; port identity / named-accessor / exec-time-output / variable-write-back behaviors; fold JS variable write-back into the checkpoint-commit durable-value path (`WorkflowInvokeActivitySchedulerWorkHandler.BuildWorkflowScopeWriteBackChanges`); retire `IWorkflowExecutionContext` as a DI dependency; add the guardrail test. Re-base [spec 064](../../specs/064-runtime-workflow-execution-context/spec.md) FR-001…FR-005 onto the carrier (064 intent carried forward, mechanism superseded). Independent of ADR 0029.

## Reconciliation checkpoint (2026-07-02)

A read-only source verification (three independent sweeps) re-baselined this bucket against merged code. See [runtime expression-context source reconciliation](../reports/runtime-expression-context-source-reconciliation.md).

- **Objectives 1–11 substantially implemented, with one exception.** The executable-artifact, split runtime-state, checkpoint/commit, bookmark-resume, input-binding/durable-capture, operational-recovery/outbox, and Elsa-3 import-boundary contracts conform to intent. The exception is the **execution pipeline (action-plan Slice 4)**: the workflow/activity pipeline contract exists but is **scaffolded and unwired** — nothing invokes it, execution is inlined in scheduler work handlers, and a registered `IActivity/WorkflowRuntimeMiddleware` would silently never run (false affordance). Steward confirmed (2026-07-02) the pipeline IS the intended execution spine, so this is unfinished wiring, not a settled alternative.
- **Objective 12 (expression-context) re-baselined.** Workflow variables and inputs ARE persisted and projected via `DurableValueState`; generic JS `getVariable`/`getInput`/`getOutput` resolve at input-materialization time through the `IMaterializationExpressionState` parameter-carrier (the analysis's preferred Option 2). The remaining edge is the five dead execution-time `IWorkflowExecutionContext` JS pre/post-processors — a resolution-throw landmine if `JavaScriptWorkflowsRuntimeFeature` is enabled, unguarded by tests. Decision framing D1–D4 is in the reconciliation report; the accessor keep/drop surface (D3) is deferred to the expression-context ADR (edge A, number TBD — 0029 is now the pipeline ADR).
- **Expression-context unit resumed and decided (user decision, 2026-07-02).** Edge A is no longer paused: the D1–D4 / Q1–Q3 decisions are settled in [ADR 0030](../adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md). Because Q2 kept live mid-execution evaluation in scope and D3 kept all four accessor surfaces (including JS variable write-back), this is a **feature build on the carrier**, not the narrow retire/re-point the reconciliation assumed. Implementation is objective 13; a Speckit spec follows.
- **New work units surfaced.** (a) **Route runtime execution through the workflow + activity pipelines** — a distinct unit so the module system can extend runtime execution; arguably more foundational than the expression-context edge. Decision accepted: [ADR 0029](../adr/0029-runtime-execution-flows-through-the-pipelines.md). Sizing: [pipeline wiring sizing](../reports/runtime-execution-pipeline-wiring-sizing.md). Key result — it splits into **Move 1** (inject a pipeline executor at the drainer dispatch point so registered middleware actually runs; zero behavior change; kills the false affordance; small) and **Move 2** (decompose ~10 handlers' inlined phases into slot middleware; ~4,500 LOC; incremental; holds the hazards). Move 1 alone makes the pipeline the real execution spine. (b) The sensitive-value payload-capture default (Slice 7) is unverified — confirm or specify.

## Linked Surfaces

- [Runtime execution pre-spec handoff](../reports/runtime-execution-pre-spec-handoff.md)
- [Elsa Core runtime broken windows brainstorm](../reports/elsa-core-runtime-broken-windows-brainstorm.md)
- [Elsa Core runtime expression-context wiring analysis](../reports/elsa-core-runtime-expression-context-wiring-analysis.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Test maturity and weak implementation report](../reports/test-maturity-and-weak-implementation-report.md)
- [Activity construction seam spec](../../specs/006-activity-construction-seam/spec.md)
- [Flowchart scoped execution spec](../../specs/073-flowchart-scoped-execution/spec.md)
- [Activity execution inspection spec](../../specs/079-activity-execution-inspection/spec.md)
- [Runtime checkpoint commit spec](../../specs/080-runtime-checkpoint-commit/spec.md)
- [Runtime checkpoint commit post-commit work ADR](../adr/0020-runtime-checkpoint-commit-post-commit-work.md)
- [Runtime execution flows through the pipelines ADR](../adr/0029-runtime-execution-flows-through-the-pipelines.md)
- [Runtime expression-evaluation parameter-threaded carrier ADR](../adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md)
- [Runtime expression-context source reconciliation](../reports/runtime-expression-context-source-reconciliation.md)
- [Runtime workflow execution context spec (064, intent carried forward)](../../specs/064-runtime-workflow-execution-context/spec.md)
- [Checkpoint-gated activity execution inspection ADR](../adr/0001-checkpoint-gated-activity-execution-inspection.md)
- [Elsa constitution](../../.specify/memory/constitution.md)
- [Framework constitution](../../.specify/memory/constitution-framework.md)
- [Skills catalog](../skills/catalog.md)

## Current Roadmap Notes

- Start with Work Unit Planner and Speckit Flow Guide from the skill catalog.
- Use the Elsa Core runtime broken-windows brainstorm report to preserve maintainer concerns and source-derived analysis before selecting Speckit work units.
- Do not implement `WorkflowExecutionContext`, `WorkflowDefinitionActivity.Execute`, or runtime graph behavior as a drive-by change.
- Treat `WorkflowDefinitionState.Activities`/`ActivityConnections`, `ActivityNode.Composition`, `WorkflowExecutable.Edges`/`StartNodeIds`, and `ExecutableNode.Composition` as superseded provisional slice artifacts. The workflow boundary is a single root activity; child ownership and flowchart traversal are activity behavior.
- Treat generic child-slot metadata as superseded for composite semantics. Flowchart connections/start, If branch meaning, loop bodies, and similar structure must be module-owned activity structure.
- Before relying on generated maps for verification, check [maps manifest](../maps/manifest.json); regenerate the relevant map if freshness matters.
- Treat the Runtime JavaScript Design reference as known deferred architecture debt, not as the first runtime-execution fix.

## Drift / Review Notes

- This bucket exists because runtime execution is now a distinct mid-term architecture effort, not merely an operating-model cleanup item.
- If the work turns primarily into integration testing, event failure strategy, or CShells composition, create or select a more specific bucket instead of broadening this one.
- If runtime seam decisions produce ratified gates, move those gates to the constitution and leave links here.
- If the result becomes a repeatable workflow, move the workflow to the skill catalog and leave links here.

## Removal or Completion Conditions

This bucket can be completed or paused when the runtime execution seam has an approved Speckit spec/plan, its follow-up implementation work is tracked in a more specific surface, or the incoming architect chooses a different coordination bucket.
