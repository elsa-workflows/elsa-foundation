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
| Retirement marker | A deleted generated-id instance keeps id, status and revision, with blank model, definition and state, so the id is never reused (walkthrough decision 7) | Never | Built ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs) `Retire`, EF and in-memory stores) |
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
- **Parked work has its own window.** A parked instance is Faulted, but it awaits recovery, so the `Faulted` window never applies to it. The retention worker tells the two apart through the parked-work read model of the observability page (PR #4710). `Parked` (by default null: kept until an operator recovers or discards it) is also the window that Park's kept deliveries use ([observability](observability.md#what-park-keeps), decision 2a). Their `keep_until` is the Park's time plus `Parked`, or no expiry when it is null. The sweep removes only `Done` rows past `keep_until`, and the built delete removes no done markers, so a later step must end them: recover (in either mode) and delete set the kept deliveries' `keep_until` to the ordinary outside-command window from that moment, or remove them in the same commit. Until that is built, they stay, and the Delete row of the erasure table lists them. A recovered instance runs again; a discarded one ends and falls under `Faulted` from the discard.
- **Where it is set.** On the definition, with a host default per tenant. A shortened policy also applies to instances that have already ended, with a guard: the API reports how many instances the change would delete and requires confirmation above a threshold, and the worker deletes at a bounded rate (decision 2).
- **How it runs (decision 1).** A retention worker in the host reads the instance-list read model for instances whose end time is past their policy, and adds a `kernel.delete` per instance. The worker uses read models and message adds only, so it is host-neutral layer-3 code. Every delete is the built delete, in the owner's fenced commit.
- **Repeats.** The delete's id is derived from instance and end revision, and it is an outside command with `keep_until`. A repeat inside that window meets the duplicate id. After the window, read-model lag can make a later sweep add a delete for an instance that is already gone. That delete is dropped and journalled as a revision-0 row, which compaction (format item 5) removes. So the worker sets `keep_until` above its sweep interval plus the projector's expected lag.
- **Refused deletes.** A delete refused by `ProtectsDeletion` is consumed with no change. A later sweep issues it again once its window has passed.
- **The same path serves operators.** Delete one instance, delete by filter (definition, tenant, status, age), and terminate-then-delete for a running instance (two messages from one sender, applied in order).
- **What stays after a delete:**
  - the retirement marker, for generated ids;
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
| **Delete** (built) | Checkpoint, positive journal rows, subscriptions; projected history after the projector passes the marker | Values under a later option b store until the sweeper runs; done-marker payloads until their window (format item 6 closes this); a parked instance's kept deliveries, which with a null `Parked` window never expire until recover and delete end them; kept event payloads until their window; copies other instances received by message; caller values in ids (decision 4); backups and WAL | One delete commit per instance, batched, plus a load when it is not in memory |
| **Values by reference** (decision 10) | A value held only by reference is erased with its row (option a) or object (option b), even while the instance and its journal keep the reference | The reference's digest stays. A plain SHA-256 of a short value (an e-mail address) can be found by guessing, so the digest must be keyed (HMAC with a per-tenant key) for values that need erasure | See the values table above |
| **Per-instance data keys** (crypto-shredding) | Everything encrypted under the instance's key, in every copy: rows, projected history, archives, values, backups and replicas | Data written in clear before encryption was turned on; copies in other instances | See encryption below |

- **Finding the instances.** A person's data is spread over instances by application logic, and the kernel cannot search inside state (trade-off 10). An application that needs erasure tags instances with a subject key, published as an indexed field into the instance-list read model. Erasure then deletes by that filter.
- **Running instances.** The basic form is terminate, then delete. Redacting one value inside a running instance is model-specific and comes later.
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
| Delete and its refusal rules | 3, the workflow actor's command turn | The turn contract; layer 2's commit removes the rows |
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
4. **Lifecycle transitions are journalled at every history level** (created, ended, migrated, deleted) as envelope-only entries. The instance-list read model then sees every instance's end, which the retention worker needs. Today a turn at State history writes `[]` and at Minimal writes no row; only deletes and dropped or missing-target drops are journalled at every level. So this changes both State and Minimal (decision 1).
   - **One rule with Park's journalling.** Park, recover and discard are journalled at every history level for the parked-work read model ([observability](observability.md), decision 2a). That is the same mechanism with more transitions, and the two land as one rule (decision 1).
5. **Non-positive journal rows are compactable below the projector's cursor,** with invariant 6 restated.
6. **Done markers keep only the id and window.** The consuming commit already updates the row to mark it done (`UPDATE "Messages" SET "Done"` in the owned commit). Clearing `Payload` and `Start` in the same update leaves no copy of a handled command's input, and duplicate detection needs only the id. This changes the commit's consume step (step 4), so it is kernel behaviour.
   - **Park's kept deliveries are excepted.** Park keeps its consumed deliveries as done rows with `keep_until` (the `Parked` window) so a retry can deliver them again ([observability](observability.md), decision 2a). The rows a Park keeps retain their payloads until the instance is recovered or discarded. The clearing applies to ordinary consumption only.

## What release 1 ships

"Retention and delete" in a basic form ([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)):

- delete of one ended instance, delete by filter, and terminate-then-delete, through the runtime API, on the built delete path;
- retention policies per definition with a host default, by end status, enforced by the retention worker (decision 1), with the guard for a shortened policy (decision 2);
- removal of by-reference value rows with their instance, in the delete commit (option a): a delete must not leave them behind;
- the format items 1 to 4 and 6; for item 5, only the rule that no reader needs non-positive rows below the cursor (the compaction job comes after release 1, decision 8);
- storage encryption documented per provider.

Later: archiving; payload encryption and per-instance keys; redaction inside running instances; compaction of non-positive journal rows and of retirement markers; history caps for long-lived instances.

## Costs

| Item | Cost | Status |
|---|---|---|
| Delete of one ended instance | One delete commit, batched with the partition's turns. The outside delete message adds one insert and one mark-before-run commit, and a load when the instance is not in memory (`ProtectsDeletion` reads the state). PostgreSQL sample: 4 observed TCP edges, and 512 WAL bytes per raw delete or 656 per retirement | Built; bounded sample measured |
| Retained after delete | One retirement marker per generated id (101 bytes in the sample), one deletion marker journal row per instance, done markers until their window | Built |
| Retention worker (decision 1, chosen) | One read-model page read per sweep page and one outside message per expired instance, with its mark-before-run commit | Proposed |
| Retention timer (decision 1, rejected) | One inbox row per ended instance for its whole retention period, inserted in the ending commit. When it fires weeks later, a takeover in between is likely, so it usually pays a mark-before-run commit as well | Not adopted |
| Values by reference | Option a, in the values table above | Witness measured for option b; option a not built |
| Lifecycle entries at State and Minimal | About one extra journal row per instance lifetime (start and end often share a commit). For scale: under the stress test's chaos the journal accounted for about 0.29 kB of WAL per landed instance commit, a whole-workload share and no row size | Proposed; the share is from [WRITES.md](../../prototypes/kernel-model/WRITES.md) |
| Payload encryption | Batched key-ring calls, CPU, about 40 bytes per payload, loss of compression unless the codec compresses first | Proposed, unmeasured |
| Dead rows | Deleting journal rows leaves dead tuples for vacuum (peaks of 769 to 1,059 in the stress run, from aborted inserts and deletes) | Measured in WRITES.md |

## Unverified

- **Not built:** the retention worker, the option a value rows, the later option b sweeper and its instance-scoped names, the archive command and archiver, compaction, keyed hashes, codec headers, the key ring and encryption.
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
