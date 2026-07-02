# Tasks: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

- [x] **T001** `RuntimePipelineWorkspace` (mutable; `PendingCheckpointCommit`). (FR-001)
- [x] **T002** `IRuntimePipelineContext` (WorkItem + Workspace); both context records implement it + carry a `Workspace`. (FR-001)
- [x] **T003** `IRuntimePipelineContextAccessor` + `AsyncLocalRuntimePipelineContextAccessor` + `NoopRuntimePipelineContextAccessor`. (FR-002)
- [x] **T004** Dispatcher pushes the context into the accessor around `InvokeAsync` (optional accessor, Noop default). (FR-002)
- [x] **T005** `RuntimeWorkflowCheckpointMiddleware` real impl: commit the staged commit after `next` (removed from placeholders). (FR-003)
- [x] **T006** `WorkflowCancelSchedulerWorkHandler`: stage when ambient, else commit inline (optional accessor param). (FR-004)
- [x] **T007** Register `IRuntimePipelineContextAccessor` in `WorkflowsRuntimeApiFeature`. (FR-002)
- [x] **T008** Tests: middleware commits staged / no-ops; Cancel stages when ambient / commits inline when not; end-to-end Cancel through the feature pipeline. Fix the Move-1 dispatch-test provider to register the committer the now-real Checkpoint middleware needs. (FR-005)
- [x] **T009** Build + full runtime suite green (542) + Activities.Runtime green (132). (FR-005/SC-002)
- [ ] **T010** Update `.specify/feature.json` + AGENTS.md SPECKIT pointer → spec 083; log the slice into the Runtime Execution Seam bucket.
- [ ] **T011** Draft PR framed for architect approval of the decomposition pattern before the remaining handlers.
