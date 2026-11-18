# Data lifecycle

> **Proposed.** The kernel design awaits owner approval. This page answers design-now question 2 of the [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november): retention and archiving of finished instances, the right to erasure, and encryption of state at rest, decided before the journal format is fixed. It does not authorize product implementation.

The kernel keeps an instance's data in its checkpoint and in an append-only journal, and copies parts of it into inbox rows, events and projected history. This page covers:

- where payloads live and how long each copy lasts;
- how a finished instance is removed;
- how a person's data can be erased;
- how payloads are encrypted;
- which parts of the record format must be settled now, because they are expensive to change later.

The [storage model](storage-model.md) is the reference for the records themselves. Values by reference live in the same database in release 1 ([write volume](write-volume.md), decision 1a); this page gives their purge, erasure and costs, with the external store kept as a later adapter.

## Where data lives

| Copy | What it can contain | Removed today | Status |
|---|---|---|---|
| Instance `State` | Variables, inputs, results, every frame's private state | With the instance's delete | Built |
| `Journal.Entries` | At Full history, the model's entries, which can carry values | Positive-revision rows with the instance's delete, in the same commit. The delete's own marker row (revision -1) stays. Revision-0 rows (a dropped start, or a message to a missing target that was dropped) also stay | Built ([FINDINGS 20](../../prototypes/kernel-model/FINDINGS.md#20-deleting-an-ended-instance-run)) |
| Projected history (the viewer's tables; `Views` in the prototype) | Whatever the projector copies from the journal | By the projector when it passes the deletion marker, so later than the delete | Built for `Views` |
| `Messages.Payload` and `Start` | Command and event inputs, job arguments with their captured inputs, job results, start input | Consumed rows are deleted. A handled outside command with `keep_until` stays as a done marker until the sweep after its window; its payload and start spec stay with it | Built |
| `Events.Payload` | Published event payloads that were kept | Swept after `keep_until` | Built |
| `Subscriptions.Key`, `Events.Key`, `Messages.EventMatch` | Correlation values, for example an order number or an e-mail address used as a key. `EventMatch` encodes the subscription key | Subscriptions go with the instance's delete; events and matches go by sweep | Built |
| Ids | A caller-named instance id is the caller's value. Message ids derive from the sender's id. The effect key starts with the instance origin and leaves the system as `Idempotency-Key` and `Message-ID` ([EffectKey.cs](../../prototypes/kernel-model/src/Kernel.Workflows/EffectKey.cs)) | Deletion markers, revision-0 rows and retirement markers keep the instance id indefinitely | Built |
| Values by reference (decision 10) | Large values; the checkpoint and journal keep length and SHA-256 digest | Not at all in the witness: pruning, reference tracking and erasure are unqualified. Release 1: with the instance's delete, as value rows | Witness only ([VALUES.md](../../prototypes/kernel-model/models/Values/VALUES.md)), an external content-addressed directory; release 1's value rows are decided and not built |
| Retirement marker | A deleted instance keeps id, status, revision and its creation high-water mark, with blank model, definition and state. Built for generated ids and rows with a creation, so a generated id is never reused (walkthrough decision 7). Proposed: for every id kind, caller keys included, so a reused key always gets a higher creation (the qualified delete's invariant) | Never | Built ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs) `Retire`, EF and in-memory stores) |
| Database backups, WAL archives, replicas | Every row above, as it was | Outside the kernel | Not the kernel's |

Logs, traces and metrics are outside this table; question 6 (observability, PR #4710) covers them.

## Retention and deletion of finished instances

### Built

`kernel.delete` is a control message handled by the instance's owner. The prototype implements it as follows:

- **What it removes.** An ended instance is deleted at its revision, with its subscriptions and its positive journal rows, in one fenced commit (invariant 22, the `DeleteLeavesJournal` mutation).
- **What it leaves.** A deletion marker is journalled at the next position. The projector removes the view when it passes the marker, and tells a deleted journal row from a late one by the partition's position (F15).
- **Refusals.** A delete of a running instance is consumed and dropped. A model can refuse a delete while it protects retained receipts (`ProtectsDeletion` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs)). That check loads and deserializes the full state, so deleting an instance that is not in memory costs a load.
- **Measured.** On PostgreSQL 17.11 an isolated sample measured 4 observed TCP edges each for a raw delete and a retirement, with 512 and 656 WAL bytes respectively, and a retained marker tuple of 101 bytes ([WRITES.md](../../prototypes/kernel-model/WRITES.md#15-november-required-metadata-protocol-and-raw-delete-repair)). That is a bounded sample with no general constant.

There is no retention policy, no automatic removal and no archive.

### Proposed: a retention policy, enforced by delete

```csharp
public sealed record RetentionPolicy(
    TimeSpan? Finished, TimeSpan? Cancelled, TimeSpan? Faulted,   // null: keep until deleted by hand
    TimeSpan? Parked,                                              // parked work; null: until recovered or discarded
    RetentionAction Action = RetentionAction.Delete);              // Delete; PurgePayloads, ArchiveThenDelete later
```

- **`PurgePayloads`** carries over an idea from Elsa.Jobs (design sketch, "Standalone jobs"): remove the payload bytes and keep identities, digests, outcomes and attempts. With the envelope split of format item 1, a run's shape stays available for audit after its data is gone.
- **Parked work has its own window.** A parked instance is Faulted, but it awaits recovery, so the `Faulted` window does not apply to it while its Park is unresolved. The owner tells the two apart by the unresolved `ParkRecord` on the instance row ([observability](observability.md#what-park-keeps)); the retention worker sees the same through the parked-work read model. A Park's kept rows (marked `KeptBy` on their message rows) are excluded from the sweep (`Done AND KeepUntil < :now AND KeptBy IS NULL`) until the Park is resolved. Every resolution is a bounded drain of those rows: recover, discard, terminate, the delete of a parked instance, and the expiry of a finite `Parked` window through the `kernel.park-expiry` timer that Park's commit arms. A terminate of a parked instance ends it Cancelled; the terminate drain's completing commit writes its terminal mark, with the status Cancelled, and the `Cancelled` window runs from that mark. With the default null window there is no expiry, and the rows stay until an operator resolves the Park. **The window's expiry is an automatic discard.** The expiry drain resolves the Park and clears the kept rows (ordinary window, `KeptBy` cleared). It records the disposition "expired" and sends the parent its final Faulted notice. The instance stays Faulted. The drain's completing commit writes the terminal mark, and the `Faulted` window runs from that mark's time. After expiry, recover is refused in both modes, `retry` and `resume`. A discarded instance falls under `Faulted` from the mark that the discard drain's completing commit writes.
- **Where it is set.** On the definition, with a host default per tenant. A shortened policy also applies to instances that have already ended, with a guard: the API reports how many instances the change would delete and requires confirmation above a threshold, and the worker deletes at a bounded rate (decision 2).
- **How it runs (decision 1).** A retention worker in the host reads the instance-list read model for instances whose end time is past their policy, and adds one qualified delete per instance ([The qualified delete](#proposed-the-qualified-delete)). The worker uses read models and message adds only, so it is host-neutral layer-3 code. The owner decides, in its fenced commit; the worker only proposes.
- **Repeats.** The delete's id is derived from tenant, instance, creation and terminal mark, and it is an outside command with `keep_until`, so a repeat inside that window meets the duplicate id. After K3 a deleted run leaves a retired row, so a repeat after the window meets it and is refused as `RetiredCreation`: consumed, with no journal row. A stale selection that reaches a newer run is refused by the owner's identity checks below.
- **Refused deletes.** A delete the owner refuses (protected, not yet eligible, or a different run or transition) is consumed with no change to the row. A later sweep selects the instance again if it is still eligible, with its current identity.
- **The same path serves operators.** Delete one instance and delete by filter (definition, tenant, status, age) send the same qualified delete, with reason Operator. Terminate-then-delete for a running instance is one command, below.

### Proposed: the qualified delete

> **Counterexample and answer** ([Astra High gate 2 review, B1](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272)). The trace: the projection reports an expired finished run A under caller key K. A is removed. Run B reuses K and finishes, with its retention period still ahead. A delayed retention worker then submits the selected delete by K. The built handler checks only that the row has ended and is not protected, and deletes at the revision it has just loaded (`Control` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs)), so it deletes B and its history. A message id derived from A's end revision only deduplicates submissions, and B can end at the same revision. The same creation can also change eligibility: a parked run recovered and ended again, or a policy extended after the selection.
>
> The answer, step by step:
> 1. The selection names the run. The worker reads A's tenant, creation and terminal mark from the read model and puts them in the delete.
> 2. B is a new run under K, so it has a different creation. A delete that names A's creation reaches B's row, and the owner refuses it as a different run. B's end revision plays no part.
> 3. If A is recovered and ends again, its creation is the same but its terminal mark changes. A delete naming the old mark is refused as a different transition.
> 4. If the policy was extended, the owner computes eligibility from the policy in force when the delete arrives. A not-yet-eligible run is refused.
> 5. Only after all its checks pass does the owner delete, at the revision it loaded, in the same turn and commit. The single writer and the revision guard ensure nothing changes between the check and the delete.

Every delete names the run and the transition it means:

```csharp
// The payload of kernel.delete. Every field is required.
public sealed record DeleteSelection(
    string Tenant, string Instance,        // the target: tenant and canonical tenant-qualified actor id
    long Creation,                          // the run: immutable creation
    long TerminalRevision, long TerminalAt, // the terminal mark the selector saw: revision and time
    DeleteReason Reason);                  // Retention or Operator
```

- **The terminal mark.** The commit that ends or parks an instance records `(revision, time)` on the instance row. Discard, expiry and the terminate of a parked instance are drains, so for them the drain's completing commit writes the mark. Later commits to the ended row leave the mark unchanged: a late child result, a metadata turn, an archive request. A re-park of an already-parked row continues the same Park (observability, "What Park keeps"), so it keeps the first Park's mark. A recover's Open clears the Park's mark. A mark the model's end writes during the recover drain stays through its Complete, which does not clear it. A recover that completes with the model still running leaves no mark, and the run's next end, Park or discard writes one. A re-park that interrupts the drain, which happens only while the model runs, restores the Park's own mark, since it is the same Park ([observability](observability.md#every-park-operation-names-its-park)). The Open of a discard, expiry, terminate or delete writes no mark. So a terminate of a parked instance leaves the Park's mark on the row until its Complete writes the Cancelled mark, and a terminate that interrupts a running recover finds no mark, and the row has none until that Complete. A delete takes an open drain over only when check 3 passes against the row's current mark ([observability](observability.md#delete-during-a-drain)); while a row has no mark, no separate delete qualifies. During a terminate that interrupted a running recover, the operator deletes after Complete under the Cancelled mark; during a recover whose model still runs, the operator sends a terminate with `ThenDelete` or waits for the run's next end. Revisions restart for each creation, so the mark identifies a transition only together with the creation; the selection carries both and the owner compares the revision and the time. The lifecycle entries (format item 4) carry tenant, creation and the mark, so the instance-list read model has all three.
- **The owner's checks,** in the workflow actor's command turn, in this order. The owner refuses on the first that fails:
  1. the selection's tenant equals the row's tenant, and then its `Instance`, the canonical tenant-qualified actor id the selector read, equals the row's stored id, refused as "target" (proposed, fourth review R4-M5, the same order as a Park selection's checks in [observability](observability.md#every-park-operation-names-its-park)). A creation is unique per tenant and id, so two live instances of one tenant can share a creation and a terminal mark, and only the id tells a delete read for one from a delete for the other. For a caller key, `Instance` is the actor id its binding row holds (fourth review R4-M6, #4782). A terminate with `ThenDelete` carries the same `Instance` in its own payload, so its check 1 has both parts;
  2. the selection's creation equals the row's creation, and both are known;
  3. the selection's terminal revision and time equal the row's current terminal mark. A row with no current mark (a running instance, a recover drain whose model runs, or a terminate drain that interrupted one) fails this check, refused as "transition". The owner compares the row's mark only, never `ParkRecord`'s Park identity;
  4. the reason allows it. For Retention, the policy in force is read from the host's definition registry and tenant defaults (configuration the actor already holds to run turns; no read-model read), and its window for the row's status must have elapsed since the mark's time on the owner's clock. A run whose `ParkRecord` is unresolved is never eligible. An expired Park is resolved (an automatic discard), so the run becomes eligible under `Faulted`, from the mark the expiry drain wrote. Clock skew can move a deletion by at most the skew. Operator needs no policy;
  5. `ProtectsDeletion` does not protect the row, as built.

  A delete that reaches a row whose open drain already ends in a delete is refused as "in progress" after checks 1 to 3 pass and before check 4, so a delete for another tenant, instance, creation or mark gets that check's reason and learns nothing of the drain, and "in progress" answers before check 5 even for a protected row ([observability](observability.md#delete-during-a-drain), DT-5 and DT-6). A parked instance's delete whose row is protected by its Complete resolves the Park as a discard and keeps the row, Faulted under a new mark (observability, DT-4; a coordinator design choice in the [register](decisions.md#the-gate-2-fix-round-17-november)).
- **The invariant this rests on: a creation is unique per tenant and id, across deletes.** Today the retirement marker keeps the creation high-water mark only for a row with `Creation > 0` or a generated physical address (`Retire` and the retirement checks in [Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs)). Walkthrough decision 7's "a generated id never comes back" rests on that marker. The proposal keeps the high-water mark for every id kind, caller keys included: a caller key may start a new run after its window, always with a higher creation. Retirement markers are never compacted while a selection can be outstanding, so release 1 does not compact them at all. Any later compaction must first move the high-water mark somewhere that survives. The id namespace per tenant is the one the tenant contract (Astra High M2) defines.
- **The retirement marker keeps a bounded cancellation obligation** (proposed, fourth review R4-B2, [observability](observability.md#fourth-review-counterexamples-and-answers)). Existing prototype behaviour: the built delete retires the row without terminating its children (`Control` → `Delete` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs), lines 161–166), `CreationProtocol.Retire` keeps only the id, status, revision and creation ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs) 119–120), and the worker refuses every later delivery to the marker as `RetiredCreation` (`Creation.cs` 105, `Worker.cs` 1050–1055). The child registry holds every started child that has not finished, fire-and-forget ones included. So a parent-bound terminate committed while a delete drain was open, and delivered after its Complete, found no record of the live subtree. The proposal:
  - **What the marker keeps.** The row's recorded `Parent` and `ParentCreation`, and the obligation, in a layer-2-neutral form the actor type hands the creation protocol at retirement: each child its registry lists, as the child's `InstanceRef` and creation, read from the layer 3 `ChildRegistry`, and each in-flight job as the full key of its ticket at the registry's current `JobGeneration`, the generation the restarted ticket's id carries ([observability](observability.md#recover), "Job ticket generations"). `Retire` stores both on the marker, as they were at retirement. A root, with no recorded parent, keeps none of them, since no terminate can pass the parent binding at a row with no parent.
  - **The cascade.** A terminate that reaches the marker is refused with `RetiredCreation`, as built, by the worker's refusal path, before the actor type runs (`Worker.cs` 1050–1055). When it is parent-bound (issued origin, `TargetCreation` equal to the marker's creation, `Sender` and `SenderCreation` equal to the recorded `Parent` and `ParentCreation`), that refusal path composes the cascade from the marker's list in the same commit: a terminate to each listed child, with the marker's id and creation as `Sender` and `SenderCreation` and the child's creation as `TargetCreation`, under an issued id derived from the marker's origin and its next revision, as the built tree terminate derives its ids (`WorkflowInstance.cs` 196–197); and a cancel of each listed ticket, the current-generation one. The commit writes the marker at that next revision, its revision plus one, with the list cleared and every other field unchanged. So layer 3 stays host-neutral: it states the obligation once, at retirement, and runs no turn at a retired row. Any other sender gets the plain refusal, with no cascade. The marker sends the parent nothing, so the one final notice the parent already had stays the only one ([observability](observability.md#every-park-operation-names-its-park)).
  - **The durable obligation.** The retained list on the marker is what makes the cascade finish: each child then cascades through its own terminate, and a child that was itself deleted cascades from its own marker by the same rule.
  - **The bound, and when it clears.** The obligation is at most the children and job tickets the row's registries held at retirement, so the marker is never larger than the row it replaces, within the row cap. It clears in the commit of that one parent-bound cascade. Entries for children that ended after the retirement stay until then; their notices to the retired creation are refused and edit nothing, and a cascade terminate to an ended child with neither children nor jobs is consumed with no change (existing prototype behaviour, `WorkflowInstance.cs` 78–90). Retirement markers are never compacted in release 1 (above), so a marker whose parent never sends that terminate, because the parent finished or consumed the child's final notice first, keeps its obligation for the marker's life. Its cost is storage only: no worker reads it until a terminate arrives.
  - **No successor takes it over.** Only a parent-bound terminate can use the obligation, and a marker with a recorded parent belongs to a generated child id: a start to a retired generated id is a supplied creator and is consumed with no change (existing prototype behaviour, `Messages.cs` 52–54, `Turns.cs` 112, `Worker.cs` 1030–1037), and a keyed start, the only kind whose id comes back, has no parent (`KeyRequest.Validate`, `KeyAdmission.cs` 31–32). So no successor replaces a marker that retains an obligation, and a successor's terminate never reaches a predecessor's children. As a defence in depth (proposed), the creation protocol also refuses a new creation at an id whose retirement marker still holds a cancellation obligation, with reason `ObligationOutstanding` (beside the start checks of `StartError`, `Creation.cs` 89–101). In valid operation it never fires, for the reasons just given; it exists so that no path, a defect below ingress included, can strand the obligation under a successor. Once the cascade clears the obligation, the ordinary creation rules apply again.
  - **Delete semantics stay as they are.** Deleting an instance terminates none of its children; the marker only keeps what a parent's terminate, already sent or still to come, needs to end the subtree.
  - **The creation protocol** gains one write of a retired row: the cascade's, above. Every other write to a retired row, except a successor's creation while the marker holds no obligation, stays refused (`Validate`, `Creation.cs` 156–162).
  - **What the commit checks.** Today `Validate` requires a retirement to equal `Retire(previous)` exactly (`Creation.cs` 158), which no marker carrying a list can meet. So it checks instead: the marker equals `Retire(previous)` in every field but the list and the recorded parent, and the parent equals the previous row's `Parent` and `ParentCreation`; every listed child is an `InstanceRef` with a positive creation, drawn from the previous row's own child registry; every listed ticket is a ticket key of the marker's own instance and creation, at a `JobGeneration` the previous row's job registry recorded for that key; the list holds no more entries than the two registries did; and a root's marker lists nothing. A marker with any other content is refused as a definite error, as a composed terminate naming a foreign `Sender` is. The cascade's write is checked the same way: it equals the previous marker with the list cleared, at its revision plus one.
- **Refusing ambiguous selections.** A delete without these fields, naming creation 0, or reaching a row whose creation or terminal mark is unknown (its store does not persist them) is refused as ambiguous. A row that persists the mark and has none at the moment fails check 3 instead. The rule is the same for every id kind. There is no id-only path for deletion. The migration page's id-only fallback ([migration](migrating-running-instances.md#plans-and-bulk-migration)) does not extend to erasure.
- **Terminate, then delete** is one command, `kernel.terminate` with `ThenDelete`, naming tenant, instance and creation; the instance is compared right after the tenant, reason "target", on every path below. The owner terminates and, unless `ProtectsDeletion` protects the row, deletes in the same turn and commit. That commit's single journal row is the deletion marker. No separate delete message ever waits for a terminal mark it cannot name. If the run ended before the command arrives, the command takes the qualified-delete path with reason Operator: checks 1, 2 and 5, and the deletion if they pass. When the run ended during an open recover drain that a delete already owns, the command is refused as "in progress" after checks 1 and 2, before check 5, so the owning delete keeps its outcome ([observability](observability.md#delete-during-a-drain), DT-3). A parked run has not ended in this sense, since its Park is unresolved. On a parked run with no resolution open, or one whose recover drain is open with the model running, the terminate is a drain (with a discard, expiry, terminate or delete drain open, every terminate is refused as "resolution open" and its `ThenDelete` is not added; the tenant and creation checks run first, so a terminate that names another tenant or another creation is refused as tenant or run, never as "resolution open"; a terminate with authority over the run, an operator's whose selection names the Park or the parent's, still sends terminate to the run's children and cancels its jobs in the refusing commit, [observability](observability.md#every-park-operation-names-its-park)): its Open records `ThenDelete` (an operator's terminate of a parked run also names the Park), and its Complete ends the run Cancelled with its mark, sends the parent the one Cancelled notice and, unless `ProtectsDeletion` protects the row at that moment, deletes in the same commit, whose single journal row is the deletion marker ([observability](observability.md#delete-during-a-drain), DT-3). A protected run is terminated and kept. The command's answer goes to the reply address of the API request that sent it (an outside command carries one), and the instance-list read model shows the run terminated and kept.
- **What the store must hold.** The checks need the row's tenant, its creation and its terminal mark persisted on every provider.
  - The tenant column is already a release 1 store change (fairness).
  - Persisted creation headers are the same store change that migration needs ("Persisted creation headers on every provider" in the program plan's K3 list). For deletion it is a precondition, with no fallback.
  - The terminal mark is a new column pair.
  - All three are kernel behaviour, with two reviews when built, and they fit a MongoDB document as well as an EF row.
  - Today only the in-memory store persists creation; EF rows carry creation 0 ([storage model](storage-model.md#physical-records)), and no store has the terminal mark. Until K3 lands, every delete on EF is refused as ambiguous.
  - Release 1 starts from empty databases (owner, 17 November), and every new run gets a creation once K3 lands, so release 1 never meets an ambiguous row.
- **Observability.** A refusal adds no journal row, as for other no-op commands. It counts in a layer-3 metric, `elsa.kernel.delete.refused`, by reason: tenant, target, run, transition, not eligible, protected, ambiguous, and in progress (a delete reaching a drain that already ends in a delete).

- **What stays after a delete:**
  - the retirement marker, for every id kind, caller keys included (proposed; built for generated ids and rows with a creation), with the retained cancellation obligation above;
  - the journal's deletion marker;
  - done markers, until their window passes;
  - anything in backups.

### Values held by reference

Release 1 uses option a ([write volume](write-volume.md), decision 1a). Option b stays as a later value-store adapter, so the table keeps both.

| | **a. A values record in the same provider** (release 1) | **b. An external value store** (the built witness; a later adapter) |
|---|---|---|
| Naming | Keyed by instance and digest | The witness names objects by SHA-256 of their content alone (`DirectoryValues` in [ValueStore.cs](../../prototypes/kernel-model/models/Values/ValueStore.cs)), and two writers of one digest share an object. Erasure needs names scoped to the instance (instance origin plus digest), which gives up cross-instance deduplication and changes the witness |
| Purge | In the delete commit: the store's delete removes the instance's value rows as it removes its journal rows. This adds the value rows to the delete step, which is kernel behaviour (layer 2) | After the delete commit, by a layer-3 value sweeper: the projector's handling of the deletion marker feeds a "deleted instances" read model, and the sweeper deletes that instance's objects. An ended instance runs no model turns, so "a purge job, then delete" would need a new kernel path; the sweeper avoids it |
| Orphans | None: the row commits with its reference | A put whose commit never landed leaves an object; the sweeper removes instance prefixes that have no instance after a grace period |
| Erasure lag | None beyond the delete | Until the sweeper runs |
| Costs | One row write per value, in the fenced batch, plus its own WAL and TOAST once per value; one more row delete per value at delete time | Witness: by reference costs more engine calls than inline (13 against 5 to the first value-bearing commit; 20 against 12 for a fresh owner's read), plus one object put and one read. At delete time: one read-model row per deleted instance, then object deletes |

### Proposed: archiving, after release 1

An archive is written before the instance's delete and must not read the store directly, so layer 3 stays off positions:

1. An `ArchiveThenDelete` policy sends a `kernel.archive` command to the ended instance. An ended instance takes only commands, so this kind needs the command-kind seam proposed on the migration page (`IActorType.IsCommand`, PR #4706). That seam is kernel behaviour. The instance's command turn sends its final checkpoint, in a message to an archiver actor in layer 3, in one commit. That commit bumps the instance's revision with its state unchanged, as the built path for a late child result to an ended parent does, so each archive request derives its own message id. A repeat of the same command meets its own duplicate id within `keep_until`.
2. The archiver reads the run's history through the viewer's read API (projected rows plus journal tail, by instance), writes one self-describing document to an `IArchiveSink`, and then adds the `kernel.delete`.

The document carries the envelope (id, model, definition and version, state version, status, tenant, times), the final state, the history entries, and their codec headers. Its name derives from instance and revision, so a repeat overwrites it. An archive written under an instance key is erased by destroying that key.

### Proposed: compacting journal rows that are never deleted

Deletion markers and revision-0 rows stay indefinitely today. Below the projector's cursor no reader needs them: the projector reads only past its cursor, and the viewer reads the tail past the cursor.

- **The job.** A compaction job in the durable layer, beside the sweep, deletes non-positive rows at positions at or below their partition's projection cursor.
- **The invariant.** Invariant 6 reads "In a partition, the journal rows present plus the rows removed by deletes equal the position". It must be restated to count compacted rows with deleted ones.
- **Tier.** Compaction works on positions and cursors, so it is kernel behaviour.
- **What stays.** Positive rows of live instances stay: they are the source for rebuilding a projection.

This answers POSTGRES.md's open item on journal retention: retention is per instance, through the `(Partition, Instance)` index that delete already uses. No time-based journal segments are needed. Range partitioning by partition gives smaller vacuum units and leaves retention to the per-instance delete ([POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#journal)).

## The right to erasure

Erasing a person's data means finding every instance that holds it and removing or destroying every copy. Three mechanisms, which compose:

| Mechanism | What it erases | What it misses | Cost |
|---|---|---|---|
| **Delete** (built) | Checkpoint, positive journal rows, subscriptions; projected history after the projector passes the marker | Values under a later option b store until the sweeper runs; done-marker payloads until their window (format item 6 closes this); a parked instance's kept rows until its delete drain clears them (with a null `Parked` window, nothing else ends them); kept event payloads until their window; copies other instances received by message; caller values in ids (decision 4); backups and WAL | One delete commit per ended instance, batched, plus a load when it is not in memory; for a parked instance a bounded drain of its kept rows, then the delete |
| **Values by reference** (decision 10) | A value held only by reference is erased with its row (option a) or object (option b), even while the instance and its journal keep the reference | The reference's digest stays. A plain SHA-256 of a short value (an e-mail address) can be found by guessing, so the digest must be keyed (HMAC with a per-tenant key) for values that need erasure | See the values table above |
| **Per-instance data keys** (crypto-shredding) | Everything encrypted under the instance's key, in every copy: rows, projected history, archives, values, backups and replicas | Data written in clear before encryption was turned on; copies in other instances | See encryption below |

- **Finding the instances.** A person's data is spread over instances by application logic, and the kernel cannot search inside state (trade-off 10). An application that needs erasure tags instances with a subject key, published as an indexed field into the instance-list read model. Erasure then deletes by that filter.
- **Running instances.** The basic form is terminate, then delete, as one command (`ThenDelete`). Redacting one value inside a running instance is model-specific and comes later.
- **Ids and keys.** Generated instance ids, positions and the kernel's own message ids carry no payload values. Caller-named instance ids and correlation keys can, and through them so can the derived message ids, `EventMatch`, effect keys sent to other systems, and the markers that keep an instance id indefinitely. Release 1 documents a contract that caller keys and correlation keys carry no personal data, with a warning in Studio where keys are configured (decision 4). Keys declared sensitive are stored as keyed hashes (`h:` prefix, HMAC with a per-tenant key, computed at the API boundary on both the subscribe and the publish side) from 1.x, and the format reserves the prefix now (decision 5).

## Encryption of state at rest

Two layers, with different protection:

1. **Storage encryption.** PostgreSQL on encrypted volumes or a managed service, SQL Server TDE, MySQL InnoDB data-at-rest encryption, MongoDB's encrypted storage engine or encrypted volumes, SQLite through an encrypting build. It protects disks and backups from theft, costs the kernel nothing and is transparent. It does not protect payloads from database users or replicas, and it does not support erasure. Release 1 relies on it and documents it per provider.
2. **Payload encryption by the kernel** (later, opt-in per definition). A codec in the durable layer's store adapter encrypts the payload values of a row before they are written and decrypts them on load. That covers state, journal payloads, message payloads and start input, event payloads and value records. Models are unchanged: they see plain state.

```csharp
// Layer 2, under the persistence abstraction. One ring per host; keys live outside the kernel's database.
public interface IInstanceKeyRing
{
    ValueTask<IReadOnlyList<DataKey>> GetOrCreateAsync(IReadOnlyList<InstanceCreation> instances, CancellationToken ct); // batched; idempotent
    ValueTask<DataKey?> GetAsync(string keyId, CancellationToken ct);                                                 // null once destroyed
    ValueTask DestroyAsync(InstanceCreation instance, CancellationToken ct);
}
```

- **The commit principle.** "Replace whole layers and edges, never pieces of the commit." The codec is a property of the whole store edge. It applies to every value the provider writes and reads, adds no statement, and changes no statement's order or the transaction's atomicity. Key-ring calls happen while a flush is prepared, before its transaction opens, never inside it. It is still a layer-2 change, and kernel behaviour.
- **Which key.**
  - **Instance data:** state, journal payloads, projected history, values and archives use the key of the instance's id and creation. Keying by creation matters: a caller key reused after its `keep_until` window starts a new instance with a new key, so destroying the old one does not touch the new run.
  - **Messages:** a message whose sender knows the target's creation (a generated child address, or a message carrying `TargetCreation`) is encrypted with the target's key; the ring is batched and idempotent, so this works before the child exists. Any other message, including a caller-key command, which resolves to a run only when it is accepted, is encrypted with a per-tenant inbox key. A sender cannot read another instance's creation. On consumption the payload becomes part of the receiver's state under the receiver's key.
  - **Events:** a published event reaches many receivers, so its payload, and the event-start deliveries that copy it, use a per-tenant event key rotated by keep window. On consumption the payload becomes part of the receiver's state, under the receiver's key.
- **Where keys live.** In a key ring outside the kernel's database, for example a KMS that wraps per-instance data keys under a key-encryption key, or a separate key table with its own short backup retention. A wrapped key kept in the kernel's own database would come back with every restored backup, and crypto-shredding would fail.
- **Order.** The key is created before the first commit that writes ciphertext. An orphan key from a commit that never landed is harmless and swept. Ephemeral instances that never commit need no key.
- **The projector needs no key.** It expands entries from their clear envelopes and copies payloads as they are. The API decrypts one run's payloads when the viewer reads it.
- **After erasure** the viewer still shows the run's shape (which elements ran, when, with which outcome) with payloads marked erased.
- **Encryption makes states incompressible.** Ciphertext does not compress, and text columns add a third in base64. PostgreSQL's TOAST compression then no longer helps: every encrypted state above about 2 kB pays the incompressible cost that [POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#decision-10-values-by-reference-a-release-1-priority) measured, about 4.5 to 4.8 kB more WAL per commit for a 4 kB state. The codec therefore compresses before it encrypts. Values by reference keep large values out of the state.

Costs, all unmeasured:

- **Key ring:** one batched call per flush that creates instances or sends to targets whose keys this node does not hold, so a fan-out to 1,000 children costs one call. A node can also pre-create keys. Plus one key-ring read per cold load, batched with loads and held while the instance is in memory.
- **CPU:** AES-GCM per payload.
- **Bytes:** about 40 per payload, for nonce, tag and header.

## Where it lives

| Piece | Layer | Depends on |
|---|---|---|
| The qualified delete, its checks and refusal rules | 3, the workflow actor's command turn | The turn contract and the persisted tenant, creation and terminal mark; layer 2's commit removes the rows |
| Tenant, creation and terminal mark persisted on the instance row | 2, the store (kernel behaviour) | Every provider |
| Retention worker, delete by filter, subject tags, a later option b value sweeper | 3, host services | Read models, message adds and the value store only |
| Archive command and archiver | 3, with the command-kind seam in layer 2 (kernel behaviour) | The viewer's read API and `IArchiveSink`; nothing in the commit |
| Value rows removed with their instance (option a) | 2, the store's delete | Kernel behaviour |
| Compaction of non-positive journal rows | 2, maintenance beside the sweep | Positions and projection cursors; kernel behaviour |
| Payload codec and `IInstanceKeyRing` | 2, the store edge, under the persistence abstraction | Row values only: one codec serves EF Core and MongoDB; kernel behaviour |
| Journal envelope, codec headers, the `h:` key form | The record format shared by layers 2 and 3 | Nothing provider-specific |

## What must be fixed before the journal format is final

These cost little now and require rewriting stored history later:

1. **Journal entries split into a clear envelope and an opaque payload.** The envelope has generic fields that layer 2 never interprets: an element id, a parent element id, kind, time and outcome, all opaque strings the model fills in. The projector expands entries from the envelope and the viewer shows it; the payload is the model's. Encryption, by-reference values and erasure then act on payloads only, and a run's shape survives erasure. The design sketch's history section already gives entries "a common envelope (node, parent node, kind, time)". Walkthrough decision 12 records the journal as model-agnostic with opaque entries; decision 6 adopts this envelope beside it, and layer 2 still interprets nothing.
2. **Every payload carries a codec header:** plain, by reference, or encrypted with a key id. Rows written before a codec is switched on read as plain, so encryption and references can be enabled per definition without rewriting old rows. The same header goes on state, message payloads, start input, event payloads, value records and projected history payloads.
3. **The rule for values in ids and keys** (decisions 4 and 5), with the `h:` keyed-hash form reserved for caller keys and correlation keys from the start.
4. **Lifecycle transitions are journalled at every history level** (created, ended, migrated, deleted) as envelope-only entries carrying tenant, creation and, for an end, Park or discard, the terminal mark. The instance-list read model then sees every instance's end and the identity a delete must name, which the retention worker needs. Today a turn at State history writes `[]` and at Minimal writes no row; only deletes and dropped or missing-target drops are journalled at every level. So this changes both State and Minimal (decision 1).
   - **One rule with Park's journalling.** Park, recover and discard are journalled at every history level for the parked-work read model ([observability](observability.md), decision 2a). That is the same mechanism with more transitions, and the two land as one rule (decision 1).
5. **Non-positive journal rows are compactable below the projector's cursor,** with invariant 6 restated.
6. **Done markers keep only the id and window.** The consuming commit already updates the row to mark it done (`UPDATE "Messages" SET "Done"` in the owned commit). Clearing `Payload` and `Start` in the same update leaves no copy of a handled command's input, and duplicate detection needs only the id. This changes the commit's consume step (step 4), so it is kernel behaviour.
   - **Park's kept deliveries are excepted.** Park keeps its consumed deliveries and its arrivals as done rows marked `KeptBy`, which the sweep skips, so a retry can deliver them again ([observability](observability.md#what-park-keeps), decision 2a). The rows a Park keeps retain their payloads until the drain that resolves the Park clears them: recover, discard, the delete of the parked instance, or the expiry of a finite `Parked` window. After an expiry, recover is refused in both modes. The clearing applies to ordinary consumption only. A drain's page that clears a kept timer leaves an ordinary done marker, which may be payload-free; a later re-arm of that timer writes its kind, payload and original due from the model's restore plan, never from the marker ([observability](observability.md#recover)).

## What release 1 ships

"Retention and delete" in a basic form ([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)):

- delete of one ended instance, delete by filter, and terminate-then-delete, through the runtime API, as qualified deletes that the owner checks against tenant, creation, terminal mark and policy;
- retention policies per definition with a host default, by end status, enforced by the retention worker (decision 1), with the guard for a shortened policy (decision 2);
- removal of by-reference value rows with their instance, in the delete commit (option a): a delete must not leave them behind;
- the format items 1 to 4 and 6; for item 5, only the rule that no reader needs non-positive rows below the cursor (the compaction job comes after release 1, decision 8);
- storage encryption documented per provider.

Later: archiving; payload encryption and per-instance keys; redaction inside running instances; compaction of non-positive journal rows; history caps for long-lived instances.

## Costs

| Item | Cost | Status |
|---|---|---|
| Delete of one ended instance | One delete commit, batched with the partition's turns; for a parked instance, a bounded drain of its kept rows first (observability, "Resolving a Park"). The outside delete message adds one insert and one mark-before-run commit, and a load when the instance is not in memory (`ProtectsDeletion` reads the state). PostgreSQL sample: 4 observed TCP edges, and 512 WAL bytes per raw delete or 656 per retirement | Built; bounded sample measured |
| The qualified delete's checks | In memory, in the turn that already loads the row: no extra database call. The ending, Park and discard commits write the two terminal-mark columns; the lifecycle entry carries the identity | Proposed |
| Retained after delete | One retirement marker per deleted instance, for every id kind, caller keys included (proposed; built for generated ids and rows with a creation; 101 bytes in the sample), one deletion marker journal row per instance, done markers until their window | Built |
| Retention worker (decision 1, chosen) | One read-model page read per sweep page and one outside message per expired instance, with its mark-before-run commit | Proposed |
| Retention timer (decision 1, rejected) | One inbox row per ended instance for its whole retention period, inserted in the ending commit. When it fires weeks later, a takeover in between is likely, so it usually pays a mark-before-run commit as well | Not adopted |
| Values by reference | Option a, in the values table above | Witness measured for option b; option a not built |
| Lifecycle entries at State and Minimal | About one extra journal row per instance lifetime (start and end often share a commit). For scale: under the stress test's chaos the journal accounted for about 0.29 kB of WAL per landed instance commit, a whole-workload share and no row size | Proposed; the share is from [WRITES.md](../../prototypes/kernel-model/WRITES.md) |
| Payload encryption | Batched key-ring calls, CPU, about 40 bytes per payload, loss of compression unless the codec compresses first | Proposed, unmeasured |
| Dead rows | Deleting journal rows leaves dead tuples for vacuum (peaks of 769 to 1,059 in the stress run, from aborted inserts and deletes) | Measured in WRITES.md |

## Acceptance cases

For [B1](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272). Milestones are those of the [program plan](../program-goals/clean-slate-kernel.md).

- **K3:** each case runs on the prototype's in-memory store and in the simulation. The simulation gains the invariant "a delete removes only the run and terminal transition it names", and a mutation `DeleteIgnoresSelection` (today's behaviour: delete whatever ended row the id loads) must fail it.
- **K4:** the same cases run on every provider.
- **K9 and K11:** cover the read model and the worker.
- **Deletes during a drain:** the observability page's DT-1 to DT-6 ([acceptance cases](observability.md#acceptance-cases)) cover a delete that takes a terminate drain over, the refusal while a terminate that interrupted a running recover has no mark, `ThenDelete` on a drain, protection that begins before a parked instance's delete completes, and the refusal order, at K3, K4 and K11. DT-7 to DT-9 cover the retirement marker's cancellation obligation (fourth review R4-B2) at K3, K4 and K6, and RM-1 below its shape and bound.

| Case | Order of events | Expected | Milestone |
|---|---|---|---|
| RD1 Lagging projection, key reuse | Select A (key K, creation cA, mark r). Delete A by hand. Start B under K after the window. B ends at revision r. Deliver the delayed retention delete | Refused as a different run; B's row, journal, subscriptions and values are intact; the refusal metric counts "run" | K3, K4 |
| RD2 Equal end revisions | As RD1, with A and B both ending at the same revision and the same status | Refused; the decision does not depend on revision equality | K3, K4 |
| RD3 Policy extended before execution | Select A under a 30-day window. Change the policy to 90 days. Deliver the delete on day 31 | Refused as not eligible; a sweep after day 90 selects and deletes A | K3, K11 |
| RD4 Recovery after selection | Park A (mark r1). An operator selects A for deletion (reason Operator, naming r1). Before the delete arrives, another operator recovers A, which then ends at r2. Deliver the old delete | Refused as a different transition; a later sweep deletes under r2's window | K3, K11 |
| RD4b Expiry is a discard | Park A with a finite `Parked` window and let it expire; then send `kernel.recover` in each mode; then let the `Faulted` window elapse | The expiry drain clears the kept rows, records "expired", sends the parent its final Faulted notice and writes mark r2; both recovers are refused; the retention worker then selects A under r2 and the delete proceeds | K3, K11 |
| RD5 Tenant mismatch | Two tenants; a delete naming tenant T1 reaches T2's row with the same id | Refused as tenant | K3, K4 |
| RD5b Distinct targets of one tenant (fourth review R4-M5) | Tenant t; A under caller key K1 and B under K2, both live and ended, each on creation 1, with equal terminal marks (revision and time pinned; the test asserts it). A delete, once Retention and once Operator, is read for A from the instance-list read model and sent with B as its target; a control run sends it to A | At B the delete is refused as "target" before the creation and mark checks; B's row, journal, subscriptions and values are intact, and the refusal metric counts "target"; at A the delete proceeds. The instance-list read model shows the instance id beside tenant, creation and mark (K9), and the retention worker and the operator API pass it through unchanged (K11). Mutation: no actor check; the delete read for A deletes B | K3, K9, K11 |
| RD6 Ambiguous selection | A delete without creation or mark; a delete of a caller-keyed row whose creation is unknown (EF before K3) | Refused as ambiguous; nothing deleted | K3, K4 |
| RD7 Generated id | A generated-id instance with matching creation and mark; then the same selection again after its window | Deleted, leaving its retirement marker; the repeat meets the retired row, is refused as `RetiredCreation` and writes no journal row | K3, K4 |
| RD7b Reuse after marker loss | Delete run A under caller key K. Remove A's retirement marker (a mutation, `RetireWithoutHighWater`, or a compaction). Start B under K and end it at A's revision and a later time. Deliver A's delayed delete | With the invariant, B's creation is higher than A's and the delete is refused as a different run. The mutation run must fail the simulation's creation-uniqueness check | K3, K4 |
| RD8 Terminate then delete | `kernel.terminate` with `ThenDelete` on a running instance; once unprotected, once with `ProtectsDeletion` true | Terminated and deleted in one commit, whose only journal row is the deletion marker; when protected, terminated and kept, with the answer on the API request's reply address and the run shown terminated in the read model | K11 |
| RD8b Run ended before `ThenDelete` | The run finishes; then `kernel.terminate` with `ThenDelete` naming its tenant and creation arrives | Deleted through checks 1, 2 and 5 with reason Operator; a `ThenDelete` naming another creation is refused as a different run | K11 |
| RD8d `ThenDelete` names its target (fourth review R4-M5) | Tenant t; A under caller key K1 and B under K2, both ended on creation 1 with equal terminal marks. An operator's `kernel.terminate` with `ThenDelete`, carrying A's tenant, instance and creation, is sent with B as its target; a control run sends it to A | At B it is refused as "target" after the tenant check and before checks 2 and 5; B's row, journal, subscriptions and values are intact; at A it deletes through the ended-run path. Mutation: no instance check on `ThenDelete`; the terminate passes checks 1, 2 and 5 at B and B is deleted | K3, K11 |
| RD8c Re-park keeps the mark | Park A (mark r1, t1); a re-park follows from a definite commit error; a retention delete names (r1, t1) | The mark is still (r1, t1); a retention delete is refused (an unresolved Park is never eligible); an operator delete naming (r1, t1) opens the delete drain and deletes when it completes | K3, K11 |
| RD9 Shortened policy guard | Shorten a policy over N ended instances | The API reports N and requires confirmation above the threshold; deletes proceed at the bounded rate, each still checked by the owner | K11 |
| RD10 Read model carries identity | End, Park, recover and discard at every history level | The instance-list read model shows tenant, creation and the current terminal mark after each | K9 |
| RM-1 Retirement marker obligation (fourth review R4-B2) | Delete child A (recorded parent P, registry {G1, G2}, job keys {J}) through an unprotected delete drain; separately delete a root R with a live child; then deliver P's parent-bound terminate, and in another run a non-parent's terminate, to A's marker | A's marker carries the creation high-water mark, P, P's creation, the children (G1 and G2, each with its creation) and J's ticket key at its current generation, and nothing else of the row; R's marker retains no registry or job keys; neither marker is larger than the row it replaced. The non-parent's terminate is refused as `RetiredCreation` and the marker is unchanged. P's terminate is refused as `RetiredCreation`, and its commit, composed by the worker's refusal path from the marker's list, sends G1 and G2 a terminate from A whose ids carry the marker's next revision, cancels J's current-generation ticket and writes the marker at that revision (its revision plus one) with the list cleared and every other field unchanged; a further write to the marker other than a successor's creation is refused as a definite error; P receives nothing. A retirement whose list names a child outside A's registry, or a ticket of another instance or creation, is refused as a definite error, and A stays unretired. Mutation: `Retire` as built (the marker keeps nothing); G1 and G2 survive P's terminate, and observability's DT-7 fails. Mutation: `Validate` admits a foreign ticket in the list; the cascade cancels another instance's job | K3, K4 |

## Unverified

- **Not built:** the qualified delete and its checks, the terminal mark, persisted creation on EF and MongoDB, the retention worker, the option a value rows, the later option b sweeper and its instance-scoped names, the archive command and archiver, compaction, keyed hashes, codec headers, the key ring and encryption.
- **Done markers:** clearing their payloads assumes no reader uses them. The event-start acceptance receipt and the outside-command duplicate answer must be checked before it is built.
- **Value store:** its crash durability, pruning and erasure are unqualified (VALUES.md). Decision 10's acceptance run, the `pad=` case against a reference on PostgreSQL with option a, is unrun (POSTGRES.md; write volume decision 2).
- **Estimates:** all encryption costs.

## Decisions (owner, 16–17 November 2026)

The owner took these on 17 November; the [decision register](decisions.md#the-seven-design-now-answers-1617-november) lists them with the rest of
the kernel's. Values by reference live in the same database ([write volume](write-volume.md), decision 1a). Rejected
options are kept for the record.

1. **Retention runs as a retention worker over the instance-list read model (B),** with lifecycle entries at every
   history level (format item 4), landed as one rule with Park's journalling. State and Minimal write a little more.
   Rejected: (A) a retention timer per ended instance.
2. **A shortened policy applies to ended instances, with a guard (C):** a count, confirmation above a threshold, and
   deletes at a bounded rate. Rejected: (A) apply without a guard; (B) apply only to instances that end later.
3. **Erasure: delete now, keys in 1.x (B).** Release 1 has delete plus values by reference, and the format items are
   fixed now; per-instance data keys come in 1.x as an opt-in per definition, kept outside the kernel's database.
   Rejected: (A) delete and values by reference only, with no keys later; (C) keys per tenant or per data subject.
4. **Caller keys and correlation keys carry no personal data (A):** a documented contract with a Studio warning.
   Rejected for release 1: (B) keyed hashes for keys declared sensitive, which comes with decision 5's key ring.
5. **Release 1 reserves the `h:` form only (A);** keyed hashes and their key management come in 1.x. Rejected: (B)
   keyed hashes with a per-tenant HMAC key in release 1.
6. **The journal has a clear envelope (A),** adopted beside walkthrough decision 12: generic opaque fields the model
   fills in. Rejected: (B) fully opaque entries.
7. **Done markers keep no payloads (format item 6),** with Park's kept deliveries excepted, after checking the
   readers. Kernel behaviour, two-review tier when built.
8. **Non-positive journal rows below the projector's cursor are compacted after release 1 (format item 5),** with the
   restated invariant 6 checked in the simulation. Kernel behaviour.
9. **Every delete is qualified (A),** answered by the owner on 17 November, by card. The runtime API resolves an id to the current run through the read model and sends the qualified delete; there is no "delete whatever run this id has now". Rejected: (B) an operator-only `Force` without creation or mark.
