# Execution reader and rollback boundary

This is reader qualification on draft switch PR3338, not permission for a running rollback.
The current adapter retains both v1 and v2 readers; continuing v1 uses its declared original wire
names/version. The frozen old-host databases and their digests remain unchanged.

| Writer/checkpoint | Reader | Evidence | Limit |
|---|---|---|---|
| Actual preview0.1.1-preview.19 v1 writer | Released-library Foundation adapter | Six frozen whole EF databases reopen in fresh upgraded providers and reach their expected outcomes. | [Exact queued-loser duplicate callback after pruning/restart](queued-loser-replay.md) is separately qualified. [Actual late-fault absorption/replay](late-fault-upgrade.md) is also qualified. Dedicated combined durability remains open. |
| Actual upgraded host continuing v1 | Original production `BpmnStatePersister.LoadState` and original v1 model | Real MI/listener continuation after resumed work: CommittedAt advances, MI sequence advances; the original reader parses and every original serialized field equals the persisted upgraded v1 payload recursively. The original deferred fault parses too. | Parsing is not old-engine execution. Additive stopped-handle/scope-correlation data is unknown to that reader. |
| Actual new host fresh v2 checkpoint | Original v1 production reader | Explicit BpmnExecutionException refusal. | Fresh v2 is not supported by old binaries. |
| Old/new alias, version, truncated or conflicting payload | Current codec | Named codec and actual-database refusal controls. Original checkpoint retained; no extra child dispatch. | No inference that malformed data is a fresh empty execution. |
| Upgraded active database | Old host binary | **Unqualified.** No old-host continuation from an upgraded database copy has been proved. | Live binary rollback is unsupported by this delivery. Keep a coherent pre-upgrade backup; do not claim rollback based on a parser result. |

The original reader's parse control runs while its production subject still exists, before T032
removes the duplicate engine/model. Its tested source revision must remain in the durable receipt.
When that subject is deleted, retire this historical control with the subject and retain the exact
checkout/reproduction record; the adapter's current v1/v2 compatibility and actual frozen-database
recovery tests stay active. Do not keep the duplicated production semantics merely to run an old-reader
control. Do not remove the current v1 reader, replay identity or required durable history as cleanup.

## Terminal callback identity correction

The actual legacy parallel-MI database exposed a callback hole: a completed reporting child is absent
from the live-child projection, so its corrupted durable element ID could bypass the live check and
advance the parent. The new actual-database control was red before the correction. All four callback
paths now validate exact handle, parent/node/iteration and semantic element/work identity before
invoking the library, including terminal history. Refusal keeps the original parent private checkpoint,
dispatches no new child and preserves the other accepted MI waits for repair.

Token provenance must be unique except for the runtime's validated supersession chain. Mutually
linked predecessor/successor records with the same parent/node/element/iteration remain history of
one semantic work token; unrelated duplicate claims are refused. The same validation is used before
legacy stopped-handle restoration. Superseded records are excluded from the adapter's live snapshot.
Synthetic positive/broken-lineage boundary controls supplement the actual corruption witness; they
do not claim complete operator-alteration qualification.
