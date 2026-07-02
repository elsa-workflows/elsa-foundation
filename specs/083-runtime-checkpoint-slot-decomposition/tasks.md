# Tasks: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Reflects the **slot-invoked handler model** (ADR 0029 addendum). An earlier draft of this slice used an ambient accessor + after-`next` commit; those were superseded.

- [x] **T001** `RuntimePipelineWorkspace` (mutable; `InvokeHandler` + `PendingCheckpointCommit`). (FR-001)
- [x] **T002** `IRuntimePipelineContext` (WorkItem + Workspace); both context records implement it + carry a `Workspace`. (FR-001)
- [x] **T003** `IRuntimePipelineWorkHandler` opt-in context-aware handler interface. (FR-003)
- [x] **T004** Workflow `Invoke` slot (order 150): `RuntimeWorkflowInvokeMiddleware` runs the staged handler before-`next`; register in slots + builder. (FR-002)
- [x] **T005** `RuntimeWorkflowCheckpointMiddleware` real impl: commit the staged commit before `next` (removed from placeholders). (FR-004)
- [x] **T006** Dispatcher: workflow stages the handler invocation (aware vs plain) on the workspace + no-op terminal; activity unchanged; no accessor. (FR-002/FR-003)
- [x] **T007** `WorkflowCancelSchedulerWorkHandler`: implement `IRuntimePipelineWorkHandler`; `BuildCommitAsync`; plain commits, aware stages. (FR-005)
- [x] **T008** Register `RuntimeWorkflowInvokeMiddleware` in `WorkflowsRuntimeApiFeature`; remove the deleted accessor registration. (FR-002)
- [x] **T009** Tests: middleware commits staged / no-ops; Cancel stages via the aware method / commits inline on direct dispatch; end-to-end Cancel through the feature pipeline. Add the `Invoke` middleware to the Move-1 dispatch-test provider; update `RuntimePipelineContractTests` for the new slot. (FR-006)
- [x] **T010** Build + full runtime suite green (542). (FR-006/SC-002) Activities.Runtime flaky test (`BreakPropagation…`) confirmed pre-existing/order-dependent — tracked separately.
- [x] **T011** ADR 0029 addendum (slot-invoked model) + spec/plan/bucket synced.
- [x] **T012** Update `.specify/feature.json` + AGENTS.md SPECKIT pointer → spec 083.
- [ ] **T013** Draft PR reworked onto the approved model.
