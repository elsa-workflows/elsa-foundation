# Implementation Plan: Preserve Dispatch Test-Run Scope

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #682 and the approved specification in this work unit.

## Summary

Carry one immutable, finite test-scope snapshot from root test-run dispatch through workflow execution state and every DispatchWorkflow child. Keep child artifact resolution on the retained Published pin and preserve run-kind propagation. Add a durable Runtime Core scope registry whose open/closing boundary is checked transactionally when a child dispatch is committed. Expiry or an authorized explicit teardown closes the scope once, then a restart-safe reconciler pages through scope-indexed dispatches: pending children are cancelled before admission; admitted children atomically receive the existing deterministic child-cancel responsibility. Ordinary parent completion never closes the scope. Groundwork persists and indexes scope state and exercises the before/after-materialization crash matrix.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Workflows Runtime/Core/Resumption, Publishing Core/API, DispatchWorkflow Design/Runtime, Persistence Groundwork, Microsoft.Extensions.DependencyInjection, FastEndpoints, Mediator

**Storage**: Existing workflow execution, dispatch, checkpoint/outbox, and Groundwork document/transaction substrate plus one provider-neutral test-scope registry

**Testing**: xUnit runtime/activity/publishing API tests, Groundwork transaction/restart fixtures, deterministic race/replay tests, and architecture guards

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project runtime library, publishing API, activity runtime, and durable provider adapter

**Performance Goals**: Scope lookup by ID; bounded expiry/closing queries; bounded dispatch pages by scope; one provider transaction per child registration or cleanup transition; no complete-history or cross-tenant scan

**Constraints**: Exact Published child pin; immutable run kind/scope; ordinary parent completion is not teardown; direct scope cleanup applies only to detached test children while waited children retain production cancellation semantics; tenant/partition isolation; no broker, Studio, #683 transport, activity inputs, or WorkflowDefinitionActivity

**Scale/Scope**: At least 100 duplicate teardown races, nested descendants, and Groundwork crash/restart before and after child admission

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Runtime/Design boundary**: PASS — runtime carries compiled test-scope and retained Published artifact facts; no Design project dependency is introduced.
- **Artifact-only execution**: PASS — DispatchWorkflow continues to use the publication-created retained pin even when the parent itself is a draft test artifact.
- **Checkpoint/post-commit rule**: PASS — child scope membership is committed with the dispatch; admitted-child cancellation responsibility is committed before delivery.
- **Atomic invariant boundary**: PASS — scope-open validation and child registration share the provider transaction; cleanup marks dispatch state and cancel responsibility atomically per child.
- **Single-writer/actor boundary**: PASS — cleanup never edits child execution state directly; admitted children receive the existing actor Cancel command.
- **Provider neutrality**: PASS — scope, closing, query, and cleanup replacement contracts live in Runtime Core; in-memory and Groundwork provide equivalent semantics and feature composition rejects multiple implementations.
- **Safety/authorization**: PASS — internal teardown uses active persistence scope and accepts no tenant, partition, child, executable, or authority selection; this slice adds no public route.
- **Replay/idempotency**: PASS — immutable scope ID, monotonic Open→Closing→Closed state, deterministic child-cancel identity, and provider fences/version checks converge duplicates.
- **Compatibility**: PASS — public activity inputs/outcomes stay unchanged; new execution/dispatch scope fields use null legacy defaults and additive constructors/contracts.
- **Naming**: PASS — planned Elsa type names remain within the five-component hard cap.
- **Test discipline**: PASS — contract/lifecycle tests precede implementation; every new public branch and Groundwork crash boundary is exercised.
- **Constitution status**: The constitution remains draft/provisional. Accepted checkpoint, persistence, authentication, single-writer, and artifact-retention ADRs remain controlling gates.

Post-design re-check: PASS. No constitution exception is required.

## Architecture and Flow

1. The test-run start handler creates one Runtime-owned `WorkflowTestScope` with the existing test-run ID, finite expiry, selected tenant, and partition through the replacement `IWorkflowTestScopeStore`, then includes its immutable snapshot in the root start request. The Publishing `WorkflowTestRun` record is only a projection with the same identity/expiry; its cleanup invokes the internal scope-close capability before deleting projection/source artifacts. No new public teardown route is added.
2. The publishing handler selects the active tenant and execution partition once, uses them for both scope creation and root dispatch, and carries the optional scope snapshot through start request, command payload, checkpoint payload, and `WorkflowExecutionState`. `TestRun` requires a scope for new roots; non-test runs reject one. The durable root start/checkpoint transaction validates that scope is still Open; teardown winning before delayed or replayed materialization prevents the root from starting. Rejected dispatch closes the newly created scope, and retry with the same test-run identity is idempotent. Legacy `TestRun` state without scope remains readable but cannot be cleanup-selected.
3. `DispatchPinSource` accepts parent compile scope `Published` or `TestRun` but always resolves the authored child from live `Published` references. DispatchWorkflow copies the parent's run kind and test scope into `WorkflowDispatchRecord` and start payload. Nested descendants inherit the same snapshot. Child-start validation compares scope alongside tenant, partition, authority, run kind, and retained pin.
4. Root and parent checkpoint commits validate an attached test scope against the current provider record in the same transaction that admits execution or creates dispatch/outbox responsibility. A closing, closed, expired, missing, wrong-tenant, or wrong-partition scope rejects new work. Child cleanup and `IWorkflowDispatchAdmissionStore.TryAdmitAsync` are mutually exclusive provider-atomic transitions: a cleanup-winning Pending record makes claimed/replayed start delivery a no-op; an admission-winning Started record atomically gains cancellation responsibility.
5. `IWorkflowTestScopeStore` owns monotonic `Open`, `Closing`, and `Closed` lifecycle plus bounded expiry/closing queries. Expiry and explicit teardown call the same idempotent close transition; ordinary execution completion does not.
6. `IWorkflowTestScopeCleaner` processes bounded detached-dispatch pages for one closing scope. A provider-owned `IWorkflowTestScopeCleanupStore` atomically resolves each fire-and-forget child: Pending becomes Cancelled-before-admission; Started is marked scope-cancellation-requested and receives the existing deterministic child-cancel outbox item; terminal state is unchanged. Waited dispatches are not direct cleanup targets.
7. `ChildCancelExecutor` accepts the authoritative scope-cancellation marker for detached children while retaining the existing waited parent-cancellation validation. It dispatches one actor Cancel command and waits for terminal evidence. Waited success/fault/cancellation remains exactly #679/#680 behavior; detached parents are never resumed.
8. The global resumption pump sweeps expired/closing scopes and continues cleanup after restart or response loss. Publishing expiry cleanup uses that same close operation and never independently owns lifecycle. A scope becomes Closed only after no eligible live detached dispatch remains; deterministic responsibilities may be replayed safely.
9. In-memory state shares the scope registry with checkpoint/dispatch/outbox state. Groundwork adds a scope document plus bounded indexes by lifecycle/expiry and dispatch scope, and performs scope assertion/cleanup mutations through its transaction substrate.
10. Inspection continues exposing run kind through existing execution/dispatch views. Scope identifiers and cleanup details remain on authorized publishing/runtime surfaces only; no raw inputs, authority, or provider diagnostics are added.

## Project Structure

### Documentation

```text
specs/102-dispatch-test-run-scope/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-test-run-scope.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/                         # scope registry, cleaner, atomic cleanup capability
└── Models/                            # immutable scope, lifecycle, close/cleanup requests/results

src/Elsa/Workflows/Runtime/
├── Services/                          # in-memory scope store, cleaner, start/checkpoint propagation
└── Resumption/                        # expiry/closing sweep integration

src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Activities/DispatchWorkflow.cs     # scope inheritance
└── Services/                          # child start/cancel validation and cleanup contribution

src/Elsa/Activities/DispatchWorkflow/Design/
└── Services/DispatchPinSource.cs       # TestRun-parent compile, Published-child lookup only

src/Elsa/Workflows/Publishing/Api/
├── Handlers/                          # root scope creation and projection cleanup coordination
└── Services/                          # internal/application teardown integration

src/Elsa/Persistence/Groundwork/
├── Stores/                            # durable scope registry and cleanup boundary
└── manifests/registration             # kinds, indexes, query declarations

tests/Elsa/Activities/DispatchWorkflow/Tests/
tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Workflows/Publishing/Api/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Workflows/Runtime/Resumption/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend the existing runtime, publishing API, DispatchWorkflow, Groundwork, and resumption projects. The scope registry is a runtime lifecycle fact; publishing creates/closes it, DispatchWorkflow inherits it, and providers persist it. No parallel queue, broker, UI, or distributed transport is introduced.

## Ordered Delivery

1. Lock scope model/lifecycle, TestRun-parent/Published-child pinning, run-kind compatibility, and root start/teardown API contracts with failing tests.
2. Enable TestRun parent compilation against Published child references only, then propagate scope through root start, checkpoint, execution state, DispatchWorkflow record/payload, and child admission.
3. Implement scope-open transactional assertion and in-memory close/cleanup transitions for before/after admission.
4. Reuse deterministic child-cancel delivery for scope cancellation and verify waited production-parity plus detached independence.
5. Integrate bounded expiry/closing sweep with global resumption and verify duplicate/restart convergence.
6. Implement Groundwork scope persistence, indexes, transactional assertions/cleanup, and crash/race fixtures.
7. Add internal close-coordination and cross-tenant/partition/production isolation tests, leave generated maps unchanged for explicit user invocation, and run the full completion audit.

## Complexity Tracking

No constitution violation requires justification. A first-class scope record is necessary because run kind alone cannot represent explicit closure, expiry ownership, tenant/partition binding, or the checkpoint-versus-teardown race.
