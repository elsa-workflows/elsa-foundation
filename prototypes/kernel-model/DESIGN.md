# Execution kernel: design as built (the kernel model in three layers, 12 November 2026)

Status: the design of the kernel model after round 4 stage A, which split the merged kernel into three layer projects
and renamed step to turn and off-step work (run) to background job, at the owner's decisions of 12 November. Stage A
changed no behaviour: every gate gives the numbers it gave before (FINDINGS section 24). The source of design intent is
draft 5 of the kernel sketch (three layers, partitions only, deployment profiles, persistence as an abstraction,
synchronous HTTP, trade-offs). This text says what the code is; where the code and draft 5 differ, the section "Where
the code and draft 5 differ" says which and why. What was found, with the seeds and traces, is in
[FINDINGS.md](FINDINGS.md) (section 24 is stage A, section 23 the merge), [ENGINES.md](ENGINES.md),
[MONGO.md](MONGO.md) and [NATIVE.md](NATIVE.md).

Words: a **turn** is one call of an instance's code with the messages that are due, ending in its commit; a
**background job** (job) is work a turn starts outside itself, whose result comes back as a message; a **pass** is the
runtime's call into a partition, which runs the turns that are due and commits them in batches.

The kernel is 2,240 lines in three projects: `src/Kernel.Runtime` 279, `src/Kernel.Durable` 1,680 (of which 254 are
the in-memory store) and `src/Kernel.Workflows` 281. Before the split, `src/Kernel` was 1,758; the difference is the
seam's interfaces, `PartitionRuntime`, the durable half of a turn (`DurableTurn`) and their comments. Around it: the EF
store and its four dialects, the MongoDB store, the store conformance suite, the deterministic simulation, and the
native model.

## Requirements (the owner's, 12 November)

- Very fast, elegant, well architected, clean code.
- Runs Elsa native workflows.
- Runs other execution models: BPMN, ETL, agent coordination, and models nobody has thought of yet.
- Persistence is an abstraction. Entity Framework Core (SQLite, PostgreSQL, SQL Server, MySQL) is one provider; any
  other store, including a document store such as MongoDB, must be possible.
- The actor model is its own layer, and a workflow instance is one kind of virtual actor on it. The built-in actor
  runtime can be replaced through a public seam.
- Synchronous HTTP: a user can design a workflow that handles an HTTP request and produces the HTTP response itself.
- Unreleased code: any API and any ADR may change.

## Three layers

| Layer | Owns | Project and types |
|---|---|---|
| 1. Actor runtime | Placement of scopes on nodes, activation and deactivation, one pass at a time per scope, wake-ups between nodes. No guarantee about messages or state | `src/Kernel.Runtime`: the seam (`IActorRuntime`, `IScopeHost`, `IScopeActivation`), the wake signal (`IWakeSignal`, `InProcessWakeSignal`), membership (`IMembership`, `Cluster`), and `PartitionRuntime`, the built-in runtime (fair share from `node` heartbeats, activations, passes). Also `InstanceRef`, `Flaw` and `Defaults` |
| 2. Durable actors | The durable inbox, a turn's state and messages committed atomically, the fence, timers, background jobs, events, poison, the journal, live output | `src/Kernel.Durable`: `Worker` (one partition's `IScopeActivation`), `DurableHost` (the `IScopeHost`), the seam to layer 3 (`IActorType`, `TurnInput`, `TurnOutcome`, `Delivery`, `DurableTurn`, `ITurnServices`), `IKernelStore` and `InMemoryStore`, `Projector`, `Requests` and `IReplyTransport`, messages and kinds |
| 3. Actor types | What an actor does with its messages | `src/Kernel.Workflows`: `WorkflowInstance`, the one actor type (definition and version, status, parent and children, cancel of a tree, suspend, resume, terminate, rebind, delete, the history envelope), and the execution-model contract it runs (`IExecutionModel`, `ITurn`). Models: the simulation's toy models, `NativeModel` |

Dependencies point downward only: `Kernel.Workflows` references `Kernel.Durable`, which references `Kernel.Runtime`,
which references nothing. Where a clean cut would have changed behaviour or round trips, the code keeps what it had and
FINDINGS section 24 lists the leak: one row for the instance (layer 3's columns beside layer 2's), the commands'
message kinds and the instance status in layer 2, the lease in the take that is also the fence, the flaws in layer 1.

Layer 2 is ours whatever the runtime: Orleans grain calls are at most once and a grain saves state and sends messages
apart; Proto.Actor's messages are not durable. So layer 1 only decides where a scope runs; every guarantee comes from
layer 2, and its fence catches the second activation a runtime may briefly allow.

### The seam between layers 1 and 2

```csharp
public interface IActorRuntime                     // layer 1
{
    int ScopeOf(InstanceRef actor);                // partitions only: the actor's partition
    ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken = default);
    ValueTask WakeAsync(InstanceRef actor, CancellationToken cancellationToken = default);   // a hint: there may be work
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IScopeHost                        // implemented by layer 2: DurableHost
{
    ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default);   // takes the fence, or declines
}

public interface IScopeActivation : IAsyncDisposable    // layer 2: Worker
{
    int Scope { get; }
    bool Active { get; }                           // false once the fence is lost or the scope given up
    ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default);   // the next pass reads the inbox
    ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default);              // one pass; when to call again
}

public interface IWakeSignal                       // layer 1, pluggable; not durable, not needed for correctness
{
    void Send(InstanceRef actor);
    void Listen(Action<InstanceRef> woken);
}
```

- **Partitions only** (the owner's decision of 12 November): a scope is a partition, so there is no granularity and no
  sweep. Orleans hosts each partition as a grain (`runtime/Kernel.Runtime.Orleans`, below).
- **Activating a scope takes the fence.** `DurableHost.ActivateAsync` asks the partition's worker to take it (a take
  bumps the epoch and starts from empty memory) and returns the worker, or null when the store refuses. Disposing the
  activation releases the partition. Layer 2 never trusts the runtime: two activations of one scope on two nodes
  leave one writer, because the fence lets one of them commit.
- **One pass at a time per scope.** `PartitionRuntime.PassAsync` calls `RunAsync` and never two at once for a scope.
  A pass reads the inbox if it was woken, loads what is missing, reads kept events, runs every turn that is ready in id
  order and flushes; if no commit renewed the lease for a third of it, it beats (an empty fenced commit). It returns
  zero while there is work, otherwise the sooner of the next timer it holds and the next beat (never zero), and null
  once the scope is not held: a runtime that calls again when asked keeps an idle scope's lease. A cancelled token
  stops it at a turn boundary, before its commit: a process that goes away.
- **The wake signal is a hint; polling is the guarantee.** `WakeAsync` sends over the signal; a runtime that hears it
  for a scope it holds calls `WokenAsync`, and the next pass polls. A lost hint costs latency until the host's poll
  interval, when the host calls `WokenAsync(null)`. The built-in signal is a call within one process.
- **No clock, thread, timer or random number** in `PartitionRuntime`, `Worker` or the actor type: the host decides when
  a balancing round, a pass or a poll happens, and gives the worker its monotonic clock (`WorkerOptions.Monotonic`,
  which defaults to `Environment.TickCount64` for hosts that pass none; the simulation always passes its own).
- **Who uses what.** The simulation is the runtime of its processes: each has a `PartitionRuntime` over a
  `DurableHost`; takes, releases and balancing go through it, and its settling loop wakes each scope over the signal and
  runs whole passes. During faults it still calls the pieces of a pass (poll, load, turn, flush, beat, meet) as separate
  seeded actions, so that a seed keeps its schedule. The EF, MongoDB, conformance and native hosts own one or a few
  partitions without balancing: they activate through `DurableHost` and run passes with `RunAsync` in their own loop
  (on purpose: FINDINGS section 26, L13). Two runtimes run on a clock of their own, and the runtime conformance suite
  runs them both:
  - `RuntimeNode` (`runtime/Kernel.Runtime.Hosting`) is the built-in runtime of one node: one loop around a
    `PartitionRuntime` that balances, runs a held scope's pass when the last one asked, at once on a hint, and wakes
    every held scope at the poll interval. The loop runs on a scheduler of the node's own that runs one task at a
    time, so a job's continuation never runs beside a pass (workers set `WorkerOptions.JobScheduler` so that a job's
    body runs elsewhere). A stop lets the pass under way end and then releases; a kill gives nothing up.
  - The Orleans adapter (`runtime/Kernel.Runtime.Orleans`) makes each partition a grain, `PartitionGrain`, keyed by
    the partition. It activates its scope through layer 2, runs passes on one grain timer armed a tick at a time,
    asks again every `RetryEvery` while layer 2 declines, and gives the scope up when it deactivates. A wake hint is
    a one-way grain call. A keeper on every silo calls every partition grain, so each is active on some silo, also
    after one died. Orleans may activate a grain on each side of a network split; the fence lets one of them write.
- **The runtime conformance suite** (`conformance/Kernel.Runtime.Conformance`) runs ten cases, a mutation (the store's
  fence broken) and a stress test against any runtime, through the seam only. Its cases force two activations of one
  scope at once, by nodes on two sides of a split and by commits held in flight past the lease, and require that the
  fence refused the stale one; with the fence broken they must fail. The built-in runtime passes every case; Orleans
  passes every required case and differs on one reference case (a silo that joins gets no grains). FINDINGS section
  26 has the cases, the numbers and what they found.

### Deployment profiles

What varies is the persistence mode (per workflow, by its author), the actor runtime and the store. A profile is a
named preset of the last two; a workflow behaves the same under every profile.

| Profile | Runtime | Store | In the code |
|---|---|---|---|
| Embedded | Built-in, one node | SQLite typically: **1 partition** by default | `EfStore.CreateAsync()` on SQLite |
| Cluster | Built-in, partitions over nodes | PostgreSQL, SQL Server, MySQL, MongoDB: **64 partitions** by default | `EfStore.CreateAsync()`, `MongoStore.CreateAsync()` |
| Orleans | The adapter: each partition a grain | Any | `AddKernelRuntime` on a silo, then `OrleansActorRuntime.StartAsync` with layer 2's host |

Inline execution is what `Worker.Post` does: a start without a caller's key runs in the receiving process, in a
partition it owns, with no row written until the instance commits.

## The idea in five sentences

1. Layer 2 knows nothing about workflows (with the leaks of FINDINGS section 24). It runs **instances**: durable actors that own a piece of state and react to
   **messages**.
2. An **execution model** is a plug-in that turns an instance's state and its incoming messages into new state and
   outgoing messages. That is the only extension point.
3. Everything that crosses an instance boundary is a message: creating an instance, a child finishing, a timer, an
   event, a cancel, a kernel command. No code reads another instance's state.
4. Under the built-in runtime instances are grouped into **partitions**. One node owns a partition at a time and is its
   only writer, so the hot path needs no reads, no per-instance lease and no per-instance version check.
5. A **commit** writes everything a turn changed in one transaction, and the owner batches the commits of many
   instances into one.

**The inbox alone says what is owed.** Anything the kernel still has to do for an instance after a commit (continue,
run background jobs, read kept events, publish) is a message in that commit. A new owner needs nothing but the rows.

## Persistence

The kernel depends on one storage contract, `IKernelStore` (`src/Kernel.Durable/Store.cs`), stated by what each operation
guarantees. A provider is correct when it passes the **store conformance suite** (`conformance/`): 57 semantic cases,
a concurrent stress test with injected connection failures and the provider's own connection killer, and the cost runs.

| Operation | Guarantee |
|---|---|
| `TakeAsync`, `ReleaseAsync` | A conditional take that bumps the epoch; a take from another owner is a death and adds one attempt to exactly the rows on trial at that owner's last epoch, in the same transaction |
| `CommitAsync` | The fence (epoch and position) and everything else of the batch, atomic. A fence that matched nothing answers `Landed = false`, **whatever else in the batch would have failed** (E1, M5). Counts are checked: an update at the wrong revision, a consumed, done or held message that is not there, a delete at the wrong revision |
| `PollAsync` | A plain read of a partition's unseen, undone, due rows in order-key order, with the store's time |
| `AddAsync`, `PublishAsync`, `SubscribeAsync` | Create-only by id: a second `AddAsync` answers false; `PublishAsync` and `SubscribeAsync` are idempotent |
| `LoadAsync`, `FindSubscriptionsAsync`, `FindEventsAsync`, `ReadJournalAsync` | Reads by key |
| `HeartbeatAsync` (`IMembership`, the built-in runtime's) | The node's row, the nodes heard from within a window, the partitions nobody holds |
| `ProjectedAsync`, `ProjectAsync`, `ReadViewsAsync` | The projector's own row and the partition's position; a move of the projector's row only from where it was read, with the view rows, in one transaction; the viewer's read |
| `SweepAsync` | Delete done outside commands and events whose window passed |
| Failures | Unknown (reset), transient (try the same batch again), definite (commit one by one, park the one that fails). When a provider cannot tell, unknown (M4) |
| Order key | A later send from the same sender gets a larger key (M1). An identity column on SQL; on MongoDB a clock in the sender's partition document, cut under the fence |

| Provider | How | Evidence |
|---|---|---|
| In-memory | A lock; the reference, and the simulation's store | Suite, simulation |
| EF Core, PostgreSQL | The fence as its own statement (3 round trips) or as concurrency tokens in the one `SaveChanges` (2) | Suite, stress, cost |
| EF Core, SQL Server | `Engine` dialect: `OUTPUT`, `MERGE`; **RCSI on** (E5); an index for the take (E8); `MaxBatchSize(1000)` (E7); **the checked fence by default** (E9) | Suite, stress, cost |
| EF Core, MySQL | Dialect: no `RETURNING`, `INSERT IGNORE`, upserts; the take at READ COMMITTED (E8); `MaxBatchSize(1000)` | Suite, stress, cost |
| EF Core, SQLite | One writer; `seq` assigned under the write lock; BUSY is transient | Suite, stress, cost |
| MongoDB 8.0 | A replica set; the fence then one ordered client-level bulk write in a transaction; an upsert fence at index 0 when it rides in the batch; abort before reporting a failure (M2) | Suite, stress, cost |

The EF store's default fence is the one decided on 12 and 13 November: in the batch on every engine but SQL Server,
whose default is the checked fence (`Engine.FenceInBatchByDefault`; E9). Either can still be chosen. The MongoDB store
keeps its own default, the checked fence. Every provider turns EF's savepoints off (E4). The set-based deletes of a terminate and a delete are statements of
their own on EF, after the batch, so that with the fence in the batch they run only after it held; on MongoDB they are
in the one bulk write.

## Data

| Table | Row | Notes |
|---|---|---|
| `partition` | id, owner, epoch, lease_until, position, died | `position` counts journal rows written. `epoch` changes only when ownership is taken. Times are database time in milliseconds |
| `node` | id, seen_at | A heartbeat per process start, for the fair share. Liveness only |
| `instance` | id, partition, model, definition, status, state, state_version, history, suspended, jobs, children, revision, parent, fault | `state` is opaque to the kernel. `jobs`: the background jobs in flight; `children`: the instances it started that have not finished. Layer 2's columns and layer 3's in one row (FINDINGS section 24) |
| `message` | seq, id, partition, target, kind, payload, sender, reply_to, start, due, seen, trial, attempts, keep_until, done | The inbox. `seq` is the order key; `id` makes adding idempotent; `due` makes a timer; `start` creates the target; `reply_to` is a caller on some node |
| `subscription` | key, instance, wait, partition, start | A wait of an instance, or with a start spec and no instance a definition an event starts |
| `event` | key, id, payload, keep_until | Published events, for waits that ask for kept events and for start subscriptions registered later |
| `journal` | partition, position, instance, revision, entries | One row per instance-commit, at its history level; a delete's own row has revision -1 |
| `projection` | partition, position | The projector's position: its own row, apart from the fence |
| `view` | instance, partition, revision, commits | The viewer's row per instance |

Indexes beyond the keys: messages `(partition, seq)`; on SQL Server and MySQL `(partition, trial)` for the take (E8);
subscriptions `(instance)` and journal `(partition, instance)` for terminate and delete (the merge, as E8).

## The commit

The owner of partition `p` at epoch `e`, which believes the position is `n`, commits `k` instance turns in one
transaction: the fence `UPDATE partition SET position = n + j, lease_until = ... WHERE id = p AND epoch = e AND
position = n` (`j` the journal rows written), checked before anything else is sent, or carried as concurrency tokens
in the batch; the instance rows at their revisions (inserts, updates, deletes); consumed messages deleted, outside
commands marked done, held messages taken off trial, polled messages marked seen or put on trial; sent messages
inserted; subscriptions inserted and deleted; journal rows at `n + 1 .. n + j`; then the set-based deletes of a
terminate (every wait of the instance) and a delete (its journal rows); then COMMIT.

- **Two round trips, not one** on PostgreSQL with the fence in the batch (three with it checked). SQL Server adds one
  for BEGIN; MySQL's driver four more (E10); MongoDB is the same as PostgreSQL.
- **A fence that matched nothing wins:** with the fence in the batch, a stale owner meets its successor's rows (a
  journal position, a message id) before the fence is checked. After a failure that says the batch did not happen, the
  store reads the partition row; if epoch and position moved, the answer is "not landed" (E1, M5). A conformance case
  checks it for round 3's writes too.
- **The outbox is the recipient's inbox.** A message to another instance is a row inserted in the sender's
  transaction. A recipient in the same partition is also handed it in memory after the commit, once the owner has
  caught up (F1).
- **What a failed commit means:** unknown, or the fence matched nothing: reset. Transient: the same batch again.
  Definite: commit one by one and park the instance that fails.

## Ownership

- **Take** (`PartitionRuntime.ActivateAsync` → `DurableHost` → `Worker.TakeAsync`) bumps the epoch; a take from another owner can only succeed because its lease expired. Every take starts
  from empty memory. **Reset** is the same path as start-up.
- **The lease is 10 s by default** (`Defaults.Lease`, the owner's of 12 November). A takeover waits for it, so
  failover takes about one lease. The simulation and the stress tests use short leases of their own.
- **Leases decide who should write. The fence decides who can.**
- **Fair share** (`PartitionRuntime.BalanceAsync`): a node beats, counts the nodes heard from, releases what is above its share and takes free
  partitions up to it. The number of partitions is fixed when the store is created: 64 on a server database, 1 on
  SQLite by default.
- **A node's name is unique per process start** (F9). One process owns several partitions and dies for all of them,
  which matters for poison (F11).

## Messages

- **Adding** is an insert anyone may do; the id makes it idempotent. **A message from outside carries `keep_until`**:
  its consuming commit marks it done, so a retry inside the window is refused.
- **Polling** is a plain read; the owner skips ids it holds and ids consumed while the poll was in flight (F5).
- **Marked before run** (F3): a polled message is not acted on until a fenced commit marked it seen at this epoch.
- **Order:** a target's messages are handled in key order, so one sender's messages to one target arrive in the order
  sent.
- **Timers** are messages with a future `due`. The poll returns database time; the owner anchors it and schedules on a
  monotonic clock. **A turn takes a timer back with `Recall`**: the row is deleted with the turn's commit, like a
  cancelled job's ticket (merge, F-N4).
- **Redelivery:** `ITurn.Redelivered(message)` is true when the row was seen at an earlier epoch, so a turn that took
  it may have run and its result been lost (merge, F-N6). "May have run", never "did run".
- **Poison:** suspects (rows another owner had) are tried apart, alone in the process (F2, F11), one at a time or in
  halving groups; a take that finds a death adds one attempt to what was on trial; at the threshold the message is
  parked with its instance. A trial starts only when its turn can run at once (F13); a suspect of a withheld instance
  is shelved (F12). A turn that throws parks its instance at once.
- **Hold:** a turn hands a message back; it keeps its place and comes with the next turn.
- **Edge cases:** a message to a missing target without a start spec is dropped and journalled; to an ended instance,
  consumed and dropped.

## Kernel commands

Messages anyone may add, handled by the actor type alone (`IActorType.Command`; for workflows `WorkflowInstance`), in
order and without any model seeing them. Layer 2 delivers them apart (they pass a withheld instance, go alone, are never
suspects), which is why their kinds are named in layer 2. **Suspend and resume** (deliver nothing but kernel
messages meanwhile); **terminate** (the instance ends Cancelled, its background jobs are cancelled, its waits deleted, its parent
told, a terminate goes to every child in `children`, in any partition, after the child's start); **delete** (an ended
instance's row at its revision, its waits and journal rows; its own journal row removes the view).

## Creating instances and placement

- **A message may carry a start spec**; the owner creates the target when it delivers it.
- **A start without a caller's key** is placed in a partition the starting node owns (`Worker.Post`); one with a key
  hashes to a partition and pays a hop.
- **A child** goes into its parent's partition by default, or is spread. **A detached start**
  (`Start(..., detached: true)`) has no parent: it reports to nobody and a terminate does not go on to it; its id is
  minted by the turn, so it needs no read (merge, F-N5).

## Events

- **Publishing** inserts the event with its window, then reads the subscriptions of its key and adds one delivery per
  match, with an id derived from event and wait. A turn publishes with `ITurn.Publish`: a ticket to self in the commit,
  which the owner carries out after the commit lands (N1).
- **A wait gets the events published from its commit on.** That is the owner's default of 12 November: published
  events are not kept for a wait unless it asks. `Subscribe(key, wait, keep: true)` also gives it the events still
  kept from before: only such a wait carries the "read the events" ticket, which costs a read and a commit (F6, F-N8).
  BPMN-style correlation, where a message may arrive before its catch event, is what it is for.
- When a kept wait and an event cross, each side looks after its own commit, so at least one sees the other, and the
  derived id removes the double.
- **Starts on an event:** a subscription with a start spec, registered from outside; the registrar inserts it and then
  reads the kept events of its key. One instance is created per event and subscription, whichever side delivers.
- `Events.Delivered(message)` says which event and which wait a delivery is for (merge, F-N3).
- Correlation as an ordinary instance per key is a model for narrow, quiet keys (FINDINGS section 8).

## Instances in memory

The owner keeps active instances in memory; a turn reads nothing. Loads are batched; above `MaxInstances` the least
recently used idle instances are dropped. A row written at a newer state version than this node's model is withheld
like a suspended one (F14).

## The execution model contract

Layer 3's (`src/Kernel.Workflows/Contracts.cs`). The workflow instance runs a model for everything it does not own
itself; what is durable in a turn (sends, jobs, holds, waits, live output) it hands to layer 2's `DurableTurn`.

```csharp
public interface IExecutionModel
{
    string Key { get; }
    Type StateType { get; }
    int StateVersion { get; }
    object CreateState(StartSpec start);
    object UpgradeState(string state, int fromVersion);
    ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken);
    ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken);   // a background job
}

public interface ITurn
{
    InstanceInfo Instance { get; }
    IReadOnlyList<Message> Inbox { get; }
    object State { get; set; }
    object? Ambient { get; }                                  // from the starter on this node; this activation only
    void Emit(string channel, object? item);                  // live; released after this turn's commit
    string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null);
    void Recall(string message);                              // take back a message to self not yet received
    InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false);
    void StartJob(string key, object? arguments = null);
    void CancelJob(string key);
    void Hold(Message message);
    void Subscribe(string key, string wait, bool keep = false);
    void Unsubscribe(string key, string wait);
    bool Redelivered(Message message);                        // may a turn with it have run before?
    string Publish(string key, string? payload, TimeSpan keep);
    void Record(object entry);
    void Rebind(string definition);
    void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null);
}
```

- **A turn commits at its end and only there.** A durable boundary is a turn that sends itself "continue". That is all
  a persistence mode is. An ephemeral instance that never waits commits nothing.
- **Background jobs** (`StartJob`): the intent is a ticket in the commit (kind `kernel.job`); the job may execute more
  than once, its result (`job-finished`) changes state once; a result goes ahead of other messages, so a model never
  sees its own cancel first.
- **Models compose through messages only.** A parent starts a child of any model and gets `child-finished`.
- **How the known models sit on it:** native (built: activity frames, agenda, waits and variables as state; slow
  activities are background jobs; NATIVE.md); BPMN (tokens; tasks are jobs; kept waits for early messages; start subscriptions for
  message start events; `Recall` for boundary timers that do not fire); ETL (cursors; children spread over partitions);
  agents (a checkpoint; tool calls are jobs; streamed output is emitted); durable entities (operations with a reply-to).

## Synchronous HTTP requests

A workflow handles a request and writes the response itself, as live output. Built in the kernel (round 3) and used by
the native model (merge):

- **A request that starts a workflow:** the receiving node finds the workflow in an in-memory route table, places the
  instance in a partition it owns, and hands the start over in memory with its reply address and the request as
  `Ambient`. Ephemeral and done in one turn: zero database calls.
- **Release:** a response is released after the commit of the turn that wrote it, or at once if the turn writes
  nothing (the owner's default of 12 November). **A route may opt out** (`HttpEndpoint.RespondBeforeCommit` in
  native): the opt-out rides on the caller's reply address, and that caller gets the turn's output when the turn ends.
- **Respond, then continue; wait first** (a timer, a background job): the request stays open, the instance stays in
  memory, up to the request's deadline.
- **A request that resumes a waiting workflow** adds a delivery carrying its reply address; the owner, on any node,
  sends what the turn emits, and the end, over the runtime's transport (`IReplyTransport`), which is not durable.
- **The caller** (`Requests`) takes the first response or end and ignores the rest. A reset tells every caller it knows
  "lost" (503). An owner lost on another node is noticed only by the deadline (504).
- Disconnect and timeout end the wait and leave the instance alone.

## History and the viewer

One journal row per commit, in the commit, at the history level (Full, State, Minimal: no row). Projectors on any
node expand the journal into view rows in position order, moving their own row only from where they read it, and pass
a position that is missing below the partition's position: it was deleted, not written late (F15).

## Invariants (what the simulation checks)

1 one writer; 2 atomic turn; 3 no lost message; 4 once in state; 5 no early or lost timer; 6 journal order; 7 takeover;
8 index matches state; 9 memory matches rows; 10 create once; 11 order; 12 background jobs; 13 events meet (for waits that ask for
kept events: an event and a wait both committed within the window are delivered once; for the others, an event
published after the wait committed is delivered once); 14 one bad instance; 15 callers; 16 suspended; 17 termination;
18 state versions; 19 rebind; 20 event starts; 21 memory bound; 22 delete; 23 fair share. With native instances in the
mix: N1 an activity completes once, N2 a join fires once, N3 a child starts once and resumes once, N4 a Durable
External effect runs only under a committed attempt, N5 final variables equal a run without faults, and N6 (merge) an
External effect that runs again is told that its turn may have run before.

## What it costs (measured on the model)

Round trips per run, counted on the TCP stream (ENGINES.md); SQLite in engine calls. Fence checked / in the batch.
All run on the merged kernel (FINDINGS section 23); stage A's layered kernel gives the same counts (section 24:
PostgreSQL's and SQLite's cost runs, row for row).

| Run | PostgreSQL | SQL Server | MySQL | MongoDB | SQLite |
|---|---:|---:|---:|---:|---:|
| Ten no-ops, ephemeral, started on the owner | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| Ten no-ops, Balanced, on the owner | 3 / 2 | 4 / 3 | 7 / 6 | 3 / 2 | 5 |
| Ten no-ops, Durable, on the owner | 30 / 20 | 40 / 30 | 70 / 60 | 30 / 20 | 77 |
| Ten no-ops, Balanced, from another node | 9 / 7 | 11 / 9 | 23 / 21 | 10 / 8 | 13 |
| Parent with three children, one partition | 12 / 8 | 16 / 12 | 28 / 24 | 12 / 8 | 57 |
| Terminate a parent with three waiting children, from another node | 17 / 13 | 21 / 17 | 35 / 31 | | 41 |
| Delete that parent, from another node | 9 / 7 | 11 / 9 | 19 / 17 | | 13 |
| A start subscription and an event that starts an instance | 13 / 11 | 15 / 13 | 35 / 33 | | 17 |
| 100 Balanced instances started at once | 3 / 2 | 4 / 3 | 7 / 6 | 3 / 2 | 203 |
| 1,000 Balanced instances started at once | 4 / 5 | 13 / 12 | 8 / 8 | | 2,003 |

Native workflows on PostgreSQL (NATIVE.md's runs, merged kernel): Balanced writeline sequence 3 / 2; http-reference
Ephemeral 0, Balanced 3 / 2; fork-join with a direct resume 11 / 8 (15 / 11 on the native branch: no meet ticket);
resume at an HttpEndpoint, both requests, 12 / 9 (16 / 12).

- The model's activities do nothing and its history is one journal row per commit. A projector round costs one
  statement per instance in its page (17 round trips for about a dozen instances); not optimized.
- MySQL's driver sends four more round trips per transaction than PostgreSQL's (E10).

## Trade-offs

What the design gains, what it pays, and the lever. The owner decides the defaults; those decided on 12 November are
marked.

| # | Gain | Price | Lever |
|---|---|---|---|
| 1 | No reads while a workflow runs | Memory per active instance; the whole state rewritten per commit | Bound on instances held; values by reference |
| 2 | One writer per partition: no locks, many instances per commit | Failover takes about one lease; a hot partition has one writer; the partition count is fixed at creation | Partitions (64 on servers, 1 on SQLite: decided); lease (10 s: decided) |
| 3 | An idle owner needs little traffic | A heartbeat per owned partition about three times per lease, plus one node beat | Lease |
| 4 | Batching under load | A commit waits for the one before it | Rows per flush |
| 5 | Balanced mode: one commit per wait | Activities since the last commit run again after a crash; `Redelivered` says when they may have | Persistence mode per workflow |
| 6 | A background job does not block the instance | It can execute more than once | None: inherent |
| 7 | The viewer costs nothing on the hot path | The viewer lags the run; projector writes cost calls | History level |
| 8 | Instances talk only through messages | No reading another instance's state | None: the design |
| 9 | State is a snapshot | No rebuilding a run from history; old states need upgrade code | State versions |
| 10 | State is opaque: any model fits | No search inside state | A model's summary or index |
| 11 | A message from outside costs little | A mark commit before its turn; a hop when another node owns it | Poll interval; notifier |
| 12 | One shared database | Its write throughput is the ceiling | Later |
| 13 | Any store | Only the contract can be used; each provider needs tuning (ENGINES.md, MONGO.md); MongoDB needs a replica set | Conformance suite |
| 14 | The runtime can be swapped | Every runtime hosts partitions (decided); one grain per instance would need an owner epoch per row and a sweep | One runtime; adapters on demand |
| 15 | A poison message cannot walk the cluster | Slow recovery of a deep backlog; rare innocent parking | Threshold; trial group |
| 16 | A response is never sent for lost work | It waits for its turn's commit (2 to 3 round trips); a resume elsewhere pays a hop | Opt-out per route (decided) |
| 17 | A wait sees only events published after it (decided): Elsa's meaning, and no meet ticket | An event that crosses a plain wait's commit is lost to it | `keep` per wait |
| 18 | A clean break | No migration of runs; two cores until the switch | None: accepted |

## Where the code and draft 5 differ

| Draft 5 says | The code | Why |
|---|---|---|
| `IActorRuntime` has `Granularity`; `ScopeId`, `ActorId` | No granularity; a scope is an `int`, the partition; an actor is an `InstanceRef` | Partitions only (the owner's decision of 12 November); the ids are the ones the kernel had |
| `IScopeActivation` has `WokenAsync` and `RunAsync` | Also `Scope` and `Active`, so a runtime can see that layer 2 lost the fence | The runtime must know when to activate again; nothing else changed |
| The runtime polls; a wake-up is a hint | `PartitionRuntime` has no timer: its host calls `BalanceAsync`, `PassAsync` and `WokenAsync(null)` when its poll interval passes | No clock or thread in the kernel, so the simulation decides every moment |
| Layer 3 owns the workflow instance's data | One `instance` row with both layers' columns; the command kinds and `InstanceStatus` in layer 2 | One write per commit; see FINDINGS section 24 for every leak |
| An Orleans adapter and a runtime conformance suite | Both built; the seam gained what they needed: a pass renews an idle lease, a new activation is woken, and where a job's body runs | FINDINGS section 26 (R1, R4, R14) |
| Workflow instance owns children, cancel of a tree, the history envelope | `WorkflowInstance` does (layer 3); layer 2 carries `Unsubscribe`, `Delete` and the journal entries it is given | Built in stage A from round 3's code |
| `ITurn.Ambient` is `IAmbient`; `Record(JournalEntry)`; `Describe(state)` for lists | `object?`; `Record(object)`; no `Describe` | Not needed by any model built; the envelope is not designed |
| `Emit(channel, object item)`, "a workflow can opt out and respond before its commit" | Opt-out per **route**, on the caller's reply address | The owner's default of 12 November; a route is the HTTP layer's notion, so the model needs nothing |
| "Publishing inserts the event; subscribing sends a read-the-events message" | Only a wait that asks for kept events carries that message | The owner's default of 12 November; F-N8, F-N9 |
| `ITurn` has no `Publish`, `Recall`, `Redelivered`, detached start, `keep` | It has them | Native needed them (N1; F-N3 to F-N6); each is small and on the existing ticket and commit mechanism |
| "`seq` is assigned by the database" (Data) and "an order key per partition" (Persistence) | Per sender: a later send of a sender has a larger key | M1: MongoDB has no identity column; the contract needed only that |
| "Everything else is definite" (failures) | Unknown when the provider cannot tell | M4: an interrupted operation would park an innocent instance |
| The store contract lists nine guarantees | Plus heartbeat, start subscriptions, projector row, view rows, delete, held messages | Round 3 built them; the suite tests each on every store |
| Owner lost while a request waits: 503 | 503 on the receiving node; 504 at the deadline when the owner was on another node | No signal from owner to receiving node yet |
| Lease and partition count left to the owner | 10 s; 64 on servers, 1 on SQLite | Decided 12 November |

## What the tests did and did not show

Run on the merged kernel (FINDINGS section 23) and again on the layered kernel of stage A (section 24), with the same
numbers.

- **Simulation:** 200,000 runs of 300 actions and 20,000 of 1,000 pass, without and with native instances; 12 and 15
  innocent messages parked at the threshold in the 1,000-action runs, which invariant 14 allows.
- **Mutation check:** 39 mutations at 20,000 runs (37 kernel flaws alone and two in combination): all caught but the two that another rule covers, at the first
  failing seeds round 3 recorded; the five native flaws are caught too.
- **Store conformance suite:** 57 semantic cases pass (required) on in-memory, SQLite, PostgreSQL, SQL Server, MySQL
  and MongoDB, with either fence; the reference cases differ where the contract is silent (M6, F8).
- **Stress:** 40 s and 5,000 outside messages with killed sessions, stalls and takeovers, including round 3's commands
  and starts on events: every property holds on PostgreSQL, SQL Server (RCSI, checked fence), MySQL, MongoDB and
  SQLite. With the fence broken on purpose every engine fails (26 to 611 violations).
- **Runtime conformance suite** (FINDINGS section 26): the built-in runtime on a clock and the Orleans adapter on
  Orleans' in-process test cluster pass every required case and the stress test, including two activations of one
  scope at once on two sides of a split, which the fence refuses; with the fence broken both fail.
- **Not tested:** a runtime on a store other than the in-memory one (the suite takes any provider); Orleans across
  processes or machines; a three-node MongoDB replica set or any failover; SQL Server on native
  hardware; MySQL's SERIALIZABLE and SQL Server's SNAPSHOT; the stress test with native instances; the projector and
  the heartbeat under the stress test (they run in the simulation, the suite and the cost run).

## Open questions

- **The seam to layer 1** is built and proven by two runtimes (FINDINGS section 26). Open: a way for layer 2 to ask
  its runtime for a pass when a job finishes (R12); a leave in membership (R5); a lock in `TrialGate` for runtimes that
  run a node's scopes at once (R6); backing off a declined activation (R7).
- **Poison counting** (FINDINGS sections 7, 21): innocent parking is rare but not zero; recovery of a backlog is serial.
- **Kept events for plain waits:** the publisher still inserts every event with its window, because a kept wait may
  subscribe later; a deployment where no wait keeps could skip the insert. Not built.
- **Reply addresses** should carry the caller's deadline; an owner on another node cannot tell a waiting node it was
  lost.
- **Delete and keys:** a start after a delete creates a new instance with the same id.
- **State size**, **the notifier between nodes**, **one database**: as in draft 5.
- **MySQL's driver** costs four round trips per commit more than PostgreSQL's (E10).
