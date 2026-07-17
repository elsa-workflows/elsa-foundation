# Implementation Plan: Execute DispatchWorkflow Across Distributed Nodes

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #683 and parent #674, current bodies and comments fetched 2026-07-16.

## Summary

Prove and complete the distributed DispatchWorkflow path by composing the existing child-start intent handler with the Groundwork distributed execution actor provider, durable command transport, placement leases, and checkpoint fencing. A parent dispatch checkpoint may be committed on one node while an eligible second node claims and executes the child, with duplicate delivery, stale placement, and restart converging on one logical child. The activity contract remains transport-neutral and local in-process behavior stays unchanged.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Workflows Runtime/Core/Distributed, DispatchWorkflow Runtime, Persistence Groundwork, Runtime API, Microsoft.Extensions.DependencyInjection

**Storage**: Existing Groundwork runtime persistence, distributed command transport, placement leases, checkpoint/outbox records, workflow dispatch records, and workflow execution state

**Testing**: xUnit unit/integration tests; two-node distributed Groundwork host fixtures; architecture guard tests

**Target Platform**: Cross-platform .NET server hosts using Elsa Foundation runtime libraries

**Project Type**: Multi-project runtime library, activity runtime, distributed runtime provider, durable provider adapter, and tests

**Performance Goals**: Distributed child-start delivery is bounded by existing durable transport claim and provider checkpoint writes; no activity-level polling or cross-node scan is added

**Constraints**: No activity transport inputs, broker concepts, Studio UI, WorkflowDefinitionActivity changes, or weakening of #675-#682 semantics; checkpoint fencing remains the safety boundary

**Scale/Scope**: Two-node deterministic acceptance, duplicate/stale ownership/restart scenarios, readiness classification across in-memory, single-node durable Groundwork, and distributed Groundwork

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Runtime/Design boundary**: PASS - work remains in runtime, distributed runtime, DispatchWorkflow runtime, Groundwork, tests, and docs. No new Runtime-to-Design dependency is introduced.
- **Artifact-only execution**: PASS - child starts continue through retained pins and existing workflow start dispatcher.
- **Checkpoint/post-commit rule**: PASS - distributed work starts only from committed child-start intent and durable command/placement state.
- **Atomic invariant boundary**: PASS - checkpoint fencing and provider leases own stale/duplicate writer safety.
- **Single-writer/actor boundary**: PASS - child execution remains owned by the configured actor provider; the activity does not write child state directly.
- **Provider neutrality**: PASS - activity-facing seam remains `IWorkflowStartDispatcher` plus actor provider; Groundwork distributed provider supplies one implementation.
- **Safety/authorization**: PASS - authority, tenant, partition, run kind, test scope, and safe diagnostics from prior slices remain unchanged.
- **Replay/idempotency**: PASS - deterministic dispatch/child identities and durable fencing converge duplicate delivery.
- **Compatibility**: PASS - local in-process behavior and public activity inputs/outcomes are preserved.
- **Naming**: PASS - no planned type name exceeds the five-component hard cap.
- **Test discipline**: PASS - tests precede or accompany implementation across two-node, duplicate, restart, readiness, architecture, and regression paths.
- **Constitution status**: The constitution remains draft/provisional. Accepted runtime checkpoint, artifact, pipeline, and single-writer decisions remain controlling gates.

Post-design re-check: PASS. No constitution exception is required.

## Architecture and Flow

1. A parent workflow commits the existing DispatchWorkflow checkpoint on node A, producing the same deterministic dispatch record and child-start post-commit intent introduced by #675-#682.
2. Runtime post-commit delivery invokes `ChildStartExecutor`, which still calls `IWorkflowStartDispatcher` with the retained child pin, inherited authority, tenant, partition, run kind, and test scope.
3. In distributed composition, the workflow start dispatcher uses the configured distributed execution actor provider. The provider durably forwards command work through the existing Groundwork command transport and returns durable forwarding metadata to the child-start handler.
4. An eligible node B drains distributed command transport, claims placement with provider lease/fencing, and executes the child through the normal workflow execution actor path.
5. Duplicate child-start delivery and duplicate distributed transport handling reuse the same deterministic child execution ID and idempotency key. Placement and checkpoint fencing reject stale writes or convert them to no-ops.
6. Restart after durable intent creation is handled by existing outbox/resumption and distributed transport pumps. Recovery reuses the same dispatch/child identities and converges to Started or terminal lifecycle state.
7. Inspection reads the durable dispatch/execution state and reports the same lifecycle facts regardless of the executing node. Readiness diagnostics classify whether the host is in-memory development, durable single-node Groundwork, or distributed Groundwork.

## Project Structure

### Documentation

```text
specs/103-dispatch-distributed-execution/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-distributed-execution.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Services/ChildStartExecutor.cs
└── EXTENSION_POINTS.md

src/Elsa/Workflows/Runtime/Distributed/
├── Services/
└── EXTENSION_POINTS.md

src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/
├── Stores/
└── WorkflowsRuntimeDistributedGroundworkPersistenceFeature.cs

src/Elsa/Persistence/Groundwork/
└── Stores/

tests/Elsa/Activities/DispatchWorkflow/Tests/
tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend existing distributed runtime and Groundwork seams only. No new transport abstraction, broker package, Studio surface, or activity input model is introduced.

## Ordered Delivery

1. Baseline the current distributed provider, command transport, placement, and fencing behavior against #683 acceptance.
2. Add two-node DispatchWorkflow tests that prove node A commit and node B execution through the existing start dispatcher/actor provider seam.
3. Add duplicate delivery, placement change, and stale ownership tests, then close any gaps in fencing/idempotency.
4. Add restart tests for node A and node B after durable child-start intent creation and around child materialization.
5. Add readiness diagnostics/tests distinguishing in-memory, durable single-node Groundwork, and distributed Groundwork composition.
6. Add architecture guards against broker/transport activity-contract drift and local in-process regression tests.
7. Update the authored docs, leave generated maps for explicit user invocation, run full verification, analyze artifacts, and commit #683 locally.

## Complexity Tracking

No constitution violation requires justification. Distributed execution is implemented through the existing Groundwork distributed provider because #683 explicitly requires proving that existing provider, transport, placement, and fencing surface.
