# Data Model: Collapse the per-activity hop chain under per-hop persistence

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md)

No table, column, migration or stored-document shape changes. The feature adds in-memory model members, one options
type, two diagnostics counters and one feature setting. Names follow Elsa §E6 (R1–R7); `Runtime` is
`src/essentials/Workflows/Runtime/`.

## Checkpoint commit (spec key entity)

`RuntimeCheckpointCommit` and its `RuntimeCheckpointStateChangeSet` (`Runtime/Core/Models/RuntimeCheckpointCommit.cs`).

| Member | Change | Rules |
|---|---|---|
| `RuntimeCheckpointStateChangeSet.SchedulerWorkInCommit` | **new**, `IReadOnlyCollection<string>`, default empty | Outbox item ids (from `PostCommitOutbox`) whose `EnqueueSchedulerWork` continuation the store writes as a scheduler work item inside this commit instead of an outbox row. Each id MUST name an entry of `PostCommitOutbox` whose intent kind is `EnqueueSchedulerWork` and whose work item targets `RuntimeCheckpointCommit.WorkflowExecutionId`. No duplicates. Preserved by every `With…` copy method. **Excluded from `RuntimeCheckpointCommitFingerprint`** (research R2). |
| `RuntimeCheckpointStateChangeSet.WithSchedulerWorkInCommit(IReadOnlyCollection<string>)` | **new** | Mirrors `WithConsumedSchedulerWorkItems`. |
| `PostCommitOutbox` | unchanged | Still holds one pending entry per intent, including the ones marked in `SchedulerWorkInCommit`. |
| `ConsumedSchedulerWorkItems` | unchanged shape | While the switch is on, never folded into an `ActivityAttemptClaimed` checkpoint (research R6). |

Validation lives in `RuntimeCheckpointCommitValidator` (`Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/`), so
every store receives an already-validated commit.

## Checkpoint commit store result

`RuntimeCheckpointCommitStoreResult` (`Runtime/Core/Models/RuntimeCheckpointCommitResult.cs`).

| Member | Change | Rules |
|---|---|---|
| `PendingPostCommitWorkIds` | meaning narrowed | The outbox rows actually written (excludes in-commit ids). On replay, read from the marker as today. |
| `ScheduledSchedulerWorkItemIds` | **new**, `init`, default empty | Outbox item ids whose continuation was written as a scheduler work item in this commit. On replay: the commit's `EnqueueSchedulerWork` outbox ids absent from the marker's pending set. |

Committer guard: `PendingPostCommitWorkIds ∪ ScheduledSchedulerWorkItemIds` equals the folded outbox ids and the two
sets are disjoint; otherwise `InvalidOperationException` naming the commit (same failure style as today's count guard).

## Checkpoint commit store capability

`IRuntimeCheckpointCommitStore` (`Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs`).

| Member | Change | Rules |
|---|---|---|
| `bool CanStageSchedulerWork(IWorkflowSchedulerWorkQueue schedulerWorkQueue) => false;` | **new** default interface member | `true` only when the store can write that queue's rows inside its own commit transaction. `EfRuntimeCheckpointCommitStore`: the queue is an `EfSchedulerWorkQueueStore` on the same `RuntimeDbContext` instance. `CoalescingRuntimeCheckpointCommitStore`: forwards to the inner store with the inner queue only when no coalescing session applies, else `false`. `InMemoryRuntimeCheckpointCommitStore`: `false` (default). A store reporting `false` MUST refuse a commit with a non-empty `SchedulerWorkInCommit`. |

## Scheduler work item (spec key entity)

`RuntimeSchedulerWorkItem` and `SchedulerWorkItemEntity`: unchanged. In-commit rows are built by
`EfSchedulerWorkQueueStore.ToEntity` (visibility `private` → `internal`), so identity, `WorkOrderKey`, content and
schema stamp equal an `EnqueueAsync` row. Lifecycle of an in-commit row:

```text
(checkpoint tx: staged) --commit--> Queued --ClaimAsync--> Claimed --consume in a later commit / CompleteClaimAsync--> deleted
        \--rollback--> never existed
```

Routed items (research R5) never become rows unless the routing pass falls back, in which case they are enqueued
exactly as today.

## Post-commit outbox item (spec key entity)

`RuntimePostCommitOutboxItem`: unchanged. Written for every intent not marked in `SchedulerWorkInCommit`.

## Routing pass (spec key entity)

`RuntimeRoutingPass` — `internal sealed`, scoped (drain scope),
`Runtime/Elsa.Workflows.Runtime/Services/Scheduler/RuntimeRoutingPass.cs`.

| Member | Purpose |
|---|---|
| `IDisposable Open(RuntimeSchedulerWorkItem claimedItem)` | Called by `WorkflowSchedulerDrainer` around one claimed dispatch when the FR-004 gate holds (research R4). One open pass per scope at a time. |
| `ValueTask RouteAsync(RuntimeSchedulerWorkItem next, CancellationToken)` | Called by `WorkflowStartSchedulerWorkHandler` and `WorkflowCompleteActivitySchedulerWorkHandler` in place of `IWorkflowSchedulerWorkQueue.EnqueueAsync`. Captures `next` when the pass is open, applies to `next.WorkflowExecutionId`, has not passed its boundary, and `HopRoutingClassifier` says routable; otherwise enqueues it. |
| `bool TryTakeNext(out RuntimeSchedulerWorkItem item)` | Drainer reads captured items in capture order. |
| `ValueTask FallBackAsync(RuntimeSchedulerWorkItem item, CancellationToken)` | Enqueues a captured item durably (create-only) and closes the pass. |
| `void Close()` | Marks the boundary; later `RouteAsync` calls enqueue. |

States:

```text
Closed --Open(claimed)--> Open --capture*--> Open --boundary commit or last item ran--> Closed
                                  \--pause / terminal / fault / no handler--> FallBack(item) --> Closed
```

Invariants: the claimed item is not acknowledged or released while the pass is open; a captured item is either
dispatched inline or enqueued before the claimed item is acknowledged; nothing is captured after the boundary.

## Routing classifier

`HopRoutingClassifier` — `internal sealed`, scoped,
`Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/HopRoutingClassifier.cs`. Holds the spec-228 completion
classification moved out of `ReplaySafeFusionDriver.ClassifyCompletionAsync` (child-faulted and parent-profile rules),
plus `IsStartCheckpoint(item)` for the `WorkflowStarted` item and `IsTailStep(item)` for the workflow tail
(`ContinuationScheduling` and the tail `Checkpoint` it derives; owner decision 2026-10-11). `RuntimeRoutingPass` uses
all three; the fusion driver uses only the spec-228 rules and keeps its tail fallback under coalescing.
Rule table: [research R7](research.md#r7--routable-in-process-fr-005-reconciled-with-spec-228).

## Consume frame link (FR-008)

| Element | Change | Rules |
|---|---|---|
| `RuntimePipelineWorkspace.ConsumedWorkClaim` (`Runtime/Core/Models/RuntimePipelineWorkspace.cs`) | **new**, `IRuntimeConsumedSchedulerWorkClaimAccessor?` | Staged by `RuntimeExecutionPipelineDispatcher` from its own (drain-scope) accessor. Null when no accessor is registered. |
| `RuntimeConsumedSchedulerWorkClaimAccessor.LinkTo(IRuntimeConsumedSchedulerWorkClaimAccessor target)` (`Runtime/Elsa.Workflows.Runtime/Services/Scheduler/`) | **new**, `internal`, returns `IDisposable` | While linked, every member delegates to `target`. Linking to itself is a no-op. Disposing restores the unlinked frame. Not supported while the instance has its own open frame (throws). |

## Switch (FR-009)

| Element | Change | Rules |
|---|---|---|
| `RuntimeHopCollapseOptions` (`Runtime/Core/Models/RuntimeHopCollapseOptions.cs`) | **new**, `public sealed` (options are public surface, §2.23.3) | `bool Enabled { get; set; } = true;`. Registered `TryAddSingleton` in `RuntimeCoreServiceCollectionExtensions`. Off restores today's behaviour for FR-001, FR-004 and FR-008 together. |
| `WorkflowsRuntimeCheckpointPersistenceFeature.CollapseHopChain` (`Runtime/Api/Coalescing/`) | **new** `[ManifestSetting]`, `bool`, default `true` | `PostConfigureServices` replaces the registered `RuntimeHopCollapseOptions` with `Enabled = CollapseHopChain` before its Immediate early return. |

## Diagnostics (FR-010)

`RuntimeSchedulerDispatchDiagnostics` (`Runtime/Core/Diagnostics/RuntimeSchedulerDispatchDiagnostics.cs`):

| Member | Meaning |
|---|---|
| `InProcessRoutingSteps` / `RecordInProcessRoutingStep()` | One per routed item dispatched inline by a routing pass. |
| `InProcessRoutingFallbacks` / `RecordInProcessRoutingFallback()` | One per routed candidate that an open pass handed to the queue (not routable, pass past its boundary, pause, terminal, fault, no handler). |

Both are reset by `Reset()`. A dispatch that never opened a pass records neither.
