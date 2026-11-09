# Implementation Plan: Native durable background activities

**Branch**: `agent/codex/2914-native-durable-offload` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

**Status**: Design accepted for implementation; no implementation evidence claimed.

## Summary

Add opt-in background placement to ordinary native leaf invocations. Keep one workflow coordinator and the existing Sequence/Flowchart semantics. Record a durable work item in the existing EF checkpoint transaction, execute it in a separate worker scope under an independent expiring claim, record a terminal receipt, and return through the coordinator's normal typed completion/fault path. Receipt acceptance and the resulting state/continuation share one fenced checkpoint. Cancellation creates a durable tombstone; shutdown leaves work reclaimable.

This implements #2914 under #2905/#2892. It does not extract a universal engine or use a child workflow to stand in for the configured activity.

## Technical Context

- C#/.NET and SDK versions pinned by the repository; existing CShell feature composition and Elsa task pumps.
- Provider-neutral contracts in `Essentials/src/Workflows/Runtime/Core`; activity execution in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime`.
- Existing Runtime EF module, `RuntimeDbContext`, scope resolver, migrations/schema lifecycle and `RuntimeCheckpointCommit` transaction. No raw SQLite store or second persistence stack.
- Test frameworks and fixtures already used by affected projects. File-backed SQLite is the actual EF integration provider; provider model/migration checks cover the shared relational shape.
- No performance benchmark or timing budget. Barriers prove concurrency, and faults/restarts prove durability.
- This extends an existing implementation. Tests are required; no new greenfield test-cadence choice is needed.

## Constitution Check

| Gate | Application |
|---|---|
| Runtime independent of Design (§E2.2) | Authoring setting is copied at publishing; worker/coordinator consume the executable only. |
| Artifact-only runtime (§E2.6) | Placement, eligibility, input and result contract are pinned. Old Inline shapes/hashes stay stable. Published background artifacts require compatible workers until drained/cancelled. |
| Core dependencies | Contracts carry immutable values and no EF, DI container or concrete queue dependency. |
| EF sole first-party persistence (ADR 0073) | Extend the Runtime EF participant and common checkpoint; use real EF tests. |
| Serialized orchestration (ADR 0031/0078) | One coordinator fence; independent worker fences authorize only the worker row, never workflow state. Amend 0031's blanket intra-execution parallelism wording. |
| Activity-owned composition (§E2.9.1, provisional) | No generic graph edges/joins. Sequence and Flowchart continue to own their structures. This section remains provisional. |
| Optional composition and tests | Feature registration and shell task lifecycle tests; missing durable store fails before background execution. |
| Security and value policy | Scope-isolated claims, pinned types, no worker-selected CLR names, secret masking; v1 rejects transient/sensitive or encryption-required results it cannot safely carry. |

No unresolved product decision blocks this slice. Implementation may refine internal shapes while preserving [contracts](contracts/background-activity.md) and all acceptance tests.

## Architecture and ownership

1. **Authoring/publishing:** `ActivityNode.ExecutionPlacement` defaults Inline. Publishing copies it to `ExecutableNode`, includes nondefault placement in both executable hash paths, and validates a fingerprinted `ActivityContract.SupportsBackgroundExecution` capability declared by a separate activity attribute. Background placement rejects intrinsics, structural/stateful/checkpoint-participating and unsupported reusable/staging-dependent activities. The attribute is a capability declaration, not scheduling code.
2. **Coordinator admission:** The invocation handler prepares its logical attempt and immutable inputs without running the activity. One mandatory checkpoint records its background identity and a new Pending work item. Admission is an immediate flush even with checkpoint coalescing. Redelivery finds the same item and does not create another logical attempt.
3. **Worker ledger:** One scope-keyed EF row stores the immutable request and its claim/receipt lifecycle. It is not the workflow scheduler FIFO and not the generic post-commit outbox. Workers claim with a monotonic fence and visibility deadline, renew from a separate persistence scope, and commit terminal results only while current and unexpired.
4. **Worker execution:** Each claim gets an isolated DI scope, pinned artifact validation, normal activity activation/input hydration and disposal, and a linked cooperative cancellation token. It runs only the declared leaf body. Terminal typed result is serialized using the pinned result contract; exceptions and disposal failures become masked fault results. Suspension or structural/runtime-staged work fails explicitly. Worker host cancellation abandons the lease; it does not return a business Cancel.
5. **Receipt delivery:** ResultReady is durable before a wake-up. A bounded sweep re-enqueues a deterministic coordinator work pointer until accepted. Notifications are hints, and a crash after the receipt commit cannot lose the result. A worker never runs the coordinator's scheduler item itself.
6. **Coordinator application:** Load and validate the ready receipt against the activity's pinned logical attempt. Reconstruct the result type through the well-known registry and normal Web JSON serialization. Reuse existing `ActivityCompletionProjector`, `RuntimeOutputCaptureProjector`, fault/incident and cancellation checkpoint services without reactivating or rerunning the original body. Accept the receipt in the same workflow-fenced checkpoint. An explicit, scoped acceptance context/enricher is allowed to connect existing terminal checkpoint builders to the new participant; no ambient global state.
7. **Cancellation:** Workflow and subtree cancellation checkpoint participants resolve matching background items inside the transaction, including pending, claimed and ready rows. Mark Cancelled, increment revision/fence, preserve identity/tombstone and reject late worker writes. A worker sees cancellation or lease loss on renewal and signals its body. Uncooperative work may continue physically but cannot commit or resume the cancelled scope.
8. **Native continuation:** Reuse the existing per-child parent-callback marker. Resolve the #2232 late completion-cascade replay gap with stable propagation identity/content so replay after queue consumption cannot poison a workflow or advance its parent again. Do not use ActivityExecutionStatus.Completed alone to suppress the first legitimate completion.

## Data, contracts and lifecycle

See [data-model.md](data-model.md) for records and the race table, [contracts/background-activity.md](contracts/background-activity.md) for the extension surface, and [research.md](research.md) for source-backed choices.

Only the workflow coordinator admits/accepts/cancels work. Only worker claims authorize execution/result recording. The two fences are independent. Immutable tombstones and applied identities remain at least as long as the corresponding workflow history; cleanup must not resurrect a deleted identity.

## Project structure and delivery lanes

| Lane | Owned surfaces |
|---|---|
| Root: integration | Spec/plan/tasks, native invoke admission/result reuse, worker pump and task registration, cancellation orchestration, ADR and program records, final QA. |
| Persistence | Core background contracts/lifecycle, checkpoint copy/fingerprint/validator/coalescing propagation, in-memory parity, EF entity/config/store/staging/registration/migration and focused store tests. |
| Authoring | Safe capability attribute/contract fingerprint, authored and executable placement, publishing validation and hash compatibility, focused compiler/serialization tests. |
| Verification | Independent plan review, then focused production EF journey/process witness and late completion replay fix/tests in isolated checkout after interfaces settle. |

Writers use isolated worktrees. Root reviews and cherry-picks bounded commits, owns cross-lane fixes and final validation. No simultaneous full-solution builds.

## Verification and acceptance

[quickstart.md](quickstart.md) is the run guide; [tasks.md](tasks.md) records progress. Required evidence includes both result orders, separate worker processes held at a barrier, fresh provider/process recovery, stale and expired claims, conflicting and exact-retry receipts, wrong scope/pin, competing coordinator fence, cancellation at all named boundaries, returned faults/Cancel, exceptions/disposal failures, output captures, legacy Inline compatibility and meaningful guard mutations. Main CI failure #2293 predates this branch and is reported separately, not called passed.

## Complexity and risks

- A new ledger participant is justified: the existing FIFO serializes coordinator work, and outbox completion has dispatch-specific follow-up semantics. Running user code inside either would violate the intended boundary.
- Lease expiry can duplicate physical effects. Stable invocation identity is exposed by the existing activity context; activities must use it for external idempotency. This feature does not promise exactly-once external effects.
- An opt-in background-safe declaration is a contract promise. It cannot prove absence of arbitrary side effects or hidden DI usage; eligibility plus runtime staging rejection avoids silently accepting known engine-owned effects.
- Sensitive/encryption-required payloads must not be copied into an unprotected receipt. V1 rejects policies/types it cannot round-trip safely, with visible diagnostics.
- Worker fairness, backlog bounds, retention, multi-provider SQL behavior and shutdown require explicit tests/operational defaults; do not infer production scalability from this correctness slice.
- Do not mark the spec Implemented until its code is merged; finishing the goal requires executable production evidence and review, not merely this plan.
