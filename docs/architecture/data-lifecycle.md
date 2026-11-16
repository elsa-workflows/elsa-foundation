# Data lifecycle

> **Proposed.** The kernel design awaits owner approval. This page answers design-now question 2 of the [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november): retention and archiving of finished instances, the right to erasure, and encryption of state at rest, decided before the journal format is fixed. It does not authorize product implementation.

The kernel keeps an instance's data in its checkpoint and in an append-only journal, and copies parts of it into inbox rows, events and projected history. This page says where payloads live, how long each copy lasts, how a finished instance is removed, how a person's data can be erased, how payloads are encrypted, and which parts of the record format must be settled now because they are expensive to change later. The [storage model](storage-model.md) is the reference for the records themselves.

## Where payloads live

| Copy | What it can contain | Removed today | Status |
|---|---|---|---|
| Instance `State` | Variables, inputs, results, every frame's private state | With the instance's delete | Built |
| `Journal.Entries` | At Full history, the model's entries, which can carry values | Positive-revision rows with the instance's delete, in the same commit; the delete's own marker row (revision -1) and revision-0 rows stay | Built ([FINDINGS 20](../../prototypes/kernel-model/FINDINGS.md#20-deleting-an-ended-instance-run)) |
| Projected history (the viewer's tables; `Views` in the prototype) | Whatever the projector copies from the journal | By the projector when it passes the deletion marker, so later than the delete | Built for `Views` |
| `Messages.Payload` and `Start` | Command and event inputs, job arguments with their captured inputs, job results | Consumed rows are deleted. A handled outside command with `keep_until` stays as a done marker, payload included, until the sweep after its window | Built |
| `Events.Payload` | Published event payloads that were kept | Swept after `keep_until` | Built |
| `Subscriptions.Key`, `Events.Key` | Correlation values, for example an order number or an e-mail address used as a key | Subscriptions with the instance's delete; events by sweep | Built |
| Value store (values by reference, decision 10) | Large values; the checkpoint and journal keep length and SHA-256 digest | Not at all: pruning, reference tracking and erasure are unqualified | Witness only ([VALUES.md](../../prototypes/kernel-model/models/Values/VALUES.md)) |
| Retirement marker | A deleted generated-id instance keeps id, status and revision, with blank model, definition and state, so the id is never reused (walkthrough decision 7) | Never | Built ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs) `Retire`, EF and in-memory stores) |
| Database backups, WAL archives, replicas | Every row above, as it was | Outside the kernel | Not the kernel's |

Logs, traces and metrics are outside this table; question 6 (observability) keeps payloads out of them.

## Retention and deletion of finished instances

### Built

`kernel.delete` is handled by the instance's owner in a turn. An ended instance is deleted at its revision, with its subscriptions and its positive journal rows, and a deletion marker is journalled at the next position; the projector removes the view when it passes the marker. A delete of a running instance is consumed and dropped. A model can refuse a delete while it protects retained receipts (`ProtectsDeletion` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs)). Everything happens in one fenced commit (invariant 22, `DeleteLeavesJournal` mutation). The projector tells a deleted journal row from a late one by the partition's position (F15). On PostgreSQL 17.11 an isolated raw-delete and retirement sample measured 4 observed TCP edges each and 512 and 656 WAL bytes respectively, with a retained marker tuple of 101 bytes ([WRITES.md](../../prototypes/kernel-model/WRITES.md#15-november-required-metadata-protocol-and-raw-delete-repair); a bounded sample with no general constant). There is no retention policy, no automatic removal and no archive.

### Proposed: a retention policy, enforced by delete

```csharp
public sealed record RetentionPolicy(
    TimeSpan? Finished, TimeSpan? Cancelled, TimeSpan? Faulted,   // null: keep until deleted by hand
    RetentionAction Action = RetentionAction.Delete);              // Delete; PurgePayloads, ArchiveThenDelete later
```

`PurgePayloads` is the idea carried over from Elsa.Jobs (design sketch, "Standalone jobs"): remove payload bytes and keep identities, digests, outcomes and attempts. With the envelope split of format item 1 it keeps a run's shape for audit after its data is gone.

- **Where it is set.** On the definition, with a host default per tenant. The policy in force when the sweep runs applies, so shortening it applies to instances that already ended.
- **How it runs.** A retention worker in the host reads the instance-list read model for instances whose end time is past their policy, and adds a `kernel.delete` per instance with an id derived from instance and end revision, so a repeated sweep adds nothing twice. Every delete is the built delete, in the owner's fenced commit; the worker never writes owner rows. The worker uses read models only, so it is host-neutral layer-3 code.
- **The same path serves operators.** Delete one instance, delete by filter (definition, tenant, status, age), and terminate-then-delete for a running instance (two messages from one sender, applied in order).
- **Value-store objects.** The value store names objects under their instance's id. An instance that holds references first runs a purge job that deletes everything under that name, which also removes orphans of puts that never committed. The job's result triggers the delete commit. The job may run more than once; deleting an object twice is harmless.
- **What stays after a delete:** the retirement marker for generated ids, the journal's deletion marker, done markers until their window, and anything in backups.

### Proposed: archiving, after release 1

Archiving is a job of the instance, run before its delete. The job reads the instance's committed final row and its journal by key, writes one self-describing document to an archive sink under a name derived from instance and revision (so a repeat overwrites the same object), and returns the document's digest; the next turn deletes the instance. The document carries the envelope (id, model, definition and version, state version, status, tenant, times), the final state, the journal entries in position order, and the codec headers described below. Archives inherit the erasure rules: an archive written under an instance key is erased by destroying that key. The archive sink is an `IArchiveSink` beside the store, never part of the commit.

### Proposed: journal rows that are never deleted

Deletion markers and revision-0 rows stay for ever today, one per deleted instance or dropped start. Below the projector's cursor no reader needs them: the projector reads only past its cursor, and the viewer reads the tail past the cursor. A compaction job can delete non-positive rows at positions at or below the cursor of their partition. Invariant 6 already counts removed rows ("rows present plus rows removed equal the position"); its simulation check must be restated to count compacted markers with deleted rows. Positive rows of live instances stay: they are the source for rebuilding a projection.

## The right to erasure

Erasing a person's data means finding every instance that holds it and removing or destroying every copy. Three mechanisms, which compose:

| Mechanism | What it erases | What it misses | Cost |
|---|---|---|---|
| **Delete** (built) | Checkpoint, positive journal rows, subscriptions; projected history after the projector passes the marker | Value-store objects (until the purge job is built), done-marker payloads until their window, other instances that received copies by message, backups and WAL | One delete commit per instance, batched |
| **Values by reference** (decision 10, a release 1 priority; [POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#decision-10-values-by-reference-a-release-1-priority)) | A value held only by reference is erased by deleting its object, even while the instance and its journal keep the reference | The reference's digest stays. A plain SHA-256 of a short value (an e-mail address) can be found by guessing, so the digest must be keyed (HMAC with a per-instance or per-tenant key) for values that need erasure | One object write per value and one read per cold use; 5 against 13 engine calls to the first value-bearing commit and 12 against 20 for a fresh owner's read in the SQLite witness ([VALUES.md](../../prototypes/kernel-model/models/Values/VALUES.md)) |
| **Per-instance data keys** (crypto-shredding) | Everything encrypted under the instance's key, in every copy: rows, projected history, archives, value objects, backups and replicas | Data written in clear before encryption was turned on; copies in other instances | See encryption below |

- **Finding the instances.** A person's data is spread over instances by application logic. The kernel cannot search inside state (trade-off 10). An application that needs erasure tags instances with a subject key, published as an indexed field into the instance-list read model; erasure deletes by that filter.
- **Running instances.** The basic form is terminate, then delete. Redacting one value inside a running instance is model-specific and comes later.
- **Keys and ids.** Instance ids, message ids and positions carry no payload values. Correlation keys can. For keys declared sensitive, the subscription and event key is stored as a keyed hash of the value (`h:` prefix, HMAC with a per-tenant key), computed on both the subscribe and the publish side, so the rows hold no readable value. It costs one HMAC per subscribe and per publish.

## Encryption of state at rest

Two layers, with different protection:

1. **Storage encryption** (PostgreSQL on encrypted volumes or a managed service, SQL Server TDE, SQLite through an encrypting build). It protects disks and backups from theft, costs the kernel nothing and is transparent. It does not protect payloads from database users or replicas, and it does not support erasure. Release 1 relies on it and documents it per provider.
2. **Payload encryption by the kernel** (later). A codec in the durable layer's store adapter encrypts the payload fields of a row (state, journal payloads, message payloads and start input, event payloads) before they are written, and decrypts them on load. The codec changes values inside the same commit; it adds no statement to the commit and never sees partitions or the fence. Models are unchanged: they see plain state.

```csharp
// Layer 2, under the persistence abstraction. One ring per host; keys live outside the kernel's database.
public interface IInstanceKeyRing
{
    ValueTask<DataKey> GetOrCreateAsync(string instance, CancellationToken cancellationToken);  // idempotent by instance
    ValueTask<DataKey?> GetAsync(string keyId, CancellationToken cancellationToken);          // null once destroyed
    ValueTask DestroyAsync(string instance, CancellationToken cancellationToken);
}
```

- **Which key.** The instance's own key for state, journal payloads, projected history, values and archives. A message is encrypted with its target's key; `GetOrCreateAsync` is idempotent by instance id, so a start message can be encrypted for an instance that does not exist yet. Done markers keep no payload (format item 6).
- **Where keys live.** In a key ring outside the kernel's database, for example a KMS that wraps per-instance data keys under a key-encryption key, or a separate key table with its own short backup retention. A wrapped key kept in the kernel's own database would come back with every restored backup, and crypto-shredding would fail.
- **Order.** The key is created before the first commit that writes ciphertext. An orphan key from a commit that never landed is harmless and swept. Ephemeral instances that never commit need no key.
- **The projector needs no key.** It expands journal entries from their clear envelopes and copies payloads as they are. The API decrypts one run's payloads when the viewer reads it.
- **After erasure** the viewer still shows the run's shape (which nodes ran, when, with which outcome) with payloads marked erased.

Costs, all unmeasured: one key-ring write per new durable instance (a node can pre-create keys in batches to amortize it), one key-ring read per cold load (batched with loads, held while the instance is in memory), AES-GCM CPU per payload, and about 40 bytes per payload for nonce, tag and header. Ciphertext does not compress. Stored as text it grows by a third in base64, and PostgreSQL's TOAST compression no longer helps: every encrypted state above about 2 kB pays the incompressible cost that [POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#decision-10-values-by-reference-a-release-1-priority) measured, about 4.5 to 4.8 kB more WAL per commit for a 4 kB state. The codec therefore compresses before it encrypts.

## Where it lives

| Piece | Layer | Depends on |
|---|---|---|
| Delete and its refusal rules | 3, the workflow actor's control turn | The turn contract; layer 2's commit removes the rows |
| Retention worker, delete by filter, subject tags | 3, host services | Read models and message adds only |
| Purge and archive jobs, `IArchiveSink` | 3, jobs of the instance | The job contract; the sink is outside the commit |
| Payload codec and `IInstanceKeyRing` | 2, inside the store adapter, under the persistence abstraction | Row values only: the same codec serves EF Core and MongoDB |
| Journal envelope, codec headers, keyed correlation hashes | The record format shared by layers 2 and 3 | Nothing provider-specific |

## What must be fixed before the journal format is final

These cost little now and require rewriting stored history later:

1. **Journal entries split into a clear envelope and an opaque payload.** The envelope (node, parent node, kind, time, outcome) is what the projector expands and the viewer shows; the payload is the model's. Encryption, by-reference values and erasure then act on payloads only, and a run's shape survives erasure.
2. **Every payload carries a codec header:** plain, by reference, or encrypted with a key id. Rows written before a codec is switched on read as plain, so encryption and references can be enabled per definition without rewriting old rows. The same header goes on state, message payloads, start input, event payloads and projected history payloads.
3. **No payload values in ids, positions or keys,** except correlation keys, and those have the keyed-hash form above from the start.
4. **Lifecycle transitions are journalled at every history level** (created, ended, migrated, deleted) as envelope-only entries, so the instance-list read model sees every instance's end, which the retention worker needs. At Minimal history today an instance's turns write no journal row; only deletes and dropped starts are journalled at every level. This changes Minimal; see the decisions.
5. **Deletion markers are compactable below the projector's cursor,** with invariant 6 restated.
6. **Done markers keep only the id and window.** The consuming commit already updates the row to mark it done; clearing `Payload` in the same update leaves no copy of a command's input after it was handled. Duplicate detection needs only the id. This is a change to the commit's consume step (step 4), so it is kernel behaviour.

The physical journal layout needs no time-based segments for this design: retention is per instance, through the `(Partition, Instance)` index that delete already uses. Range partitioning by partition gives smaller vacuum units and leaves retention to the per-instance delete ([POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#journal), "Journal").

## What release 1 ships

"Retention and delete" in a basic form ([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)):

- delete of one ended instance, delete by filter, and terminate-then-delete, through the runtime API, on the built delete path;
- retention policies per definition with a host default, by end status, enforced by the retention worker;
- deletion of value-store objects with their instance (purge job), since values by reference are a release 1 priority and a delete must not leave them behind;
- the format items above, so that later features need no history rewrite: items 1 to 3, items 4 and 6 as the owner decides them, and for item 5 only the rule that no reader needs markers below the cursor (the compaction job comes later);
- storage encryption documented per provider.

Later: archiving; payload encryption and per-instance keys; redaction inside running instances; compaction of deletion markers and of retirement markers; history caps for long-lived instances.

## Costs

| Item | Cost | Status |
|---|---|---|
| Delete of one ended instance | One delete commit, batched with the partition's turns; the outside delete message adds one insert and one mark-before-run commit. PostgreSQL sample: 4 observed TCP edges and 512 to 656 WAL bytes per raw delete or retirement | Built; bounded sample measured |
| Retained after delete | One retirement marker (about 101 bytes in the sample) per generated id, one deletion marker journal row per instance, done markers until their window | Built |
| Retention worker | One read-model page read per sweep page and one message add per expired instance | Proposed |
| Purge of referenced values | One job per instance that holds references: object deletes by prefix, then the delete commit | Proposed |
| Lifecycle entries at Minimal | About one extra journal row per instance lifetime (start and end usually share a commit); the journal's share of WAL under chaos was about 0.29 kB per landed instance commit | Proposed; per-row share measured in [WRITES.md](../../prototypes/kernel-model/WRITES.md) |
| Payload encryption | Key-ring calls, CPU, about 40 bytes per payload, loss of compression unless compressed first | Proposed, unmeasured |
| Dead rows | Deleting journal rows leaves dead tuples for vacuum (peaks of 769 to 1,059 in the stress run, from aborted inserts and deletes) | Measured in WRITES.md |

## Unverified

- None of the retention worker, purge job, archive job, compaction, keyed correlation hashes, codec headers, key ring or encryption is built.
- Clearing a done marker's payload assumes no reader uses it; the event-start acceptance receipt and the outside-command duplicate answer must be checked before it is built.
- The value store's crash durability, pruning and erasure are unqualified (VALUES.md). Decision 10's acceptance run, the `pad=` case against a reference on PostgreSQL, is unrun (POSTGRES.md).
- All encryption costs above are estimates.

## Decisions for the owner

1. **What triggers retention.**
   - **A: a retention timer** per ended instance, sent in its ending commit. No read-model dependency and no extra round trip, but the inbox holds one future row per ended instance for its whole retention period, and a changed policy does not reach instances that already ended.
   - **B: a retention worker** over the instance-list read model, adding deletes. The inbox stays small, policy changes apply to instances that already ended, and the same path serves operator and erasure deletes. It costs one outside message and one mark-before-run commit per instance, and it needs format item 4.
   - **Recommendation: B, with lifecycle entries at every history level** (format item 4, which changes what Minimal writes).
2. **The erasure mechanism.**
   - **A:** delete plus values by reference only. Backups and replicas keep data until they expire.
   - **B:** A in release 1, then per-instance data keys (crypto-shredding) as an opt-in per definition in 1.x, with keys outside the kernel's database.
   - **C:** keys per tenant or per data subject: fewer keys, coarser erasure.
   - **Recommendation: B,** with the format items in release 1 so that B needs no history rewrite.
3. **Done markers without payloads** (format item 6). **Recommendation: adopt it,** after checking its readers. It is a kernel behaviour change and needs the two-review tier when built.
4. **Compacting deletion markers below the projector's cursor** (format item 5), while positive journal rows stay until their instance is deleted. **Recommendation: adopt it, after release 1,** with the restated invariant 6 checked in the simulation.
