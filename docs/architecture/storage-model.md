# Storage model

> **Proposed.** The kernel design is not approved. Product implementation is blocked until design approval.
> This page describes the standalone prototype's existing storage and identifies proposed additions explicitly.

The [design proposal](../reports/clean-slate-kernel/design-sketch-draft-5.md) owns the design decisions.
The [walkthrough](../reports/clean-slate-kernel/walkthrough.html) shows how the pieces work together.
Use the [Elsa glossary](../glossary/elsa.md) and [framework glossary](../glossary/root.md) for canonical terms;
this page is a reference for persisted records, their writers and their query paths.

The physical inventory below comes from [KernelDb and EfStore](../../prototypes/kernel-model/ef/Kernel.EntityFramework/EfStore.cs),
[SQL engine mappings](../../prototypes/kernel-model/ef/Kernel.EntityFramework/Engines.cs) and
[MongoStore](../../prototypes/kernel-model/mongo/Kernel.Mongo/MongoStore.cs).
The [store contract](../../prototypes/kernel-model/src/Kernel.Durable/Store.cs) states the operations independently of either provider.
The [in-memory store](../../prototypes/kernel-model/src/Kernel.Durable/InMemoryStore.cs) is the executable reference,
not another physical schema.

## Physical records

EF Core uses nine **tables**. MongoDB uses ten **collections**: the extra `clocks` collection allocates order keys
for outside message insertion without writing the owner's partition document.
Capitalization below is physical: `Messages` is an EF table; `messages` is a MongoDB collection.
Column types follow each SQL provider's EF mappings; the lists below name columns, not a portable SQL DDL script.
MongoDB `_id` is a scalar or an embedded key document as shown. Other listed fields are top-level fields.

| Purpose | EF Core table and columns | MongoDB collection and document fields |
|---|---|---|
| Partition ownership and journal allocation | `Partitions`: `Id`, `Owner`, `Epoch`, `LeaseUntil`, `Position`, `Died`. Key: `Id`. | `partitions`: `_id` (partition), `owner`, `epoch`, `leaseUntil`, `position`, `died`, `clock`, `now`. |
| Outside inbox order-key allocation | No table: engine-generated `Messages.Seq`, or SQLite's allocation under its write transaction. | `clocks`: `_id` (target partition), `clock`. |
| Runtime membership | `Nodes`: `Id`, `SeenAt`. Key: `Id`. | `nodes`: `_id` (node/process name), `seenAt`. |
| Instance checkpoint | `Instances`: `Id`, `Partition`, `Model`, `Definition`, `Status`, `State`, `Jobs`, `Revision`, `Parent`, `Fault`, `History`, `Suspended`, `Children`, `StateVersion`. Key: `Id`. | `instances`: `_id` (instance), `partition`, `model`, `definition`, `status`, `state`, `jobs`, `revision`, `parent`, `fault`, `history`, `suspended`, `children`, `stateVersion`. |
| Durable inbox, including job and timer tickets | `Messages`: `Id`, `Seq`, `Partition`, `Target`, `Kind`, `Payload`, `Sender`, `Start`, `Due`, `Seen`, `Attempts`, `Trial`, `KeepUntil`, `Done`, `ReplyTo`. Key: `Id`. | `messages`: `_id` (message), `seq`, `partition`, `target`, `kind`, `payload`, `sender`, `start`, `due`, `seen`, `attempts`, `trial`, `keepUntil`, `done`, `replyTo`. |
| Instance wait or start subscription | `Subscriptions`: `Key`, `Instance`, `Wait`, `Partition`, `Start`. Key: `(Key, Instance, Wait)`. | `subscriptions`: `_id: {k: key, i: instance, w: wait}`, plus `key`, `instance`, `wait`, `partition`, `start`. |
| Kept published event | `Events`: `Key`, `Id`, `Payload`, `KeepUntil`. Key: `(Key, Id)`. | `events`: `_id: {k: key, id: id}`, plus `key`, `id`, `payload`, `keepUntil`. |
| Committed history | `Journal`: `Partition`, `Position`, `Instance`, `Revision`, `Entries`. Key: `(Partition, Position)`. | `journal`: `_id: {p: partition, n: position}`, plus `partition`, `position`, `instance`, `revision`, `entries`. |
| Projector cursor | `Projections`: `Partition`, `Position`. Key: `Partition`. | `projections`: `_id` (partition), `position`. |
| Projected instance summary | `Views`: `Instance`, `Partition`, `Revision`, `Commits`. Key: `Instance`. | `views`: `_id` (instance), `partition`, `revision`, `commits`. |

### Values and embedded shape

The two providers store the same opaque checkpoint content. MongoDB does **not** turn the execution model's
state into indexed BSON properties: `state` is a JSON **string**, as `State` is a SQL text value.
Likewise `jobs`, `children`, `start` and `entries` are serialized JSON strings, not nested MongoDB arrays or documents.
The embedded MongoDB documents in the inventory are the composite `_id` keys.

| Field group | Stored meaning and shape |
|---|---|
| Instance identity | `Id` / `_id` is a string; `Partition` / `partition` is an integer routing key. `Model` and `Definition` identify the execution model and its definition. |
| Checkpoint | `State` is opaque model JSON; `StateVersion` is its integer version. `Revision` is a 64-bit optimistic concurrency value. |
| Instance lifecycle | `Status` and `History` are stored enum values; `Suspended` is Boolean. `Parent` and `Fault` are nullable strings. |
| Work in flight | `Jobs` is a nullable JSON array of job keys. `Children` is a nullable JSON array of child instance IDs. Job arguments and durable job tickets live in `Messages`, not in a separate jobs table. |
| Message envelope | `Target`, `Kind` and optional `Sender` are strings. `Payload` is an optional opaque string, commonly serialized JSON. `Start` is an optional serialized `StartSpec`, including model, definition, input and execution options. |
| Message scheduling | `Seq`, `Due`, `Seen`, `Trial` and non-null `KeepUntil` are 64-bit numbers; `Attempts` is an integer and `Done` is Boolean. Stored `Due=0` means immediately due. |
| Reply address | `ReplyTo` is a nullable string carrying node, absolute deadline, request ID and an optional early-output flag. It is not a durable response receipt. |
| Journal payload | `Entries` is model history JSON. Positive `Revision` identifies a checkpoint revision; `-1` is a deletion marker; `0` can describe work dropped without an instance row. |
| MongoDB ordering state | Partition `clock` and inbox `clocks.clock` are 64-bit order-key counters. Partition `now` records the primary's milliseconds when its take or fence ran. |

Lease, due, retention and membership timestamps use epoch milliseconds. MongoDB order keys advance a clock using
`max(previous, timeInMicrosecondUnits) + sends`. The checked fence and outside add use primary time; the batch fence
uses the owner's UTC clock with its remembered-clock condition. Its `seq` must not be interpreted as a timestamp.
SQL Server/MySQL/PostgreSQL assign message sequence values through engine generation; SQLite assigns them explicitly.
A sequence value and a journal position have different allocation rules.

The [checkpoint contract](../../prototypes/kernel-model/src/Kernel.Durable/Store.cs) deliberately keeps workflow
fields and durable-layer fields in one instance row. There is no separate physical record per activity or variable
in these providers. The kernel does not search inside model state.
The separate [values-by-reference witness](../../prototypes/kernel-model/models/Values/VALUES.md) does not add a values table or
collection to `KernelDb` or `MongoStore`; it uses an external directory through existing jobs.
MongoDB's [document-size experiment](../../prototypes/kernel-model/MONGO.md#5-findings)
records a 16 MiB document limit: the instance envelope shares that limit with its state string. This is a provider
constraint, not a chosen inline-value threshold.

## Record lifecycles

| Record | Creation and update | Removal or retention boundary |
|---|---|---|
| Partition | Created once for each configured partition. A take changes owner, increments epoch and sets the lease; every owner commit checks the fence and renews the lease. Journal-bearing commits advance position. | Release clears owner only at the matching epoch. Partition rows are not swept. Partition count is fixed at store creation. |
| MongoDB inbox clock | Created at store initialization, or upserted by an outside add; increments before attempting its message insert. | Not swept. A duplicate add may advance this clock without adding another message. |
| Node | Heartbeat inserts or updates its database-time `SeenAt` / `seenAt`. Membership counts only heartbeats inside the supplied window. | The current sweep does not delete stale node rows. Expiry from the live count is not physical deletion. |
| Instance | Created with its first durable checkpoint; subsequent commits replace its opaque state and increment revision. Finish, fault and cancellation remain stored states. Suspension remains a checkpoint flag. | The workflow actor accepts delete only after the instance ends; the store deletion matches its expected revision and removes the row. There is no automatic completed-instance retirement in this schema. |
| Message | Inserted by an outside/API add or in a sender's fenced commit. Polling reads it; later commits mark it seen, put it on trial, hold it, consume it or mark it done. | Ordinary consumed messages are deleted. Handled messages with a retention window remain `Done` until sweep. Pending messages are not swept merely because their window elapsed. |
| Subscription | A turn subscribes/unsubscribes atomically with its checkpoint. A registrar separately upserts start subscriptions. Repeated subscribe/unsubscribe is idempotent. | A turn can remove a wait; forced termination or parking can remove all waits of the instance. No subscription TTL or periodic subscription sweep is configured. |
| Event | Publisher inserts `(key,id)` once. Both publisher and start registrar read the other side to close their registration race. | Kept-event lookup includes events with `KeepUntil >= now`; sweep deletes those with `KeepUntil < now`. |
| Journal | A commit with non-null journal content appends at the partition's allocated positions. Full stores history entries; State stores `[]`; ordinary Minimal turns omit a journal row. | Explicit instance delete removes its positive-revision history. Deletion markers and other non-positive rows remain. No age-based journal sweep exists here. |
| Projection cursor | Initialized to zero per partition. A projector moves it with a compare-and-set against its previous position. | Not swept; advancing it is atomic with the corresponding view writes. |
| View | Projector creates or updates the instance summary from positive journal rows and removes it for a deletion marker. | Removal is projector work, so it can lag the instance delete. Views have no independent TTL. |

### Message transition details

A poll is a plain read, not a claim that mutates the inbox. The normal durable path is:

1. **Insert.** A new message has `Seen=0`, `Trial=0`, `Attempts=0`, `Done=false`; `Due=0` unless scheduled.
   A message handed over to another instance in the same owned partition can be inserted already seen at that epoch.
2. **Observe.** Poll returns stored metadata to the owner. The owner keeps its own memory of deliveries and
   serializes the instance's turns; another poll alone does not change `Seen`.
3. **Mark.** A fenced commit sets `Seen` to the current epoch before ordinary durable delivery is allowed to run.
   A suspect selected for trial has both `Seen` and `Trial` set to that epoch.
4. **Takeover.** A successful take from another process's expired lease increments `Attempts` on that partition's
   unfinished messages whose `Trial` equals the previous epoch. It does not increment every polled message.
   Releasing ownership, or the same process taking it again, is not that death condition.
5. **Hold or complete.** A held message stays, with `Trial` cleared when its turn commits. An ordinary consumed
   message is deleted in that commit. A handled outside command or delivery with `KeepUntil` is marked `Done`
   so a retry still encounters its message ID during the retained window.
6. **Sweep.** The current predicate is `Done && KeepUntil < databaseNow`; null windows do not qualify.
   Sweep also removes expired events. It does not remove an unfinished message, even when `KeepUntil` is in the past.

These writes are represented explicitly by `TurnCommit.Consumed`, `Done`, `Held`, and `CommitBatch.Seen` / `Trial`.
A job ticket uses this same inbox. Once the ticket creation commits, a job can run outside the transaction;
its result is applied through another turn. Cancellation consumes its ticket. The owner can execute outside work
again after recovery, so message deduplication is not an exactly-once guarantee for external effects.

`Due`, `Seen` and `Trial` control different stages: due time controls when work may run; seen epoch controls which
owner polls it; trial metadata supports poison handling. There is no separate `seen_epoch` column: the physical
field is named `Seen` in SQL and `seen` in MongoDB.

## Operations and writers

The matrix describes direct store operations. The process calling an API may also be an owner; the distinction
is which operation is authorized to write which records. An outside insert cannot update an instance checkpoint.

| Operation / caller | Reads and conditions | Writes |
|---|---|---|
| Store initialization | Existing tables/collections and partition initialization | Partitions and projection cursors; MongoDB also initializes clocks. Creates declared indexes. |
| Take / runtime activation | Partition free, expired (`LeaseUntil < now`), or already held by this process name | Owner, epoch, lease and death flag; only a take from another process's expired lease increments attempts of the previous epoch's unfinished trial messages, in the same transaction. A free-partition take or same-process retake does not. |
| Release / runtime | Matching partition epoch | Clears owner; does not record a death or delete instances. |
| Heartbeat / runtime membership | Database time, live node count and free/expired partitions | Upserts the node's heartbeat. |
| Commit / partition owner | Expected partition epoch and position; expected instance revision; required message affected-row counts | Checkpoints, inbox changes, outgoing inbox rows, waits and journal; partition lease and journal allocation. All-or-none transaction. |
| Poll / owner | Partition, unseen epoch, unfinished messages, due horizon and limit | None. Returns database time even when no messages qualify. |
| Load / owner | Instance ID set | None; reads checkpoint rows by key. |
| Add / external API, publisher or registrar | Unique message ID | Inserts one inbox row unless its ID exists. MongoDB first advances its separate inbox clock. |
| Publish / event publisher | Event key and ID | Inserts the kept event; the publisher then finds subscriptions and adds delivery messages as separate operations. |
| Register start subscription / registrar | Subscription identity | Upserts/inserts the subscription; catch-up reads kept events and adds deliveries separately. |
| Project / projector | Journal page and cursor; cursor must still equal `from` | Moves projection cursor and writes/removes view rows in its own transaction, separate from owner commits. |
| Viewer/API reads | Projection cursor, journal pages and instance view keys | None. No owner commit is forced by these reads. |
| Sweep / maintenance | Database time and expiry predicates | Deletes handled expired inbox rows and expired events; no checkpoint, subscription, node, view or journal TTL cleanup. |

The commit's fence is either checked before its writes or included in the same batch, followed by commit.
SQL Server defaults to a checked fence; PostgreSQL, SQLite and MySQL default to the batch fence.
MongoDB supports both; its batch path uses the partition clock remembered from its own take and falls back to
checked fencing when that local knowledge is absent. A stale fence cannot authorize partial checkpoint writes.
The [engine findings](../../prototypes/kernel-model/ENGINES.md) and
[MongoDB findings](../../prototypes/kernel-model/MONGO.md) qualify provider behavior and failure handling.

Instance subscriptions apply in turn order: each turn's subscribes precede its unsubscribes. The normal EF batch
writes the final desired membership of each named wait once; an idempotency fallback uses tolerant statements if
a pre-existing wait or absent removal makes the tracked path fail. That fallback adds calls only on that path.
MongoDB upserts/removes the subscription documents without requiring a removed wait to have existed.

## Poll query and indexes

The poll's selection is intentionally smaller than the owner's full eligibility decision.
Its provider predicate is:

```text
partition = requested partition
AND seen < current epoch
AND done = false
AND due <= databaseNow + horizon
ORDER BY seq
LIMIT requested count
```

MongoDB adds `_id` to the order as a tie-breaker: `ORDER BY seq, _id`.
Its aggregate first returns the primary's time through the partition document, then unions the qualifying messages.
SQL returns database time through a left join, including the empty-inbox result.
There is no poll predicate on `KeepUntil`, target status, suspension, job state, attempts or trial.

After the poll, the [owner loop](../../prototypes/kernel-model/src/Kernel.Durable/Worker.cs) and
[workflow actor](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs) do the remaining work:

- Deliver future timers only when due, even if they entered memory inside the horizon.
- Hold suspended instances' ordinary messages while allowing their control commands through.
- Load the checkpoint by target ID; turns maintain persisted job keys and map cancellation back to durable tickets.
- Treat another epoch's seen/trial metadata as suspect work and coordinate trials before running it.
- Preserve message order, including messages waiting for their seen mark, held messages and control commands.

The store does not join `Instances` in the poll to apply these rules. Adding `Suspended` or `Jobs` to an inbox index
would not make those instance-level rules a provider-side filter.

### Declared key and index paths

Tuple order below is index column order. EF names are described by their declared columns rather than inferred
engine-generated index names. All MongoDB collections have the ordinary unique `_id` index.

| Query path | EF Core declaration | MongoDB declaration |
|---|---|---|
| Poll messages in partition order | Secondary `(Partition, Seq)`; message key `(Id)` | Named `poll`: `(partition, done, seq, _id)`; `_id` unique |
| Charge attempts at takeover | SQL Server/MySQL additionally declare `(Partition, Trial)` | Named `trial`: `(partition, trial)` |
| Sweep handled inbox | No additional sweep index declared | Named `sweep`: `(done, keepUntil)` |
| Load/update/delete one instance | Key `(Id)`; revision is a concurrency predicate, not a declared extra index | `_id`; replace/delete also matches `revision` |
| Find waits for an event key | Composite key `(Key, Instance, Wait)` begins with `Key` | Secondary `(key)` |
| Remove an instance's waits | Secondary `(Instance)` | Secondary `(instance)` |
| Find kept events by key | Composite key `(Key, Id)` | `_id: {k,id}` plus secondary `(key)` |
| Read a partition's history in position order | Composite key `(Partition, Position)` | Unique secondary `(partition, position)`; `_id: {p,n}` |
| Delete an instance's positive history | Secondary `(Partition, Instance)` | Secondary `(partition, instance)` |
| Move a projector cursor | Key `(Partition)` | `_id` (partition) |
| Read/update an instance view | Key `(Instance)` | `_id` (instance) |
| Take a partition / heartbeat a node | Keys `Partitions.Id`, `Nodes.Id` | Respective `_id` keys |

MySQL also declares `Messages.Seq` as an alternate key so its auto-increment column leads a key.
PostgreSQL and SQL Server configure sequence/identity generation; SQLite supplies the value in the store.
No additional due-time, seen-epoch, instance-suspension, job-state, event-expiry or membership-expiry index is declared.
No EF foreign-key relationships are declared between these tables. Target, parent and instance references are
application fields; a durable message may target an instance that has not been created or has already ended.
The poll indexes supply a useful ordering path; they are not covering indexes for the full message payload or every
predicate. The declaration alone does not establish a query plan or selectivity under production load.
SQL Server enables read-committed snapshot by default; its experimental `READPAST` poll is not the default because
skipping a locked earlier message can break ordering. MySQL's take uses READ COMMITTED to narrow its lock footprint.

## Invariants carried by the records

These are store requirements, not a claim that every provider, failure mode and workload is production-qualified.
The [qualification findings](../../prototypes/kernel-model/FINDINGS.md) retain known gaps and measured coverage.

- **Fence:** every take advances epoch. An owner commit must match both its epoch and its believed partition position.
  The fence, checkpoint changes, consumed inputs and outgoing messages are one transaction.
- **Revision guard:** an update/delete must match the instance's expected revision. Creation is a create-only insert.
  A duplicate instance or journal key is not permission to overwrite a successor's work.
- **Journal allocation:** a batch allocates exactly as many positions as it writes non-null journal rows.
  Allocation is contiguous inside the committing batch and commits atomically with those rows. A committed position
  at or below the partition high-water mark is not awaiting a later insert; an absent positive row was explicitly deleted.
  Current journal rows need not be contiguous after deletion. Minimal commits can leave the position unchanged.
- **Message identity:** `Id` / `_id` is unique across the store, not just within a partition. `AddAsync` refuses an
  existing ID. A consumed row's removal, or a handled row's eventual sweep, ends that particular deduplication record.
  No permanent delivery-receipt table is supplied by these providers.
- **Event and subscription identity:** events are keyed by `(Key, Id)`; subscriptions by `(Key, Instance, Wait)`.
  These are separate identities from a generated delivery message ID. Do not assume a provider key repairs a
  collision in a caller's derived message or instance IDs.
- **Order:** `Seq` is an order key, not a gapless commit counter. PostgreSQL sequence allocation can leave gaps;
  MongoDB can have equal order keys from its separate allocation clocks and therefore sorts by ID as well.
- **Projection:** advancing the projector cursor and changing its views is atomic and conditional on the old cursor.
  The cursor does not participate in the owner's checkpoint fence.

### Viewer read proposed beyond this schema baseline

The existing store exposes journal pages, projection positions and views. The reviewed viewer change (#4359),
implementing the [proposal's viewer path](../reports/clean-slate-kernel/design-sketch-draft-5.md), adds a required
`ReadJournalOfAsync(partition, instance, after)` query returning one committed per-instance snapshot.
That addition is not in the source baseline described above. Its proposed viewer reads cursor, view and the
instance's tail, then merges in memory; paging unrelated snapshots can race a deletion and lose both history and
its later marker. The proposal uses a consistent provider query and explicit snapshot/locking semantics where needed.
It reuses the journal's partition/instance access path and does not add a table, collection or owner hot-path write.

## Growth, retention and measurement limits

Logical row growth and physical write amplification are different quantities.

| Source of growth | Existing bound or cleanup | Remaining consequence |
|---|---|---|
| Checkpoints | One current row/document per instance; explicit delete | Finished instances remain until deleted. Rewriting growing state causes database version/TOAST churn even without logical row growth. |
| Ordinary inbox traffic | Consumption deletes the input | Insert/mark/delete writes and dead tuples still cost space and WAL. A pending or held backlog has no automatic age-based removal. |
| Retained input IDs | `Done` plus non-null `KeepUntil`, then sweep | Deduplication lasts while that row remains; the window is not a permanent receipt. |
| Events | Kept until expiry, then sweep | Event volume and window length determine the retained set; lookup and removal are separate operations. |
| Subscriptions | Explicit unsubscribe or instance-wide removal | Waits and start subscriptions need their owning lifecycle; sweep does not age them out. |
| Journal | History level and explicit removal of positive rows on instance delete | Full/State append with history-bearing commits; deletion markers and other non-positive rows remain. No default journal-retention policy is implemented here. |
| Views and membership | View removal by projector; live membership count uses heartbeat age | Projection can lag; stale node documents/rows are not removed by current sweep. |
| Partition metadata | Fixed partition count | One ownership/cursor record per partition, plus MongoDB's outside-add clock per partition. |

The [PostgreSQL write-volume measurement](../../prototypes/kernel-model/WRITES.md) supplies the following observations,
not provider-independent capacity promises. The measurement used PostgreSQL 17.11, a checked fence, three nodes and
two kernel partitions for the stress runs, with outside traffic and killed sessions included as documented there.

| Measured workload | WAL per landed instance commit |
|---|---:|
| Small state, Full history: 1,000 starts on the owner | 708 B |
| Small state, Full history: ten durable no-op commits | 912 B |
| Small state, Full history: 1,000 starts arriving by poll, including outside inserts | About 2,339 B |
| Small state, Full history under chaos, two runs | 2,125–2,144 B |
| Full history plus a largely incompressible 4 KiB pad, under chaos | 6,913–6,931 B |

Under that chaos workload the inbox contributed roughly 0.93–1.08 kB per landed instance commit, the journal about
0.29 kB, and checkpoints about 0.64–0.71 kB. These are costs across the whole workload, not the byte size of one row.
Aborted transactions were counted separately; their relation WAL added roughly 5.5–12% on top across the variants.
With checkpoints every 30 seconds, one small-state comparison increased relation WAL from 2,032 to 2,732 B per
landed instance commit because of full-page images. These windows mostly touched small heaps and few checkpoints;
the low-image results are a floor for deployments touching many more pages.

Autovacuum kept up at the measured rates, approximately 70 instance commits per second on the shared test machine.
The 4 KiB-pad runs still grew the checkpoint TOAST table to 36–39 MB for about 17 MB of live chunks, then reused
space after vacuum rather than returning it. Setting fill factor to 70 on both Instances and Messages helped the
burst workload; the same two-table experiment's roughly 4% improvement in the stress comparison was below the
measurement's resolution. None is a selected host default.
The report did not establish MongoDB/oplog volume, SQL Server/MySQL/SQLite write amplification, production capacity,
or WAL savings from the subsequently built external-values witness.

Partitioning the PostgreSQL inbox by kernel partition was probed, not adopted: global message-ID uniqueness conflicts
with a partitioned unique key that omits the partition column. Adding that column would change the deduplication
scope and store operations. Lazy/delta snapshots, segmented checkpoints, completed-instance retirement and durable
response receipts are not added to this provider contract by this page. Candidate retention/receipt designs require
separate approval and qualification; the current tables and sweep predicates above remain the reference.
