# MongoDB, and a conformance suite for every store

Does `IKernelStore` quietly assume SQL? To find out: a provider-neutral conformance suite, a MongoDB implementation
of the contract, and both run against MongoDB 8.0 (a single-node replica set), the in-memory store and, as an
extra, the EF store on SQLite and PostgreSQL. `src/Kernel/` and `EfStore.cs` are unchanged.

**Run** means a command produced it (outputs under `.out/`, not committed); **reasoned** means nobody ran it.

**After the merge (FINDINGS.md section 23).** `MongoStore` implements round 3's operations (heartbeat, start
subscriptions, the projector's document and the view documents, delete, unsubscribe-all, held messages, reply
addresses), following M1 to M5; the suite has a case for each. "`src/Kernel/` and `EfStore.cs` are unchanged" was
true on this branch only. The figures below are the branch's; section 23 has the merged ones.

**Round 4 (FINDINGS.md section 24).** The names are round 4's: an instance's step is a *turn* (`ITurn`), off-step
work is a *background job* (`StartJob`, `IJob`, `job-finished`, the row's `jobs`), and the runtime's call into a
partition is a *pass*. `src/Kernel` is now three projects: `src/Kernel.Runtime`, `src/Kernel.Durable` and
`src/Kernel.Workflows`; a file named below without a path is in one of them. Seeds, counts and costs are unchanged.

## 1. What was built

| Path | What |
|---|---|
| `conformance/Kernel.Store.Conformance` | The suite: 46 semantic cases, the stress test, the cost runs, a command line (`Cli`) |
| `conformance/Kernel.Store.Conformance.Run` | The suite against `InMemoryStore`, the reference |
| `mongo/Kernel.Mongo` | `MongoStore : IKernelStore` (MongoDB.Driver 3.9; needs a replica set and MongoDB 8.0) |
| `mongo/Kernel.Mongo.Run` | The suite's plug for MongoDB, a TCP proxy that cuts connections, probes |
| `ef/Kernel.Ef.Conformance` | The suite's plug for `EfStore`, SQLite and PostgreSQL |

### The plug

A provider implements `IStoreProvider.CreateAsync(partitions, setup)`, which returns a `ProvidedStore`: a fresh,
empty store that disposing removes. The suite reads and writes only through `IKernelStore`; the rest is optional:

| Member | Used for | MongoDB | EF |
|---|---|---|---|
| `Store` | what the workers use | worker client, through the cutting proxy | |
| `NewClient()` | adds "from another node", the end-state check | two other clients | another context factory |
| `KillAsync` | break a connection in use | cut a busy TCP connection, or `killSessions` on a running op or open transaction | `pg_terminate_backend` |
| `Refused(row)` | a row refused every time | a 17 MiB state | a NUL in text (PostgreSQL only) |
| `HoldPartitionAsync` | another transaction holds the partition record | an open transaction that wrote it | `SELECT ... FOR UPDATE` |
| `StoreSetup.BrokenFence` | the mutation | fence filter `{_id}` only | `Unfenced` |
| `Counter` | round trips | driver command events | EF interceptors |

### The semantic cases

Take (free, mine again, someone else's live lease, expired, the attempt bump on a death and only on rows on trial at
the lost owner's last epoch, not on a retake by the same node); release (frees at once, is not a death, ignores an
old epoch); the commit (lands with every record kind; refused for epoch or position with nothing written; atomic
across all kinds, refused whole for a wrong revision, a create of an existing row, a missing or done consumed message,
a duplicate send; revision checks; a turn without a journal row keeps the position; consumed versus done; seen and
trial marks; hand-over marks; a beat renews the lease and tells a stale owner); order (seqs of one commit in the order
sent; a later send of one sender across a takeover by another node; outside adds in turn from three clients); the
fence under concurrency (twelve commits at one position, exactly one lands, five rounds; a take beside a commit in
flight reads the true position, twenty rounds); poll (order, limit, horizon, `seen < e`, done excluded, partition,
the store's time when empty, time moves); create-only add with every field; load; subscriptions; events and the
double delivery; journal reads; sweep; the three failure kinds (a refused row is definite and the partition goes
on; beside a held partition record a commit waits or is transient; after an unknown outcome a take reads the true
position).

Four cases are **Reference**: what the in-memory store does where the contract is silent and the kernel does not rely
on it. A provider that differs is reported (`DIFF`), not failed.

### The stress test

`Stress.RunAsync`, ported from `Kernel.Ef.Run stress` and made provider-neutral: three nodes, two partitions, six
real `Worker`s that each believe they own their partition, 150 ms leases, stalls of 150 to 500 ms, releases, crashes
(a new process with a new name, F9), injected failures (1 % of store calls fail, half before executing and half
after), the provider's killer every 20 to 60 ms, an outside producer that retries until its add answers, alternating
two clients. Then calm, settle, and check the end state of FINDINGS section 4 through `IKernelStore` only: the
journal by `ReadJournalAsync`, the position by a take from a fresh node once every lease ran out, what is left by a
poll at epoch `long.MaxValue`, instances by `LoadAsync`. A recording wrapper adds invariant 1 seen from outside: no two
commits answered "landed" cover one position, and along the positions the epoch never goes back.

## 2. MongoDB: documents and guarantees

| Collection | `_id` | Fields | Why |
|---|---|---|---|
| `partitions` | partition | owner, epoch, leaseUntil, position, **clock**, now, died | The fence record. `clock` is the partition's order-key clock, so the fence update hands out the commit's keys at no extra cost |
| `clocks` | partition | clock | The inbox clock for outside adds. Apart from `partitions` so that adds never conflict with the owner's commits |
| `instances` | instance id | partition, model, definition, status, state (string), jobs, revision, parent, fault | One document per row. `state` stays an opaque string: the kernel never looks inside, and a string needs no conversion |
| `messages` | message id | partition, seq, target, kind, payload, sender, start (JSON), due (0 = now), seen, attempts, trial, keepUntil, done | `_id` makes adding create-only. Indexes `(partition, done, seq, _id)` for the poll, `(partition, trial)` for the take, `(done, keepUntil)` for the sweep |
| `subscriptions` | `{k, i, w}` | key, instance, wait, partition | Upserted, so subscribing twice keeps one, as the reference does |
| `events` | `{k, id}` | key, id, payload, keepUntil | Inserted; a duplicate is ignored |
| `journal` | `{p, n}` | partition, position, instance, revision, entries | Unique `(partition, position)` as well: a second safety beside the fence |

This is the schema of the MongoDB branch. Round 3 and the merge added fields and collections: the instance's `history`, `suspended`, `children` and `stateVersion`; the message's `replyTo`; the subscription's `start`; and the `nodes`, `projections` and `views` collections (`MongoStore.cs`).

Records are not embedded in each other (for instance messages inside their instance): a message is added by anyone
while the owner may be writing the instance, and the poll reads a partition's messages across instances in key order.

| Guarantee | How |
|---|---|
| Atomic commit | One multi-document transaction (snapshot reads, majority writes): the fence, then **one** ordered client-level bulk write (MongoDB 8.0) that carries every other write to every collection, then `commitTransaction`. 3 round trips |
| The fence | `findOneAndUpdate({_id: p, epoch: e, position: n})` with a pipeline update that sets position, lease and clock from `$$NOW`; null means it matched nothing, and nothing else is sent. A competing transaction that touched the document fails with a write conflict, so two commits never land at one position (semantic case, 5 × 12 concurrent commits; stress, I1 and I6) |
| Fence in the batch (`--fence-in-batch`) | The fence is the first operation of the bulk write, as an **upsert**: matching nothing, it tries to insert the partition document again, a duplicate key at index 0, and the ordered bulk stops before anything else executes (finding M5). Keys come from the clock as this store saw it at the take, checked in the filter. 2 round trips; lease from `$$NOW` still |
| Counts | Verbose bulk results: each replace must match 1, each consume must delete all its ids (`done: false`), each done must match all. A mismatch aborts and answers "not landed" |
| Take | A transaction: conditional `findOneAndUpdate` (free, mine, or `leaseUntil < $$NOW`), which records `died`; if so, `updateMany` adds one attempt to the rows on trial at the old epoch |
| Order key | See M1: per sender, from the sender's partition clock, `max(clock, $$NOW in µs) + 1 ...` |
| Poll | One `aggregate` on the partition document: `$project` of `$$NOW`, then `$unionWith` of the messages, sorted by `(seq, _id)`, limited; batch size limit + 1 so it is one round trip |
| Store time | `$$NOW` of the primary, in milliseconds (M7) |
| Read and write concern | Majority for every write and every read outside a transaction, primary reads, snapshot inside transactions (M8) |
| Failures | `TransientTransactionError` → transient; `UnknownTransactionCommitResult` → unknown; duplicate key, document too large, validation, bad value → definite; everything else → unknown (M4). Before reporting, the open transaction is aborted (M2) and a transient failure waits 5 to 30 ms (M3) |

## 3. Results. Run

### Semantic cases

| Store | Required | Reference |
|---|---|---|
| In-memory | 45 pass, 1 skipped (it cannot hold a record) | all agree (they are its behaviour) |
| MongoDB, checked fence | 46 pass | all agree |
| MongoDB, fence in batch | 46 pass (after M5; before: the concurrent fence case failed) | all agree |
| MongoDB, fence broken on purpose | **4 fail**: old epoch, wrong position, a stale beat, the concurrent fence | |
| EF, SQLite | 45 pass, 1 skipped (no refused row) | 2 differ: subscribe twice, unsubscribe what is not there (M6) |
| EF, PostgreSQL, checked fence | 46 pass | 3 differ: the two above, and seqs in send order whatever the ids (F8) |
| EF, PostgreSQL, fence in the batch | **1 fails**: the concurrent fence case, a definite `PK_Journal` violation (M5) | the same 3 |

How each store refuses a count that does not match (the atomic cases): in-memory always *unknown*; MongoDB *not
landed* for revision and consumed, *definite* for an existing row or message id; EF the same, except that two turns marking one message done end in
*unknown* (EF's change tracker refuses the second attach of one id before anything is sent).

### Stress

| Run | Killed (cut + ended) | Injected | Stalls | Takeovers p0, p1 | Turns | Journal rows | End state |
|---|---|---|---|---|---|---|---|
| In-memory, 15 s, 1,500 | | 36 + 49 | 136 | 89, 88 | 2,987 | 2,630 | all properties hold |
| In-memory, 15 s, 1,500, fence broken | | | | 34,053, 34,181 | 68,320 | 113 | **601 violations** (I6, the store's own I1 ghosts, I3/I4, I11) |
| MongoDB, 40 s, 5,000 | 183 + 377 | 30 + 32 | 52 | 131, 128 | 7,929 | 6,706 | all properties hold |
| MongoDB, 60 s, 8,000 | 292 + 487 | 41 + 47 | 94 | 206, 206 | 14,054 | 11,663 | all properties hold |
| MongoDB, 15 s, 1,500, fence broken | 57 + 85 | 8 + 10 | 13 | 3,418, 3,479 | 7,697 | 711 | **470 violations**: 659 journal rows at position 658, messages and subscriptions left, tokens missing |
| MongoDB, fence in batch, 40 s, 5,000 | 182 + 258 | 30 + 31 | 61 | 117, 139 | 8,953 | 7,104 | all properties hold |
| MongoDB, 40 s, 5,000, labels only (M4), a kill every 2 to 8 ms | 328 + 350 | 7 + 4 | 10 | 95, 95 | 6,551 | 6,299 | all properties hold; 29 commits misread as definite |
| MongoDB, 40 s, 5,000, no abort, no pause (M2, M3) | 232 + 428 | 30 + 32 | 52 | 130, 144 | 8,009 | 6,635 | all properties hold |
| MongoDB, 15 s, 1,500, cuts only, no abort, no pause | 65 + 0 | 6 + 9 | 14 | 36, 39 | 2,440 | 2,094 | all properties hold |
| MongoDB, 15 s, 1,500, cuts only | 74 + 0 | 5 + 9 | 9 | 44, 46 | 2,531 | 2,088 | all properties hold |
| MongoDB, 15 s, 1,500, no killer | 0 | 46 + 65 | 135 | 92, 91 | 3,007 | 2,782 | all properties hold |

On MongoDB the chaos phase lasts longer than asked (91 s for 40 s): the producer's adds cost two round trips, and
every cut connection stalls the whole worker client for a moment (M9).

Commit failures in the 40 s MongoDB run, as the store classified them: transient write conflicts on the fence (35),
transient bulk writes and commits whose transaction a killed session had aborted, unknown "operation was
interrupted" (28) and unknown network errors on a commit (6), plus the injected ones. Under the brief's rule these 28
interrupted operations would have been *definite* (M4).

## 4. Cost. Run

The reference runs of FINDINGS section 5 (`Cost.RunAsync`, the same harness for every provider). MongoDB: a round
trip is one `CommandStartedEvent` of the driver, whatever its name; connection handshakes and the monitor's heartbeats
are not command events and are not counted, and none was opened during a run. Transactions start with their first
command (`startTransaction` rides on it), so there is no BEGIN. A commit is one `commitTransaction`. PostgreSQL through
the same harness reproduces FINDINGS section 5 exactly.

| Run | MongoDB, checked fence | MongoDB, fence in batch | PostgreSQL, checked | PostgreSQL, in batch |
|---|---|---|---|---|
| Ten no-ops, ephemeral, on the owner | 0 | 0 | 0 | 0 |
| Ten no-ops, balanced, on the owner | 1 commit: **3** | 1 commit: **2** | 3 | 2 |
| Ten no-ops, durable, on the owner | 10 commits: **30** | 10 commits: **20** | 30 | 20 |
| Ten no-ops, ephemeral, from another node | 10 | 8 | 9 | 7 |
| Ten no-ops, balanced, from another node | 2 commits: **10** | 2 commits: **8** | 9 | 7 |
| Ten no-ops, durable, from another node | 11 commits: 37 | 11 commits: 26 | 36 | 25 |
| Parent with three children, one partition | 4 commits: 12 | 4 commits: 8 | 12 | 8 |
| 100 balanced instances at once | 1 commit: **3** | 1 commit: **2** | 3 | 2 |

A commit on MongoDB is `findAndModify` (the fence) + `bulkWrite` + `commitTransaction`, or `bulkWrite` +
`commitTransaction` with the fence in the batch: the same as PostgreSQL. The only difference is the outside add:
`findAndModify` on the inbox clock + `insert`, one more than PostgreSQL's identity column (M1). From another node,
balanced: add 2, poll (`aggregate`) 1, the marking commit 3, the load (`find`) 1, the turn's commit 3.

## 5. Findings

### Where the contract assumed SQL

**M1. "`seq` is assigned by the database." Run.** MongoDB has no identity column, and nothing in the contract had to
change: `Committed.Seqs` already lets the store say which keys it assigned, and `long` is wide enough. The keys are cut
in the fence update from a clock kept in the sender's partition document, `max(clock, $$NOW·1000) + 1 ... + k`, and
returned with it. So a later send of a sender gets a larger key whoever owns the partition and whatever its clock
says, because the clock moves only under the fence (semantic case across a takeover by another node; stress I11 with
6,087 tokens). Keys of different partitions stay close to the time in microseconds, so the poll stays roughly first
in, first out across senders. Outside adds have no fence, so they take their key from a per-partition inbox clock: one
more round trip per add. A one-trip add keyed on `$$NOW` alone would break "a later add gets a larger key" when the
primary's clock steps back or a failover lands on a node whose clock is behind (reasoned). **Change to the sketch:**
"`seq` is an order key the store assigns: per sender, a later send has a larger key", and "the ordinal of a derived
id has a fixed width" is then only EF's need, not the contract's (the reference case "whatever the ids" passes on
MongoDB, SQLite and in memory, fails on PostgreSQL).

**M2. A broken connection does not end a MongoDB transaction. Run.** In SQL a connection that goes away rolls its
transaction back. In MongoDB the transaction belongs to a session, not a connection; the server does not notice the
client went away, and the transaction holds what it wrote until `transactionLifetimeLimitSeconds`. A commit that dies
after its fence leaves the partition document write-locked: every take and every commit meets a write conflict.
`probe crash` (a child process takes the partition with a 500 ms lease, runs the fence inside a commit's transaction
and calls `Environment.FailFast`):

| `transactionLifetimeLimitSeconds` | The next take succeeded after | Takes refused meanwhile |
|---|---|---|
| 60 (the default) | **70.9 s** | 667, all `WriteConflict (112)`, labelled `TransientTransactionError` |
| 3 | 4.1 s | 34, the same |

A poll is not blocked meanwhile (snapshot reads); every write to the partition document is.

Sketch sentences: "Leases decide who should write. The fence decides who can. A wrong lease costs availability, never
correctness" holds (nothing wrong landed), but the outage is the transaction limit, not the lease. **Provider's job,
fixed in `mongo/`:** after any failure the store aborts the open transaction explicitly before it reports
(`AbortOnFailure`), which covers a broken connection of a living process (reasoned); for a dead process only the server limit
helps, so a deployment sets `transactionLifetimeLimitSeconds` to a few seconds (a commit takes milliseconds; see M10
for the largest). In a living process the remedy made no difference that the stress test could see (the runs
"no abort, no pause" against the default, with killSessions and with cuts only): the driver also aborts when a session
is disposed (reasoned from those runs; not traced). One early run, made while the killer's `killSessions` was failing
on a read-concern error and without either remedy, crawled: 99 turns in 32 s of chaos and 398 transient conflicts on
the fence. It was not reproduced, and its cause is not established.

**M3. A take does not wait behind a commit in flight; it fails, and the kernel retries a transient failure at once. Run.**
The sketch: "The take waits behind a commit that is still in flight, so it reads the true position" assumes row locks
that wait. MongoDB fails the second writer of a document at once with `WriteConflict` (`TransientTransactionError`).
The take is a transaction (it must bump attempts atomically), so it fails rather than waits; the host takes again and
then reads the true position (semantic case, 20 rounds, "take retried"). The commit path is the problem:
`Worker.CommitAsync` answers a transient failure with `continue`, the same batch again, at once and without bound.
Beside a held document (M2) that is a busy loop (reasoned from the code; the crash probe's heir, which pauses 100 ms,
met 667 conflicts in 71 s). **Provider's job for now:** the store waits 5 to 30 ms before it
reports a transient failure. **Contract change proposed:** "Transient: try again as it is, after a pause; after a
bound, reset." That is a kernel change (`Worker.CommitAsync`), so it is not made here.

**M4. "Everything else is definite" is not safe. Run.** In the 40 s run 28 commits failed with "operation was
interrupted" (`Interrupted`, 11601: a session ended by `killSessions`) carrying neither label; in the run below, 5 of
them were on `commitTransaction`. The kernel answers a
definite failure by committing the batch one instance at a time and **parking** the instance that fails; an
interrupted commit says nothing about the batch, and on `commitTransaction` its outcome is not even known. Contract:
`Failure.Definite` is "it did not happen and never will". **Fixed in `mongo/`:** definite only for errors the same
batch would meet again (duplicate key, document too large, validation, bad value, type mismatch); everything else is
unknown, which resets and is always safe. With the brief's rule and the killer at every 2 to 8 ms (`--labels-only`,
40 s), 29 commits were reported definite:
18 `MongoConnectionPoolPausedException` (thrown before anything was sent), 5 `commitTransaction ... operation was
interrupted` (whose outcome is unknown), 4 bulk writes and 2 receive errors. No instance was parked in that run:
each time the one-by-one retry of the batch landed, or met the fence. Parking needs the retry of the same instance to
fail definitely too (reasoned). A pool-paused error is in truth transient; with the fixed rule it resets, which costs
a take but is safe. **Contract wording proposed:** "When a provider cannot tell,
the failure is unknown."

**M5. With the fence in the batch, a fence that matched nothing must win over every other error of the batch. Run,
on MongoDB and on EF PostgreSQL.** Two commits at one position: the loser's fence matches nothing, but the writes behind
it meet the winner's rows (the journal position, a derived message id) and fail first as a duplicate key, a definite
error. MongoDB before the fix: `StoreException: An error occurred during ordered bulkWrite operation`; EF on
PostgreSQL with `fenceInBatch`: `duplicate key value violates unique constraint "PK_Journal"` (before the EF fix below). For the kernel
(reasoned from `Worker.FlushAsync`): a stale owner gets "definite", commits its instances one by one, tries to park
the first, every one of these is fenced off, and it resets; nothing wrong lands, but it is the parking path entered
for a stale epoch. The checked fence is not affected. **Fixed for MongoDB** (the upsert fence stops the bulk at index
0). **EF:** fixed since (ENGINES.md E1, `EfStore.CommitAsync`): a key violation maps to "not landed" when the fence row
did not match. **Contract sentence proposed:** "A commit whose fence does not match answers
Landed = false, whatever else in it would have failed."

### Where the contract is silent (Reference cases)

**M6. Run.** Subscribing twice: in memory and MongoDB keep one; EF throws a definite key violation (which parks).
Unsubscribing what is not there: in memory and MongoDB change nothing; EF answers "not landed" (which resets). A count
that does not match: in memory *unknown*, EF and MongoDB *not landed* or *definite*. The kernel does none of these
today (reasoned from `Turn`), but a model that re-subscribes would park on EF. Proposed: subscriptions are idempotent,
and a key that exists where a commit creates a row is a count that does not match (not landed).

### The provider's own job

**M7. Store time. Reasoned, single node.** The store's time is `$$NOW`, the primary's wall clock, read in the same
command that uses it (the take, the fence, the poll, the sweep). After a failover it is another machine's clock: a
lease may end early or late and a timer may fire early by the clock difference, as on a PostgreSQL failover. Order
keys do not depend on it beyond the `max`. The cluster time would be monotonic but has a resolution of one second.

**M8. Read concern. Reasoned, single node.** Reads outside transactions are majority, so a poll or a load never sees a
write a failover could roll back; writes are majority, so an add that answered survives a failover. Local reads
would let a poll act on a message that is then rolled back; the count on the consuming commit would catch it and reset
(reasoned), but majority costs nothing measurable here. A single node cannot show either.

**M9. A cut connection stalls the whole client. Run, cause reasoned.** In 15 s runs with only the injected failures
the workers made about 3,000 turns and 135 stalls (a stall is drawn once per 25 loop rounds, so it counts rounds);
with connections cut on the wire every 20 to 60 ms (74 cuts), 1,170 turns and 9 stalls in 28 s. The
driver treats a network error as the server being unavailable, clears the connection pool and waits for its monitor;
every worker on that client pauses. Availability only; one client per partition, or a shorter `heartbeatFrequency`,
would narrow it.

**M10. Transaction limits and the batch size. Run.** `probe limits`, one commit each, on one node:

| One commit | Documents | State | Time | Round trips | Outcome |
|---|---:|---:|---:|---|---|
| 1 turn (instance, journal row, 2 sends) | 5 | 0 | 17 ms | 3 | landed |
| 500 turns, the kernel's default `MaxBatch` | 2,001 | 0.1 MiB | 51 ms | 3 | landed |
| 2,000 turns | 8,001 | 0.4 MiB | 185 ms | 3 | landed |
| 10,000 turns | 40,001 | 1.9 MiB | 709 ms | 3 | landed |
| 50,000 turns | 200,001 | 9.5 MiB | 2,754 ms | 4 (the bulk write split in two) | landed |
| 100 turns of 1 MiB of state | 201 | 100 MiB | 532 ms | 5 (split in three) | landed |
| 1 turn of 15 MiB of state | 3 | 15 MiB | 35 ms | 3 | landed |
| 1 turn of 17 MiB of state | 3 | 17 MiB | 29 ms | 3 (the last is the abort) | **definite**: the bulk write was refused (`ClientBulkWriteException`) |

MongoDB has no limit on the documents of one transaction since 4.2, and the driver splits a large bulk write into
several messages of at most 48 MiB, each a round trip. A document is at most 16 MiB: an instance state above that is refused
as definite and the instance is parked, which is the design's "a value too long". What bounds the batch is time: a
transaction must finish within `transactionLifetimeLimitSeconds`, which M2 wants short, and while it runs it blocks
the partition's next commit and every take. The kernel's default of 500 instance-commits per flush is far inside.

## 6. What was not done

- A real replica set of three: failover, rollback, `$$NOW` jumping, majority reads lagging. All single node.
- The stress test against EF PostgreSQL through the suite (its own stress test exists); SQLite under concurrency.
- The `node` heartbeat, fair share, the projection row: not in the contract yet, so not in the suite.
