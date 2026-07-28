# Implementation Plan: Single Durable Transaction per Drain Step

**Spec**: [spec.md](spec.md)

## Technical context

- Language/stack: C# / .NET, Elsa runtime core + Groundwork persistence bridge.
- Persistence strategy is the load-bearing part of this unit (why it needs a Speckit spec, not thin glue): the work-item ack becomes part of the checkpoint commit's cross-store atomic unit-of-work.
- No new document kind, no storage-manifest change: `SchedulerWorkItemDocumentKind` is already inside `RuntimeCheckpointCommitScope`.

## Design decisions

### D1 — Claim fence uses owner + fencing token, not document revision

The claim renewal loop (`WorkflowSchedulerDrainer.RenewClaimUntilStoppedAsync`) advances the claim's provider revision while the handler runs, but keeps the claim **owner id** and **fencing token** stable (`RenewClaimAsync` only moves the visibility deadline; `ClaimAsync` is the only transition that bumps the fencing token, and only for a successor reclaim). The consume-change therefore fences on **owner id + fencing token** and deletes by the document's *current* version loaded inside the unit-of-work. This is renewal-safe (a renewal in flight cannot fail the consume) yet still detects a successor reclaim (token advanced) as claim-lost. `ConsumedSchedulerWorkItem` carries no revision.

### D2 — Claim transport: a scoped ambient accessor shared by drainer and committer

The drainer and the committer are both scoped and resolved from the same drain scope (verified in `RuntimeCoreServiceCollectionExtensions`). A new scoped `IRuntimeConsumedSchedulerWorkClaimAccessor` holds the current dispatch's claim and a "consumed durably" flag. The drainer sets the claim before dispatch (in a `using` scope that clears it after) and reads the flag after; the committer reads the claim to fold the consume-change and sets the flag from the store result. No new AsyncLocal (RT-7): the accessor is explicit scoped DI state, consistent with the drain already threading ambient services.

### D3 — Consume-once per dispatch

A dispatch may commit multiple checkpoints (e.g. `InvokeActivity`). The consume-change is attached to the **first** commit that lands during the dispatch; the accessor marks the pending claim consumed so later commits in the same dispatch do not re-attach it (a second fence-delete would find the item gone and fail claim-lost). For the dominant single-commit handlers (Cancel, Checkpoint, Start, routing CompleteActivity) this is the only commit, so the fold is exact.

### D4 — Coalesced mode: committer-level suppression

While an active coalescing session owns the workflow execution, the committer does **not** fold the consume-change. In coalesced mode the queue is advanced durably per-segment by `RuntimeCoalescingSession.AdvanceInnerQueueAsync` (via the overlay queue), decoupled from each checkpoint; folding a consume-change into the flush commit would double-delete against that advance. Suppression keeps the overlay authoritative. `RuntimeCheckpointFold` still unions `ConsumedSchedulerWorkItems` defensively so the fold utility is correct and unit-testable (spec test d).

**Deviation note vs. the work-unit brief (design point 5)**: the brief lists having the coalescing decorator "recognize the new change in the overlay." The realized approach recognizes it by suppressing the fold at the committer while a session is active (single choke point covering the decorator's buffer, flush, and pass-through paths), rather than reconciling a durable consume-delete against `AdvanceInnerQueueAsync` inside the decorator. Rationale: reconciling two durable queue-advance mechanisms in coalesced mode is strictly riskier and yields no additional transaction saving there (coalesced mode already batches queue advance per segment). The durable consume-fold is therefore an Immediate-mode optimization, which is exactly where the measured 40–60-transaction cost lives.

### D5 — Store result + replay marker carry consumed ids

`RuntimeCheckpointCommitStoreResult` gains `ConsumedSchedulerWorkItemIds`. Both durable stores return the ids they actually deleted; the durable replay marker (Groundwork `CheckpointCommitMarker`, in-memory `RuntimeCheckpointCommitRecord`) records them so replay returns them and the committer's acknowledgement-count guard passes idempotently (FR-008).

## Changed components

| File | Change |
|---|---|
| `Core/Models/ConsumedSchedulerWorkItem.cs` (new) | The fence-carrying consume record + `FromClaim`. |
| `Core/Models/RuntimeCheckpointCommit.cs` | `ConsumedSchedulerWorkItems` collection + constructor param + `WithConsumedSchedulerWorkItems`. |
| `Core/Models/RuntimeCheckpointCommitResult.cs` | `ConsumedSchedulerWorkItemIds` on the store result. |
| `Core/Models/RuntimeCheckpointCommitFingerprint.cs` | Include consumed items in the canonical shape only when present (preserve old fingerprints). |
| `Core/Contracts/IRuntimeConsumedSchedulerWorkClaimAccessor.cs` (new) + `Services/RuntimeConsumedSchedulerWorkClaimAccessor.cs` (new) | Scoped claim transport. |
| `Core/Exceptions/RuntimeSchedulerWorkConsumeConflictException.cs` (new) | Claim-lost signal from a failed consume fence. |
| `Core/Contracts/IWorkflowSchedulerWorkQueue.cs` | `ConsumeClaimedAsync` default method (legacy providers throw NotSupported). |
| `Services/InMemoryWorkflowSchedulerWorkQueue.cs` | Fence-checked consume. |
| `Persistence/Groundwork/Stores/GroundworkWorkflowSchedulerWorkQueue.cs` | Fence-checked consume (in the UoW store). |
| `Services/RuntimeCheckpointCommitter.cs` | Attach consume-change (session-aware), guard, mark accessor. |
| `Services/InMemoryRuntimeCheckpointCommitStore.cs` | Apply consume in the UoW; return + record consumed ids. |
| `Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs` | Apply consume in the UoW; validate; return + record consumed ids in the marker. |
| `Services/Coalescing/RuntimeCheckpointFold.cs` | Union `ConsumedSchedulerWorkItems`. |
| `Services/WorkflowSchedulerDrainer.cs` | Set/read the accessor; skip ack on the atomic path; treat consume-conflict as claim-lost. |
| `Extensions/RuntimeCoreServiceCollectionExtensions.cs` | Register the accessor; thread it into drainer + committer. |

## Test strategy

Guard/extend the listed runtime + Groundwork suites; add the four new tests (atomic consume iff commit lands; stale-claim claim-lost; handler-fault legacy ack + poison once; coalesced fold union). Run the full runtime and Groundwork persistence test projects.
