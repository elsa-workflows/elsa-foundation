# Execution kernel: clean-slate design sketch (draft 3, revised after round 3 of the model, 12 November 2026)

Status: a sketch that has been tested in three rounds of a model. Draft 1 had two first-principles reviews (protocol and model contract); both said "right shape, needs changes". Draft 2 was built as a standalone model (853 lines of kernel) and attacked by a simulation and a PostgreSQL stress test, which found eight more faults; this draft carries their fixes. With the fixes, 200,000 simulated runs of 300 actions pass every invariant, and so do two stress runs on PostgreSQL. What is still weak or untested is listed at the end. It replaces the approach of ADR 0098.

Round 3 built every piece draft 3 had only described (fair share, the projector's row and the history levels, suspend and resume, forced termination of a tree, `Hold`, `Emit` and `Ambient` with synchronous HTTP, state versions, `Rebind`, starts triggered by events, placement across partitions, the bound on instances in memory, deletion), each with a simulation check and a deliberate flaw it catches. It found five more faults (F11 to F15 in FINDINGS.md); this text carries their fixes. The kernel is 1,633 lines (it was 1,102 after round 2), of which 247 are the in-memory store.

The model, its findings and how to replay each failing seed are on branch `agent/claude/kernel-model-r4` (round 3), under `prototypes/kernel-model/`.

## Requirements (the owner's, 12 November)

- Very fast, elegant, well architected, clean code.
- Runs Elsa native workflows.
- Runs other execution models: BPMN, ETL, agent coordination, and models nobody has thought of yet.
- Persistence through Entity Framework Core.
- Unreleased code: any API and any ADR may change.

## The idea in five sentences

1. The kernel knows nothing about workflows. It runs **instances**: durable actors that own a piece of state and react to **messages**.
2. An **execution model** (native, BPMN, ETL, agent, ...) is a plug-in that turns an instance's state and its incoming messages into new state and outgoing messages. That is the only extension point.
3. Everything that crosses an instance boundary is a message: creating an instance, a child finishing, a timer, an external event, a cancel, an alteration. No code reads another instance's state.
4. Instances are grouped into **partitions**. One node owns a partition at a time and is its only writer, so the hot path needs no reads, no per-instance lease and no per-instance version check.
5. A **commit** writes everything a step changed in one transaction, and the owner batches the commits of many instances into one.

One rule came out of the simulation three times and is now a principle: **the inbox alone says what is owed.** Anything the kernel still has to do for an instance after a commit (continue a sequence, run off-step work, look for events) is a message in that commit. A new owner needs nothing but the rows.

This is the shape proven by Temporal (shard fencing; state, history and tasks in one transaction), Zeebe (one writer per partition, batched appends, every boundary a command), Durable Task and Orleans. It is not a new protocol.

## Data

| Table | Row | Notes |
|---|---|---|
| `partition` | id, owner, epoch, lease_until, position | `position` counts the instance-commits made in the partition. `epoch` changes only when ownership is taken. Times are database time in milliseconds |
| `node` | id, seen_at | A heartbeat per process start, so nodes can count each other for a fair share of partitions. Liveness only |
| `instance` | id, partition, model, definition, status, sub_status, state, state_version, history, suspended, runs, children, revision, parent, root, correlation, tenant, created, updated | `state` is opaque to the kernel and owned by the model. `runs` is the kernel's list of off-step work in flight, `children` the instances it started that have not finished. The row is the truth about the present; lists and counts read it directly |
| `message` | seq, id, partition, target, kind, payload, headers, sender, reply_to, start, due, seen, trial, attempts, keep_until, done | The inbox. `seq` is assigned by the database and gives the order. `id` is unique and makes adding idempotent. `due` in the future makes it a timer or a retry. `start` is an optional spec for creating the target. `reply_to` is a caller that waits on some node for what the consuming step emits. `trial` is the epoch at which the row was last on trial |
| `subscription` | key, instance, wait, partition, start | Routes external events to waiting instances, or, with a start spec and no instance, to a definition that starts one |
| `event` | key, id, payload, keep_until | Published events, kept for a while so that an event and a subscription that cross each other still meet |
| `journal` | partition, position, instance, entries | One row per instance-commit: what happened, as one compact value. The truth about the past. Feeds the viewer's tables. Written according to the history level |
| `projection` | partition, position | How far the projector has got. Its own row, so the projector never touches the fence row |
| `view` | instance, partition, revision, commits | The viewer's row per instance, written by the projector with its position |

The viewer's tables are projections of the journal. There is no outbox, dispatcher, scheduler queue, per-run lease, commit marker, dispatch record or timer table.

## The commit

The owner of partition `p` at epoch `e`, which believes the position is `n`, commits `k` instance steps in one transaction:

1. `UPDATE partition SET position = n + k, lease_until = <database time + lease> WHERE id = p AND epoch = e AND position = n`.
2. **Check that it changed exactly one row before anything else is sent.** If not, roll back and reset.
3. Insert or update the `k` instance rows, each update `WHERE revision = r`.
4. Delete the messages those steps consumed, or mark them `done` when they carry `keep_until`. Check the count.
5. Mark `seen = e` on messages the owner holds in memory and did not consume.
6. Insert the messages those steps sent. A message's id is derived from its sender and the sender's revision.
7. Insert and delete subscriptions.
8. Insert `k` journal rows at positions `n + 1 .. n + k`.
9. Commit.

Statement 1 is the fence. The row lock on the partition row orders it against a takeover. The check in step 2 is why a commit is **two round trips, not one**: a commit sent in the same packet as a fence that matched nothing would still land. There are two ways to do it, both measured: `epoch` and `position` as EF concurrency tokens in the one `SaveChanges`, which checks before it sends the commit (2 round trips on PostgreSQL, lease time from the node's clock); or the fence as its own statement first (3 round trips, lease time from the database). The fence also renews the lease, so a busy partition needs no heartbeat. An idle owner sends an empty fenced commit as its heartbeat, which is also how a stale one finds out.

The ordinal in a derived message id has a fixed width. EF sends the inserts of one commit in key order, so without it the eleventh message of a step overtook the third (found on PostgreSQL).

Rows per flush are capped (parameter limits, lock escalation). 100 instance-commits cost the same round trips as one.

**The outbox is the recipient's inbox.** A message to another instance is a row in `message`, inserted in the sender's transaction. Nothing is relayed afterwards. For a recipient in the same partition the owner also hands it over in memory right after the commit, but only once it has caught up: after a takeover it hands nothing over until a poll has come back short, or a new message would overtake older rows it has not read yet.

**What a failed commit means.**
- **Unknown outcome** (the connection broke): reset.
- **The fence matched nothing:** reset.
- **A definite error** (a value too long, a key violation, a deadlock victim): no reset. A deadlock victim retries the batch. Otherwise the owner commits the batch's instances one by one and **parks** the one that fails: its status becomes faulted with the error, and the rest go on.

## Ownership

- **Take:** `UPDATE partition SET owner = me, epoch = epoch + 1, lease_until = ... WHERE id = p AND (owner IS NULL OR lease_until < <database time> OR owner = me)`, returning epoch and position.
- **Reset:** the owner drops everything it holds in memory for the partition and takes it again, or stands down. Recovery after a doubt is the same path as start-up. The take waits behind a commit that is still in flight, so it reads the true position. Every take bumps the epoch, so results of steps started under an older epoch are discarded.
- **Leases decide who should write. The fence decides who can.** A wrong lease costs availability, never correctness.
- **Fair share:** a node writes its heartbeat, counts the nodes heard from within a window that spans more than one beat, releases what is above partitions / nodes (rounded up), and takes free or expired partitions up to it. The number of partitions is fixed when the database is created. SQLite uses one.
- **A node's name is unique per process start**, so a restarted process is not taken for its predecessor (F9). One process owns several partitions and dies for all of them at once, which matters for poison (below).

## Messages

- **Adding** one is an insert that anybody may do: the API, an event router, another partition's commit. The unique id makes it idempotent.
- **A message from outside the kernel carries `keep_until`.** Its consuming commit marks it `done` and a sweep deletes it after that time. So a client that retries a command after it was handled gets a duplicate-key answer, not a second effect. Messages between instances need no such window: the send is atomic with the sender's state, so a repeat cannot exist.
- **Polling:** `SELECT ... WHERE partition = p AND seen < e AND done = false AND due <= <database time + horizon> ORDER BY seq LIMIT m`. A plain read. The owner skips ids it holds and ids it consumed while the poll was in flight. There is no watermark, so a row whose insert commits late is found by the next poll. After a takeover the new epoch finds every row again.
- **Marked before run:** a message that arrived by poll is not acted on until a fenced commit has marked it `seen = e`. That is what lets the next owner know which messages its predecessor was holding. It costs one commit before the first step of anything that arrives by poll. Messages inside a partition are written as seen and pay nothing.
- **Order:** a target's messages are handled in `seq` order. A sender's commits are serial, so messages from one sender to one target arrive in the order sent.
- **Consuming:** state change and delete are one transaction, so a message changes state exactly once, though a step's code may run more than once after a crash.
- **Timers** are messages with a future `due`. The poll returns database time; the owner anchors it when the answer arrives and schedules by the difference on a monotonic clock. A timer is cancelled by deleting its message, whose id the sender knows.
- **Poison:** a new owner tries the messages another owner had marked (suspects) one at a time, or in groups that halve. A fenced commit writes `trial = e`; while a trial is in flight **the whole process** starts no other step and no other off-step work, and at most one partition of the process has a trial (F11). A take that finds another owner's lease expired adds one attempt to exactly the rows on trial at that owner's epoch; nothing else changes attempts. A message at the threshold is parked, with its instance faulted. So a step that kills the process cannot walk the cluster. A step that throws parks its instance at once; retries are the model's business.
- **A trial starts only when its step can run at once:** the instance is loaded, has nothing in flight, is not withheld (suspended, or written by a newer model) and has no kernel message waiting ahead (F13). A suspect that cannot be tried because its instance is withheld is **shelved**: a fenced commit marks it seen so polls move past it, without putting it on trial (F12).
- **Hold:** a step may hand a message back. It stays a row, keeps its place in `seq` order and comes with the next step, when something new arrives. A held message that was on trial ran without killing the process: its trial ends and its row's trial mark is cleared in the same commit. Kernel messages pass held ones. After a takeover a held message may come alone.
- **Edge cases:** a message whose target does not exist and that carries no start spec is dropped and journalled. A message to a finished instance is consumed and dropped.
- **Wake-up:** whoever adds a message signals the owner (in process on one node; a pluggable notifier between nodes). The signal is a hint; the poll is the guarantee.

## Kernel commands

Messages anyone may add, handled by the kernel alone, in `seq` order after what came before them (held messages excepted); no model sees them.

- **Suspend and resume:** `suspended` on the row. While set, the instance is given nothing but kernel messages; its messages, results and due timers wait in order and come after the resume. A suspend waits while the instance has a message on trial.
- **Terminate:** the instance ends Cancelled, its runs are cancelled, all its subscriptions are deleted, its parent is told, and a terminate goes to every child in `children`. A child whose start is still on its way gets the terminate after the start (one sender, one target, in order), in any partition. A terminate of an ended instance still goes on to its remaining children.
- **Delete:** an ended instance's row is deleted at its revision, with its subscriptions and its journal rows; the delete writes its own journal row, which removes the view. What arrives afterwards is dropped as for a missing target, or, if it carries a start spec, creates a new instance under the same id.

## Creating instances and placement

- **A message may carry a start spec** (model, definition, input). If its target does not exist, the owner creates it when it delivers the message. Starting a child, starting on an event and "signal with start" are all this.
- **A start without a caller's key** is placed in a partition the starting node owns, and runs in that process. A request that starts a workflow therefore runs it locally with no hop.
- **A start with a caller's key** (so that a retry finds the same instance) hashes the key to a partition and goes in as a message. Only these pay a hop.
- **A child** goes into its parent's partition by default, so a call tree talks in memory and commits together. `Placement.Spread` hashes it into another partition for a wide fan-out; its start is then an ordinary row there.

## Events

- **Subscribing** is part of a step's commit, which also sends the instance a message that says "read the events for these keys". The owner reads `event`, delivers what it finds, and a later commit deletes that message. A new owner inherits it and reads again.
- **Publishing** inserts the event with `keep_until`, commits, then reads `subscription` and adds one message per match, with an id derived from event and wait.
- Each side looks after its own commit, so when the two cross, at least one sees the other, and the derived id removes the double.
- **Starts on an event:** a subscription with a start spec and no instance, registered from outside. The registrar inserts it and then reads the kept events of its key, as a publisher inserts and then reads; it retries until both are done. The delivering message and its target are derived from event and subscription, so one instance is created per pair whichever side delivers.
- Correlation as an ordinary instance per key needs no tables and no rule, at the cost of one hop; built and simulated, it stays a model for narrow, quiet keys, and the tables stay in the kernel (FINDINGS section 8).

## Instances in memory

The owner keeps the state of its active instances in memory. A step reads nothing. An instance that is not in memory is loaded by key when a message for it arrives; loads are batched. Above `MaxInstances` the least recently used idle instances are dropped; idle means no step or commit in flight (a load would read the row before it lands), no message waiting and no caller waiting. Each instance handles one step at a time; instances of a partition run concurrently; their commits go through the partition's one committer, which flushes as soon as the previous flush returns.

A row written at a state version newer than this node's model is **withheld** like a suspended one: an older model would read the newer state as far as it understands it and write it back older (F14).

## The execution model contract

```csharp
public interface IExecutionModel
{
    string Key { get; }                                       // "elsa.native", "bpmn", "etl", "agent", ...
    Type StateType { get; }                                   // what the kernel deserializes a current state as
    int StateVersion { get; }
    object CreateState(StartSpec start);
    object UpgradeState(string state, int fromVersion);       // the stored form: an old state may not fit StateType
    ValueTask StepAsync(IStep step, CancellationToken cancellationToken);
    ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken);   // off-step work
    InstanceSummary Describe(object state);                   // sub-status, waits, active nodes: for lists
}

public interface IStep
{
    InstanceInfo Instance { get; }                            // id, model, definition, tenant, parent, root
    IReadOnlyList<Message> Inbox { get; }                     // in order; sender, reply-to, headers
    object? Ambient { get; }                                  // not durable: from the starter on this node, this activation only
    object State { get; set; }
    void Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null);
    InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent);
    void Run(string key, object? arguments = null);           // start off-step work
    void CancelRun(string key);
    void Hold(Message message);                               // hand it to the next step again, in its place
    void Subscribe(string key, string wait);
    void Unsubscribe(string key, string wait);
    void Record(JournalEntry entry);                          // common envelope plus the model's payload
    void Emit(string channel, object? item);                  // live, not durable: released when the step's commit lands
    void Rebind(string definition);                           // migrate to another definition version
    void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null);
}
```

- **A step commits at its end and only there.** A model that wants a durable boundary ends the step and sends itself a "continue" message, which the owner hands over in memory. That is all a persistence mode is. After a crash the inbox alone says what to do next. An ephemeral instance that never waits commits nothing.
- **Off-step work.** `Run` saves the intent with the step's commit, as an entry in the instance's `runs` and as a ticket: a message to the instance whose id is derived from instance and key. The kernel then calls the model's `RunAsync` outside the instance's step, so the instance still takes a cancel or a timer meanwhile. When the work completes the ticket becomes the result. The work may run more than once; its result changes state once. A new owner finds the ticket by polling and starts the work again. A result is handled ahead of other messages in the same inbox, so a model never sees a result after its own cancel.
- **Models compose through messages only.** A parent of any model starts a child of any model and gets a message when it finishes. A cancel goes to the children the parent itself started, so admitting a child and cancelling the parent cannot race.
- **Live channel.** `Emit` and `Ambient` are for what must not be durable: an HTTP response, streamed agent output, the request's context. See "Synchronous HTTP requests".
- **The kernel owns** suspend and resume, forced termination of a tree, deletion (see "Kernel commands"), tenant checks on sends, and trace headers on messages (the last two are not built).
- Replay-style authoring (Temporal, Durable Task) and journaled awaits (Restate) fit as models on this contract; the kernel does not impose either.

How the known models sit on it:

- **Native:** state is the live activity frames, variables and scheduled work; the model interprets the graph; slow activities are runs.
- **BPMN:** state is the tokens and scopes; a task is a run, so an interrupting boundary event can cancel it; a call activity is a start; a message that arrives early waits in `event`.
- **ETL:** state is stage cursors and references to data kept elsewhere; partitions of work are children spread over partitions; backpressure is a credit protocol between instances.
- **Agents:** state is the conversation's checkpoint (for example a Microsoft Agent Framework thread); a tool call is a run; streamed output is emitted; another agent is another instance; a human approval is a subscription.
- **Durable entities** (the reviewer's test of a fifth model): state is the entity's value, operations are messages with a reply-to. It fits without change.

## Synchronous HTTP requests

A workflow handles a request and writes the response itself, on the `Emit` channel. Built and simulated (FINDINGS section 11):

- **A request that starts a workflow** places it in a partition the receiving node owns and hands the start over in memory (`Worker.Post`) with a reply address and the request as `Ambient`. A workflow that responds and ends in its first step, ephemeral, commits nothing.
- **When the response is released:** with the unit of the step that emitted it, when its commit lands; at once if the step writes nothing. A response before the end does not end the workflow.
- **Waiting first** (a timer, off-step work): the address stays with the instance in the owner's memory, which keeps the instance from being evicted; the request waits up to its deadline.
- **A request that resumes a waiting workflow** adds a message with `reply_to` (node and request id). Whoever consumes it sends what the step emits, and the end, to that address over the runtime's transport (`IReplyTransport`), which may drop, delay or repeat.
- **The caller** (`Requests`) takes the first response or the first end and ignores the rest, and ends at its deadline. The deadline is load-bearing: an owner that dies after consuming the resume cannot tell anyone. A reset tells every caller it knows of "lost" at once.
- **Disconnect and timeout** end the wait and leave the instance alone; a route may add a cancel.
- **Not built:** responding before the commit (opt-out), streaming beyond a sequence of emitted items, deadlines carried in the address so an owner can drop a caller that gave up.

## History and the viewer

The journal row is the durable hand-off the owner decided on 11 November: one compact row per commit, in the commit. Each entry has a common envelope (node, parent node, kind, time) and the model's own payload, so one viewer works across models. A projector expands journal rows into the viewer's tables afterwards, in position order per partition. Positions are assigned by the one writer under the lock it holds to commit, so a cursor cannot skip a row. The history level (Full, State only, Minimal) decides what is recorded; at Minimal no journal row is written. The projector reads its own `projection` row and then the partition's position, reads the journal after its own, and writes the view rows and its new position in one transaction, only if its position is still where it read it: so projectors may run on any node, several at once, and die between the read and the write. Every journal position up to the partition's is committed, so a missing one there was deleted, not written late: the projector passes it (F15).

## Invariants (what the simulation checks)

1. **One writer.** No two commits land at the same position of a partition. A commit prepared under an epoch that is no longer current never lands.
2. **Atomic step.** New state, consumed messages, sent messages, subscriptions and the journal row of a step are all there or none are.
3. **No lost message.** A committed message is delivered to a step whose commit consumes it, as long as some node owns the partition.
4. **Once in state.** A message changes its target's committed state at most once, including an outside command retried after it was handled, within its window.
5. **No early or lost timer.** A message is not delivered before it is due and is delivered after.
6. **Journal order.** A partition's journal has no gap among the rows written, and the projector never passes an unwritten position.
7. **Takeover.** After a take at epoch `e + 1`, nothing prepared under `e` commits, and the new owner sees everything that did commit.
8. **Index matches state.** The subscriptions of an instance are exactly the waits in its committed state.
9. **Memory matches rows.** At its current epoch, what an owner holds in memory for an instance equals the committed row plus its own uncommitted step.
10. **Create once.** Two messages that would create the same instance create one, in either order and across a takeover.
11. **Order.** Messages from one sender to one target are handled in the order sent.
12. **Runs.** A run's result is applied exactly once. A cancelled run's result never is.
13. **Events meet.** An event and a subscription for the same key that are both committed within the event's window result in exactly one delivery.
14. **One bad instance.** An instance whose commit or step always fails is parked, and the rest of its partition keeps going. A message is parked for killing the process only after the threshold number of owners were lost while it was on trial alone in its process.
15. **Callers.** A caller receives at most one response and at most one end; the response only if the step that emitted it committed or wrote nothing; it never waits for ever. The ambient is seen only on the process that started the instance.
16. **Suspended.** While its row says suspended, an instance's owner runs no step of it; what waited is delivered after the resume.
17. **Termination.** A terminate reaches every descendant, in any partition, including those whose start was on its way; none runs afterwards and none keeps a wait.
18. **State versions.** A state is read at its version or upgraded; no instance's state version goes back.
19. **Rebind.** After a rebind every step of the instance sees the new definition, and so does its row.
20. **Event starts.** An event and a start subscription of its key, both committed, start exactly one instance, which takes exactly that event.
21. **Memory bound.** No owner holds more idle instances than the bound; reloads match the rows (invariant 9).
22. **Delete.** A row disappears only when its instance was deleted, and with it its journal rows, waits and view; a deleted instance never comes back.
23. **Fair share.** Once faults stop, every partition has one owner and no node holds more than its share.

Faults to inject: a crash before, during and after a commit and before the in-memory hand-over; a commit whose outcome is unknown; a paused owner that wakes after a takeover; two nodes that both believe they own a partition; a message inserted while a poll runs; an outside command retried after it was handled; a step that always throws; clocks that disagree.

## What it costs (measured on the model)

PostgreSQL figures are round trips: one per command execution and one per commit. SQLite figures are calls into the engine, where EF sends one command per row. "Today" is the current core, counted on 11 November.

| Run | Today, PostgreSQL | Model, PostgreSQL | Today, SQLite | Model, SQLite |
|---|---:|---:|---:|---:|
| Ten no-op activities, ephemeral, started on the owner | 32 | 0 | 29 | 0 |
| The same, Balanced | 48 | 2 (3 with the fence as its own statement) | 64 | 5 |
| The same, Durable | 230 | 20 (30) | 221 | 77 |
| Balanced, started from a node that does not own the partition | | 7 (9) | | 13 |
| Parent with three children in one partition | | 8 (12) | | 49 |
| 100 Balanced instances started at once | not measured | 2 (3) for all of them | not measured | 203 |

- The model's activities do nothing and its history is one journal row per commit; the projector's writes and a real model's own reads come on top. The current core's figures include its viewer rows.
- Durable is 10 commits: one per activity, and the last one also finishes the instance.
- A start from another node costs the insert, a poll, the commit that marks the message, a read that finds no instance yet, and the step's commit.

## What the tests did and did not show

- **Simulation:** several nodes against one in-memory store, one random action at a time: takes, balancing, polls, steps, commits, crashes, a paused owner that wakes with stale memory, commits whose answer is wrong, retried outside commands, clocks that disagree, rolling deploys of a model version with rollbacks, kernel commands, HTTP requests over a transport that drops and repeats replies, projectors on every node. 200,000 runs of 300 actions pass every invariant, and so do 20,000 runs of 1,000 actions; 12 of the latter parked an innocent message at the threshold, which invariant 14 allows (round 2: 1,412; FINDINGS section 21).
- **Mutation check:** 39 deliberate breakages of the kernel, 20 of them added in round 3, one per rule of a new piece. All but two are caught within 20,000 runs, most in the first. Two are not caught alone (the position in the fence; the count on deletes) because another rule covers each; both are caught when that other rule is removed too. One (`StepNewerState`) is rare enough that it runs at 1,000 actions.
- **PostgreSQL stress:** six workers on two partitions with 150 ms leases, stalls and randomly killed connections, on the round-3 store. Two runs (5,000 and 8,000 messages, about 500 and 825 takeovers) end with every property holding; with the fence broken on purpose the same test reports 482 violations. Cost per run is unchanged from round 2.
- **Not tested:** MySQL, SQL Server and SQLite under concurrency (another branch), the fence in the batch under stress, the new store operations under the PostgreSQL stress test (heartbeat, projection, start subscriptions, delete: they are exercised only by the simulation and the cost run).

## Open questions

- **Poison counting.** Round 3 made "alone" mean alone in the process (F11) and lets a trial start only when its step can run at once (F13); innocent parking at 1,000 actions fell from about 3 percent of seeds to about 0.1 percent (FINDINGS section 21). What remains comes from owner losses during the few actions of a trial; in the simulation most owner losses are owners that let their lease run out, not crashes. Off-step work started before a trial and still running can kill the process during it and is counted against the trial. Recovery of a deep backlog after a takeover is serial per process.
- **Mixed model versions.** A node behind a rolling deploy withholds instances written by a newer model (F14). Whether a node should refuse partitions while a newer version runs in the cluster (the `node` row could carry versions) is open.
- **Reply addresses** should carry the caller's deadline, so an owner can forget a caller that gave up; today such an instance stays in memory until it ends.
- **Delete and keys.** A message with a start spec after a delete creates a new instance with the same id. Right for a keyed instance ("start over"), wrong for a child id; to be decided per kind of id.
- **State size.** The whole state is rewritten at each commit. Fine for kilobytes. A growing agent thread or a large variable needs segments or values stored by reference. Measure before adding.
- **Events:** the tables with the two-sided check are kept; correlation as an instance per key is a model for narrow, quiet keys (FINDINGS section 8).
- **Notifier between nodes.** Polling only, or a pluggable signal; the HTTP reply transport is the same channel.
- **What carries over.** The activity library and the designer's definitions carry over with an adapted activity context. The native interpreter is rewritten as a model. The runtime API and Studio's run views are re-pointed at `instance` and the projections.
- **One database.** "The outbox is the recipient's inbox" assumes all partitions share a database.
