# Execution kernel: clean-slate design sketch (draft 5, 12 November 2026)

Status: **proposed, not approved; no product code exists.** The owner decisions recorded below are inputs to the design proposal and its review; they bind the build once the design is approved. History: draft 1 had two first-principles reviews (protocol and model contract); both said "right shape, needs changes". Draft 2 was built as a standalone model and attacked by a deterministic simulation and a PostgreSQL stress test, which found eight more faults. Three more rounds and a merge of four branches ran it on five database engines, and round 4 split it into the three layers. The evidence is the prototype itself and its own documents (DESIGN, FINDINGS, NATIVE, ENGINES, MONGO); this sketch summarises it in "What it costs" and "What the tests show". It replaces the journal-first approach proposed as ADR 0098 in open PR #4176 (not merged).

Draft 4 separates three layers, at the owner's decision of 12 November: an actor runtime, a durable layer, and workflow instances as one actor type. Every runtime hosts partitions, so the sections that describe partitions hold for any runtime. The prototype's code was split into the three layers in round 4 stage A, with every gate unchanged. Draft 5 renames step to turn and run (off-step work) to job, at the owner's decision of 12 November; the runtime's call into a partition is a pass. It also adds the containers' semantics: Flowchart on a graph engine extracted from today's Flowchart, StateMachine on its own statechart engine, and BPMN hosted on the Bpmn.Semantics interpreter (valence-works/bpmn).

The prototype is `prototypes/kernel-model/` on main (landed through PRs #4250 to #4260). The commit hashes its documents cite are kept at the tag `prototype/kernel-model-history`.

## Requirements (the owner's, 12 November)

- Very fast, elegant, well architected, clean code.
- Runs Elsa native workflows.
- Runs other execution models: BPMN, ETL, agent coordination, and models nobody has thought of yet.
- Persistence is an abstraction. Entity Framework Core (SQLite, PostgreSQL, SQL Server, MySQL) is one provider; any other store, including a document store such as MongoDB, must be possible, even if it is not shipped.
- The actor model is its own layer, and a workflow instance is one kind of virtual actor on it. The built-in actor runtime can be replaced by another (Orleans, Proto.Actor, ...) through a public seam. Integrators who want that write the adapter; Elsa ships one runtime and keeps one reference adapter, for Orleans, in its tests.
- Synchronous HTTP: a user can design a workflow that handles an HTTP request and produces the HTTP response itself.
- Unreleased code: any API and any ADR may change.

## Decisions taken so far (12 November)

Owner decisions on this proposal. They become binding when the design is approved; changing one is an owner decision.

**Kernel defaults:**
1. **Activity side effects:** at least once in Balanced; idempotency keys are documented. Durable mode narrows the window, but no mode prevents a repeat after a crash between an effect and its commit: effects that must not repeat need idempotency keys or reconciliation.
2. **History level:** Full by default; a busy workflow may choose State only.
3. **Lease:** 10 seconds, so failover takes about 10 seconds and idle load stays low.
4. **Partitions:** 64 by default on server databases, 1 on SQLite.
5. **HTTP response:** released after the commit of the turn that wrote it; opt-out per route.
6. **Reads across instances:** none; questions to another instance go as messages or through projections.
7. **Events:** not kept by default. Keeping is switched on per subscription; BPMN message correlation uses it.

**Optimisation order:** correctness, then database calls per workflow, then latency, then flexibility.

**Walkthrough decisions:**
1. **Orleans:** hosts each partition as a grain. One grain per instance, the per-actor fence and the sweep are dropped (see "The seam").
2. **Commit fence:** in the batch by default, using EF concurrency tokens: 2 round trips on PostgreSQL, the lease time from the node clock. The fence as its own statement stays an option. **SQL Server (13 November):** the fence as its own statement is SQL Server's default, because the fence in the batch deadlocks takes there (E9, below).
3. **Wake-ups between nodes:** a pluggable wake signal over the runtime's transport; polling remains the guarantee.
4. **Short waits of an Ephemeral instance:** short timers and jobs stay in memory up to a threshold (the route timeout or a few seconds), so request-response workflows stay at zero database calls. Not built yet.
5. **Events:** the subscription and event tables stay the kernel's one mechanism; a correlation instance per key remains a sample model.
6. **Reply addresses:** expire at the route's timeout, carried as an absolute deadline; after it the owner drops the reply and unpins the instance.
7. **Recreate after delete:** a rule per kind of id. A generated id never comes back (the start is dropped and journalled). A caller key refuses a repeat within `keep_until` and may start a fresh run after it.
8. **Projector:** off the hot path in its own transaction, each round batched into set-based writes (about 2 to 3 round trips per round on PostgreSQL).
9. **Job poison:** jobs are counted like messages. A job goes on trial alone, and a take after a lost owner adds an attempt to its ticket; past the threshold the job is parked and its instance faulted.
10. **State size:** large values are stored by reference, and a size benchmark goes into the cost runs; segments only if measurements show the need.
11. **Viewer read path:** the API shows a run from its projected rows plus the journal tail past the projector's cursor, expanded in memory, so the view is always current. Studio may ask for a projector catch-up for the partition; rounds are coalesced. Studio never forces an owner commit or shows uncommitted state. Live watching is a push stream of committed journal entries.

**Built in the prototype so far:** decision 2 (both fence modes, and since round 4 each engine's default: the checked fence on SQL Server, the fence in the batch elsewhere), 5 (the tables) and part of 9 (job tickets are counted like messages; parking the job and faulting the instance is not built). Decision 8 (round 4): set-based projector rounds on PostgreSQL and SQLite. Decision 3 in part: round 4 added the wake-signal interface with an in-process implementation. Decision 6 (round 4): the reply address carries the deadline, and past it the owner drops the reply and unpins the instance. Decision 10: the state-size benchmark and an opt-in values-by-reference spike are built; their limits remain in the prototype reports. The rest is not built.

## Three layers

| Layer | Owns | Does not own | Replaceable by |
|---|---|---|---|
| 1. Actor runtime | Placement of actors on nodes, activation and deactivation, one pass at a time per scope, wake-ups between nodes | Any guarantee about messages or state | Integrators: Orleans, Proto.Actor, Akka.NET, Dapr actors. Built in: partitions |
| 2. Durable actors | The durable inbox; a turn's new state and sent messages committed atomically; the fence against a stale owner; timers; background jobs; events; poison handling; the journal. Over the persistence abstraction | Where an actor runs | Not replaceable. Its storage is: EF Core, MongoDB, ... |
| 3. Actor types | What an actor does with its messages. **Workflow instance** is one actor type: it holds what all workflows share (definition and version, status, parent and children, cancel of a tree, the history envelope the viewer reads, what lists show) and runs an **execution model** (native, BPMN, ETL, agents, ...) for the rest. Other actor types: event correlation keys, durable entities | Persistence, placement | Extended by adding actor types and execution models |

Why the durable layer is ours whatever the runtime: Orleans grain calls are at most once, a grain saves its state on its own with an ETag, and saving state and sending a message are never one atomic act; Orleans can also briefly run two activations of one grain when the network splits. Proto.Actor's persistence is event sourcing per actor, and its messages are not durable. So layer 1 only decides where an actor runs and wakes it up; every guarantee comes from layer 2, which works the same on any runtime.

### The seam between layers 1 and 2

Layer 2 hands the runtime a set of **ownership scopes**, one per partition, and the runtime decides which node hosts each one. A scope is a partition of many actors: one conditional update fences them all, one poll covers their inboxes, and the commits of many actors go in one transaction, which is why 100 instances commit in 2 round trips on PostgreSQL. Every runtime works this way; Orleans hosts each partition as a grain.

**Dropped on 12 November: actor granularity.** Draft 4 also allowed a scope of one actor (one grain per workflow instance), with its own fence record, its inbox read on activation, each actor committing alone at 2 to 3 round trips on PostgreSQL, and a sweep to wake idle actors. It loses batching and buys nothing the goals need. It may come back later as a new grain type, which would need an owner epoch on the instance row and the sweep. Until then, user code that wants to call an instance like a grain gets a reference that forwards to the grain of the instance's partition, with no kernel change.

A sketch of the interface; the restructuring will settle it:

```csharp
public interface IActorRuntime
{
    ScopeId ScopeOf(ActorId actor);
    ValueTask StartAsync(IScopeHost host, CancellationToken cancellationToken);
    ValueTask WakeAsync(ActorId actor, CancellationToken cancellationToken);   // a hint: there is work
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IScopeHost                                     // implemented by layer 2
{
    ValueTask<IScopeActivation?> ActivateAsync(ScopeId scope, CancellationToken cancellationToken);  // takes the fence, or declines
}

public interface IScopeActivation : IAsyncDisposable
{
    ValueTask WokenAsync(ActorId actor, CancellationToken cancellationToken);
    ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken);      // one pass: turns until idle; when to call again
}
```

- The runtime calls a scope one pass at a time and never on two nodes at once as far as it knows (Orleans calls this a turn; Temporal calls an instance's turn a workflow task). Layer 2 does not trust that: its fence catches a second activation, which is exactly the Orleans case above.
- A wake-up is only a hint: the partition's own poll is the guarantee, so a lost hint never loses work. The sweep that actor granularity needed is dropped with it.
- A **runtime conformance suite**, like the store conformance suite, runs the simulation's invariants and a stress test against any runtime adapter. The Orleans adapter must pass it, including runs where two activations of one grain are forced.

### Deployment profiles, not runtimes

Three things vary independently, and each has its own seam:

- how much a workflow saves and when: its **persistence mode**, chosen per workflow by its author;
- where actors run: the **actor runtime** (layer 1);
- what stores the data: the **persistence provider**.

There is no fourth "runtime" plug-in. A **deployment profile** is a named preset of the last two:

| Profile | Actor runtime | Storage | Typical use |
|---|---|---|---|
| Embedded | Built-in, one node, no transport between nodes | Any provider, SQLite typically | Windows service, desktop, a single API |
| Cluster | Built-in, partitions shared over nodes | PostgreSQL, SQL Server, MySQL, MongoDB, ... | Scaling out |
| Orleans | An Orleans adapter (written by the integrator; one is kept in Elsa's tests) | Any provider | Hosts that already run Orleans |
| Log (later) | Built-in, partitions as replicated-log groups | A Zeebe-like provider | Very high throughput |

A workflow behaves the same under every profile. The guarantees it gets come from its persistence mode, which its author chooses, not from the deployment. No capability flag makes a workflow fail on one profile: synchronous HTTP works on all of them.

**Inline execution** is chosen per workflow or per route. A run that a request starts without a caller's key executes in that request until its first wait, under any persistence mode, because it is placed in a partition the receiving node owns. A start with a caller's key goes to the key's partition; when another node owns it, the run executes there and the response comes back over a reply address, one hop. Inline plus Ephemeral costs no database calls and no hop. Every profile supports it, because every node owns partitions to place the new instance in.

## The idea in five sentences

1. The durable layer knows nothing about workflows. It runs **durable actors** that own a piece of state and react to **messages**. A workflow instance is one kind.
2. An **execution model** (native, BPMN, ETL, agent, ...) is a plug-in that turns an instance's state and its incoming messages into new state and outgoing messages. That is the only extension point.
3. Everything that crosses an instance boundary is a message: creating an instance, a child finishing, a timer, an external event, a cancel, an alteration. No code reads another instance's state.
4. Under the built-in runtime, actors are grouped into **partitions**. One node owns a partition at a time and is its only writer, so the hot path needs no reads, no per-instance lease and no read before a turn: the commit's `revision` condition only asserts that nothing else wrote. Every runtime hosts partitions; actor granularity was dropped on 12 November.
5. A **commit** writes everything a turn changed in one transaction, and the owner batches the commits of many instances into one.

One rule came out of the simulation three times and is now a principle: **the inbox alone says what is owed.** Anything the kernel still has to do for an instance after a commit (continue a sequence, run background jobs, look for events) is a message in that commit. A new owner needs nothing but the rows.

This is the shape proven by Temporal (shard fencing; state, history and tasks in one transaction), Zeebe (one writer per partition, batched appends, every boundary a command), Durable Task and Orleans. It is not a new protocol.

## Persistence

The kernel depends on one storage contract, `IKernelStore`, and nothing else. The contract is stated by what each operation guarantees, never by SQL. A provider is correct when it passes the kernel's store conformance suite: the same semantic tests and the same concurrent stress test, run against every provider.

What a provider has to give:

- **An atomic commit** of everything a batch writes, across instances, messages, subscriptions and the journal, all of it or none of it.
- **A fence** inside that commit: the commit lands only if the partition record still has the expected epoch and position, and two commits can never both land at the same position.
- **A conditional take** of a partition, which bumps the epoch, and a release.
- **Create-only inserts** by id, for messages, events and new instances.
- **An order key per partition** for messages, such that a later send from the same sender gets a larger key.
- **A read of a partition's messages** in key order, which sees every committed row eventually and no uncommitted one.
- **Reads by key:** instances, subscriptions, events, journal rows after a position.
- **Its own time,** in milliseconds, for due times and for checking leases at a take. With the fence in the batch on EF (the default) the owner writes `lease_until` from its own clock (the prototype's MongoDB store still takes the database's time), so clock skew costs availability, never safety.
- **Failures told apart:** unknown outcome, transient (try again as it is), definite.

The prototype's `IKernelStore` also carries the node heartbeat (for fair share), start subscriptions, held messages, delete and unsubscribing all in a commit, the sweep of expired commands and events, the projector's own row and the view rows; the conformance suite tests each of them on every store.

A relational database meets these with one transaction, a conditional update of the partition row and an identity column. A document store such as MongoDB meets them with a multi-document transaction (which needs a replica set), a conditional update of the partition document, and an order key the store assigns (in the fenced update for sends, from a per-partition clock for outside adds). The prototype's MongoDB store meets them; MONGO.md has what it cost.

The tables below are the relational shape. A document provider stores the same records as documents.

## Data

| Table | Row | Notes |
|---|---|---|
| `partition` | id, owner, epoch, lease_until, position | `position` counts the journal rows written in the partition; a turn at the Minimal history level writes none and does not move it. `epoch` changes only when ownership is taken. Times are in milliseconds; `lease_until` is written from the owner's clock under the default fence and compared with database time at a take |
| `node` | id, seen_at | A heartbeat, so nodes can count each other for a fair share of partitions. Liveness only |
| `instance` | id, partition, model, definition, status, sub_status, state, state_version, jobs, revision, parent, root, correlation, tenant, created, updated | `state` is opaque to the kernel and owned by the model. `jobs` is the kernel's list of background jobs in flight. The row is the truth about the present; lists and counts read it directly |
| `message` | seq, id, partition, target, kind, payload, headers, sender, reply_to, start, due, seen, trial, attempts, keep_until, done | The inbox. `seq` is assigned by the database and gives the order. `id` is unique and makes adding idempotent. `due` in the future makes it a timer or a retry. `start` is an optional spec for creating the target |
| `subscription` | key, instance, wait, partition, kind | Routes external events to waiting instances (kind wait) or to a definition that starts one (kind start) |
| `event` | key, id, payload, keep_until | Published events. By default an event reaches only the waits committed before it is published, and a wait committed later misses it (default 7, as Elsa 4 behaves today). A wait that asks for kept events (`keep: true`; BPMN message correlation asks) also gets events published within their window before it |
| `journal` | partition, position, instance, entries | One row per instance-commit: what happened, as one compact value. The truth about the past. Feeds the viewer's tables. Written according to the history level |
| `projection` | partition, position | How far the projector has got. Its own row, so the projector never touches the fence row |

This is the proposed shape, and the interfaces in this sketch are the proposed contract. The prototype already uses turn, job and pass; where its schema and contract still differ (a few columns not added yet, a few it keeps, some signatures), its DESIGN.md gives its schema in "Data" and most other differences in "Where the code and draft 5 differ".

The viewer's tables are projections of the journal. There is no outbox, dispatcher, scheduler queue, per-run lease, commit marker, dispatch record or timer table.

## The commit

The owner of partition `p` at epoch `e`, which believes the position is `n`, commits a batch of instance turns in one transaction, `k` of which write a journal row:

1. `UPDATE partition SET position = n + k, lease_until = <now + lease> WHERE id = p AND epoch = e AND position = n`.
2. **Check that it changed exactly one row before anything else is sent.** If not, roll back and reset.
3. Insert or update the instance rows the batch changed (with or without a journal row), each update `WHERE revision = r`.
4. Delete the messages those turns consumed, or mark them `done` when they carry `keep_until`. Check the count.
5. Mark `seen = e` on messages the owner holds in memory and did not consume.
6. Insert the messages those turns sent. A message's id is derived from its sender and the sender's revision.
7. Insert and delete subscriptions.
8. Insert `k` journal rows at positions `n + 1 .. n + k`.
9. Commit.

Statement 1 is the fence. With the fence as its own statement, the row lock on the partition row orders the commit against a takeover; with the fence in the batch EF chooses the order, see E9 below. The check in step 2 is why a commit is **two round trips, not one**: a commit sent in the same packet as a fence that matched nothing would still land. There are two ways to do it, both measured: `epoch` and `position` as EF concurrency tokens in the one `SaveChanges`, which checks before it sends the commit (2 round trips on PostgreSQL, lease time from the node's clock); or the fence as its own statement first (3 round trips, lease time from the database). The fence also renews the lease, so a busy partition needs no heartbeat. An idle owner sends an empty fenced commit as its heartbeat, which is also how a stale one finds out. **Default (12 November): the fence in the batch; the fence as its own statement stays an option.**

On SQL Server the fence in the batch has a known problem (finding E9 of the prototype): a stale owner's commit holds a message lock and waits for the partition row, because EF sends the fence last, while a take holds the partition row and waits for that message. In one stress run that made 127 deadlocks, every victim a take. Nothing wrong landed; it costs availability during takeovers. The fence as its own statement had no take deadlocks there, so it is SQL Server's default (decided 13 November); the prototype's EF store applies it since round 4, and a stress run with the default had no take deadlocks.

The ordinal in a derived message id has a fixed width. EF sends the inserts of one commit in key order, so without it the eleventh message of a turn overtook the third (found on PostgreSQL).

Rows per flush are capped (parameter limits, lock escalation). On the server engines 100 instance-commits cost the same round trips as one; SQLite's engine calls grow with the rows.

**The outbox is the recipient's inbox.** A message to another instance is a row in `message`, inserted in the sender's transaction. Nothing is relayed afterwards. For a recipient in the same partition the owner also hands it over in memory right after the commit, but only once it has caught up: after a takeover it hands nothing over until a poll has come back short, or a new message would overtake older rows it has not read yet.

**What a failed commit means.**
- **Unknown outcome** (the connection broke): reset.
- **The fence matched nothing:** reset.
- **A transient error** (a deadlock victim, `SQLITE_BUSY`): the batch is retried as it is.
- **A definite error** (a value too long, a key violation): no reset. The owner commits the batch's instances one by one and **parks** the one that fails: its status becomes faulted with the error, and the rest go on.

## Ownership

- **Take:** `UPDATE partition SET owner = me, epoch = epoch + 1, lease_until = ... WHERE id = p AND (owner IS NULL OR lease_until < <database time> OR owner = me)`, returning epoch and position.
- **Reset:** the owner drops everything it holds in memory for the partition and takes it again, or stands down. Recovery after a doubt is the same path as start-up. The take waits behind a commit that is still in flight, so it reads the true position. Every take bumps the epoch, so results of turns started under an older epoch are discarded.
- **Leases decide who should write. The fence decides who can.** A wrong lease costs availability, never correctness.
- A node holds its fair share of partitions, counted from the `node` heartbeats, and releases the surplus. The number of partitions is fixed when the database is created. SQLite uses one.

## Messages

- **Adding** one is an insert that anybody may do: the API, an event router, another partition's commit. The unique id makes it idempotent.
- **A message from outside the kernel carries `keep_until`.** Its consuming commit marks it `done` and a sweep deletes it after that time. So a client that retries a command after it was handled gets a duplicate-key answer, not a second effect. Messages between instances need no such window: the send is atomic with the sender's state, so a repeat cannot exist.
- **Polling:** `SELECT ... WHERE partition = p AND seen < e AND done = false AND due <= <database time + horizon> ORDER BY seq LIMIT m`. A plain read. The owner skips ids it holds and ids it consumed while the poll was in flight. There is no watermark, so a row whose insert commits late is found by the next poll. After a takeover the new epoch finds every row again.
- **Marked before run:** a message that arrived by poll is not acted on until a fenced commit has marked it `seen = e`. That is what lets the next owner know which messages its predecessor was holding. It costs one commit before the first turn of anything that arrives by poll. Messages inside a partition are written as seen and pay nothing.
- **Order:** a target's messages are handled in `seq` order. A sender's commits are serial, so messages from one sender to one target arrive in the order sent.
- **Consuming:** state change and delete are one transaction, so a message changes state exactly once, though a turn's code may run more than once after a crash.
- **Timers** are messages with a future `due`. The poll returns database time; the owner anchors it when the answer arrives and schedules by the difference on a monotonic clock. A timer is cancelled by deleting its message, whose id the sender knows.
- **Poison:** a new owner puts the messages its predecessor had marked on trial: a fenced commit sets their `trial` to its epoch, and their turns run with nothing else in flight. If that owner is lost too, the next take adds one attempt to exactly the messages that were on trial, in the take's own transaction. Suspects are tried one at a time by default; an optional larger starting group halves with every attempt, so a poison message ends up on trial alone either way, and a death stops counting against its neighbours. A message past the threshold is parked, with its instance faulted. So a turn that kills the process cannot walk the cluster. A turn that throws parks its instance at once; retries are the model's business.
- **Edge cases:** a message whose target does not exist and that carries no start spec is dropped and journalled. A message to a finished instance is consumed and dropped.
- **Wake-up:** whoever adds a message signals the owner (in process on one node; a pluggable notifier between nodes). The signal is a hint; the poll is the guarantee.

## Creating instances and placement

- **A message may carry a start spec** (model, definition, input). If its target does not exist, the owner creates it when it delivers the message. Starting a child, starting on an event and "signal with start" are all this.
- **A start without a caller's key** is placed in a partition the starting node owns, and runs in that process. A request that starts a workflow therefore runs it locally with no hop.
- **A start with a caller's key** (so that a retry finds the same instance) hashes the key to a partition and goes in as a message. Only these pay a hop.
- **A child** goes into its parent's partition by default, so a call tree talks in memory and commits together. A placement option spreads a wide fan-out over partitions.

## Events

- **Subscribing** is part of a turn's commit. A wait that asks for kept events also sends the instance a message that says "read the events for these keys". The owner reads `event`, delivers what it finds, and a later commit deletes that message. A new owner inherits it and reads again.
- **Publishing** inserts the event with `keep_until`, commits, then reads `subscription` and adds one message per match, with an id derived from event and wait. A turn that publishes puts a publish ticket in its own commit; the owner carries it out after the commit, and a new owner repeats it, so a crash between the event and the fan-out loses nothing. A publisher outside the kernel gets its answer only after the fan-out; the derived ids make its retry harmless. Recovery here rests on the caller: the prototype keeps no durable record of an outside publish's pending fan-out, so if the caller gives up after a crash, waits that did not ask for kept events can miss the event. Open: send outside publishes into the inbox as a publish ticket, as a turn's publish already is, so the kernel owns the retry.
- Each side looks after its own commit, so when the two cross, at least one sees the other, and the derived id removes the double.
- Built and measured as a sample model: correlation as an ordinary instance per key (`sim/Correlation.cs`, no kernel change), which needs no tables and no rule, at the cost of one hop. Decision 5 keeps the tables as the kernel's mechanism.

## Instances in memory

The owner keeps the state of its active instances in memory. A turn reads nothing. An instance that is not in memory is loaded by key when a message for it arrives; loads are batched, and the number held is bounded. Each instance handles one turn at a time; instances of a partition run concurrently; their commits go through the partition's one committer, which flushes as soon as the previous flush returns.

## The execution model contract

```csharp
public interface IExecutionModel
{
    string Key { get; }                                       // "elsa.native", "bpmn", "etl", "agent", ...
    int StateVersion { get; }
    object CreateState(StartSpec start);
    object UpgradeState(object state, int fromVersion);
    ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken);
    ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken);   // a background job
    InstanceSummary Describe(object state);                   // sub-status, waits, active nodes: for lists
}

public interface ITurn
{
    InstanceInfo Instance { get; }                            // id, model, definition, tenant, parent, root
    IReadOnlyList<Message> Inbox { get; }                     // in order; sender, reply-to, headers
    IAmbient Ambient { get; }                                 // not durable: this activation only
    object State { get; set; }
    string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null);   // returns the message id
    void Recall(string message);                              // delete a message to self, e.g. a timer
    InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false);   // detached: not cancelled with the parent
    void StartJob(string key, object? arguments = null);           // start a background job
    void CancelJob(string key);
    void Hold(Message message);                               // hand it to the next turn again
    void Subscribe(string key, string wait, bool keep = false); // keep: also get events published earlier
    void Unsubscribe(string key, string wait);
    string Publish(string key, string? payload, TimeSpan keep);  // the one way a turn publishes an event
    bool Redelivered(Message message);                        // observed delivery history; false does not prove no earlier execution
    void Record(JournalEntry entry);                          // common envelope plus the model's payload
    void Emit(string channel, object item);                   // live, not durable
    void Rebind(string definition);                           // migrate to another definition version
    void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null);
}
```

- **A turn commits at its end and only there.** A model that wants a durable boundary ends the turn and sends itself a "continue" message, which the owner hands over in memory. That is all a persistence mode is. After a crash the inbox alone says what to do next. An ephemeral instance that never waits commits nothing.
- **Background jobs.** `StartJob` saves the intent with the turn's commit, as an entry in the instance's `jobs` and as a ticket: a message to the instance whose id is derived from instance and key. The kernel then calls the model's `ExecuteJobAsync` outside the instance's turn, so the instance still takes a cancel or a timer meanwhile. When the work completes the ticket becomes the result. The work may run more than once; its result changes state once. A new owner finds the ticket by polling and starts the work again. A result is handled ahead of other messages in the same inbox, so a model never sees a result after its own cancel.
- **Models compose through messages only.** A parent of any model starts a child of any model and gets a message when it finishes. A cancel goes to the children the parent itself started, so admitting a child and cancelling the parent cannot race.
- **Live channel.** `Emit` and `Ambient` are for what must not be durable: an HTTP response, streamed agent output, the request's context. A caller on the starting node can start an instance and observe what it emits until it finishes.
- **The kernel owns** suspend and resume (it stops delivering all but resume), forced termination of a tree, tenant checks on sends, and trace headers on messages.
- Replay-style authoring (Temporal, Durable Task) and journaled awaits (Restate) fit as models on this contract; the kernel does not impose either.

How the known models sit on it (only Native is built, as a spike; the others are the planned fit):

- **Native:** state is the live activity frames, variables and scheduled work; slow activities are jobs. Each container brings its own semantics (see "Containers and their semantics" below): structured containers built in, Flowchart on the graph engine, StateMachine on the statechart engine.
- **BPMN:** hosted on the `Bpmn.Semantics` interpreter (see below); state is the tokens and scopes; a task is a job, so an interrupting boundary event can cancel it; a call activity is a start; a message that arrives early waits in `event`.
- **ETL:** state is stage cursors and references to data kept elsewhere; partitions of work are children spread over partitions; backpressure is a credit protocol between instances.
- **Agents:** state is the conversation's checkpoint (for example a Microsoft Agent Framework thread); a tool call is a job; streamed output is emitted; another agent is another instance; a human approval is a subscription.
- **Durable entities** (the reviewer's test of a fifth model): state is the entity's value, operations are messages with a reply-to. It fits without change.

## Containers and their semantics (owner decision, 12 November)

The native model stays one activity tree: typed, composable activities, containers with scopes and variables, code-first or designed. Each **container** brings its own semantics, its own runtime state and its own kind of diagram, and containers nest freely inside each other. The kernel sees none of this; it sees turns.

| Container | Shape | What drives it | Runtime state | Semantics |
|---|---|---|---|---|
| Sequence (also If, ForEach, While, Parallel) | Structured | The previous child completing | An index; a fixed join ("all my branches") | Built in. The structure answers "can more still arrive?", so no tokens |
| Flowchart | Any directed graph: splits, merges, loops | Outcomes: a completing activity's outcome chooses the flows | Tokens (execution paths) and join arrivals per loop round | **Graph engine** (token flow), extracted from today's Flowchart |
| StateMachine | States, nested states, parallel regions, event-triggered transitions | Events: a guarded transition fires; exit, transition and entry actions run | The active configuration: the active states of every region | **Statechart engine** (Harel / SCXML semantics) |

**Flowchart: extract, don't invent.** Today's Elsa 4 Flowchart on main already runs on token semantics under other names: execution paths with iteration keys (tokens with loop rounds), arrivals at joins, BPMN's gateway kinds (decision, parallel fork and join, inclusive fork and join, first-wins, merge), and dead-path joins as the default (`FlowchartOptions.JoinPolicyKind = DeadPathJoin`), with the older reachability search kept beside it and a conformance corpus that checks both agree. Elsa 3's join failures came from inferring joins from the graph; Elsa 4 has largely fixed them. The kernel prototype's native model did not port the dead-path policy, which is why, by its own reasoning, its All join after a decision never fires (NATIVE.md: reasoned, not run). The graph engine is that code lifted out of `Elsa.Activities.Flowchart` into a library with no kernel dependency: graph, paths, arrivals, join policies, dead paths, reachability, and the conformance corpus. The native Flowchart uses it.

**StateMachine: its own engine, not tokens.** A configuration looks like a set of tokens, one per active state per region, but the rules differ: transitions are triggered by events, not completions; exit and entry follow the hierarchy (exit up to the nearest common ancestor, enter down to the target); enabled transitions in parallel regions are arbitrated; and one event is processed to completion before the next. That last rule is the kernel's turn: one business event is one message, handled in one turn. When an entry, exit or transition action suspends (the StateMachine program requires it may), later events wait: the model hands them back with `Hold` until the transition finishes. An event with no eligible transition ends as an observable unhandled outcome.

**StateMachine is a native composite**, not its own execution model: users want it as a workflow root and nested, with ordinary activities in its states, and only a composite gives both. The statechart engine stays independent of the kernel, so a standalone statechart model (SCXML) can reuse it later. This placement is the question StateMachine program #3815 left open for its M0; this sketch recommends it, and #3815's M0 decides.

**BPMN stays a separate execution model, hosted on `Bpmn.Semantics`** (owner decision, 12 November). [valence-works/bpmn](https://github.com/valence-works/bpmn) (MIT, no dependencies) reads and writes BPMN 2.0 XML without losing content, and its interpreter is a pure function: `(definition, token state, one event) -> (next state, commands for the host)`, with no clock, I/O, threads or persistence. That is the execution-model contract's shape: a turn feeds the incoming message to the interpreter as the event; its commands become kernel operations (background jobs, timers, subscriptions, sends); its token state is the instance's state. It already covers inclusive and event-based gateways, interrupting and non-interrupting boundary events, embedded and event subprocesses, multi-instance, compensation, transactions, escalation and loops. It does not evaluate FEEL: the host resolves sequence-flow conditions, so Elsa needs an expression story for BPMN either way.

**The Flowchart's logic does not move into that repository.** Its identity is BPMN independent of any host, and Elsa's Flowchart semantics are not BPMN: outcome-driven routing, first-wins with cancelling the losers, fault joins, and a dead-path join as the default where BPMN treats several flows into a plain activity as an exclusive merge. Moving them there would muddy a public library, let a BPMN fix change Elsa's behaviour, and tie Elsa's core to another repository's releases. The two engines are siblings (both have a join coordinator, a state mutator, iteration keys, a diagnostics accumulator and a behaviour or policy registry), so a neutral core of tokens, flows, join rounds and reachability may exist. If it does, it becomes its own small package that both depend on, extracted by comparing the two working engines, not designed upfront.

**Cost.** Paths, arrivals and configurations are part of the instance's state, in memory during a turn. They add no database calls; the native cost table must not move.

**Journal and viewer.** Each engine writes its own entries (graph: path moves, dead paths, joins fired; statechart: transitions, entries and exits, deferred and unhandled events). Studio shows paths on a Flowchart and the active configuration on a StateMachine, each through the native model's projection.

### Plan

Each step has a gate. Nothing reaches product code before the design is approved.

1. **Graph engine** (alongside the three-layer restructuring). Extract the Flowchart engine from main into a kernel-independent library in the prototype, with its conformance corpus, and port the prototype's native Flowchart onto it. Simulation invariants for graph containers:
   - G1 path conservation: every path created is completed, discarded as dead, or still live;
   - G2 a join fires at most once per round;
   - G3 an All join fires once every inbound flow of its round has arrived, live or dead;
   - G4 a graph completes exactly when it has no live path;
   - G5 no path outlives its cancelled scope.

   Mutation checks that must each be caught: no dead paths (the All-join hang), no round reset (a loop fires a join twice), a dead path that runs its node, a cancel that leaves paths. Gate: a test first shows the All join after a decision hanging without dead paths, then firing with them; the corpus passes; the native simulation and mutation checks pass; the cost table is unchanged.
2. **BPMN spike: host `Bpmn.Semantics` on the kernel**, covering parallel, exclusive and inclusive gateways; a service task as a job; an interrupting boundary timer that cancels it; a message that arrives before its catch (it waits in `event`); a call activity as a child start; a loop. Gate: its instances join the simulation's mix and pass G1–G5 and the kernel invariants; the execution-model contract needs no change, or the changes are listed. The interpreter runs inside the deterministic simulation unchanged. After the spike, compare its join code with the graph engine's and decide whether a shared token package is worth extracting.
3. **StateMachine spike** as a native composite in the prototype: one nested state, two parallel regions, a transition whose entry action suspends while a second event arrives (deferred, then handled), an unhandled event, and a StateMachine nested in a Flowchart. Invariants:
   - S1 one event at a time per machine: no event is evaluated while a transition's actions are in flight;
   - S2 every accepted event ends as exactly one of handled or unhandled;
   - S3 the configuration is legal after every turn: one active state per region of every active composite state;
   - S4 exits run before entries, in hierarchy order.

   Gate: S1–S4 and the kernel invariants hold under the simulation's faults. The result feeds #3815's M0; it does not replace it.
4. **Design proposal** for the Fable review: carries this section, the results of steps 1–3, and the decisions below.
5. **Product build** (after approval). The native interpreter is rewritten as an execution model anyway (the clean break), so the Flowchart is built on the graph engine and the StateMachine on the statechart engine from the start. Studio: the graph designer shows paths and dead paths, the statechart designer shows the configuration.
6. **Later.** Design-time checks in Studio (a join that can never fire, a state that can never be left or reached); an SCXML model on the statechart engine if users ask.

### Decisions this needs

- **StateMachine placement:** a native composite (recommended here), to be confirmed by #3815's M0.
- **Default join for a Flowchart node with several inbound connections** and no explicit gateway: keep main's current default (`DeadPathJoin` decides implicit joins), so Flowcharts authored for Elsa 4 behave the same.
- **BPMN scope for its first release (after release 1):** recommended, the subset most Camunda users rely on (gateways, service, user and call tasks, timer and message events, boundary events, embedded subprocesses); compensation and event subprocesses after.

## Standalone jobs: Elsa.Jobs on the kernel (owner decision, 12 November)

There is one job concept. A workflow's background job and a standalone job are the same thing on the kernel, and Elsa.Jobs is to be rebuilt on it rather than kept as a second durable authority. Not built yet: a spike comes first.

The reference is the `Elsa.Jobs` domain on `agent/codex/3586-background-jobs` (source `a786c52f`, about 40,000 added lines under `Extensions/Jobs`, about 30,000 of them C#; draft PR #3909, closed; ADR 0096, not on main). It is a standalone runner whose own store owns execution authority, bridged to native workflows and BPMN through checkpoint/outbox sources, a pending-admission address, delivery responsibility and exact consumption proofs. That bridge is the cross-store handshake this kernel removes: on the kernel, starting a job is one commit (a ticket in the instance's inbox) and the result is a message applied once.

- **A standalone job is an actor type** in layer 3, next to the workflow instance. An ordinary .NET application installs layers 1 and 2 plus the job actor type: no workflow runtime, BPMN, Studio or HTTP API. One store, one fence, one projector, one retention policy.
- **The public API is kept:** `IJobClient`, `IJobHandler<TInput, TResult>`, `JobExecutionContext`, submission with an idempotency key and an immutable request (the provider recomputes the request digest), retry and attempt-timeout policies, queues, schedules and holds.
- **A workflow's job is the kernel's background job,** in the instance's partition and commit: no bridge, no consumption proofs. Awaited `RunJob` is a job whose result comes back as a message. A detached or independent `SubmitJob` sends a start message to a job actor; the sent message is the recipient's inbox row, so it is still one commit.
- **Ideas carried over from Elsa.Jobs:**
  - the handler implementation identity is pinned at the first attempt; later attempts need matching support, and work for an unavailable implementation stays durable and ineligible rather than running different code;
  - a stable invocation idempotency key per job for downstream systems, surviving retries;
  - claim and completion requests keep their identity across lost answers, so a replay resolves the original grant or receipt;
  - what a worker reports is kept apart from what the system accepts: a stale worker may add evidence but never replaces the selected result;
  - holds at queue, job type, definition and instance level, and capacity permits;
  - retention that removes payload bytes and keeps identities, digests, outcomes and attempts.
- **Mapping onto the kernel:** schedules are kernel timers with cron start specs, materialized into job actors; instance and definition holds are suspend; queue and type holds and capacity are admission rules; the worker protocol (claim, renew, complete, with a lease, a fence, replay-safe receipts and implementation pinning) is the answer to design-now question 5.
- **To prove in the prototype:** a claim checks due time, handler compatibility, holds, capacity and recovery at once. In Elsa.Jobs that is one database transaction per claim; on the kernel the partition owner should decide it in memory and commit the grant with its batch. A spike measures the round trips per job (submit, claim, complete, result delivered) against Elsa.Jobs's path.
- **Release placement** of standalone jobs is decided in the design proposal. The external worker protocol follows in 1.x; its contract is designed now. Background jobs inside workflows are in release 1 either way.

## Synchronous HTTP requests

A workflow handles a request and writes the response itself. The response is live output on the `Emit` channel, not a durable message: if it cannot reach the caller, the caller gets an error, and the workflow itself is unaffected. The cases:

- **A request that starts a workflow.** The receiving node finds the workflow by route in an in-memory route table (built when definitions are published, so routing reads nothing), places the new instance in a partition it owns, and runs it in-process with the request as `Ambient`. The request observes the instance and returns when the workflow writes its response. A workflow that responds and finishes without waiting commits nothing in Ephemeral mode: zero database calls.
- **A response before the end.** Writing the response does not end the workflow. The request returns and the workflow goes on.
- **When the response is released.** If the turn that writes the response commits, the response is held until that commit lands, so a caller never sees a response for work that was then lost. A turn that does not commit releases it at once. A route can opt out and respond before its commit.
- **A workflow that waits before it responds** (a background job, a short timer). The instance stays in memory on the receiving node and the request keeps waiting, up to the route's timeout.
- **A request that resumes a waiting workflow.** The instance may be owned by another node. The receiving node adds a resume message that carries a reply address (its node and the request's id), and keeps the request open. The owner runs the turn and sends the emitted response to that address over the runtime's transport between nodes, which is not durable. With the built-in runtime and one node this is in-process; across nodes it uses the same transport as wake-ups.
- **The owner lost while a request waits.** The request ends with a clear error (503 by default), never a hang, and never two responses. In the prototype only a reset on the receiving node answers 503; an owner lost on another node is noticed at the deadline (504), because the signal from owner to receiving node is not built. The workflow resumes under its new owner as usual; its later output is not sent to a caller that is gone.
- **Client disconnect and timeout** end the observation. By default they do not cancel the workflow; a route can map them to a cancel message.
- **Streaming** (chunked responses, server-sent events) uses the same channel, as an agent's streamed output does.

Large request and response bodies stay out of the instance's state; the workflow holds a reference.

## History and the viewer

The journal row is the durable hand-off the owner decided on 11 November: one compact row per commit, in the commit. Each entry has a common envelope (node, parent node, kind, time) and the model's own payload, so one viewer works across models. A projector expands journal rows into the viewer's tables afterwards, in position order per partition. Positions are assigned by the one writer under the lock it holds to commit, so a cursor cannot skip a row. The history level (Full, State only, Minimal) decides what is recorded; at Minimal no journal row is written.

The projector runs as a background worker, off the hot path, in its own transaction, nudged after commits and on a timer. Each round is set-based (decision 8): in the prototype a round costs 3 round trips on PostgreSQL (the two reads and one statement that moves the cursor and writes every view row of the page), where one statement per instance cost 17 for about a dozen instances; SQLite is set-based too, SQL Server and MySQL still write one statement per view. Its cursor is its own row, guarded by a conditional update, so any node may project any partition.

**The viewer's read path** (owner decision, 12 November):

- **The tail read.** To show one run, the API reads the projector's cursor first, then that run's projected rows and its journal rows past that cursor, and expands the tail in memory. A round that commits between the reads shows up in both and is removed by position, never as a gap. The view is always current, with no waiting and no extra writes.
- **Catch-up on request.** The API may ask the projector to run a round for that partition now. Rounds per partition are coalesced, so many viewers cause one round.
- **Committed history only.** Studio never makes an owner commit, and never shows uncommitted state.
- **Live watching** is a push stream to Studio of the journal entries each commit added. It carries committed entries only, and is part of the Studio work in the program plan.

## Invariants (what the simulation checks)

1. **One writer.** No two commits land at the same position of a partition. A commit prepared under an epoch that is no longer current never lands.
2. **Atomic turn.** New state, consumed messages, sent messages, subscriptions and the journal row of a turn are all there or none are.
3. **No lost message.** A committed message is delivered to a turn whose commit consumes it, as long as some node owns the partition.
4. **Once in state.** A message changes its target's committed state at most once, including an outside command retried after it was handled, within its window.
5. **No early or lost timer.** A message is not delivered before it is due and is delivered after.
6. **Journal order.** In a partition, the journal rows present plus the rows removed by deletes equal the position, and the projector never passes an unwritten position.
7. **Takeover.** After a take at epoch `e + 1`, nothing prepared under `e` commits, and the new owner sees everything that did commit.
8. **Index matches state.** The subscriptions of an instance are exactly the waits in its committed state.
9. **Memory matches rows.** At its current epoch, what an owner holds in memory for an instance equals the committed row plus its own uncommitted turn.
10. **Create once.** Two messages that would create the same instance create one, in either order and across a takeover.
11. **Order.** Messages from one sender to one target are handled in the order sent.
12. **Jobs.** A job's result is applied exactly once. A cancelled job's result never is.
13. **Events meet.** For a wait that asks for kept events, an event and the wait for the same key that are both committed within the event's window result in exactly one delivery. (The simulation's waiter asks, so the check covers the crossing case.)
14. **One bad instance.** An instance whose commit or turn always fails is parked, and the rest of its partition keeps going. A message is parked for killing the process only after the threshold number of owners were lost while it was on trial alone.

Faults to inject: a crash before, during and after a commit and before the in-memory hand-over; a commit whose outcome is unknown; a paused owner that wakes after a takeover; two nodes that both believe they own a partition; a message inserted while a poll runs; an outside command retried after it was handled; a turn that always throws; clocks that disagree.

## What it costs (the prototype's measurements)

On PostgreSQL the native model on the kernel needs no database call for a request-response workflow run inline in Ephemeral mode, 3 round trips for a Balanced run (2 with the fence in the batch) where today's core needs 48 to 80, and 33 (22) for ten Durable WriteLines where today's core needs 230. On the server engines a commit of 100 instances costs the round trips of one. A Balanced run costs 3 (2) on PostgreSQL, 4 (3) on SQL Server, 7 (6) on MySQL and 3 (2) on MongoDB; SQLite takes 5 calls into the engine, growing with the rows. The full tables, with every run and both fence modes, are in the prototype's NATIVE.md, DESIGN.md ("What it costs"), ENGINES.md and MONGO.md.

## What the tests show

A deterministic simulation drives several nodes against one store with crashes, paused owners, lost answers, retried commands and skewed clocks, and checks every invariant after every action; it passes at 200,000 runs of 300 actions and 20,000 runs of 1,000, also with native instances in the mix. Mutation checks break the kernel one rule at a time, and each is caught, except two rules that another rule covers (the position in the fence, the count on deletes), which are caught when that rule is broken too. The store conformance suite (59 cases including round 4 additions) runs on the in-memory store, SQLite, PostgreSQL, SQL Server, MySQL and MongoDB; where engines differ, the contract is silent and has to be defined (round 4 defined subscriptions as idempotent; the order of seqs within one commit is still open). A stress test with killed connections and stalls holds on all five engines, and fails on every one with the fence broken on purpose. The figures, the seeds, and what each round found are in the prototype's FINDINGS.md (sections 22 to 24); the engine findings, E9 among them, are in ENGINES.md.

- **Not built yet:** the runtime conformance suite and the Orleans adapter; the graph engine and the BPMN, StateMachine, agent, standalone-jobs and ETL fan-out spikes; short timers and jobs of an Ephemeral instance kept in memory; the stress test with native instances; a three-node MongoDB replica set; the owner's signal to the receiving node; the recreate rule per kind of id; values by reference; the viewer's tail read and catch-up; `Describe`, the journal envelope, tenant checks and trace headers; streaming responses and a route mapping a client's disconnect to a cancel.
- **Done in round 4 stage A:** the three layers as separate projects and the turn/job/pass renames.
- **Next, before the design proposal (program #4229, step 2):** the rest of the card decisions (SQL Server's checked fence is built); the Orleans adapter with a runtime conformance suite; the graph engine; spikes for BPMN, StateMachine, agents, standalone jobs with the worker protocol, and ETL's fan-out pattern; and the write-volume measurement.

## Trade-offs

What the design gains, what it pays for it, and the lever that moves the balance. The owner decides the defaults.

| # | Gain | Price | Lever |
|---|---|---|---|
| 1 | No reads while a workflow runs: state is in memory | Memory per active instance; the whole state is rewritten at each commit, so cost grows with state size | Bound on instances held; large values by reference |
| 2 | One writer per partition: no locks, no reads before a turn, many instances per commit | Takeover waits for the lease to run out, so failover takes about one lease; a hot partition has one writer; one large state delays its partition's commit; the number of partitions is fixed when the database is created | Partition count; lease length |
| 3 | An idle owner needs no traffic of its own... | ...except a heartbeat per owned partition (an empty fenced commit) about three times per lease, so an idle cluster still writes | Lease length |
| 4 | Batching: many commits in one transaction under load | A commit waits for the one before it, so latency rises a little under load while throughput rises a lot | Rows per flush |
| 5 | Balanced mode: one commit per wait | After a crash, the activities since the last commit run again. Activity code runs at least once; its effect on workflow state happens exactly once. Side effects outside Elsa may repeat | Persistence mode per workflow; Durable narrows the window; idempotency keys for what must not repeat |
| 6 | Background jobs do not block the instance | A background job can run more than once after a crash; external calls need idempotency keys | None: inherent, as in Temporal |
| 7 | The run viewer costs nothing on the hot path | The run view stays current by reading the journal tail (decision 11, not built); lists and other views over projected rows lag the projector; the projector's own writes still cost database calls, most at Full history | History level per workflow |
| 8 | Instances only talk through messages: no races, no cross-run locks | No code can read another instance's state: a question to another instance is a message or a read of a projection that may lag. Some features become a little harder to write | None: it is the design |
| 9 | State is a snapshot, not a replayed history: authors have no determinism rules, and loading is fast | History is a record, not the source: no rebuilding a run from history alone, and an old state needs upgrade code when a model changes | State versions and `UpgradeState` |
| 10 | State is opaque to the kernel: any model fits | The kernel cannot search inside state. Finding instances by a variable needs the model to publish a summary or an index, which costs writes | `Describe`; indexed fields chosen per workflow |
| 11 | A message from outside costs little | It costs one extra small commit (the mark before run) and waits for a poll or a wake-up; starts and resumes owned by another node pay a hop | Poll interval; the notifier between nodes |
| 12 | One shared database: simple to run, transactional across everything | The cluster's ceiling is that database's write throughput. Unlike Zeebe, a turn is never free of database calls; no sharding over several databases | Later, if needed |
| 13 | Persistence is abstracted: any store | Only what the contract guarantees can be used: no provider-specific shortcuts in the kernel, each provider needs its own tuning, MongoDB needs a replica set | Store conformance suite |
| 14 | The actor runtime can be swapped | Runtimes host partitions only (12 November); one grain per instance is deferred because it loses batching and needs the sweep. The durable layer duplicates some of what an actor runtime offers | Ship one runtime; adapters on demand |
| 15 | A poison message cannot walk the cluster | After a takeover, suspects are retried one at a time (or in halving groups), so a deep backlog recovers slowly; an innocent message is sometimes parked when owners keep dying while it is on trial | Threshold; trial group size |
| 16 | A synchronous HTTP response is never sent for lost work | The response waits for its turn's commit (2 to 3 round trips of latency on PostgreSQL); a resume owned by another node pays a hop | Release before commit, per route |
| 17 | A clean break: the smallest, fastest design | No migration of existing runs; the native interpreter is rewritten as a model; tests bound to the current core's internals go; the API and Studio are re-pointed; two cores exist until the switch | None: accepted on 11 and 12 November |

## Open questions

### To answer in the design proposal, before the Fable review (owner decision, 12 November)

These are cheap to design in now and expensive to retrofit. The proposal must answer each one.

1. **Migrating running instances.** `Rebind` exists, but not what migration means per model and container: mapping native frames, moving Flowchart paths, mapping a StateMachine's configuration, moving BPMN tokens. Needed: a migration hook on each container's engine, and versioning of serialized activity state so a release that changes an activity's state shape can upgrade old instances.
2. **The data lifecycle.** Payloads live in state and in the append-only journal. Needed: retention and archiving of finished instances; the right to erasure (per-instance keys that can be destroyed, or payloads kept by reference); encryption of state at rest. Decide before the journal format is fixed.
3. **Fairness, quotas and backpressure.** Partitions share workers, so one tenant's burst can starve others on the same partitions. Needed: admission control at start, per-tenant quotas, fair scheduling of turns within a pass, and a bound on inbox growth. Today only ETL's credit protocol touches this.
4. **Very large fan-out and fan-in.** A ForEach over 100,000 items, or ETL partitions: if every child reports to the parent, the parent takes 100,000 messages and turns. Needed: a counting or aggregating pattern (for example a tree of combiners) on the kernel, designed rather than discovered.
5. **Job workers outside the process.** Built from Elsa.Jobs's worker protocol (see "Standalone jobs" above). Workers in any language pull jobs over gRPC or HTTP, with a lease, a heartbeat and a timeout (Zeebe's job workers). This changes the job contract (leases, timeouts, retries owned by the worker), so decide before jobs are built.
6. **Observability as a contract.** Trace context on every message; stable metric names (inbox depth, projector lag, commit latency, parked count, lease takeovers); operator actions (retry or discard a parked message, inspect an inbox, force a partition handover).
7. **Write volume and bloat.** A commit rewrites the instance's whole state and, at Full history, also appends a journal row; the inbox inserts and then deletes. On PostgreSQL that leaves dead row versions and vacuum pressure under load. Needed: bytes written per commit and dead-tuple counts in the stress run, before and after the mitigations (table fill factor so updates stay on the page, a partitioned inbox, values by reference). If the numbers demand it, a lazy snapshot (the state change in the journal, the instance row rewritten every N turns) is the fallback, as a prototype spike first. Snapshot plus journal stays the default; full event sourcing and a log-only store are not the default. Measured 13 November in `prototypes/kernel-model/WRITES.md`: at Full history and small states 0.7 to 2.3 kB of WAL per clean instance commit and about 2.1 kB per landed instance commit under the stress test's chaos (a floor: checkpoints every 30 s add about a third), bloat small and vacuumed; large states are the cost (an incompressible 4 kB state: about 6.9 kB per instance commit through TOAST, which grows to 2.1 to 2.3 times its live data), so values by reference come first; the lazy snapshot is sketched there, not built.

### Architecture documentation (owner decision, 12 November)

The design gets a reference section in `docs/architecture/` (the engineering documentation, read on GitHub), not the GitHub wiki. Decided 13 November: the proposed pages go there now, and at the switch a shorter user-facing architecture section goes into `website/docs/`, which GitBook publishes. The section has a README with the overview, a reading order, the walkthrough link and each page's status, then one page per topic, landed one page per PR beside the design proposal. Terms are defined once in the glossary; decisions stay in ADRs. Pages carry a "Proposed" banner until the design is approved and "Current" from the switch. At the switch, the pages that describe the old core (the architecture tour, how a workflow executes, durable resumption, durable timers, bookmark expiration, runtime fault behaviour) are rewritten or retired, after the owner sees the list.

### Release 1 scope (owner decision, 12 November)

The test: give Elsa 3 users a reason to move and nothing that stops them, and give new users one clear reason to pick Elsa (fast, durable .NET workflows with synchronous HTTP).

- **In:** the kernel (actor runtime and durable layer) with the built-in runtime; the Embedded and Cluster profiles; the native model with its structured containers and the Flowchart on the extracted graph engine; synchronous HTTP; PostgreSQL, SQL Server and SQLite, tuned; Studio pointed at the new core, with the run viewer on the projector's tables; and, in a basic form, trace context and metrics, retention and delete, admission control with bounded inboxes, and migrating instances between definition versions for the simple cases.
- **StateMachine:** in, unless #3815 has not passed its design milestone (M0) when the native model build starts; then it moves to 1.1. The release never waits on it.
- **MySQL:** supported, not tuned. The conformance suite and the stress test run on it in CI; the docs say it is slower (7 round trips per commit against PostgreSQL's 3 with the fence as its own statement; 6 against 2 with the fence in the batch, the default).
- **MongoDB:** proven in CI against a replica set, so the store contract stays MongoDB-safe; production support in 1.1.
- **Agents:** native activities with agent calls as background jobs; a dedicated agent execution model after the switch, checked by a spike first.
- **Large fan-out:** the aggregating pattern is designed in the proposal and ships with ETL; release 1 caps a parallel ForEach at a documented size.
- **Standalone jobs:** Elsa.Jobs rebuilt on the kernel as a job actor type; a spike now, release placement in the proposal.
- **After, with a spike now:** BPMN, the Orleans adapter, ETL (its fan-out pattern), a dedicated agent model. The out-of-process job worker protocol follows in 1.x; its contract is designed now so jobs are built right.

### Decided 13 November

- **Where the architecture section lives:** `docs/architecture/` now, a published section in `website/docs/` at the switch (see "Architecture documentation").
- **SQL Server's default fence (E9):** the fence as its own statement. With the fence in the batch EF sends the fence last, so on SQL Server a take can deadlock with a stale commit (127 deadlocks on takes in one stress run, safety held); the fence as its own statement had none. Other engines keep the fence in the batch.

### Earlier open questions

- **Outside publishes.** A turn's publish is a durable publish ticket; an outside publish relies on its caller's retry (see Events). Routing outside publishes through the inbox as a publish ticket would remove that dependency, at one more commit per publish.
- **Poison counting is the weakest part.** An owner lost while a message is on trial costs that message an attempt, so under the simulation's rate of faults an innocent message is sometimes parked (at 1,000 actions, 12 innocent instances across 20,000 simulation runs). Built: a release, a reset followed by a take by the same process, and a take of a free partition do not count; only a take from another owner does. Built: job tickets are tried like any message, and a job on trial at the threshold becomes an error result while its instance goes on; decision 9 changes that to parking the job and faulting the instance, which is not built yet. Open: an owner with a trial in flight renewing its lease on a short timer, so that only a real death counts (proposed, not built). Recovery of a deep backlog after a takeover is serial by default (one suspect at a time); a larger starting group, halving with every attempt, recovers faster.
- **State size.** The whole state is rewritten at each commit. Fine for kilobytes. A growing agent thread or a large variable needs segments or values stored by reference. Measure before adding. Decided 12 November: large values by reference, and a size benchmark in the cost runs; segments only if measured.
- **Events:** the table with the two-sided check is built and passes. A correlation instance per key would remove both tables, the owed read and invariant 8, at the cost of one hop. Built and measured as a sample model. Decided 12 November: the tables stay the kernel's one mechanism; the correlation instance remains a sample model.
- **Notifier between nodes.** Decided 12 November: a pluggable wake signal over the runtime's transport; polling remains the guarantee.
- **Projection cost.** Decided 12 November: the projector stays off the hot path in its own transaction, each round batched into set-based writes.
- **What carries over.** The activity library and the designer's definitions carry over with an adapted activity context. The native interpreter is rewritten as a model. The runtime API and Studio's run views are re-pointed at `instance` and the projections.
- **One database.** "The outbox is the recipient's inbox" assumes all partitions share a database.
