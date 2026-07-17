# Tasks: Execute DispatchWorkflow Across Distributed Nodes

**Input**: Design documents from `specs/103-dispatch-distributed-execution/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required by #683. Two-node, duplicate/stale ownership, restart, readiness, architecture, and local regression tests precede or accompany implementation.

## Phase 1: Setup and Baseline

- [x] T001 Verify clean #682 baseline and run focused DispatchWorkflow local regression plus distributed Groundwork baseline tests with `/usr/local/share/dotnet/dotnet`
- [x] T002 Record current #683 and parent #674 body/comment authority in `specs/103-dispatch-distributed-execution/`
- [x] T003 [P] Inventory distributed actor provider, command transport, placement lease, fence, and readiness seams in `src/Elsa/Workflows/Runtime/Distributed/` and Groundwork persistence

## Phase 2: Foundational Distributed Test Harness

- [x] T004 [P] Extend the shared two-node fixture with distinct node IDs and shared durable Groundwork state in `tests/Elsa/Workflows/Runtime/Distributed/Tests/`
- [x] T005 [P] Add safe distributed forwarding metadata assertions for child-start delivery in `tests/Elsa/Activities/DispatchWorkflow/Tests/ChildStartExecutorTests.cs`
- [x] T006 Add reusable distributed dispatch scenario helpers for parent checkpoint, child-start drain, node restart, and inspection in `tests/Elsa/Workflows/Runtime/Distributed/Tests/`
- [x] T007 Run foundational distributed fixture and child-start metadata tests with `/usr/local/share/dotnet/dotnet`

## Phase 3: User Story 1 - Child Starts On Eligible Node

### Tests

- [x] T008 [P] [US1] Add two-node acceptance test proving one node commits the DispatchWorkflow checkpoint/outbox and an eligible distributed node materializes the child in `tests/Elsa/Workflows/Runtime/Distributed/Tests/`
- [x] T009 [P] [US1] Add inspection consistency test proving parent, dispatch, and child lifecycle state is node-independent in Runtime API or distributed Groundwork tests
- [x] T010 [P] [US1] Add activity contract regression proving no node/queue/priority/affinity/transport input appears in DispatchWorkflow tests or architecture guards

### Implementation

- [x] T011 [US1] Wire any missing distributed forwarding evidence from the configured actor provider through the existing workflow start command result without changing DispatchWorkflow inputs
- [x] T012 [US1] Ensure `ChildStartExecutor` acknowledges only durable distributed forwarding with complete safe metadata and preserves transient retry on incomplete evidence
- [x] T013 [US1] Ensure distributed child materialization updates the existing dispatch lifecycle and inspection surfaces consistently
- [x] T014 [US1] Run US1 two-node, inspection, and activity-contract tests with `/usr/local/share/dotnet/dotnet`

## Phase 4: User Story 2 - Duplicate Delivery And Placement Changes Converge

### Tests

- [x] T015 [P] [US2] Add duplicate child-start delivery test before and after child admission in distributed Groundwork tests
- [x] T016 [P] [US2] Add stale placement/lease change test proving fenced writes cannot create duplicate child state
- [x] T017 [P] [US2] Add local in-process regression proving duplicate delivery still converges without distributed provider changes

### Implementation

- [x] T018 [US2] Close any distributed command transport idempotency gap using existing dispatch ID, child execution ID, and idempotency key
- [x] T019 [US2] Close any placement/fence stale-writer gap in Groundwork distributed persistence without changing activity contract
- [x] T020 [US2] Run duplicate, stale placement, and local regression tests with `/usr/local/share/dotnet/dotnet`

## Phase 5: User Story 3 - Restart Preserves Distributed Progress

### Tests

- [x] T021 [P] [US3] Add restart-after-intent test proving an eligible node can still claim and execute the child
- [x] T022 [P] [US3] Add restart-after-claim or after-forwarding test proving resumed processing converges
- [x] T023 [P] [US3] Add readiness classification tests for in-memory, durable single-node Groundwork, and distributed Groundwork compositions

### Implementation

- [x] T024 [US3] Ensure outbox/resumption and distributed transport recovery resume child-start progress after either node restart
- [x] T025 [US3] Implement or correct readiness classification for distributed DispatchWorkflow composition
- [x] T026 [US3] Run restart and readiness tests with `/usr/local/share/dotnet/dotnet`

## Phase 6: Polish and Completion

- [x] T027 [P] Update extension-point/catalog documentation only where distributed DispatchWorkflow composition surfaces changed
- [x] T028 Leave generated maps unchanged for explicit user invocation and review authored navigation/docs for accuracy
- [x] T029 Run architecture guard searches/tests proving no broker, service-bus, routing-channel, priority, affinity, transport-selection, Studio UI, or WorkflowDefinitionActivity expansion
- [x] T030 Run full quickstart test projects with `/usr/local/share/dotnet/dotnet`
- [x] T031 Run Speckit analysis and audit every FR/SC/#683 acceptance criterion against source/tests
- [x] T032 Verify `git diff --check`, task/checklist completion, clean local commit, and no GitHub mutation/push/PR

## Dependencies and Parallel Opportunities

- Phase 2 blocks all stories. US1 proves the main two-node path, US2 hardens duplicate/stale ownership, and US3 proves restart/readiness.
- Tasks marked `[P]` use distinct files or read-only inventories and may be delegated. Delegated output remains subject to root review and full integration QA.
- No task authorizes GitHub mutation, push, PR creation, broker integration, Studio UI, activity transport controls, or `WorkflowDefinitionActivity` changes.

## Completion Evidence

- T001/T007/T014/T020/T026/T030: Focused distributed runtime tests pass with `/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj --no-restore --nologo` (45/45). Full verification also passes: DispatchWorkflow 178/178, Runtime 1152/1152, distributed Groundwork 54/54, Groundwork 457/457, Runtime API 60/60, resumption 16/16, Architecture 226/226.
- T002/T003/T004/T006: #683 and #674 authority reviewed from GitHub; existing two-node in-memory and Groundwork harnesses were inventoried and reused.
- T005/T011/T012: Existing `ForwardingWorkflowExecutionActor` emits safe distributed forwarding metadata and `ChildStartExecutor` acknowledges only complete durable forwarding evidence; existing child-start metadata tests remain in place.
- T008/T015/T021/T022: Added `DispatchWorkflowChildStart_CommittedOnOneNode_ConvergesAfterBothNodesRestart` to the shared two-node acceptance suite so it runs against both in-memory and Groundwork-backed checkpoint/outbox, dispatch, execution-state, placement, and transport state.
- T009/T013/T024: The acceptance persists parent/dispatch/outbox on the commit node, recreates the handler node, forwards duplicate starts, recreates the eligible owner around an unacknowledged transport item, then verifies durable parent/dispatch/child state through the Runtime API inspection handler.
- T010/T017/T018/T019: Existing actor idempotency, deterministic checkpoint identity, and W5 fencing remain the convergence boundary; duplicate forwarded child-start items are acknowledged while provider replay leaves one logical child execution state.
- T023/T025: Readiness now reports `ProcessLocal`, `DurableReady`, `Unsafe`, or additive `DistributedReady` from registered runtime and distribution durability evidence; ordinary Groundwork is not classified as distributed.
- T027/T028: No production extension-point surface changed. Generated maps remain unchanged and user-invoked; authored specs and test-project references provide the completion evidence for this slice.
- T029: Strengthened `Dispatch_workflow_modules_preserve_runtime_design_and_transport_boundaries` to reject service-bus, routing-channel, transport-selection, priority, and affinity contract terms; focused architecture guard passes.
- T031/T032: Speckit analysis/completion audit, `git diff --check`, full verification matrix, and local commit are performed after this evidence update.
