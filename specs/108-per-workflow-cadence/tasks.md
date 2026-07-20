# Tasks: Per-Workflow Checkpoint Cadence (ADR 0032 R5)

All tasks completed in this work unit (single-session implementation; see [plan.md](plan.md) for the design decisions each task realizes).

- [x] T001 — Authoring shape: `WorkflowCheckpointCadenceOptions` + `WorkflowStrategyOptions.CheckpointCadence` (FR-001).
- [x] T002 — Design round-trip verification: draft replace persists/reloads the cadence; promotion carries it onto the version (FR-002; `CheckpointCadenceDraftRoundTripTests`). No production Design changes needed — the cadence rides the existing full-state `StrategyOptions` serialization.
- [x] T003 — Compiled carrier: `WorkflowExecutableCheckpointCadence` + `WorkflowExecutable.CheckpointCadence` ctor/property, old JSON deserializes to null (FR-003).
- [x] T004 — Publish compilation: `WorkflowExecutableCompiler.CompileCheckpointCadence` with fail-fast alias/cap validation (FR-003; `WorkflowExecutableCompilerTests`).
- [x] T005 — Behavioral hash: conditional `checkpointCadence` object in `WorkflowExecutableHasher`'s payload; unauthored hashes byte-identical (FR-004).
- [x] T006 — Resolver: `ResolvedCheckpointCadence`, `IRuntimeCheckpointCadenceResolver`, `RuntimeCheckpointCadenceResolver` (stamp > authored-on-executable > host default; Immediate-host clamp) + core DI registration (FR-005/FR-006; `RuntimeCheckpointCadenceResolverTests`).
- [x] T007 — Drain seam: `WorkflowDrainOrchestrator.DrainCoreAsync` resolves per execution before `Begin`; authored-Immediate takes the extracted `DrainImmediateAsync` path; `Begin(workflowExecutionId, maxSegmentCheckpoints)` per-run cap override in the scope factory (FR-006/FR-007; `RuntimeCheckpointCoalescingTests`).
- [x] T008 — Per-run stamp: `RuntimeMetadataKeys.CheckpointCadence`/`CheckpointMaxSegmentCheckpoints`; stamped at the workflow-started state change; carried forward through `PreserveSystemMetadata` (FR-008).
- [x] T009 — Read-model upgrade: `RuntimeCheckpointCadenceInspector.Resolve(WorkflowExecutionState)` prefers the stamp; `GetWorkflowInstanceRequestHandler` passes the state; limitation XML-doc replaced; response shape unchanged so spec-092 OpenAPI untouched (FR-009; `WorkflowInstancesRequestHandlerTests`).
- [x] T010 — Precedence guardrail test: mandatory bookmark boundary flushes durably under an authored relaxed cadence (FR-010; `AuthoredCoalescedCadence_MandatoryBookmarkBoundary_StillFlushesImmediately`).
- [x] T011 — Full test-project runs (Runtime, Runtime.Api, Design, Design.Api, Publishing.Api, Persistence.Groundwork).
