# Fairness, quotas and backpressure

> **Proposed.** The kernel design awaits owner approval. This page answers design-now question 3 of the
> [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#open-questions); it does not authorize product
> implementation.

Partitions share nodes, and the instances of a virtual partition share one owner, one poll and one committer. One
tenant's burst can therefore delay every other tenant whose instances hash to the same virtual partitions or run on the
same node. This page proposes four mechanisms, each at the layer that owns the resource it protects:

| Mechanism | Layer | Protects | Release 1 |
|---|---|---|---|
| Admission at the edge: per-tenant rates, per-node limits | 2, ingress | Every partition, from work that enters from outside | Yes |
| An ingress threshold on due inbox depth per virtual partition | 2 | Queueing delay inside one virtual partition, from outside work | Yes |
| Fair turn selection and a turn budget per pass | 2, with a hint to 1 | The node's passes, and tenants inside one pass | Yes |
| Credits between instances, and caps on sends per turn | 3, with a kernel guard | Inbox growth that turns cause | Caps yes; credits with ETL |

Terms used here: a **pass** is one call of `IScopeActivation.RunAsync` (layer 1 calls it, layer 2 runs it); a **turn** is
one instance handling its deliverable messages; **due depth** is the number of unfinished messages of a virtual partition
whose due time has passed. Future timers do not count towards due depth.

## What the prototype does today

Built, in `prototypes/kernel-model` at main `433c79341`:

| Behaviour | Where | Effect on fairness |
|---|---|---|
| A poll reads at most `PollLimit` (500) rows in `seq` order | `WorkerOptions`, `Worker.PollAsync`, the [poll query](storage-model.md#poll-query-and-indexes) | First in, first out across all senders of the partition. A burst ahead of another tenant's message fills the poll window first. |
| A pass gives every ready instance one turn, in ordinal id order | `Worker.RunAsync` (`ReadyForTurns(...).Order(StringComparer.Ordinal)`) | No instance takes two turns before another ready instance takes one. A pass has no turn budget, so its length grows with the ready set. |
| A turn takes the instance's contiguous deliverable messages | `Worker.Deliverable` | A deep inbox costs fewer turns. A generation-tracked parent takes each child completion alone (`WorkflowInstance.SingleMessage`). |
| A flush writes at most `MaxBatch` (500) instance commits | `Worker.FlushAsync` | Bounds one transaction; the remainder waits for the next pass. |
| At most `MaxInstances` idle instances stay in memory | `Worker.Evict`, FINDINGS section 19, invariant 21 | Bounds owner memory. The default is unbounded. |
| One loop per node runs the passes of its scopes one after another, in scope order | `RuntimeNode.LoopAsync` | Round robin over scopes. A long pass delays every other scope on the node. |
| A node holds its fair share of partitions, by node count | `PartitionRuntime.BalanceAsync`, FINDINGS section 9 | Balances partitions, not load. |
| A poison trial stops every other turn in the process | `TrialGate`, FINDINGS F11 | A deliberate, rare fairness cost. |
| The ETL spike keeps at most 8 children outstanding per coordinator | `models/Etl/EtlModel.cs`, [ETL.md](../../prototypes/kernel-model/models/Etl/ETL.md) | The one credit protocol on the kernel; measured locally, below. |
| A Native invocation inbox with a capacity: input above `Capacity` or `ReceiptCapacity` is answered `Full` or `ReceiptFull` | `models/Native/InvocationInbox.cs`, `models/Admission.Run` (its `IgnoreCapacity` control) | A bounded inbox inside one instance's state, in layer 3, that refuses by reply. M0 research, not the production contract. |

Not built in the prototype's layers 1 and 2: a tenant field (the prototype's `InstanceRow` and `Message` have none),
admission at ingress, a bound on a virtual partition's inbox, a pass budget, a pressure signal or a cap on sends per
turn. The one bounded inbox is the layer 3 invocation inbox above. No fairness measurement exists. Everything
in the sections below is **proposed and unverified** unless it cites a run.

## Where the delay comes from

1. **Ingress.** Anyone may add a message (`IKernelStore.AddAsync`). Nothing limits how many starts, commands or outside
   publishes a tenant inserts.
2. **Turns.** A turn may send or start any number of messages in its commit. A parent's children report back into one
   inbox. [Large fan-out](large-fan-out.md) covers that case.
3. **Outside publishes.** One publish takes one admission, and then adds one message per matching wait, into whichever
   virtual partitions those waits live in, whatever their depth. Admission does not cover that fan-out; the number of
   matching waits bounds it.
4. **The poll window.** A virtual partition's due rows are read in `seq` order. A message behind 10,000 rows of another
   tenant waits for 20 polls of 500.
5. **The node loop.** Passes run one after another. A scope with 5,000 ready instances holds the loop for 5,000 turns.

Virtual partitions dilute a burst of keyed starts: 100,000 caller keys spread by hash over 1,024 virtual partitions put
about 98 in each (the placement check's spread at 1,024 was 68 to 129 per virtual partition for 100,000 names,
PARTITIONS.md). Unkeyed starts are placed inline, in virtual partitions of the node that received them, so they spread
only as widely as the traffic spreads over nodes. Either way the node still has to run all of them, so the quiet
tenant's wait is the time its node needs to drain the burst. Only admission limits that; the other mechanisms bound the
tail.

## Admission at the edge

**Contract (proposed).** Every message that enters from outside the kernel passes an admission decision before its row
is written: API and HTTP starts, keyed commands, outside event publishes, and ingress adapters (brokers, webhooks,
schedule sources). Messages that turns send, timers, job results and child completions are already owed and are never
refused. Control commands and operator actions have their own rule, below.

```csharp
public interface IAdmission                                // layer 2, the ingress side of the delivery seam
{
    AdmissionDecision Admit(AdmissionRequest request);     // synchronous, in memory: no database call
}

public sealed record AdmissionRequest(BoundScope Scope, AdmissionKind Kind, InstanceRef? Target, string? Definition);
public enum AdmissionKind { Start, Command, Publish }
public sealed record AdmissionDecision(bool Admitted, RefusalReason? Reason = null, TimeSpan? RetryAfter = null);
public enum RefusalReason { RateLimited, NodeBusy, InboxFull }   // HTTP 429, 503, 503
```

`Scope` is the bound persistence scope from ADR 0097's binder, the only source of the tenant
([tenancy](tenancy.md#where-the-tenant-comes-from)). `Target` is the instance a keyed start or command addresses, by
its tenant-qualified id. Its virtual partition is read from the id inside layer 2;
the contract itself names no partition, so layer 3 and the public API stay host-neutral. A start without a caller key
has no target yet. Inline execution places it in a virtual partition the receiving node owns; placement picks one below
the high-water mark, and admission refuses the start only when every one of the node's virtual partitions is above it.
An inline Ephemeral start that writes no row passes the tenant rate check only.

**The limits (proposed):**

| Limit | Scope | State | Exact or approximate |
|---|---|---|---|
| Tenant rate: a token bucket per tenant and `AdmissionKind` (rate, burst) | Per node | In memory | Exact per node. Cluster-wide it is the configured rate divided by the node count from membership, so approximate while nodes join or leave, and below the configured rate when ingress traffic is uneven across nodes |
| Admissions in flight | Per node | In memory | Exact |
| Due depth of the target's virtual partition | Per virtual partition | The pressure list, below | Approximate by up to one refresh interval of arrivals |

Background jobs are limited in a different way: the job scheduler gives each tenant a number of slots per node, and a
job beyond them waits for a slot. That limit refuses nothing.

**Control commands and operator actions (decision 4).** `Suspend`, `Terminate`, `Delete` and an operator's discard of
parked work shed load, so they are exempt from `InboxFull` and from the tenant rate; a separate operator rate limits
them. `Resume` and an operator's retry of parked work add load, so they obey `InboxFull`.

**Cost.** An admitted request pays nothing extra: every check is in memory. A refused request writes no row, so a client
or adapter retry is harmless; a retry with the same caller key finds the same instance, as today.

**Running-instance quotas** need a count across partitions. Decision 6 (no reads across instances) keeps that off the
turn path, and lists and counts come from read models. A quota read from a projected count per tenant is soft: it can
overshoot by the tenant's admission rate times the projector's lag. A hard quota would need a quota actor per tenant
that every start passes through: one more hop and one more commit per start, and one virtual partition serialising
the tenant's starts. Release 1 has no running-instance quotas (decision 1).

## A bound on inbox growth

The durable inbox is the overflow: rows wait in the table. Owner memory is bounded by `PollLimit` per poll and by
`MaxInstances`, which is unbounded by default; release 1 sets a value. What needs a bound is the due depth per virtual
partition, because it sets the wait at the back of the poll window.

**What is bounded, and where.** The kernel bounds what enters from outside, at admission, from a published estimate:
an **ingress threshold**. It gives no hard bound on total depth. Internal arrivals (sends, timers, job results, child
completions) keep landing after ingress is refused, and only the send cap, the ForEach cap and models' credits bound
them. The store never refuses an insert for depth:

- A sent message is part of the sender's fenced commit. Refusing it would roll back the sender's whole batch, other
  instances' turns included, or split the commit into pieces, which the extensibility principle forbids.
- Two instances with full inboxes that send to each other would deadlock.
- The inbox alone says what is owed. A refused completion or job result would lose work.

**Kept rows of a parked instance.** Arrivals that a parked instance consumes become done rows kept for its recovery
([observability](observability.md#what-park-keeps)). While parked they are outside due depth and add no poll delay;
their growth is storage, which ends when the Park is resolved. Recovery restores them page by page. A page waits while
the previous page's last restored row is not yet taken up by a poll and while the instance is suspended, so a drain adds at most one
page of rows not yet taken up plus the rows its model holds ([observability](observability.md#resolving-a-park-a-bounded-drain)).
The gate does not wait on this threshold, because a recover drain's own withheld arrivals count in the backlog and
would hold it above the high water mark for good.

**The estimate (proposed).** The owner of a virtual partition knows its due depth exactly while its last poll came back
short: every due row is then in its memory. While polls come back full, it samples a capped count once per sample
interval:

```sql
SELECT count(*) FROM (
  SELECT 1 FROM message
  WHERE partition = :p AND done = false AND due <= :now
  LIMIT :highWater + 1) AS behind;
```

One query per behind virtual partition per sample interval. On today's indexes the scan is not bounded by
`highWater + 1`: the EF index is `(Partition, Seq)` and MongoDB's poll index is `(partition, done, seq, _id)`, so the
`done` and `due` filters apply to the rows scanned. The scan also reads done rows that wait for their sweep (EF) and
future timers (both) in the partition. Proposed: an index on `(partition, done, due)`, partial on `done = false` where
the engine supports it (PostgreSQL, SQLite, SQL Server's filtered index, MongoDB's `partialFilterExpression`); MySQL
takes the full index. With it the count reads at most `highWater + 1` qualifying entries. The physical scan can still
pass index entries of dead row versions that cleanup has not removed yet (PostgreSQL B-tree), so its cost is bounded
only after cleanup. The index costs one more entry written for every message insert. On PostgreSQL, indexing `done`
also means a `done = true` update is no longer HOT, so it writes an entry in every index on the message table; a
delete writes no index entry and leaves dead tuples for vacuum. All of this is unmeasured: the release 1 cost runs
measure the query plan and the scan cost under update and delete churn, with WRITES.md's method.

**The pressure list (decision 2).** The owner writes its estimate into a new `backlog` column of the virtual partition's
fence row, with its next commit or beat. Both already update that row, so this adds bytes and no round trip; it adds one
assignment to the owned commit SQL on all four engines ([COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md)).
Ingress nodes keep a list of the virtual partitions above the low-water mark. Under the built-in runtime, the heartbeat
already queries the partition rows for free ones (`EfStore.HeartbeatAsync`), and that query can return the list as
well. Another runtime refreshes it with one query per node per interval:

```sql
SELECT id, backlog FROM partition WHERE backlog >= :lowWater;
```

Admission refuses a start or command whose target virtual partition is above the high-water mark, and admits again
below the low-water mark. For admitted starts and commands the threshold is soft: they can overshoot it by one refresh
interval plus one sample interval of ingress. The fan-out of an outside publish and all internal arrivals are not
limited by it at all. A provider without a
relational fence row stores the field beside its fence record; MongoDB stores it in the partition document.

The threshold does not see tenants. A quiet tenant whose keyed instances share a virtual partition with a burst is
refused with the bursting tenant: the burst's delay becomes the quiet tenant's refusals. Tenant rates make a burst
smaller and so make this less likely. A per-tenant share of a virtual partition's depth needs a tenant column on
`message`, and stays a later option.

**What a sender sees when an inbox is full:**

| Sender | What it sees |
|---|---|
| API or HTTP caller | A refusal before any row is written: `InboxFull` or `NodeBusy` (HTTP 503), or `RateLimited` (HTTP 429), each with `Retry-After`. A retry, keyed or not, is safe. |
| Ingress adapter (broker, webhook, schedule source) | The same refusal. A broker adapter stops acknowledging and pauses consumption, so the broker keeps the backlog; a schedule source fires late. |
| Outside publisher | Admission against its tenant's rate. The publish's fan-out into waiting instances is never refused: the waits are committed. |
| A turn (`Send`, `Start`, `Publish`) | Its sends always commit with its state. It is bounded where it starts: the cap on sends per turn, the [parallel ForEach cap](large-fan-out.md), and credits. The kernel checks the cap when it seals the turn's outcome, so a model cannot catch it; a turn above it fails, and the kernel parks the instance as for any turn that throws. The target's lag shows in the depth metric. |
| A timer, job result or child completion | Nothing: it is owed and lands. |

## Fair scheduling within a pass

**Proposed changes to the Worker (layer 2):**

1. **A turn budget per pass.** A pass runs at most `PassTurnBudget` turns, flushes, and returns `TimeSpan.Zero` while
   ready work remains. Layer 1 then runs its other scopes before this one again. The flush cap stays.
2. **Deficit round robin by tenant.** The ready instances are grouped by the tenant on their row. Each round gives every
   tenant with ready instances a quantum of turns (its weight; equal by default). Within a tenant, instances rotate: a
   newly ready instance joins the back of its tenant's queue, in the order its oldest deliverable message was
   observed, and an instance that takes a turn and is still ready goes to the back. An instance therefore takes at
   most one turn before every instance ahead of it in its tenant's queue has taken one. A cursor carries the rounds
   from pass to pass; a reset drops it with the rest of memory. The ordinal order of today starves high ids once a
   budget exists, so it goes.
3. **An age hint for layer 1.** `RunAsync` also reports the age of its oldest waiting deliverable message. The
   built-in runtime works in rounds: every scope with ready work gets one budgeted pass per round, and within a round
   the scopes go in order of their oldest waiting work, scope order breaking ties. The age orders a round and never
   gives one scope a second pass before every other ready scope has had one. Layer 1 learns an age and nothing about
   tenants or messages.

The per-target order rule is unchanged: a target's messages are handled in `seq` order, and one sender's messages to
one target arrive in the order sent. The selection only chooses which instance takes the next turn.

**Cost.** In memory, O(ready instances) per pass. No database call. The seam change is one field in the result of
`RunAsync`.

**What it cannot do.** The poll stays first in, first out: one query, one index. A tenant's message beyond the poll
window is not in memory, so the in-pass selection cannot reach it. The ingress threshold limits how far outside starts
and commands can deepen that window, which is why the two come together; internal arrivals and the fan-out of outside
publishes can still deepen it. A poll per tenant needs a tenant column and index on `message` and one poll per active
tenant: a later option, if measurements show the need.

## Virtual partitions and the Cluster profile

- **Per virtual partition.** The owner's decisions of 16 November fix 1,024 virtual partitions with a fence row each
  ([PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md)). The bound, the estimate and the `backlog` field are per
  virtual partition, so the store-wide ingress threshold is 1,024 times the high-water mark. Internal arrivals can take
  the due rows above it.
- **Range ownership (later, the Worker refactor).** A scope becomes a range of virtual partitions with one pass. The
  turn budget and the round robin then span the range; the capped count runs per behind virtual partition of the range.
  Until that refactor the kernel's Worker owns one partition and the same rules apply to it.
- **Embedded.** One node: admission is exact, and the pressure list is local memory.
- **Cluster.** Each node runs its own limiters. The rate per node is the configured cluster rate divided by the node
  count the membership reports. The balancer counts nodes and does not see load; with a fence row per virtual
  partition, moving one hot virtual partition is one release and one take (PARTITIONS.md), so load-aware rebalancing is
  cheap to add later. Placing a tenant's unkeyed starts on a subset of virtual partitions (shuffle sharding) would
  limit a burst's reach. It conflicts with inline execution, which places a start in a partition the receiving node
  owns, so it stays a later option.
- **Orleans.** The partition grains run passes; the age hint and the pressure query work the same. Not in release 1.

## The relation to ETL's credit protocol

A credit protocol is cooperative flow control between instances, in layer 3: a producer keeps at most C requests
outstanding and sends the next only when a completion returns a credit. The ETL spike does this for children
(`EtlState.Pending`, `RangeInput.Credit`). Measured: at 4,096 leaves, fanout 64 and credit 8, the observed maximum
outstanding per coordinator was 8 in both the tree and the direct run (rerun at `433c79341`, exit 0). The
`--mutate-credit` control, which admits one extra child, fails the credit check with 9 observed (exit 1). The spike
runs on the in-memory store, and its credit is local to each coordinator (ETL.md).

The kernel's mechanisms are the safety net for models that do not cooperate, and credits keep a cooperating model away
from them:

- A consumer's inbox holds at most C messages from one producer. A credited pipeline stays under a virtual partition's
  high-water mark only if the outstanding credits of all producers that send into that virtual partition add up to
  less than the mark; each producer's C must then be well below it. Credits alone do not ensure that.
- Credits travel as ordinary messages and completions, so they need only the turn contract.
- Admission never touches them: they are messages between instances.
- Native's parallel ForEach with a degree of parallelism is the same pattern inside one instance.

The kernel does not implement credits; ETL ships them as part of its model.

## Release 1: the basic form

The sketch's release 1 scope names "admission control with bounded inboxes". Proposed contents:

- `IAdmission` on every ingress path, with per-tenant token buckets per node and an in-flight limit per node.
- The tenant from the bound scope and a tenant column on every tenant-keyed record, as [tenancy](tenancy.md) defines;
  the instance row's column, which the sketch's data model already lists, is the one turn selection reads.
- Per-tenant job slots in the job scheduler.
- The ingress threshold on due depth per virtual partition: the `backlog` field, the capped count with its index, and
  the pressure list, with 503 refusals and the exemptions for control commands and operator actions.
- The cap on sends per turn and the parallel ForEach cap.
- The turn budget per pass, deficit round robin by tenant, and the age hint.
- Metrics for due depth per virtual partition and refusals by reason and tenant, named by the observability page.

Defaults (rates, high and low water, budget, sample interval, send cap) are set by the release 1 cost runs. Proposed
starting points, all unmeasured: `PassTurnBudget` 500 (equal to `MaxBatch`), high water 10,000 and low water 8,000 due
rows per virtual partition, a 5 s sample interval, 10,000 sends per turn.

**Later:** hard running-instance quotas, a poll per tenant, a per-tenant share of a virtual partition's depth, charging
an outside publish for its matches, priorities, load-aware rebalancing and shuffle sharding.

**Verification planned for release 1** (none of it exists):

- Simulation invariants:
  - with deficit round robin and the rotation within a tenant, an instance at position k in its tenant's queue takes
    its turn within ⌈k / q⌉ rounds, where q is its tenant's quantum; a round is at most the sum of the quanta of the
    tenants with ready instances, in turns, spread over passes of at most the budget. Instances that become ready later
    join behind it and do not move it back. A reset drops the queues, and the bound starts again from the new owner's
    first pass;
  - a refusal writes no row;
  - an internal send is never refused.
- Mutations: ordinal selection under a budget (starvation), an unbounded pass, and a store-side refusal of a sent
  message (a rolled-back batch).
- A two-tenant stress test on PostgreSQL: one tenant bursts 100,000 starts and the other trickles. It records the
  quiet tenant's start-to-first-turn latency at the 99th percentile and its refusals, with admission on and off.

## Decisions (owner, 16–17 November 2026)

The owner took these on 17 November; the [decision register](decisions.md#the-seven-design-now-answers-1617-november) lists them with
the rest of the kernel's. Rejected options are kept for the record.

1. **Running-instance quotas in release 1: none (a).** Rates and the ingress threshold only. Rejected: (b) soft quotas
   from a projected count per tenant; (c) hard quotas through a quota actor per tenant.
2. **The backlog estimate goes on the virtual partition's fence row (a).** Rejected: (b) the node heartbeat row.
3. **A turn above the send cap fails and the instance parks (a).** Rejected: (b) no kernel cap.
4. **`InboxFull` exempts only the commands that shed load (a):** `Suspend`, `Terminate`, `Delete` and discard of parked
   work, under an operator rate; `Resume` and retry obey `InboxFull`. Rejected: (b) exempt every control command and
   operator action; (c) no exemption.

## Contract and evidence

The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md) states the question and the release 1
scope. The [storage model](storage-model.md) gives the poll and the inbox records, and [ownership and
fencing](ownership-and-fencing.md) the fence row this page extends. The prototype's
[DESIGN.md](../../prototypes/kernel-model/DESIGN.md), [FINDINGS.md](../../prototypes/kernel-model/FINDINGS.md) sections 9
and 19, [PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md) and
[ETL.md](../../prototypes/kernel-model/models/Etl/ETL.md) hold the built and measured evidence cited here.
