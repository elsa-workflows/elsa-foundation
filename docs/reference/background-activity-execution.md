# Native background activity execution

Status: native proof validated in [#2914](https://github.com/elsa-workflows/elsa-foundation/issues/2914), [spec 241](../../specs/241-native-durable-offload/spec.md), delivered as [draft PR #2915](https://github.com/elsa-workflows/elsa-foundation/pull/2915). The spec's [quickstart](../../specs/241-native-durable-offload/quickstart.md) records exact acceptance results and limits. Merge and deployment remain separate.

Background placement runs an eligible native leaf body outside its workflow's serialized coordinator. Sequence and Flowchart keep their existing composition semantics. A workflow actor still owns orchestration and checkpoint writes; independently leased workers execute bodies and record terminal receipts. See [ADR 0031](../adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) for that boundary.

## Compose hosts

Enable the `BackgroundActivities` feature in the shell that coordinates workflows. Its default also runs workers on that host:

```json
{
  "BackgroundActivities": {
    "RunWorkers": true,
    "MaxConcurrency": 4,
    "PollIntervalSeconds": 1,
    "LeaseDurationSeconds": 30,
    "RenewalIntervalSeconds": 10
  }
}
```

A coordinator-only host sets `RunWorkers` to `false`; another host must consume the same scoped store. A custom worker host can claim through `IBackgroundActivityWorkStore` and call `BackgroundActivityWorker.ExecuteClaimAsync`. Each worker needs the same pinned executable and activity types, input resolvers, and persistence scope. Enabling the coordinator without deploying a consumer deliberately leaves work pending. Omitting the feature from the coordinator produces a `BG-ACT-001` incident when it encounters background placement.

The feature depends on ActivitiesRuntime, Tasks and WorkflowsRuntimeResumption. The EF checkpoint registration selects the background-work store as a sibling in the same Runtime EF context; a separate, independently committed work database is not supported. In-memory composition is for development and tests and cannot recover after process loss. Durable recovery requires EF persistence, the normal serialized workflow owner, and the normal runtime resume/drain services.

`MaxConcurrency` bounds bodies on one host, not across the cluster. Polling visits available persistence scopes with bounded receipt batches; strict inter-scope fairness is not promised. Programmatic `BackgroundActivityWorkerOptions` also controls `ResultSweepSize` (1–500) and `MaximumResultBytes` (default 1 MiB, maximum 16 MiB). Renewal must be positive and shorter than the lease. Invalid settings fail feature composition.

## Author a background leaf

The activity type declares `[BackgroundExecutionSafe]`, and the authored `ActivityNode.ExecutionPlacement` is `Background`. These are separate decisions: declaring a capability does not change every use of that type. Inline remains the default. The capability is included in the activity contract fingerprint; placement is included in the executable hash. Existing default-inline artifacts retain their serialization and hash behavior.

The initial capability supports registered CLR leaf activities with concrete typed results and a terminal Complete, Fault or Cancel transition. It excludes structural/composite activities, intrinsics, reusable boundaries, stateful activities, child callbacks, checkpoint participants, transient results, and sensitive or encryption-required result policies. A background body cannot stage engine-owned dispatch or stimulus effects. Unsupported placement fails publication with `VF-BG-001`; runtime validation also checks the pinned contract and actual activated type. An unsupported transition becomes a native fault through the recorded worker receipt.

The worker resolves result types through the type registry. A receipt cannot select its own CLR type. Result values must satisfy the pinned type, declared outcome and configured byte limit. Input snapshots retain existing value/secret policies; secret references resolve in the worker's bound activation scope, and fault messages use the existing runtime secret mask. Authors remain responsible for marking sensitive outputs appropriately.

## Durability and cancellation

Admission commits the logical attempt, immutable inputs and owed work in one fenced native checkpoint. A worker claims the row with an expiring, monotonically fenced lease, renews while it runs, and commits its terminal receipt. A bounded sweep repairs a lost coordinator wake-up. The coordinator accepts the exact receipt in the same transaction as native terminal activity state and its continuation intent. It reconstructs the typed result and uses the existing native projection and completion path; it does not reactivate the original body.

Bodies are physically at least once. A crash after an external effect but before receipt persistence can repeat the effect. Use the stable invocation/attempt identity as an external idempotency key where appropriate. A worker lease fence protects receipt acceptance; it cannot undo external effects. Exactly one logical completion does not imply exactly one physical body execution.

Explicit native cancellation writes a tombstone in the same checkpoint as the affected activity/workflow state. Unaccepted pending, claimed or ready work is cancelled. Accepted work remains immutable. A worker observes cancellation through failed renewal and signals its body token; a body that ignores cancellation may continue external work, but its late receipt is rejected. Host shutdown only stops the worker and renewal: it does not write a business cancellation. Expiry makes unfinished work reclaimable by another host.

Deleting an execution through the EF state store atomically cancels its outstanding background work, revokes coordinator ownership, and retains a deletion marker in that persistence scope. Admission requires a current workflow fence and a live execution. The first workflow upsert and admission may share a coalesced checkpoint; after deletion, the retained marker rejects that identity even if a new owner acquires a lease and supplies another upsert. Accepted receipts remain history. The development in-memory workflow store retains its existing process-global execution-ID semantics: deleting an ID cancels that ID's work across its in-memory scopes. Use EF for tenant-isolated deletion and process recovery.

The initial recorded receipt may be retried with the exact recording claim and identical canonical content after its lease has expired. An expired first receipt, a stale claim, or conflicting content is rejected. Worker fences and workflow-coordinator ownership fences are independent.

## Inspect, pause and roll back

A waiting native activity carries `BackgroundPending` and its background work ID in metadata. Worker records distinguish Pending, Claimed, ResultReady, Accepted and Cancelled. Completion and business failures enter native inspection/incidents. The ledger is an internal persistence contract; this slice does not add a public ledger management API or designer settings UI.

To pause physical work, stop worker hosts or compose `RunWorkers: false` while retaining coordinator capability and the store. Claims expire and remain recoverable. To drain before a code rollback, stop admitting new background workflows, leave compatible workers/coordinators running until pending/claimed/ready work is empty, and retain ledger history. Removing a worker type or result type while work is outstanding can prevent compatible execution; keep the pinned implementation available.

Runtime schema migrations follow this repository's [pre-release Initial regeneration policy](../../tools/ef/README.md). This change regenerates all four Runtime provider Initial migrations. Existing development databases from a prior Initial need a fresh database or an explicitly prepared migration path; this slice does not claim an in-place production upgrade. Recovery tests restart the same build against the same migrated database. Do not roll code back over a newer schema or erase ledger rows to make a rollback appear successful.
