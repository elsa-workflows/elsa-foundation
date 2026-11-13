# Findings

Commit hashes cited below are in the prototype's history, kept at the tag `prototype/kernel-model-history`.

**Names.** Sections 1 to 23 use the names the code had when they were written: an instance's *step* (`IStep`,
`StepAsync`, `Step.cs`, a step commit) is a **turn** (`ITurn`, `TurnAsync`), and off-step work, a *run* (`IRun`,
`Run`, `CancelRun`, `RunFinished`, `kernel.run`, the `runs` column), is a **background job** (`IJob`, `StartJob`,
`CancelJob`, `JobFinished`, `kernel.job`, `jobs`). `src/Kernel` is now three projects: `src/Kernel.Runtime`,
`src/Kernel.Durable` and `src/Kernel.Workflows`. The flaws `VolatileRunTicket` and `StepNewerState` are
`VolatileJobTicket` and `TurnNewerState`. Section 24 has the mapping.

What the model and the simulation say about the kernel sketch (draft 2, 12 November 2026).

With every fix below in place: 200,000 seeds of 300 actions (60 million actions) pass every invariant and the end
state; so do two PostgreSQL stress runs. Run.

Every finding names its evidence: **run** means the simulation or a test produced it and the command replays it;
**reasoned** means nobody ran it. Seeds are for 300 actions (the default).

## 1. Contract changes of 12 November

These came from the two reviews of draft 1, not from the simulation. The model was built to them.

| Change | In the model |
|---|---|
| `IStep.CommitAsync` is gone: a step commits at its end and only there. A durable boundary is a message to self | `Sequence` in durable mode: one step and one `continue` message per activity |
| Off-step work: `Run`, `CancelRun`, the model's `RunAsync`; the result comes back as a message | `Runner` model; see F4 for what the simulation changed |
| A message may carry a start spec; a target that does not exist is created on delivery | Every counter and runner message carries one; invariant 10 is tested by two different messages for one target |
| Messages from one sender to one target are handled in the order sent | Invariant 11; see F1 |
| Outside commands carry `keep_until` and are marked `done`, not deleted | `StepCommit.Done`; a client retries old commands inside the window |
| The fence is checked before the commit is sent | Both EF variants; mutation `CommitDespiteFence` |
| Polling is a plain read, `seen < e`, ordered by `seq`, with a limit; marks ride in a fenced commit | Poll limit 3 in the simulation, so a backlog is always larger than the limit |
| Failed commits are told apart: unknown or fence means reset, a deadlock victim retries, a definite error parks one instance | `Failure.Unknown`, `Transient`, `Definite`; `Fragile` model |
| Events: an `event` table; each side reads after its own commit | Invariant 13; see F6 |
| Keyed starts go in as messages; starts without a key are handed to the owner in memory | `Worker.Post`; outside starts are retried |

## 2. What the simulation found

Each finding is switched off by one `Flaw`, so the seed that exposed it stays replayable:
`dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --seed <seed> --flaw <Flaw>`.

### F1. A hand-over in memory overtakes older rows after a takeover (invariant 11). Run

- **Replay:** seed 626, `--flaw HandOverWhileBehind`. First seen at seed 484 before later changes moved it.
- **Violation:** `I11 1/c0 applied [..., t56>, t17>]; 1/c0 sent it [t17>, t56>] in that order`.
- **Trace, in short:** an owner commits a step that sends `t17>` and dies before the hand-over. The next owner polls
  with a limit and gets the oldest rows; the row of `t17>` is further down. A step of the same sender sends `t56>`,
  which is handed over in memory and handled at once. The poll brings `t17>` later.
- **Sketch sentences:** "For a recipient in the same partition the owner also hands it over in memory right after
  the commit" and "A sender's commits are serial, so messages from one sender to one target arrive in the order sent."
  Both cannot hold while the owner has not yet read the rows it inherited.
- **Fix:** an owner hands nothing over in memory until it has caught up: a poll came back shorter than the limit and
  every row the owner itself wrote meanwhile has come back with a poll. Until then its own messages go through the
  table with `seen = 0`. `Worker._caughtUp`, `CommitBatch.HandOver`.

### F2. Bumping attempts on every inherited row parks innocent messages (invariant 14). Run

- **Replay:** seed 3, `--flaw BumpAllInherited`. Shrinks to two actions: an ordinary message and a message whose step
  kills the process, in one partition.
- **Violation, shrunk:** `I14 1/c0 did nothing wrong and was parked: message x30 was taken 9 times and never handled`.
- **Sketch sentence:** "the first poll after a take bumps `attempts` on every inherited row". Every message that
  was in the dead owner's memory is bumped, at every death. The neighbours of a poison message reach the threshold
  together with it. It failed 4,683 of 20,000 seeds.
- **Fix:** inherited messages are tried one at a time. A fenced commit bumps the attempts of one message; its step
  then runs with no other step in flight; the next one follows when it is consumed. A death counts against the
  message that was on trial. `Worker.NextTrial`, `Worker._trial`.
- **What is left, run:** an owner that is lost for another reason while a message is on trial still costs that
  message an attempt. That includes a reset after a lost connection, where nothing died. With a threshold of 2 and
  the simulation's rate of faults, 137 of 4,000 seeds parked an innocent message. With 8, the threshold the
  simulation now uses, none of 200,000 seeds of 300 actions did, and 11 of 20,000 seeds of 1,000 actions did
  (first: seed 500014, `--actions 1000`). The threshold must exceed the number of unrelated owner losses one
  message can plausibly sit through.
- **Suggested, reasoned:** an owner that resets and takes the partition again is alive, so what it held did not kill
  it. It could skip the bump for rows it had marked itself.
- **Cost, reasoned:** after a takeover the inherited rows are handled serially, two commits each. A deep backlog
  recovers slowly. Trying them in halving groups would shorten that at the price of a higher threshold.
- **Not solved:** run tickets are not counted, so off-step work that kills the process walks the cluster. Trials
  would serialize the restart of long work.

### F3. A message that kills the process before any commit is never counted (invariant 14). Run

- **Replay:** seed 3, `--flaw RunBeforeMark`. Shrinks to one action: a message whose step kills the process.
- **Violation:** `IDLE not idle after 150 rounds without faults; waiting: x31 (die to 1/f2, seen 0, attempts 0)`.
- **Sketch sentences:** "Rows the owner holds and has not consumed are marked `seen = e` by its next commit" and
  "the first poll after a take bumps `attempts` on every inherited row before any step runs". A row that was polled
  and run before any commit still says `seen = 0`. The next owner does not know anyone had it, so nothing is bumped
  and the message kills every owner in turn.
- **Fix:** a polled message is not acted on until a fenced commit has marked it seen. `Worker.Awaits`.
- **Cost, run:** one more commit before the first step of anything that arrives by poll: 3 round trips on PostgreSQL
  (section 5). Messages inside a partition are written as seen and pay nothing.

### F4. Nothing wakes an instance whose run was in flight (invariant 12). Run

- **Replay:** seed 6, `--flaw VolatileRunTicket`.
- **Violation:** `I12 run k30 of 1/r0 has neither a result nor a cancel: [start k30]`.
- **Sketch sentence:** "A new owner starts pending runs again when it loads the instance." After a takeover nothing
  loads the instance unless another message arrives for it. The flaw models the intent being nowhere in the inbox;
  that the literal rule fails for an instance with no other message is reasoned from the same trace.
- **Fix:** `Run` also sends a message to self, a ticket, in the same commit. A new owner polls it and starts the
  work. When the work completes, the ticket becomes the result message: same row, consumed by the step that handles
  it. "After a crash the inbox alone says what to do next" then holds for runs too.
- **Second finding behind it, run during development (seeds 4 and 18, no flaw kept):** a step cancelled a run whose
  ticket the owner had not yet polled; the cancel found nothing, and the result arrived afterwards
  (`[start k12, cancel k12, result k12]`). So the instance row does need the kernel's list of runs in flight, as the
  sketch has it, and the ticket's id must be derived from instance and key so that a step can delete it without
  holding it. `InstanceRow.Runs`, `Worker.TicketId`.

### F5. A poll in flight brings back a message that was consumed meanwhile. Run

- **Replay:** seed 3343, `--flaw PollRace`. It needs a slow answer, so it is rare: 25 of 20,000 seeds.
- **Violation:** `I9 actor 5 holds message x13, which is not a row any more`; after shrinking, `I9 consumed message x16 is not there`.
- **Trace, in short:** poll 19 reads `[x16]`. Its answer is slow. A commit marks `x16`, a step handles it, its commit
  deletes the row. The answer arrives; the owner no longer holds `x16`, takes it for new and runs it again. The
  second commit fails its count, and the owner resets for nothing.
- **Sketch sentence:** "The owner skips ids it already holds." It must also skip ids it consumed while that poll was
  in flight. `Worker._consumed`.
- **Why the count check matters, run:** without the skip and without the count (`--flaw PollWithoutSkip,DeleteWithoutCount`)
  the message is applied twice: `I4 1/c1 applied a message twice: [t2, t2]`, 953 of 20,000 seeds.

### F6. A crash between a subscribing commit and its read of events loses the event (invariant 13). Run

- **Replay:** seed 10, `--flaw MeetWithoutTicket`.
- **Violation:** `I13 event e1 for key k0 and the wait of 0/w25 are both committed and never met`.
- **Trace, in short:** the event is published while the subscribing commit is in flight, so the publisher finds no
  subscription. The commit lands and the node dies before it reads the events. Nobody looks again.
- **Sketch sentence:** "After the commit the owner reads `event` for the keys it just subscribed and delivers what it
  finds." Nothing durable says that this read is still owed.
- **Fix:** the subscribing commit sends a message to self that says "read the events for these keys". It is deleted
  by a later commit, after the read. A new owner inherits it and reads again. Delivery is an idempotent insert of the
  derived message id, as on the publisher's side. `Kinds.Meet`, `Worker.MeetAsync`.

### F7. Cancel beside completion in one step. Run during development, seed 4

- A cancel command and the result of the run it cancels can be in one inbox. In `seq` order the cancel came first,
  and the model saw a result for a run it had just cancelled.
- **Choice:** a completed run's result is put ahead of the other messages. Whichever the instance handles first wins,
  and a model never sees the result after its own cancel.

### F8. On Entity Framework, `seq` inside one commit follows the message ids, not the order sent (invariant 11). Run, on PostgreSQL

- **Found by:** the PostgreSQL stress test, second run (40 s, 5,000 messages): 21 violations, all of the form
  `I11 1/c2 applied the messages of 1/c1 out of order`. The simulation cannot see it: the in-memory store hands out
  `seq` in the order sent.
- **Replay:** `Kernel.Ef.Run.dll probe "<connection>"`. One commit sends twelve messages with ids `0/a#1.0` to
  `0/a#1.11`; read back by `seq` they come as `#1.0 #1.1 #1.10 #1.11 #1.2 ...`.
- **Cause:** EF Core sends the inserts of one `SaveChanges` in key order, and the identity column numbers them as
  they arrive. A step that sent more than ten messages had the eleventh overtake the third.
- **Sketch sentences:** "`seq` is assigned by the database and gives the order" and "A message's id is derived from
  its sender and the sender's revision." Together they need: ids of one commit must sort in the order sent.
- **Fix:** the ordinal in a derived id has a fixed width (`0/a#1.0010`). `Step.Send`.
- **After the fix:** see section 4.

### Confirmed from draft 1. Run

| Flaw | Seed | Violation |
|---|---|---|
| `WallClockTimers` (timers fire on the node's clock) | 40 | `I5 ... was delivered ... 1180 ms before it was due` |
| `DeleteOutsideCommands` (the consuming commit deletes an outside command) | 4 | `I4 t4 for 0/c1 is applied and waiting again` |

The owner's estimate of database time is anchored when a poll's answer arrives, not when the poll is sent. Anchoring
at the send would run ahead of the database by the request's latency (reasoned).

## 3. Mutation check

20,000 seeds each, stopping at the first failure. Run: `--mutations --seeds 20000`.

| Mutation | Result | First seed | How it showed |
|---|---|---|---|
| `FenceWithoutEpoch` | caught | 1 | `I1 a commit prepared under epoch 1 landed ... at epoch 3` |
| `FenceWithoutPosition` | **not caught** | | See below |
| `FenceWithoutPosition` with `NoResetAfterFailedCommit` | caught | 1, 7 | `IDLE`; `I9 actor 4 believes partition 0 is at 8; it is at 11`; `I11` |
| `CommitDespiteFence` | caught | 1 | `I1` |
| `NoResetAfterFailedCommit` | caught | 1 | `IDLE`: the owner never learns and nobody else may take |
| `DeliverBeforeCommit` | caught | 1 | `I9 consumed message ... is not there` |
| `PollWithoutSkip` (the owner does not skip ids it holds) | caught | 1 | `IDLE`: a reset loop |
| `KeepConsumedMessages` | caught | 1 | `I4 t1 for 1/c0 is applied and waiting again` |
| `DeleteWithoutCount` | **not caught** | | See below |
| `DeleteWithoutCount` with `PollWithoutSkip` | caught | 1, 20 | `IDLE` at seed 1; `I4 ... applied a message twice` at seed 20 |

Every caught mutation was caught at seed 1, in under 200 ms of wall time. The flaws of section 2 are in the same run;
each is caught at the seed its finding names, the slowest (`PollRace`, seed 3343) after 1.9 s.

**The two that are not caught are not holes in the simulation.** They are rules that another rule already covers:

- **The position in the fence.** With every doubt answered by a reset and every take bumping the epoch, an owner at
  the current epoch always knows the position. The position decides only when the reset is missing, and then it is
  the last thing standing (third row). Position alone is also a sufficient fence: it is a compare-and-swap on the
  partition's log (reasoned). Keep it; it costs nothing.
- **The count on deletes.** In the atomic store, with the skip rules of F5, a consumed message is always there. The
  count decides when those rules fail (last row).

The draft-1 mutation "claim without the epoch condition" has no counterpart: draft 2 has no claim statement.

## 4. PostgreSQL stress test. Run

Three nodes, two partitions, six workers that each believe they may own their partition, each on its own loop.
Leases last 150 ms. A worker stalls for 150 to 500 ms every 25 rounds or so, so another node takes over and the
stalled one wakes with stale memory. Every 20 to 60 ms a random busy backend is terminated with
`pg_terminate_backend`, whatever it is doing. An outside producer adds commands and retries until the insert answers.
Default isolation (READ COMMITTED). The explicit-fence variant of the store.

| Run | Backends killed | Stalls | Takeovers (p0, p1) | Steps run | Instance commits | End state |
|---|---|---|---|---|---|---|
| 15 s, 1,500 messages, before the fix of F8 | 166 | 109 | 103, 110 | 3,289 | 2,913 | all properties hold |
| 40 s, 5,000 messages, before the fix of F8 | 772 | 276 | 315, 319 | 9,316 | 8,636 | 21 violations, all `I11`: finding F8 |
| 40 s, 5,000 messages | 560 | 304 | 265, 272 | 9,532 | 8,670 | all properties hold |
| 60 s, 8,000 messages | 1,038 | 434 | 443, 439 | 14,617 | 13,632 | all properties hold |
| 15 s, 1,500 messages, fence broken on purpose (`stress-unfenced`) | 209 | 101 | 14,674, 102 | 16,223 | 1,394 | 286 violations: 39 journal rows at position 12, messages never handled, instances missing |

Steps run exceed instance commits by the steps whose commits were fenced off or lost with a killed backend.

End-state properties: no message and no subscription left; every counter token applied exactly once and in order per
sender; every parent, waiter and sequence finished; one event per waiter; an instance's revision equals its journal
rows; each partition's journal has exactly `position` rows.

What this does not cover: the fence-in-batch variant, SQLite under concurrency, MySQL and SQL Server.

## 5. What it costs. Run

Counted with a `DbCommandInterceptor` and a `DbTransactionInterceptor`.

- **PostgreSQL:** a round trip is one command execution or one COMMIT. Npgsql sends BEGIN together with the first
  command of a transaction, so BEGIN is not counted. This is from Npgsql's documented behaviour; the wire was not
  captured. One `SaveChanges` is one command however many rows it carries.
- **SQLite:** there is no wire. The column counts calls into the engine: BEGIN, each command, COMMIT. EF Core's
  SQLite provider sends one command per row.
- Polls are not counted for runs started on the owner: a host polls on its own schedule whatever the load.

| Run | Sketch | PostgreSQL, checked fence | PostgreSQL, fence in the batch | SQLite |
|---|---|---|---|---|
| Ten no-ops, ephemeral, started on the owner | 0 | 0 | 0 | 0 |
| Ten no-ops, balanced, started on the owner | 1 commit: 2 to 3 | 1 commit: **3** | 1 commit: **2** | 5 |
| Ten no-ops, durable, started on the owner | 12 commits: 24 to 36 | 10 commits: **30** | 10 commits: **20** | 77 |
| Ten no-ops, ephemeral, started from another node | | 9 | 7 | 11 |
| Ten no-ops, balanced, started from another node | | 9 | 7 | 13 |
| Ten no-ops, durable, started from another node | | 36 | 25 | 85 |
| Parent with three children, same partition | | 4 commits: 12 | 4 commits: 8 | 49 |
| 100 balanced instances started at once | as one | 1 commit: **3** | 1 commit: **2** | 203 |

- **Durable is 10 commits, not 12:** one per activity; the last activity's step also finishes the instance. The start
  is handed over in memory and never written.
- **From another node, balanced, 9 round trips:** the insert (1), the poll (1), the commit that marks the message
  seen (3, finding F3), the read that learns the target does not exist (1), the step's commit (3).
- **Checked fence** is one statement with `RETURNING`, then one `SaveChanges`, then COMMIT. It takes the lease time
  from the database.
- **Fence in the batch** is `epoch` and `position` as concurrency tokens in the one `SaveChanges`. EF checks the
  row count before it sends COMMIT. It saves a round trip and takes the lease time from the node's clock, which
  the sketch allows ("a wrong lease costs availability, never correctness").

## 6. Where the sketch was ambiguous, and what the model does

| Question | Choice |
|---|---|
| How does the kernel know an instance is ephemeral? `IStep` has no member for it | `StartSpec.Ephemeral`. An ephemeral instance that ends in the step that created it, having sent nothing and subscribed to nothing, is not written. Otherwise it commits like any other |
| How does the kernel read a state it does not know the type of? | `IExecutionModel.StateType` |
| "Check that it changed exactly one row before anything else is sent" | Before COMMIT is sent. The fence-in-batch variant sends the writes with the fence and checks before COMMIT |
| What is "inherited"? | `seen > 0`, due, and not a kernel ticket |
| What does a count that does not match mean? | Reset, like a fence that matched nothing. On EF a key violation is a definite error and parks the instance |
| A step throws | The instance is parked at once. Retries are the model's business |
| Messages ahead of the one that creates their target | Consumed with it, not shown to the model |
| "Dropped and journalled" | A journal row with revision 0 that takes a position |
| A finished instance's subscriptions | The model must unsubscribe; the kernel holds no list of them |
| When does an instance row first exist for a child? | At the child's first commit, not at the parent's |
| Does a parked child tell its parent? | Yes, as a child-finished message with status faulted |
| `position` when no journal row is written | It counts journal rows. Commits that only mark or delete messages do not move it |
| How does a stale owner that never commits find out? | `BeatAsync`: an empty fenced commit. The poll is a plain read and cannot tell it |
| History level | Always full in the model |

**Not modelled:** the `node` heartbeat and fair share; the `projection` row (the simulation's projector keeps its
cursor itself); `Hold`, `Emit`, `Ambient`, `Rebind`, `Describe`, state versions; headers, reply-to, tenant, root,
sub-status; subscriptions of kind start; placement other than with the parent; the bound on instances in memory;
suspend and resume; correlation as an instance per key; MySQL and SQL Server.

# Round 2 (against draft 3)

Sections 1 to 6 above are round 1 and describe the kernel as it was at commit `4b2f4d1587`. Seeds named there were
found on that kernel; the flaws still replay, but a seed may have moved.

## 7. Poison counting, reworked

### What changed. Run

- **An attempt is a death, and a death is a take.** A take from another owner can only succeed because that owner's
  lease expired. In the same transaction the take adds one attempt to every message the lost owner had on trial.
  Nothing else changes `attempts`. A release, a reset followed by a take by the same process, and a take of a free
  partition are not deaths. `IKernelStore.TakeAsync`, `ReleaseAsync`; `message.trial` is a new column (the epoch at
  which the row was last put on trial).
- **Who is a suspect.** A row that another process marked, or that is on trial already. What this process marked in
  an epoch it held earlier is not: the process is alive. `Worker.Suspect`, `Worker._mine`.
- **Trials.** Suspects are put on trial by a fenced commit (`seen = e, trial = e`), in a commit that leaves nothing
  else in flight; from the moment that commit is sent until every member is consumed, the owner starts no other step
  and no other off-step work. `Worker.NextTrial`.
- **Run tickets are tried like any message.** Off-step work that was on trial at the threshold is parked: its ticket
  becomes a result with an error and the instance goes on.
- **Groups that halve.** `WorkerOptions.TrialGroup` (default 1). A group holds up to `TrialGroup >> attempts` suspects
  with the same attempts, so a poison message is alone after log2(group) deaths.

### Invariant 14, stated exactly

1. `attempts` of a message changes only in a take that finds another owner's lease expired, and then by exactly one,
   for exactly the rows whose `trial` is the lost owner's epoch.
2. While rows are on trial at the current epoch, the owner starts no step of an instance and no off-step work that
   has no row among them.
3. A message is parked for losing owners if and only if its attempts have reached the threshold. With a trial group
   of 1 that means: the threshold number of owners were lost while it was on trial alone.
4. An instance is parked otherwise only when its step throws or its row is refused.
5. A message whose step or off-step work kills every owner is parked after threshold + 1 deaths: the first death
   finds it not yet on trial.

The simulation checks 1 by recomputing every take (`World.Take`), 2 at every start of a step or run (`World.Alone`),
3 and 4 on every faulted row, and 5 at the end.

### Results. Run

- 20,000 seeds of 1,000 actions: 0 failures, with the threshold at 3 (and at 5 with groups of 4 on every fifth seed).
- Under rule 3 an innocent message can still be parked: 1,412 instances in those 20,000 runs lost three owners while
  on trial. The simulation loses an owner about every 70 actions and a trial lasts tens of actions there; a real
  trial lasts two commits.
- `--flaw EveryTakeCounts` (any take counts): caught, seed 31. `--flaw BumpAllInherited` (all suspects at once, no
  quarantine): caught, seed 2. `--flaw RunBeforeMark`: caught, seed 3.

### F9. A node's name must be unique per process start. Run

- **Replay:** seed 3, `--flaw StableNodeName` (one node, which keeps its name across restarts): `IDLE`, the message
  that kills the process is never parked. 2,107 of 6,000 seeds.
- **Why:** "re-taken by its holder" is recognised by the owner's name. A restarted process with the old name is taken
  for the live holder, so its predecessor's death is never counted.
- **Cost, reasoned:** a restarted process must wait for its predecessor's lease to run out before it owns anything.

### F10. A refused batch lost its marks when its first instance was parked. Run

- **Replay (on the kernel before the fix):** seed 22: `IDLE`. The trial was recorded in memory when the commit was
  sent; the batch was refused, the first instance was parked in a commit that carried no marks, and the trial never
  reached the rows. The owner waited for it for ever.
- **Fix:** the marks ride with the first commit of the one-by-one sequence that lands.

### The trade of groups. Run: `--backlog 1000`

| Trial group | Commits to recover 1,000 messages held by a dead owner |
|---|---|
| 1 | 1,000 |
| 8 | 126 |
| 64 | 16 |

- With a group of 1 the next trial rides in the commit that consumes the previous one, so recovery costs one commit
  per message and the rest of the partition waits meanwhile.
- A group of `g` needs a threshold above log2(g), and a neighbour of a poison message can collect up to log2(g)
  attempts before the group has halved down to one (reasoned).
- **Not solved, reasoned:** a suspect that is off-step work holds its trial until its result is handled. Long work
  that was in flight at a crash therefore holds up its partition while it is tried. A group lets up to `g` such runs
  restart together.

## 8. Correlation as an ordinary instance per key

Built as a model on the kernel with no kernel change: `sim/Correlation.cs`, 50 lines for the instance and 25 for a
waiter that uses it. Subscribing, unsubscribing and publishing are messages to the key's instance, which carry a start
spec so the first one creates it. The instance holds the waits and the events it keeps, forwards an event to every
wait, gives a new wait the events that came before it, and drops a kept event on a timer to itself.

### Invariant 13 against it. Run

- 20,000 seeds of 300 actions and 10,000 of 1,000 pass with both versions in every run: waits and events cross in
  either order, with takeovers, crashes and retried publishes in between. It passed on its first run.
- The check bites: `--flaw CorrelationForgets` (a new wait is not given earlier events) fails 2,557 of 5,000 seeds,
  first seed 2: `I13 the wait of 0/cw57 and an event sent to 0/key:q0 never met`.

### Cost. Run, PostgreSQL round trips (in brackets: the fence as concurrency tokens)

One waiter started on its owner, one event, the waiter finishes. Both orders cost the same.

| Version | Commits | Round trips |
|---|---|---|
| Tables (`subscription`, `event`, the owed read) | 4 | 17 (13) |
| Instance, key in the waiter's partition | 6 | 21 (15) |
| Instance, key in another partition | 9 | 33 (24) |

With more than a few partitions the key is almost always in another partition, so the instance version costs about
twice the round trips, and the event passes two polls on its way (publisher to key, key to waiter) where the table
version passes one.

### Kernel size. Counted on the source, not by removing it

The table version is about 85 lines of the kernel (`MeetAsync` and its ticket, the three storage operations, the
subscription lists of a step and a commit, the in-memory tables), about 45 lines of the EF store, two tables, finding
F6 and invariants 8 and 13 as rules of the kernel. The instance version is none of that.

### Which to keep

**The tables, in the kernel; the instance version stays as a model for whoever wants it.** Measured: it is cheaper
and one hop shorter. Reasoned, and these decide it:

- **A hot or broad key.** Every wait and every event of a key goes through one instance, and its whole state (all
  waits, all kept events) is rewritten at each commit. A key with many waiters, or many events inside the keep
  window, makes that state large. In the tables these are rows.
- **A parked key.** If the key's instance is parked, every message to it is consumed and dropped: the key is dead for
  everyone. A parked waiter in the table version harms only itself.
- **Rows that stay.** A key's instance never finishes (a finished one would swallow the next subscribe), so one row
  per key ever used remains until something deletes it.

For keys that are narrow and quiet (a correlation id, an approval) the instance version is the simpler thing and
needs nothing from the kernel. So the tables were not removed.

# Round 3 (pieces of draft 3 that were not built)

Sections 1 to 8 describe the kernel at commit `a4f7c49b65`. Round 3 adds the missing pieces one commit at a time, each
with a check that fails when its rule is switched off by a `Flaw`. The simulation's actions and their mix changed, so
seeds named in rounds 1 and 2 may have moved; their flaws still replay. Every piece passed 20,000 seeds of 300 actions,
5,000 of 1,000 and the mutation check before its commit. Run.

## 9. Fair share of partitions. Run

- **Built:** a `node` row per process (`IKernelStore.HeartbeatAsync`: write the heartbeat, count the nodes heard from
  within a window, list the partitions that are free or expired) and `Balancer`: release what is above the share,
  take free partitions up to it. Share = partitions / nodes, rounded up. The simulation's nodes balance at random
  moments beside the old random takes, and the settling phase is driven by balancers only.
- **Check:** once faults stop, every partition has one owner at its current epoch and no node holds more than its
  share. `--flaw GreedyBalancer` (take every free partition, never release): caught, seed 1.
- **Window, run:** a heartbeat window shorter than the time between heartbeats makes nodes see each other every other
  round, and the balancer oscillates (seed 2 shrank to zero actions). The window must span more than one beat.

### F11. A trial was alone in its partition, not in its process (invariant 14). Run

- **Found by:** `GreedyBalancer`, seed 14: one node owned both partitions; off-step work in partition 1 killed the
  process again and again, and every death also counted against the message on trial in partition 0. An innocent
  child-finished message reached the threshold and its parent was parked.
- **Sketch sentence:** "A death therefore counts against the message that was on trial and not against its
  neighbours." A process that owns several partitions dies for all of them at once, so its trials are neighbours.
- **Fix:** `TrialGate`, one per process: at most one of its workers has a trial in flight, and while it has, the
  process starts no other step and no other off-step work. The check "alone" is now per process (`World.Alone`).
  `--flaw TrialPerPartition`: caught, seed 42 (`I14 a step of 1/c0 started on actor 15 while x2 was on trial in
  partition 0 of the same process`).
- **Cost, reasoned:** after a node dies, each survivor recovers the partitions it inherits one trial at a time for
  the whole process, not per partition. A trial is two commits; groups (`TrialGroup`) shorten it.
- **Not solved, reasoned:** off-step work started before a trial and still running during it can kill the process,
  and the death counts against the trial. The simulation's fatal work dies at its start, so it cannot show this.

## 10. Projector and history levels. Run

- **Built:** `Projector`: read its position from the `projection` row, read the journal after it, stop at the first
  position not written, and in one transaction write the view rows and move the position, only if the position is
  still where it read it (`IKernelStore.ProjectedAsync`, `ProjectAsync`). The view is one row per instance: last
  revision and number of commits. `History` (Full, State, Minimal) on `StartSpec` and the instance row: Full writes
  the model's entries, State an empty entry list, Minimal no journal row.
- **In the simulation:** every actor has a projector on its own faulty wire, so projectors run on nodes that do not
  own the partition, several at once, and die between their read and their write. Sequences and parents get a random
  level.
- **Check (I6, now at each level):** an instance at Full or State has one journal row per revision, at Minimal none;
  every view row counts exactly the journal rows of its instance up to the projector's position; at the end the
  projector reaches the partition's position. `--flaw ProjectorBlindWrite` (the write does not check the position):
  caught, seed 13 (`I6 the view of 1/r0 says revision 1 after 2 commits`).

## 11. Synchronous HTTP: `Emit`, `Ambient`, start-and-observe, reply addresses. Run

The owner's requirement of 12 November: a workflow handles an HTTP request and produces the response itself.

- **Built:** `IStep.Emit(channel, item)` and `IStep.Ambient`. What a step emits rides with its unit and is released
  only when the commit lands, or at once when the step writes nothing (`Worker.Release`). A message may carry
  `ReplyTo` (node and request id); when a step consumes it, the address joins the instance's callers in the owner's
  memory, gets every item emitted from then on, and the end when the instance ends. A start handed over in memory by
  `Worker.Post` carries the caller's address and the `Ambient`, which lives in that memory and nowhere else. A reset
  tells every caller it knows of that the instance is lost; a missing or finished target answers at once. Replies go
  through `IReplyTransport` (actor-runtime layer: a call inside a process, not durable between nodes). The caller side
  is `Requests`/`Request`: the first response or the first end answers, anything later is ignored, and a deadline
  ends the wait.
- **In the simulation:** an `Http` model with five modes, one per case: answers and finishes in its first step (also
  ephemeral, never written); answers and goes on; waits for a timer or off-step work, then answers; waits for a resume
  that a request on any node sends with a reply address, then for a timer, then answers. Callers disconnect, some with
  a cancel. The transport between nodes drops 25 percent of replies, delivers 15 percent twice, and delays all of them.
- **Invariant 15:** a caller receives at most one response and at most one end signal; it receives the response only
  if the step that emitted it committed (its answer is in a journal row) or wrote nothing (an ephemeral instance with
  no row); it never waits for ever (at the end no request on a living process is open); a step sees the ambient only
  on the process that started the instance.
- **Flaws:** `EmitBeforeCommit` caught, seed 1 (`I15 request q2 was answered 0/h2:1@0, which no commit of 0/h2
  wrote`); `ReplyTwice` caught, seed 1; `NoDeadline` caught, seed 13 (`I15 request q49 ... still waits`).
- **Coverage, run, 4,000 seeds that reached their end:** started requests: 5,933 answered, 3,636 lost by a reset,
  4,488 timed out, 2,217 disconnected; resumed from any node: 1,058 answered, 113 lost, 4,592 timed out, 176 ended
  Cancelled, 13 ended Finished without an answer, 1,481 disconnected.
- **What the check showed, run:** `NoDeadline` was caught only at seed 10,750 while the resumed instance answered in
  the step that consumed the resume: a reply is then lost only if both the response and the end are dropped. When the
  instance waits after the resume (case 4 with case 5), the address lives only in the owner's memory, and a crash
  there leaves the caller with nothing but its deadline (seed 13). So the deadline is load-bearing, not a nicety.
- **Choices, reasoned:** a reset tells callers "lost" even when the rows still hold the request's message; a new
  owner answers that message later, and the caller has already ended. A caller prefers a quick 503 to a late 200.
  "Respond before the commit" (the sketch's opt-out) is not built.

## 12. Suspend and resume by the kernel. Run

- **Built:** `kernel.suspend` and `kernel.resume`, messages anyone may add. The kernel handles them alone, in `seq`
  order, after what came before them; no model sees them. `Suspended` is a column of the instance row. While it is
  set, the instance is given nothing but kernel messages (a resume overtakes what waits); its messages, results and
  due timers wait in order and are delivered after the resume. A command that changes nothing is consumed and dropped.
- **In the simulation:** outsiders suspend and resume random running instances (counters, children, runners, HTTP
  instances, correlation keys); the settling phase resumes whatever is suspended.
- **Invariant 16:** while its committed row says suspended, the current owner runs no step of the instance; at the
  end nothing is suspended and everything that waited was delivered (the existing order and timer checks hold across
  a suspension). `--flaw DeliverWhileSuspended`: caught, seed 10.

### F12. Suspects of a suspended instance hide the resume (liveness). Run

- **Replay:** seed 989, `--flaw NoShelf`: `IDLE ... resume-1/hw9.4 (kernel.resume to 1/hw9, seen 0)`.
- **Trace, in short:** an owner dies with messages of an instance that is then suspended. The new owner suspects
  them, but a suspect of a suspended instance cannot be tried, so its row stays unseen. With a poll limit of three,
  they are the first three rows of every poll; the resume behind them is never read.
- **Sketch sentence:** "Polling: `... WHERE seen < e ... ORDER BY seq LIMIT m`" with "a new owner tries the messages
  its predecessor had marked one at a time". A row that waits for something its own poll cannot reach blocks the poll.
- **Fix:** a suspect whose instance is suspended is *shelved*: a fenced commit marks its row seen at this epoch (so
  polls pass it) without putting it on trial; in memory it stays a suspect that is not cleared to run. A later owner
  still suspects it. `Worker.Shelf`, `Item.Shelved`.

### F13. A trial must not start on an instance that can be suspended under it (liveness). Run

- **Replay:** seed 2101, `--flaw SuspendDuringTrial`: `IDLE ... 0/h1!work (kernel.run to 0/h1)`.
- **Trace, in short:** a run ticket was put on trial in the same commit that suspended its instance. The run's
  result could then never be consumed, and the trial held up the whole process for ever.
- **Fix:** a suspect is tried only when its step can run at once: its instance is loaded, has nothing in flight, is not
  suspended, and no kernel message waits ahead of it (`Worker.CanTry`). While an instance has a message on trial, a
  suspend to it waits.
- **Side effect, run:** innocent messages parked at the threshold fell from 154 to 1 in 5,000 seeds of 1,000 actions.
  A trial no longer waits for its instance's load. Section 21 measures this.

## 13. Forced termination of a tree. Run

- **Built:** `kernel.terminate`, handled by the kernel alone. The instance ends Cancelled (if it still ran), its
  off-step work is cancelled (tickets deleted), all its subscriptions are deleted (`StepCommit.Unsubscribe`: the
  kernel holds no list of them), its parent is told, and a terminate goes to every child in the kernel's list
  (`InstanceRow.Children`: added by `Start`, removed when a child-finished is handled). A terminate also passes a
  suspension, and still goes on to the children of an instance that has finished. `Placement.Spread` puts a child in
  a partition hashed from its name (`WorkerOptions.Partitions`).
- **Why a child being created is reached, reasoned and then run:** the child's start and its terminate come from one
  sender to one target, so they are handled in that order (invariant 11), wherever the child lives.
- **In the simulation:** a `Tree` model that never ends by itself: inner nodes start children, every other one spread;
  a leaf holds off-step work and a wait. Outsiders terminate random tree nodes and runners; the settling phase
  terminates only the roots.
- **Invariant 17:** after the roots are terminated, no tree instance runs, none still lists children, and no wait of
  a terminated instance is left (I8 at every action, the end check for subscriptions).
  `--flaw TerminateRootOnly`: caught, seed 1 (`END the subscription of 1/tr32~1.1~1.0 to tk is left behind`).
- **Coverage, run, 4,000 seeds:** 13,568 descendants cancelled in another partition than their parent's, 19,643 in
  the same; 431 of them were created after their parent had already been terminated.

## 14. `Hold`. Run

- **Built:** `IStep.Hold(message)`: the message is not consumed. It stays a row, goes back into memory in its place in
  `seq` order, and is delivered with the next step, which comes when something new arrives. A held result keeps its
  run in flight. Nothing is held into an instance that ends in that step. After a takeover the new owner does not
  know a message was held and may deliver it alone; the model must allow for that (the simulation's counter does).
- **Contract finding, run:** a held message that was on trial has run without killing the process, so its trial
  must end. The row's trial mark must be cleared in the same commit (`StepCommit.Held`), or the row still says "on
  trial" while the process goes on with other work, and the next owner's take would count a death against it.
  `--flaw HoldKeepsTrial`: caught, seed 2 (`I14 run work of 0/h13 started ... while x2 was on trial`).
- **Liveness, run during development (seed 66, three actions):** a held message waited for something new; a resume
  behind it waited for everything ahead of it to go first; and the nudge that would have brought the next step was
  behind the resume. Kernel messages now pass held messages. No flaw kept: the rule is one condition in
  `Worker.Deliverable`.
- **Check:** the counter holds a message that asks for it, with everything after it, and sends itself a nudge; the
  existing checks of invariants 3, 4 and 11 then say that nothing held is lost, applied twice or reordered.
  `--flaw HoldLoses` (a held message is consumed): caught, seed 1 (`I3 t41 for 1/c0 is neither waiting nor applied`).

## 15. State versions. Run

- **Built:** `IExecutionModel.StateVersion` and `UpgradeState`; `StateVersion` on the instance row. On load, a row
  at an older version is upgraded; the next commit writes the model's version.
- **Contract change, reasoned:** the sketch has `UpgradeState(object state, int fromVersion)`. The kernel cannot
  produce that object: it reads a state as the model's current `StateType`, and an old state may not fit it. The
  upgrade takes the stored form: `UpgradeState(string state, int fromVersion)`.
- **In the simulation:** every third seed is a rolling deploy. Counters start at version 1, which keeps tokens; version
  2 also keeps their count, and upgrades a version-1 state by counting. A restarting node comes back at version 1 or
  2 in turn (a deploy with rollbacks), and such seeds restart nodes more often. The settling phase finishes the deploy.

### F14. A node behind the deploy must not step a row written by a newer model (invariant 18). Run

- **Replay:** `--flaw StepNewerState --actions 1000`, seed 243 (`I18 1/c0 went from state version 2 back to 1`). It
  is rare: 7 of 5,000 seeds of 1,000 actions fail, and at 300 actions none of 20,000 once the seeds moved (an owner
  at version 1 held a counter row at version 2 at 28 moments in 4,000 seeds). The simulation's chaos phase commits
  little (3.7 instance rows per seed of 300 actions; round 2's kernel, run the same way, 2.1), so counters rarely
  reach a row before the deploy finishes. The mutation check runs this flaw at 1,000 actions.
- **Sketch:** says nothing about a cluster in which nodes run different versions of a model. One does during every
  rolling deploy. An older model reads a newer state as far as it understands it and writes it back older: data is
  lost without an error.
- **Fix:** an instance whose row is at a newer state version than this node's model is withheld like a suspended
  one: kernel messages pass, the model's wait, its suspects are shelved (F12). It moves on when an owner at the
  newer version takes the partition.
- **Proposed, reasoned:** a node should not take partitions while a newer version of one of its models is running
  in the cluster (the `node` row can carry the model versions), or withheld instances wait for the deploy.
- **Invariant 18:** an instance's state version never goes back, and a counter at version 2 counts exactly the
  tokens it holds. `--flaw SkipUpgrade` (read an old row as the new type): caught, seed 33.

## 16. `Rebind`. Run

- **Built:** `IStep.Rebind(definition)`: the commit writes the new definition into the row; from the next step on
  the instance belongs to it. Three lines of kernel.
- **Check (invariant 19):** outsiders tell counters to move to another version of their definition. The counter
  notes the definition it was bound to, and every later step checks that it sees that one; the row must say it too.
  Takeovers in between are covered because the definition is in the row. `--flaw RebindForgotten`: caught, seed 7
  (`I19 1/c0 is bound to c7, its row says c`).
- **Not built, reasoned:** a rebind that also needs the state upgraded (a definition version whose model state
  differs) is a model's business: it can rebind and rewrite its state in the same step.

## 17. Starts triggered by an event. Run

- **Built:** a subscription of kind start is a `subscription` row with a start spec and no instance, registered from
  outside (`IKernelStore.SubscribeAsync`, `Events.SubscribeAsync`). Both sides derive the delivering message from
  event and subscription: its id (`ev:<event>::<subscription>`) and its target (`on:<event>:<subscription>`, in a
  partition hashed from both), and it carries the start spec and the event's `keep_until`. So the publisher and the
  registrar may both insert it; the second insert is refused, and the instance is created once. Once handled, the
  message stays as a done marker for the event's window, so a late repeat meets a duplicate key.
- **Contract finding, reasoned:** the registrar has the same duty as the publisher: insert, then read the other side.
  An outsider that dies between the two loses the crossing, as F6 did for waits; outside callers already retry until
  done, so the registrar must too. Registration as part of a commit (a deployer instance that subscribes) would put
  the read in the inbox as F6 did, and needs nothing new.
- **In the simulation:** start subscriptions on two keys; the registrar's insert and its read are separate actions, so
  publishes fall before the insert (641 times in 4,000 seeds), between the insert and the read (507) and after (145).
- **Invariant 20:** every pair of an event and a start subscription of its key, both committed, makes exactly one
  instance, which takes exactly that event and finishes; no other instance is started. `--flaw StartWithoutRead`:
  caught, seed 9.

## 18. Placement across partitions, and cancelling a parent. Run

- **Built in the kernel:** nothing beyond `Placement.Spread` (section 13). A child in another partition is a start
  message in that partition's inbox; it is not handed over in memory and not marked fresh, so its owner reads the
  instance row before creating it.
- **In the simulation:** parents spread every other child to a hashed partition; a quarter of them start children
  that wait until cancelled. Outsiders cancel parents. A parent sends a cancel to every child in its own list of
  starts and ends Cancelled when every child has reported; a child reports that it started.
- **Check:** the tree check of round 1 now covers children wherever they live and the cancel: each pending child is at
  one stage (start waiting, running, or its child-finished waiting); a parent is Cancelled only if it was asked; at
  the end every parent has finished or been cancelled. 5,356 children were cancelled by their parent in 4,000 seeds,
  1,032 of them in another partition.
- **The sketch's argument, run:** "A cancel goes to the children the parent itself started, so admitting a child and
  cancelling the parent cannot race." `--flaw CancelKnownChildren` makes the parent cancel only the children that
  have reported back: a child whose start is still on its way is missed and waits for ever. Caught, seed 5 (`END
  parent 0/par5 did not finish`). The flaw is in the model, not the kernel: what it shows is that the kernel's order
  (one sender, one target) lets a model cancel what it started without waiting to hear from it.

## 19. A bound on instances in memory. Run

- **Built:** `WorkerOptions.MaxInstances`. After each commit and each load, the owner drops the least recently used
  idle instances above the bound; a message for a dropped instance loads it by key again (the existing load path).
  Idle means: loaded, no step or commit in flight, no message waiting for it, no caller waiting for its output. The
  `Ambient` goes with the activation; a run in flight keeps its instance in memory through its ticket.
- **In the simulation:** two idle instances per worker, so eviction is constant: 319,913 evictions in 4,000 seeds.
- **Invariant 21:** no worker holds more idle instances than the bound; invariant 9 (memory matches rows) holds for
  every reload. `--flaw EvictBusy` (an instance whose commit is in flight may be dropped): caught, seed 4 (`IDLE`),
  and at seed 67 as the race it causes: `I9 instance 1/c0 is at revision 2, the commit expected 1`. The reload read
  the row before the commit in flight landed, and the next step was prepared on the old revision.
- **Not solved, reasoned:** an instance with a caller waiting is never dropped, but the owner does not know when that
  caller gave up. A reply address should carry the caller's deadline, so the owner can drop the address and with it
  the pin.

## 20. Deleting an ended instance. Run

- **Built:** `kernel.delete`, handled by the owner. An ended instance's row is deleted at its revision, with its
  subscriptions and its journal rows, and a journal row of the delete is written at the next position (revision -1,
  which the projector turns into removing the view row). A delete of a running instance is consumed and dropped.
  What arrives afterwards finds no row and is dropped and journalled, as for a missing target; outside commands that
  were handled stay as done markers until their window passes, so a late retry still meets a duplicate key.

### F15. Deleting journal rows breaks the projector's rule (invariant 6). Run

- **Replay:** `--flaw ProjectorStopsAtGap` (the rule of section 10), seed 6: `I6 the projector of partition 0
  stopped at 1 of 50`.
- **Sketch sentences:** "A partition's journal has no gap among the rows written, and the projector never passes an
  unwritten position", with deletion "with its messages, subscriptions and journal rows". A deleted row is a gap the
  projector cannot tell from a row not written yet, so it stops for ever.
- **Fix:** the projector reads the partition's position before the journal (`IKernelStore.ProjectedAsync` returns
  both). Every position up to it is committed, because the fence moves the position in the commit that writes the
  rows, so a gap there was deleted. The projector reads only up to that position and passes such gaps.
  `ProjectAsync` takes the position to move to. Invariant 6 now reads: rows present plus rows deleted equal the
  position.
- **Check (invariant 22):** a row disappears only when its instance was deleted; a deleted instance never has a row
  again; its journal rows, waits and view are gone (the view once the projector has passed the delete).
  `--flaw DeleteLeavesJournal`: caught, seed 6 (`I2 the journal has a commit of 0/s16 and there is no row`).
- **In the simulation:** outsiders delete ended sequences, waiters and event-started instances: 104 in 4,000 seeds. Not
  instances that later messages would create again (a counter, which every add can start), because a message with
  a start spec creates a new instance with the same id: delete then means "start over", which is right for a keyed
  instance and has to be a decision.

## 21. Innocent messages parked at the threshold. Run

Round 2 left this open: at 1,000 actions about 7 percent of seeds parked an innocent message (1,412 instances in
20,000 seeds), and section 7 argued that the simulation loses owners far more often than a real cluster does.

- **How it was measured:** `--faults F` scales lost calls, wrong answers, crashes, pauses and the long silences that
  let a lease expire (`Generator.Faults`; at 1 the random stream is the default one). `--flaw TrialBeforeLoad` switches
  off the rule of F13 that a trial starts only when its step can run at once (instance loaded, nothing in flight),
  which is the trial rule of round 2. Each row is 5,000 seeds of 1,000 actions, threshold 3 (every fifth seed: 5 with
  groups of 4).

| Faults | Owners lost | Rule of F13: put on trial | attempts charged | innocent parked | Round 2's rule: innocent parked | seeds failed |
|---:|---:|---:|---:|---:|---:|---:|
| 0.5 | 47,294 | 87,331 | 21,997 | 0 | 17 | 30 |
| 1 | 68,145 | 84,342 | 23,239 | 4 | 165 | 34 |
| 2 | 102,253 | 74,531 | 22,485 | 6 | 269 | 26 |
| 4 | 139,950 | 59,938 | 19,748 | 0 | 41 | 5 |

- **What it shows:** with the trial rule of round 2, innocent parking grows faster than the fault rate between 0.5
  and 2 (17, 165, 269: three deaths have to meet one message), and at 4 so little work gets done that fewer messages
  ever reach a trial. With the rule of F13 it is 0 to 6 per 5,000 seeds at every fault rate, 30 to 45 times fewer.
  The failed seeds are `IDLE` runs of F13 itself, which the old rule brings back.
- **Why, reasoned from the counts:** in this simulation most owners are lost because their lease runs out while the
  host does nothing with them, not because they crash. A trial that waited for its instance's load spanned the poll,
  the load and their answers, tens of actions, and every one of them was a chance to lose the owner. A trial that
  starts only when its step can run spans the step and one commit.
- **The rule does not let a poison message walk, run:** a message that kills the process is still tried alone in its
  process, and the end check requires every such instance to end Faulted; no seed of any row failed it.
- **Acceptable, reasoned:** yes. At 1,000 actions the simulation loses an owner about every 70 actions; the
  remaining rate (at most 6 instances in 5 million actions) needs three such losses during the two commits of one
  message's trials. In a cluster that loses an owner every few hours and tries a message in milliseconds, it is zero
  for practical purposes.
- **Proposed, reasoned, not built:** an owner with a trial in flight renews its lease on a short timer, so only a
  stalled or dead process lets it expire. In the simulation the host renews only when a random action says so; a
  real host can do this without risk, because a renewal is a fenced commit that changes nothing.

## 22. The final runs of round 3. Run

All at commit `fca58696ed` plus the documentation, on the machine shared with other agents' runs.

- **Simulation:** 200,000 seeds of 300 actions (60 million actions, 102 s): 0 failed, 0 innocent instances parked.
  20,000 seeds of 1,000 actions (20 million actions, 71 s): 0 failed, 12 innocent instances parked (round 2: 1,412).
- **Coverage of the 200,000 seeds, run:** 907,384 owners lost; 1,365,825 messages put on trial and 295,565 attempts
  charged; 15,584,687 evictions; 5,297 deletes; 271,503 children cancelled by their parent (54,093 across
  partitions); 1,274,251 tree descendants terminated (521,942 across partitions; 10,869 created after their parent had
  ended); start events before the registrar's insert 24,784, between its insert and its read 20,574, after 4,539;
  HTTP requests that started an instance: 247,488 answered, 151,063 lost by a reset, 224,743 timed out, 94,954
  disconnected; that resumed one from any node: 39,934 answered, 4,378 lost, 245,719 timed out, 7,869 ended
  Cancelled, 596 ended Finished without an answer, 66,388 disconnected; a version-1 owner held a version-2 row at
  1,576 moments.
- **Mutation check, 20,000 seeds:** 39 mutations. Every one is caught except `FenceWithoutPosition` and
  `DeleteWithoutCount`, as in rounds 1 and 2, and both are caught together with the rule that covers them (seed 19,
  seed 1). The round-3 flaws, first failing seed: `GreedyBalancer` 2, `TrialPerPartition` 22, `ProjectorBlindWrite`
  25, `EmitBeforeCommit` 1, `ReplyTwice` 1, `NoDeadline` 31, `DeliverWhileSuspended` 2, `NoShelf` 70,
  `SuspendDuringTrial` 2016, `TerminateRootOnly` 1, `HoldLoses` 1, `HoldKeepsTrial` 4, `SkipUpgrade` 30,
  `StepNewerState` 225 (at 1,000 actions), `RebindForgotten` 5, `StartWithoutRead` 9, `CancelKnownChildren` 2,
  `EvictBusy` 4, `DeleteLeavesJournal` 6, `ProjectorStopsAtGap` 6. Seeds named in sections 9 to 20 were found at
  their pieces' commits and some have moved since, as the actions that later pieces added changed the random stream.
- **Cost, run:** PostgreSQL round trips are what section 5 measured, row for row (balanced 3 and 2, durable 30 and 20,
  from another node 9 and 7, parent with three children 12 and 8, 100 instances 3 and 2). SQLite is unchanged
  except the parent with three children, 49 to 57 calls: the simulation's child now reports that it started (section
  18), one more message per child, and SQLite sends one command per row. The heartbeat, the projector, start
  subscriptions and deletes are not in the cost run.
- **PostgreSQL stress (explicit fence), the test of section 4 on the round-3 store:**

| Run | Backends killed | Stalls | Takeovers (p0, p1) | Steps run | Instance commits | End state |
|---|---|---|---|---|---|---|
| 40 s, 5,000 messages | 553 | 311 | 256, 242 | 10,188 | 9,005 | all properties hold |
| 60 s, 8,000 messages | 943 | 469 | 434, 391 | 16,552 | 14,455 | all properties hold |
| 15 s, 1,500 messages, fence broken on purpose | 255 | 93 | 12,338, 12,432 | 25,415 | 601 | 482 violations: journal rows beyond the position, messages left, counter tokens missing |

  The stress test drives the models of round 1 only; it does not exercise the heartbeat, the projector, kernel
  commands, start subscriptions, reply addresses or deletes on PostgreSQL.

## 23. The merge of the four branches. Run

Branch `agent/claude/kernel-model-merged`: round 3 (`0f09246d32`) as the base, then the engines (`1aeede63f2`, merge
`899c5a8ce6`), MongoDB (`5d5e7fc36f`, merge `9d4f4ef851`) and native (`60feb16e9c`, merge `c7ae88efea`).

### What conflicted, and how it was resolved

| Where | Conflict | Resolution |
|---|---|---|
| `EfStore.cs` | Textually one hunk (the model); in meaning, every statement round 3 added was PostgreSQL/SQLite SQL (`ON CONFLICT`, `RETURNING`), the delete and the unsubscribe ran before `SaveChanges` | Each new operation in every dialect (`Engine.HeartbeatAsync`, `SubscribeAsync`, `ViewAsync`; `ReplyTo` in the dialects' poll and add). The instance delete moved into the batch with its revision as token; the set-based deletes run after `SaveChanges`, so with the fence in the batch they run only once it held (E1) and lock after the partition row (E9). E4 for the projector's transaction; E8-style indexes on `Subscriptions(Instance)` and `Journal(Partition, Instance)` |
| `IKernelStore` | Round 3 added the heartbeat, start subscriptions, projector and view rows, delete, unsubscribe-all, held messages and four instance columns; the suite and `MongoStore` knew none of them | Eleven semantic cases, one per operation, and one for E1 with a stale owner's delete, unsubscribe and hold. `MongoStore` implements them in its one bulk write (M5's upsert fence still stops it) and its transactions (M2, M3). `ReadViewsAsync` added so the suite can read what the projector wrote |
| Stress tests | Round 3's operations were not exercised under concurrency anywhere | Both stress harnesses now suspend, resume, terminate and delete trees and start instances by events, and check their end state. The journal check allows the gaps deletes leave |
| HTTP | Native's `ILiveChannel` and round 3's `Emit`, `Ambient`, `Requests` | Native emits with the kernel's `Emit`; an HTTP start passes the request as `Ambient`; hosts use `Requests` and `IReplyTransport`. N2 (`OnApplied`, `OnReset`, `Unit.Parked`) removed: round 3's release in `Apply` and the "lost" of `Reset` do that job. The opt-out moved from the response to the route, on the caller's reply address |
| `IStep.Publish` (N1) | Round 3's start subscriptions and tickets do not publish from a step | Kept as the one way a step publishes; now passes the partition count, so a step's event can start an instance. A publish ticket is no suspect |
| `World.cs`, `Checks.cs`, `Program.cs` | Config fields, model lists, the stray-subscription check, the settle loop, the summary line | All kept: `Upgrade` and `Native`; native waits allowed beside tree leaves; `Unfair()` then `NativeMeet()` at settle |

### Frictions and defaults applied

- Frictions of NATIVE.md, each small and on the existing ticket and commit mechanisms (run): F-N3 `Events.Delivered`;
  F-N4 `Send` returns the id and `Recall` deletes a message to self with the commit (native cancels its timers);
  F-N5 `Start(..., detached: true)` (native's DispatchWorkflow); F-N6 `Redelivered` (native journals "may have run";
  new check N6: in the runs below 3,656 and 972 External effects ran again, every one told); F-N8 a wait carries the
  meet ticket only when it asks for kept events (fork-join-resume Balanced on PostgreSQL: 15 to 11 round trips).
  Not done: F-N7 (short timers in memory for ephemeral instances: a new mechanism), F-N10 (a publish to a waiter in the
  same partition is not handed over in memory), F-N11 (two places say "ephemeral").
- The owner's defaults of 12 November (run): events are not kept for a wait unless it asks (`Subscribe(..., keep:
  true)`; the simulation's waiter asks, so invariant 13 is checked as before; F-N9 is gone); lease 10 s
  (`Defaults.Lease`); 64 partitions on server stores and 1 on SQLite (`CreateAsync()`); a response released after the
  commit of the step that wrote it, opt-out per route. The simulation and stress tests keep their short leases.

### The gates

| Gate | Result |
|---|---|
| Simulation, 200,000 × 300 | 0 failed, 0 innocent parked (106 s) |
| Simulation, 20,000 × 1,000 | 0 failed, 12 innocent parked (as section 22) |
| `--native`, 200,000 × 300 | 0 failed; 1,438,331 native instances matched a run without faults |
| `--native`, 20,000 × 1,000 | 0 failed, 15 innocent parked; 409,284 matched |
| Mutation check, 20,000 seeds | 37 of 39 caught alone, at the first failing seeds of section 22 for every round 3 flaw; `FenceWithoutPosition` and `DeleteWithoutCount` caught with the rule that covers each (seeds 19, 1) |
| Native mutations, 20,000 seeds | All 5 caught: `InFlightInMemory` 1, `JoinWithoutGuard` 6, `ResumeAnyChild` 1, `NoAttemptCommit` 12, `ScopeInMemory` 1 (NATIVE.md had 2, 5, 2, 2, 6: moved by the merged kernel's actions) |
| Native smoke | 31 of 31 ok, including release after commit, the per-route opt-out, two nodes, an owner lost while a request waits |
| Native cost, SQLite and PostgreSQL | Every row ok, nothing left |
| Semantic cases (57) | In-memory 56 + 1 skipped; SQLite 54 + 2 differ + 1 skipped; PostgreSQL 54 + 3 differ; SQL Server and MySQL 53 + 3 differ + 1 skipped; MongoDB 57. The same with the fence in the batch. With E1 off (`--m5`) the new stale-owner case fails on PostgreSQL |
| Stress, 40 s, 5,000 messages | PostgreSQL holds (630 sessions killed, 530 resets, 0 deadlocks); SQL Server RCSI checked holds (1,360 killed, 0 deadlocks); MySQL holds (781 killed, 0 deadlocks); SQLite WAL holds; MongoDB holds (350 connections killed). Each with 439 trees terminated (222 deleted) and 412 instances started by an event |
| Fence broken on purpose, 15 s | PostgreSQL 228 violations, SQL Server 26, MySQL 611, SQLite 238, MongoDB 390: every engine fails, as it must |
| Cost | DESIGN.md "What it costs": every row of ENGINES.md and MONGO.md as before within a round trip (SQL Server's 1,000 at once 13 / 12, was 11 / 10); round 3's operations measured for the first time |

Seeds named in sections 2 to 20 had already moved on the round 3 branch itself (checked on its tip: the same seeds pass
there as here); section 22's first failing seeds are the replayable ones, and all of them replay unchanged.

### New findings

None from the gates: no gate failed after the merge for a reason other than a merge mistake. Two merge mistakes were
caught before they were committed: the stress tests' journal check took the gaps of deletes for I6 (run), and round
3's set-based deletes ran before the fence in the batch (found by reading, fixed as above; whether the old order ever
answered wrongly was not run). Observations, not faults: a projector round
costs one statement per instance in its page; a terminate or a delete costs one extra round trip on EF for its
set-based delete.

### Not done

The seam between layers 1 and 2; draft 4's workflow-instance layer; F-N7, F-N10, F-N11; skipping the event insert when
no wait can want it kept; the stress test with native instances; a MongoDB replica set of three.

# Round 4

## 24. Stage A: three layers, and the names turn, job and pass. Run

The owner's decisions of 12 November: the kernel is three layers (actor runtime, durable actors, actor types) with
dependencies pointing downward only; runtimes host partitions only; an instance's step is a **turn**, off-step work
(a run) is a **background job**, and the runtime's call into a partition is a **pass**. Stage A is a pure refactor:
every gate gives the numbers it gave at `baba05a78c`, and it does (the table below). Sections 1 to 23 keep the old
names; the mapping is here.

### Renames

| Before | Now |
|---|---|
| `IStep`, `StepAsync`, `Step.cs`, `StepCommit` | `ITurn`, `TurnAsync`, `Turn.cs`, `TurnCommit` |
| `IRun`, `RunAsync` (the model's), `Run(key)`, `CancelRun` | `IJob`, `ExecuteJobAsync`, `StartJob(key)`, `CancelJob` |
| `RunFinished`, kind `run-finished`, ticket kind `kernel.run` | `JobFinished`, `job-finished`, `kernel.job` |
| Instance row field and column `runs` (EF, MongoDB) | `jobs` |
| Flaws `VolatileRunTicket`, `StepNewerState` | `VolatileJobTicket`, `TurnNewerState` (same bits, so every seed keeps its meaning) |
| `Kinds.ChildFinished` | `WorkflowKinds.ChildFinished` (layer 3's kind; the string is unchanged) |
| `Item`, `Unit` (the worker's) | `Delivery`, `TurnOutcome` |
| `Balancer.RoundAsync` | `PartitionRuntime.BalanceAsync` |

"Run" stays where it means a workflow run, a test run or the verb; `IScopeActivation.RunAsync` is a pass.

### What moved where

| Before (`src/Kernel`) | Now |
|---|---|
| `Balancer.cs`: the fair share, `Defaults` | Layer 1, `src/Kernel.Runtime/PartitionRuntime.cs`: `PartitionRuntime` (activations, deactivations, `BalanceAsync`, `PassAsync`, wake hints), `Defaults` |
| — | Layer 1, `Seam.cs`: `IActorRuntime`, `IScopeHost`, `IScopeActivation`, `IWakeSignal` and `InProcessWakeSignal`, `IMembership` and `Cluster` |
| `Contracts.cs`: `InstanceRef`, `Flaw` | Layer 1, `Addresses.cs` |
| `Contracts.cs`: messages, kinds, `StartSpec`, `InstanceStatus`, `History`, `IJob`, `Json` | Layer 2, `src/Kernel.Durable/Messages.cs` |
| `Contracts.cs`: `IExecutionModel`, `ITurn`, `InstanceInfo`, `Placement`, `ChildFinished` | Layer 3, `src/Kernel.Workflows/Contracts.cs` |
| `Store.cs` (with `HeartbeatAsync`) | Layer 2, `Store.cs`; `IKernelStore` extends layer 1's `IMembership`, which now holds the heartbeat |
| `InMemoryStore.cs`, `Projector.cs`, `Replies.cs` | Layer 2, unchanged but for namespaces |
| `Worker.cs` | Layer 2, `Worker.cs`: it is one partition's `IScopeActivation` (`RunAsync` is a pass, `WokenAsync` a hint, `DisposeAsync` a release); `TakeAsync` and `ReleaseAsync` are internal, reached through `DurableHost` |
| — | Layer 2, `DurableHost.cs`: the `IScopeHost`; activating a scope takes its fence or declines |
| `Worker.cs`: the model table, `Withheld`'s rule, `Control`, `Terminate`, `Park`, `Journal`, `Finished`, the state read and upgrade before a turn | Layer 3, `WorkflowInstance.cs`, through layer 2's `IActorType` (`Withholds`, `Command`, `TurnAsync`, `Park`, `ExecuteJobAsync`) |
| `Step.cs` | Split. Layer 2, `Turns.cs`: `DurableTurn` (sends, recalls, jobs, holds, waits, publish, emit, the meet ticket) and the seam types `TurnInput`, `TurnOutcome`, `Delivery`, `ITurnServices`. Layer 3, `Turn.cs`: what a workflow adds (status, definition and rebind, children, the parent's notice, journal entries, the ephemeral rule) and `Seal` |

Hosts. The simulation is the runtime of its processes: each has a `PartitionRuntime` over a `DurableHost`, and its
takes, releases and balancing rounds go through it. Its settling loop wakes each scope over the runtime's wake signal
and runs a whole pass with `PassAsync`; a process that dies cancels the pass's token, which stops it at a turn
boundary and before its flush, as the loop's `Dying` checks did before. During faults the simulation still calls the
pieces of a pass (poll, load, turn, flush, beat, meet) as separate seeded actions. The EF, MongoDB, conformance and
native hosts activate through `DurableHost` and drain with `RunAsync` where they called the pieces by hand.

Size: `src/Kernel` was 1,758 lines; the three projects are 279, 1,680 and 281 (2,240). The difference is the seam's
interfaces, `PartitionRuntime`, `DurableTurn` and their comments.

### Checked against the brief (by reading)

- **Downward only:** `Kernel.Workflows` references `Kernel.Durable`, which references `Kernel.Runtime`, which
  references nothing; the compiler enforces it.
- **Partitions only:** no `Granularity`, no `ScopeId` or `ActorId`; `ScopeOf` is the actor's partition. No sweep.
- **The wake signal is an interface** (`IWakeSignal`), and a lost hint loses nothing: a pass polls only when woken,
  and the host's poll interval wakes it with `WokenAsync(null)`.
- **One pass at a time per scope:** `PartitionRuntime.PassAsync` refuses a second pass of a scope while one is under
  way.
- **No clock, thread or random number** in the three projects but the default of `WorkerOptions.Monotonic`
  (`Environment.TickCount64`, as at the baseline), which the simulation always replaces. `InProcessWakeSignal` calls
  its listeners inline.
- **The renames** are complete in code, test output and the four reports; a search for the old names finds only
  "run" as a verb, a workflow run, a test run, and MongoDB's primary that "stepped down".
- **The simulation is deterministic:** every seed gives the same actions and the same first failures as at the
  baseline (the mutation table below is identical to the seed and the action count).

### Known seam leaks

Where a clean cut would have changed behaviour or round trips, the code keeps what it had.

| # | Leak | Why it stays |
|---|---|---|
| L1 | One `instance` row (`InstanceRow`) holds layer 2's columns (revision, state, state version, history, jobs) and layer 3's (model, definition, status, parent, fault, suspended, children) | One write per commit. Two rows would be two statements per instance commit, on every store |
| L2 | `InstanceStatus` is declared in layer 2 | The row carries it, and layer 2 ends a caller's observation with it (`Reply.Status`) and treats an ended actor's messages as dropped |
| L3 | The command kinds (`kernel.suspend`, `resume`, `terminate`, `delete`) and `Kinds.IsControl` are in layer 2 | Delivery treats them apart: they pass a withheld actor, go alone, are never suspects, and a trial does not start behind one (F12, F13). What they do is layer 3's (`IActorType.Command`) |
| L4 | `StartSpec` carries model, definition, parent, ephemeral and history | Layer 2 decides from it whether a message creates its target or is dropped (no row and no start spec), and the row a creating turn writes is built from it |
| L5 | The take is both the runtime's lease and layer 2's fence: one store call bumps the epoch and sets `lease_until`, and layer 2's commits and `BeatAsync` renew the lease. `Defaults.Lease` is layer 1's, read by `WorkerOptions` | Splitting them would add a round trip to every take and every renewal |
| L6 | The built-in runtime's membership (node rows, free partitions) is in the kernel store: `IKernelStore : IMembership` | The node rows are in the same database; a separate membership store would be one more provider to write |
| L7 | `Flaw` is one enum in layer 1 with every layer's flaws, read by layers 2 and 3 (`ITurnServices.Has`) | Every flaw keeps its bit, so every seed in sections 1 to 23 keeps its meaning |
| L8 | `TrialGate`: the workers of one process share a gate, so a trial is alone in the process (F11) | Which partitions die together is the runtime's placement, but the poison rule that needs it is layer 2's |
| L9 | Layer 3's terminate derives job tickets with layer 2's `Worker.TicketId` | Tickets are layer 2's ids; a turn must cancel a job it does not hold |
| L10 | `IReplyTransport` is declared in layer 2, though it rides on the runtime's transport between nodes | It carries layer 2's replies; layer 1 has no transport of its own yet |
| L11 | `InstanceRef` is named for instances and embeds the partition in the id | Placement is fixed by the address; a runtime that moved actors between scopes would need another address. Partitions only, so not needed now |
| L12 | During faults the simulation calls the pieces of a pass one at a time, not through `PassAsync`, so "one pass at a time" holds there only because the pieces guard themselves (`Polling`, `Flushing`, ...) | The pieces as separate actions keep every seed's schedule, and with it every first failing seed |
| L13 | Only the simulation uses `PartitionRuntime`; the EF, MongoDB, conformance and native hosts activate through `DurableHost` and loop over `RunAsync` themselves | They own one or a few partitions and never balance. Moving them onto `PartitionRuntime` is no change of round trips, but it is not a rename either; left for a later stage |

### The gates, before and after

Before: a scratch worktree at `baba05a78c`. After: this branch. Both built with README's loop, run from
`prototypes/kernel-model`, on the machine shared with other agents' runs (times are not comparable). PostgreSQL 17 in
Docker, port chosen by Docker (`-P`). The outputs were compared line by line after removing times: the only
differences are the renamed words ("step" to "turn", "off-step work" to "a background job", "kernel.run" to
"kernel.job", the two flaw names) and one TCP count, below.

| Gate | Command | Before | After |
|---|---|---|---|
| Simulation, 200,000 × 300 | `$SIM --seeds 200000` | 0 failed, 0 parked | 0 failed, 0 parked |
| Simulation, 20,000 × 1,000 | `$SIM --seeds 20000 --actions 1000` | 0 failed, 12 innocent parked | 0 failed, 12 innocent parked |
| Native, 20,000 × 1,000 | `$SIM --native --seeds 20000 --actions 1000` | 0 failed, 15 parked; 409,284 matched; 972 reruns, all told | the same |
| Mutations | `$SIM --mutations --seeds 20000` | 37 single mutations, 35 caught alone, every round-3 flaw at section 22's seed; `FenceWithoutPosition` (with `NoResetAfterFailedCommit`, seed 19) and `DeleteWithoutCount` (with `PollWithoutSkip`, seed 1) caught only in combination | the same, seed for seed and action count for action count |
| Native mutations | `$SIM --native-mutations --seeds 20000` | 5 of 5 (seeds 1, 6, 1, 12, 1) | the same |
| Semantic cases, in memory | `Kernel.Store.Conformance.Run.dll semantic` | 56 passed, 1 skipped | the same |
| Semantic cases, SQLite | `$EFC sqlite semantic`, and with `--fence-in-batch` | 54 passed, 0 required failed, 2 reference differ, 1 skipped; both | the same |
| Semantic cases, PostgreSQL | `$EFC postgres "$PG" semantic`, and with `--fence-in-batch` | 54 passed, 0 required failed, 3 reference differ; both | the same |
| Native smoke | `$NAT smoke` | 31 of 31 ok | the same |
| Native cost, SQLite | `$NAT cost sqlite` | every row ok; 26 instances, nothing left | the same, row for row |
| Native cost, PostgreSQL | `$NAT cost postgres "$PG;Database=kernel_native"` | every row ok | the same, row for row |
| EF cost, SQLite | `mkdir -p .out`, then `$EFR x cost sqlite "Data Source=.out/cost.db"`, and with `fence=batch` | DESIGN.md's SQLite column | identical output |
| EF cost, PostgreSQL | `$EFR x cost postgres "$PG;Database=kernel_cost" proxy=1`, and with `fence=batch` | DESIGN.md's PostgreSQL column, counted and sent | the same counted and sent, every row; see below for one TCP count |

**One cell moved, and it is noise.** In the first runs, "1,000 balanced instances started at once" with the fence in
the batch showed 6 TCP reads before and 7 after; counted and sent were 4 in both. Three more runs of each: before 4,
6, 5 (checked fence 4, 4, 4), after 5, 6, 5 (checked 4, 4, 4). The TCP column counts reads on the proxy's stream,
and a 1,000-row batch splits into a varying number of segments; the round trips themselves did not move. DESIGN.md's
"4 / 5" for that row is one such sample.

No parked count moved, so no schedule changed: stage A kept the order in which a seed's actions call the kernel.

### Surprises and small differences

- **The previous agent's work was right but unfinished.** Its four commits are faithful moves (checked by reading
  the diffs against `baba05a78c` and by the gates), but it had not run the 200,000-seed gate, PostgreSQL or the EF
  cost runs, and README already pointed to this section, which did not exist; DESIGN.md still described the
  unlayered code.
- **Three differences that no gate can see**, because nothing exercises them: (1) the workflow turn's constructor
  (reading `jobs`, `children` and the results in the inbox) now runs inside the try that parks an instance whose turn
  throws; before, a throw there escaped `RunTurn`. (2) A command's and a park's consumed list is a copy of the taken
  list, not the list itself; nothing changes the list afterwards. (3) The non-simulation hosts' drains now run the
  ready turns in id order (`RunAsync`), where they took the dictionary's order; every count is the same.
- `PartitionRuntime` is small (121 lines) because it has no clock: its host calls `BalanceAsync`, `PassAsync` and
  `WokenAsync(null)`. A production runtime adds a loop with a poll interval around those, outside the kernel.

### Not done

The Orleans adapter and the runtime conformance suite; the non-simulation hosts on `PartitionRuntime` (L13); the
stress tests (not in stage A's gates); SQL Server, MySQL and MongoDB (not in stage A's gates; the store code changed
only by the `jobs` rename).

## 25. Round 4: the owner's decided items. Run

The items the owner decided on 12 and 13 November (the sketch's "Decisions taken so far"), built one pull request
each. Each subsection says what changed, what was run, and what it did not change.

### 25.1 SQL Server's default fence (E9, decided 13 November). Run

**What changed.** The EF store's fence mode is the engine's default when it is not told: the fence in the batch on
PostgreSQL, SQLite and MySQL, the checked fence on SQL Server (`Engine.FenceInBatchByDefault`, false only in
`SqlServerEngine`; `EfStore.FenceInBatch` resolves it). Before, every EF host defaulted to the checked fence and the
fence in the batch had to be asked for. The runners take the same default: `fence=checked|batch` in `EngineRun`, and
`--checked-fence` beside `--fence-in-batch` in the conformance runner. So a run given no fence option is now a
different run on PostgreSQL, SQLite and MySQL (the fence in the batch) from the one ENGINES.md and section 24 recorded
under the same command (checked); README's commands name the mode where a recorded result depends on it, and the
broken-fence stress command names the mode ENGINES.md recorded for each engine (batch on PostgreSQL, checked
elsewhere). `$EFR x cost` runs one mode per call, the engine's default unless
told; `$NAT cost` and the old `cost` commands run both modes, as before. The old PostgreSQL `stress` and `probe`
commands of section 4 keep the checked fence they were recorded with. The MongoDB store keeps its own default
(checked); the decision is EF's.

**Gate: the SQL Server stress test with the default.** SQL Server 2022 in Docker (amd64 emulation on arm64), RCSI, 40 s
and 5,000 outside messages, as ENGINES.md:

| Fence | Result | Deadlocks | Resets | Takeovers | Settled |
|---|---|---|---|---|---|
| default (checked) | holds | **0** | 1,372 (288 fenced, 1,084 unknown) | 713, 753 | 105 s after the chaos |
| `fence=batch` | 1,517 violations at the default patience (180 s); see below | 310, all takes | 1,869 | 1,025, 1,028 | not within 180 s |
| `fence=batch patience=900` | holds | 1,417, all takes | 2,080 (756 fenced, 1,324 unknown) | 1,161, 966 | 858 s after the chaos |

The default reproduces ENGINES.md's checked row: no deadlock of any kind, every property holds. The fence in the batch
reproduces E9 at a larger count than before (310 against 127; this machine's emulation is slower, commits took a
median of 21 ms): every victim a take. Its end-state check failed because the run had not drained when the settle
limit ran out; the 20 violations printed are all work left behind (messages not done, waits not removed, counters
missing tokens, parents still running), none is a message applied twice, out of order or a wrong journal.
Run again with a settle limit of 900 s, the fence in the batch held every property, with 1,417 take deadlocks and
858 s to drain against the checked fence's 105 s: the deadlocks cost availability, as E9 says, and here enough of it
that the default limit ran out. The checked fence is the right default on SQL Server.

**Also run, unchanged:** the semantic suite on in-memory (56 passed, 1 skipped), SQLite with no flag (now the fence in
the batch), `--checked-fence` and `--fence-in-batch` (54 passed, 2 reference differ, 1 skipped, each), PostgreSQL with
no flag and `--checked-fence` (54 passed, 3 reference differ), SQL Server with no flag (now checked; 53 passed, 3
reference differ, 1 skipped). Not run on this branch: the simulation (it uses the in-memory store, which has no
fence mode), MongoDB (no code of it changed), the cost runs, the broken-fence stress runs, the PostgreSQL and MySQL
stress tests, and the semantic suite on MySQL; ENGINES.md and sections 23 and 24 have their earlier results.

**The new defaults of the other stress commands.** SQLite's default stress run is now the fence in the batch, which no
stress run had covered (ENGINES.md's SQLite rows are all checked). Run here: WAL, one process, 40 s, 5,000 messages:
holds, 0 deadlocks, 1,143 resets (all fenced), takeovers 572 and 571, settled 218 s after the start. MySQL's default
is now the fence in the batch, recorded at REPEATABLE READ in ENGINES.md (holds, one take deadlock); with
`isolation=rc` the fence in the batch has no recorded stress run, and was not run here. README's stress commands now
show the fence option on every engine.

## 26. Round 4: the graph engine. Run

The owner's decision of 12 November (design sketch draft 5, "Containers and their semantics"): the native model stays
one activity tree, each container brings its own semantics, and the Flowchart runs on a graph engine extracted from
main's Flowchart, not invented. Plan step 1 of that section is what this round did. (Section 25 is the owner's decided
items of the same round.)

### The engine: extracted, and the same as main's

`models/Graph` is main's Elsa 4 Flowchart engine (`Essentials/src/Activities/Flowchart` at `a3eb06e851`) lifted out
of the runtime: execution paths with iteration keys, scopes (root, loop iteration, first-wins race), arrivals at joins,
the gateway policies, dead paths, the dead-path join as the default and the reachability join beside it, pruning
(#382). No reference to the kernel, to Elsa or to any package. The first commit of its PR is the verbatim copy; the
adaptations are at the runtime seam (`models/Graph/README.md`): a pure step, `(graph, state, one callback) ->
(state, schedules, continuation)`, where main reads an `IRuntimeActivityExecutionContext` and returns a
`RuntimeStructuralContinuation`. For the simulation the library adds four deliberate flaws (`FlowchartOptions.Flaws`,
off by default, marked MUTATION) and the step's unpruned state (`FlowchartStep.Live`).

`models/Graph.Tests` ports main's conformance corpus (C1 to C9 under both join semantics) and its dead-path, implicit,
cyclic, fault and loop-iteration join tests, with main's execution-id pools: 41 pass. Main's two determinism
byte-locks pass with main's golden strings, so for those runs the extracted engine makes the same state mutations as
main's, in the same order. That is the evidence that "extract, don't invent" held.

### The missed join, shown, then gone

NATIVE.md reasoned that the native Flowchart never fires an All join after a decision. Run first (smoke,
`smoke-decision-join`): the join and the node after it never ran, and the Flowchart completed without them (`left no
no`; a missed join, not a stuck instance). On the graph engine the untaken connection is a dead path that reaches the
join, which fires on one live and one dead arrival (`left yes yes`, Balanced and Durable).

### The port

The native `Flowchart` is the engine's host: it schedules what the engine asks for (path and scope on the child's
tag), hands completions and faults back, keeps the engine's state as its private state, and cancels a child whose
path the engine cancelled. What changed for authors, and why:

| Change | Why |
|---|---|
| `JoinMode` (All, Any) is gone; `Gateway` names main's policy kinds (Decision, ParallelFork, ParallelJoin, InclusiveFork, InclusiveJoin, FirstWins, Merge) | Explicit join modes replace inference. A node with several inbound connections and no gateway is an implicit join decided by the dead-path join, main's default (the sketch's "decisions this needs"). The old Any (fire on the first arrival only) had no user and has no equal in main; Merge (every arrival) and FirstWins (cancel the losers) are the nearest |
| A run starts at one node: a pending trigger that is a child, else the start node | Main's `SelectEntryNode`; the old Flowchart started every child without inbound connections. Unlike main, the trigger counts only while undelivered, and no node carries main's trigger flag |
| A first-wins loser is cancelled | Main lets the loser run on and ignores its completion; the sketch says Elsa's first-wins cancels the losers, and G5 requires it |
| A child's fault is the Flowchart's fault | The engine faults the Flowchart (main, #308) with a generic `flowchart.child.faulted`; the native composites pass the child's own fault on |
| Diagnostics are not kept in the state, and the state is written compactly (defaults left out) | The engine never reads diagnostics; the journal is the trail. The state is rewritten at every commit |

**Cost.** Round trips are unchanged: every row of `cost sqlite` equals main's, and a new Flowchart row (three Event
branches, an All join, three direct resumes) has the old native Flowchart's counts (29 / 29 / 85 SQLite engine
calls in Ephemeral, Balanced and Durable). The state is larger: 1,233 bytes per commit in Balanced (at most 2,135)
against 584 (836) for the old native Flowchart, measured with the same row on #4330's head before the port (#4333's
description has both). That is main's state shape (paths, scopes and arrivals with their ids) after leaving out
diagnostics and defaults; reshaping it means changing main's model, which this round did not do.

### G1 to G5 in the simulation

The sketch's graph invariants are checked under the kernel's faults (NATIVE.md section 6 has the table): G1 path
conservation, G2 a join fires at most once per round, G3 an All join fires once every inbound of its round has
arrived, live or dead, G4 a graph completes exactly when no path is live, G5 no path outlives its cancelled scope.
They read the native Flowchart's journal entries (`g|x`, `g|f`, `g|c`, in the turn's commit, no extra database call)
and the committed state; the definition, not the engine, says which connections reach a node. A new workflow,
`n-graph`, runs the engine's shapes under the faults: a loop with a fork and join, the decision that leaves it, a
first-wins race between an Event and a Delay, and an All join after a decision.

The four mutations of the plan, as engine flaws (`FlowchartOptions.Flaws`), are each caught by their own check at seed
2, and the mutation check now fails a graph flaw that another check catches first: `NoDeadPaths` by G3,
`NoRoundReset` by G2, `DeadPathRunsNode` by G1, `CancelLeavesPaths` by G5. G4 has no mutation of its own (none of the
plan's aims at it), so that it bites is not shown.

| Run (20,000 seeds unless said) | Main | Round 4, graph engine |
|---|---|---|
| 300 actions | 0 failed | 0 failed |
| `--native`, 300 actions | 0 failed; 144,318 matched | 0 failed; 133,483 matched (the mix has `n-graph`, which moves every seed) |
| `--native`, 1,000 actions, 5,000 from 500001 | 0 failed; 103,091; 2 parked | 0 failed; 97,388; 2 parked |
| `--native`, 1,000 actions | 0 failed; 409,284; 15 parked | 0 failed; 386,774; 14 parked |
| `--mutations` | 37 of 39 caught | the same, each at the same seed |
| `--native-mutations` | 5 of 5 | 9 of 9, the four graph flaws by their own check |

### What the round found

- **G3's exception had to be narrow.** The engine releases a join behind a first-wins race's cancelled loser when the
  graph goes quiet (main's `ReleaseStarvedJoins`), so an inbound may go unanswered there. The first exception ("a
  node reachable from a cancelled one") also excused the decision behind the race, and `NoDeadPaths` went uncaught; the
  run showed it. The exception now covers only the cancelled nodes and nodes only they lead to, in the same round.
- **A native defect** (I9, seed 8486 at 1,000 actions): in Durable mode a cancel recalled a fired Delay's timer
  message that an earlier turn had consumed, because its resume still waited in the agenda. A timer whose message has
  arrived is no longer recalled. The old mix never cancelled an instance with a Delay.
- **A kernel check met a kernel behaviour** (I8, seed 2282 at 1,000 actions, once `n-graph` moved the seeds): the
  poison rule parks an instance without removing its subscriptions, and I8's stray-subscription test failed it. I8
  now skips an instance the poison rule parked while its faulted row exists (#4337, its own PR; the gates are main's).
- **N2 is narrowed for Flowchart joins.** A join behind a merge fires once per token in a round, so N2's entry names
  the scheduling id; G2 holds a join to once per round. G2 and G3 skip a merge and every node forward of it.

### Open, and for the owner

- **Should Park remove a parked instance's subscriptions?** The kernel keeps them (a parked instance is faulted, not
  deleted; recovery might want its waits). The simulation now accepts that; a kernel decision, not made here.
- **Main's engine, possibly:** a decision with both outcomes connected to one node schedules that node on the live
  arrival and then carries the dead arrival from the same source through it as well, which could fire a later join
  twice. Found by reading (a reviewer), not run; it is main's behaviour and no workflow in the mix has that shape. A
  corpus case on main would settle it.
- **State size** of a Flowchart, above. Paths and arrivals by reference, or a smaller encoding, are the levers.
- **Not covered by the mix:** merges, inclusive gateways, a race inside a loop, a trigger inside a Flowchart under
  the faults (smoke covers the trigger entry). The BPMN spike (its own session) does not use this engine; comparing
  the two join codes is the sketch's next step.

## 27. The runtime conformance suite and the Orleans adapter. Run

The owner's decisions of 12 November: the actor runtime is its own layer and must be swappable; runtimes host
partitions only, so Orleans hosts each partition as a grain, with no grain per instance and no sweep. This section is
what proves the seam: the built-in runtime on a clock (`RuntimeNode`), a runtime conformance suite like the store
suite, and an Orleans adapter that passes it, including runs that force two activations of one scope at once.

### What was built

| Piece | Where | What |
|---|---|---|
| The pass renews an idle lease | `src/Kernel.Durable/Worker.cs` (`RunAsync`) | A pass beats when no commit renewed the lease for a third of it, and says when to call again for that (R1) |
| Where a job's body runs | `WorkerOptions.JobScheduler` | Null (inline, as before) or a scheduler, so that a host on a scheduler of its own is not held up by a job (R14) |
| The built-in runtime on a clock | `runtime/Kernel.Runtime.Hosting` (`RuntimeNode`) | One loop per node around `PartitionRuntime`, on a scheduler of its own that runs one task at a time: balancing rounds, passes when due, hints, the poll interval; a stop that drains, a kill that gives nothing up |
| The runtime conformance suite | `conformance/Kernel.Runtime.Conformance`, `.Run` | Ten semantic cases, a mutation (the fence broken) and a stress test, against any `IRuntimeProvider`; the store suite's workload moved to `Workload.cs` for it |
| The Orleans adapter | `runtime/Kernel.Runtime.Orleans` | `PartitionGrain` (one per partition), `OrleansActorRuntime` (the silo's `IActorRuntime`), `PartitionKeeper`, `AddKernelRuntime` |
| The Orleans provider | `runtime/Kernel.Runtime.Orleans.Conformance` | The suite on Orleans' in-process test cluster; a side of a split is a cluster of its own |

Orleans 10.4.2 (released 30 October), pinned in the projects. The prototype's `NuGet.config` maps every package to
nuget.org, and an empty-cache restore of every runner restores 114 packages. The test cluster runs in process on its
in-memory transport: no socket was open during a run, and nothing ran in a cloud.

### How the suite works

The suite builds layer 2 itself and meets the runtime only at the seam. For each node the runtime starts, it asks the
suite for that node's layer 2 by name: a worker per partition over one fresh store, behind a `Watcher` that wraps the
node's `IScopeHost` and every activation it returns. Every commit that lands is recorded, and invariant 1 is checked
on them from outside the store, as in the store suite. The watcher fails a case when a runtime:

- makes two calls into one node's scope at once (an activation, a pass, a release; a worker is single-threaded);
- runs a pass after it gave the activation up, or after its node stopped or was killed;
- gives a scope up after its node's kill returned (a crash gives nothing up, also later);
- stops a node without giving up every scope it held;
- gives up an old activation after a newer one of the same node succeeded (that would release the new epoch).

It counts activations asked for while another node held the scope, every activation layer 2 declined (also while a
crashed node's lease ran out), pairs of activations of
one scope held at once on two live nodes, and passes that started holding the scope and ended without it. The harness
also checks that every node started under the name it asked layer 2 for and that no name comes twice (F9).

A provider starts nodes on two **sides**, as in a network split; a side splits what the runtime carries itself.
Orleans' sides are two clusters with their own membership and directory, so each side activates every scope. The
built-in runtime's sides split only its wake signal (its membership is the store, which both sides reach), so its
double activations come from stalls and held commits. `NodeStore`, each node's way to the store, can **stall** (a
pause longer than the lease) or **hold the node's next commit of a partition's turns in flight** (a commit sent just
before the split), so that it executes under an epoch that is no longer the partition's. It records what the held
commit got back and counts the node's commits the store answered "not landed".

| Case | Level | What it requires |
|---|---|---|
| placement | Required | Every scope held by exactly one live node; after an idle spell of four leases still so, and a take by another node is refused |
| idle churn | Reference | No activation comes or goes while the cluster is idle |
| poll | Required | A message added without a hint is handled within twice the poll interval and half a second |
| hint | Reference | With a 20 s poll interval, a hinted message is handled within 2 s |
| timer | Reference | With a 20 s poll interval, a child's 1.5 s timer fires within 2.5 s of being due |
| crash | Required | With a 6 s lease, once no holder changed for two balancing rounds, the holder of partition 0 dies while thirty messages come in and it has handled some. The kill must return within 3.5 s (two thirds of the lease less half a second), and right after it every scope the node held still refuses a take, unless the node began giving it up before the kill (a crash gives nothing up); its scopes are taken over and every message is applied once, in order |
| stop | Reference | The holder of partition 0 stops; its scopes are taken over before its 6 s lease would have run out |
| fence | Required | A node stalls until another process has taken the scope (retrying until the node's lease ran out) and died; the node commits after that, the store refuses it, and the runtime activates the scope again |
| split | Required | Nodes on two sides; the holder's next commit in partition 0 is held in flight for three leases (all its calls wait) while the other side handles the twenty messages sent to it. Partition 0 must have been held on both sides at once, the held commit answered "not landed", and every message applied once, in order |
| join | Reference | A node that joins gets a share |

Every required case also ends with no due message left in any partition, and every case's violations are read after
its nodes have stopped and their stalled calls have executed. A case and its teardown each have two minutes.

`semantic-broken` runs the two fence cases with the store's fence broken on purpose (the commit ignores epoch and
position). Each must fail on a check of what the fence guards (`FenceException`, invariant 1, the store's ghosts); a
precondition that did not hold, or a timeout, is no catch. The stress test drives the store suite's workload through
the runtime: two nodes on each side of a split for the whole run, crashes, stops and restarts under new names, stalls,
commits held in flight and injected connection failures; then the end state of section 4. A run is exercised only if
two activations of one scope were held at once and (with the fence whole) a held commit was refused once it
executed; an unexercised run fails. `stress-broken` passes only with a violation of what the fence guards (I1,
ghosts, I2, I3/I4, I4, I6, I11, I13), not with liveness, seam or name violations.

### Results

In-memory store; four partitions. Lease 800 ms and poll interval 400 ms in the cases but crash (6 s lease), stop (6 s
lease), hint and timer (20 s poll); 600 ms and 300 ms in the stress test. One run each, on a machine shared with other sessions' runs, at the suite's final state (`f782bc2a4e`); the stress counts vary from run to run.

| Run | Built-in (`RuntimeNode`) | Orleans |
|---|---|---|
| `semantic` | 10 passed, 0 required failed, 0 reference differ | 9 passed, 0 required failed, 1 reference differs (join, R8) |
| `semantic-broken` | 2 of 2 fence cases failed on the fence | 2 of 2 fence cases failed on the fence |
| `stress 20 2000` | All properties hold; 51 pairs held at once; 13 held commits refused; 38 passes told by the fence | All properties hold; 44 pairs held at once; 8 held commits refused; 44 passes told by the fence; 355 activations asked for while another node held the scope; 349 declined in all (also while a crashed node's lease ran out) |
| `stress-broken 10 1000` | 175 violations | 17 violations |

With the fence broken, the split case's held commit "threw StoreException: instance 0/c is at revision 20": it was
not refused, and the store's count check is what stopped it.

### Findings

- **R1. The seam had no lease renewal.** Commits renew the lease and `Worker.BeatAsync` (an empty fenced commit) does,
  but `IScopeActivation` has no beat: only the simulation called `BeatAsync`, directly. Under a runtime that only calls
  `RunAsync`, an idle partition's lease ran out after one lease, another node could take it, and the old owner went on
  believing it held it until its next commit. The suite's first case found it at once: after an idle spell, two nodes
  held partitions 0 and 1, and the fence case's stalled node was never told. Fixed in layer 2: a pass beats when no
  commit renewed the lease for a third of it (the sketch's cadence), and answers the sooner of the next timer and the
  next beat. A runtime that calls again when asked keeps an idle scope's lease, and a stale owner learns within a third
  of a lease. No round trip moved in any cost run.
- **R2. A store may answer a commit without the database's time.** The EF store with the fence in the batch answers
  `Now = 0`. The first version of R1 took the renewal time from that answer, so every idle pass beat and answered "call
  again in 0 ms", which every host reads as "there is work": the native cost run on SQLite hung on its first Balanced
  row. The renewal time is the worker's own estimate of database time when it sends the commit (it never runs ahead of
  the database, and the commit executes after it is sent), and an idle pass never answers zero.
- **R3. With the fence in the batch, the lease follows the node's clock.** That store writes `lease_until` from the
  node's wall clock (ENGINES.md), so a beat at a third of the lease tolerates up to two thirds of a lease of skew
  between the node's clock and the database's. Not changed: the fix is in the store contract (the commit would carry
  the worker's estimate and the fence would write from it).
- **R4. A new activation must read its inbox.** A take starts from empty memory, but `RunAsync` polls only when woken,
  so a scope activated with no hint waited for the poll interval before it saw its first message. `PartitionRuntime`
  and the partition grain now wake a new activation, and the seam says a runtime must.
- **R5. The built-in runtime has no "leave".** A node that stops releases its partitions at once, but the others count
  it as alive until its heartbeat ages out of the window, so they wait up to the window (not the lease) before taking
  its share. The suite's provider sets the window to two balancing rounds (half a lease). A leave in `IMembership`
  (delete the node's row) would cut it to one round; it is a store change, left for later.
- **R6. The trial gate is not safe for a runtime that runs a node's scopes at once.** `TrialGate` (F11: a trial is
  alone in its process) is shared by a process's workers and has no lock. `RuntimeNode` runs a node's passes one at a
  time; Orleans runs a silo's grains in parallel. The suite gives every worker its own gate under both runtimes, so F11
  does not hold there: a crash during a trial may count against a message in another partition's trial. The stress
  tests set the attempts high enough that nothing is parked. A lock in `TrialGate` is a small layer 2 change (leak L8).
- **R7. A split costs takes.** Every partition grain on the side that does not hold the fence asks again every
  `RetryEvery`, and each ask is a take: a store round trip on the holder's partition row, declined. In one Orleans
  stress run (four partitions, `RetryEvery` 150 ms, 20 s of load and its settling) 355 activations were asked for
  while another node held the scope, nearly all from the side without the fence, and 349 takes were declined in all. A
  deployment pays at most partitions × (sides − 1) / `RetryEvery` takes per second while a split lasts (fewer when
  the store is slow, since the retry is armed after the take answers); backing the retry off towards the lease would
  cut it. A first version also asked on every wake hint; review found it, and a hint now runs only a held scope.
- **R8. Orleans does not move active grains to a silo that joins.** The partition grains stay where they were first
  activated, so a new silo holds nothing until a silo leaves or dies (the reference case "join" differs). Orleans'
  activation rebalancer or a placement director would spread them.
- **R9. Orleans' failure detection sets the failover time with the lease.** A killed silo's grains are activated
  elsewhere only once Orleans declares it dead; with the defaults that takes tens of seconds, so the provider tunes
  membership (300 ms probes, two missed, one vote) and a 2 s response timeout. Failover under Orleans is the later of
  the lease and the death declaration. The keeper's call always interleaves and is capped at a round, so a silo that
  cannot answer holds up no round.
- **R10. Orleans' messaging is the wake signal.** `IActorRuntime.WakeAsync` is a one-way grain call; the adapter does
  not use `IWakeSignal`, which stays the built-in runtime's.
- **R11. Orleans' in-process kill is not a crash.** `InProcessTestCluster.KillSiloAsync` still runs the grains'
  `OnDeactivateAsync`, so the first version of the adapter released every scope when its silo was "killed": the crash
  case passed, but as a graceful stop. A watcher rule (no scope given up while a node is being killed) found it; that
  rule also flagged a release a correct runtime had begun just before the kill, so the crash case now checks it
  through the store instead: right after the kill, every scope the node held must still refuse a take. With the
  adapter's guard switched off, that check fails ("the killed node gave partition 0 up"). After `Crash()` the grain
  gives nothing up, as a dead process would, and a take that lands after the crash starts no pass.
- **R12. A finished background job wakes nobody.** Its result goes into the instance's inbox in memory, but the
  runtime is not told, so the turn that handles it waits for the next pass, at worst the poll interval (1 s by default;
  under Orleans and `RuntimeNode` alike). The simulation and the native host's pump run passes back to back, so they do
  not see it. The seam needs a way for layer 2 to ask for a pass (a callback on the activation); it is a seam change,
  left for later, and it matters for a synchronous HTTP request that waits for a job.
- **R13. A pass whose reads keep failing answers "there is work".** `RunAsync` answers zero whenever it found something
  to load or flush at its start, and a load that fails leaves it so; a runtime that calls again at once then hammers the
  store while it fails. Layer 2 should answer a short delay after a failed read; not changed here (layer 2).
- **R14. Under a runtime with a scheduler of its own, a job's body must run elsewhere.** `RuntimeNode` runs a node on
  one scheduler, as an Orleans grain does, so that a job's continuation never runs beside a pass on the
  single-threaded worker. A job's body started inline then runs there too, so a long or blocking job holds up every
  scope of the node, its beats included. `WorkerOptions.JobScheduler` moves the body off it; the continuation still
  comes back. The default stays inline, which the simulation needs.
- **R15. A cost row could count a renewal.** With R1, a row of a cost run that takes more than a third of a lease
  (3.3 s by default) counts a beat. Under load, SQLite's "1,000 balanced instances, their starts arriving by poll"
  counted one more commit (4,005 commands instead of 4,004). The cost runs now give their workers an hour's lease (the
  EF and native runs also beat both owners before each row); a lease's length changes no round trip.
- **R16. A stop must drain and a kill must win.** `RuntimeNode`'s first stop cancelled the pass under way, so a commit
  in flight had an unknown outcome, the worker reset, and the scope was never released (its successor found a death).
  A stop now lets the pass end, then releases; a kill, or a cancelled stop token, during a stop ends the release.

### L13: the other hosts and `PartitionRuntime`

Not moved, on purpose. The store suite's and the EF engines' stress tests run every worker as if it owned its
partition, with no runtime: they are the adversary of the fence, and a runtime in front of them would take away the
double activations they test. The cost runs and the native host own one partition each and drain it: through
`PartitionRuntime` their round trips would be the same and the change would test nothing they do not, and through `RuntimeNode` its
balancing rounds would add heartbeats to every count. What L13 asked for, hosts that run on a runtime, is now the
runtime suite, on the built-in runtime and on Orleans.

### The gates of R1

Before: main at `a3eb06e851`. After: the renewal's branch (#4345). Times removed, outputs compared line by line. The
cost runs ran again against main at `acda0dd642` (the SQL Server fence default) after R15, with the same result.

| Gate | Result |
|---|---|
| `$SIM --seeds 200000`, `--seeds 20000 --actions 1000`, `--native --seeds 20000 --actions 1000` | Identical: 0 failed; 0, 12 and 15 parked; 409,284 matched |
| `$SIM --mutations --seeds 20000`, `--native-mutations --seeds 20000` | Identical, seed for seed |
| Store conformance in memory; EF on SQLite and PostgreSQL, both fences | Identical: 56, 54 and 54 passed |
| Native `smoke`, `cost sqlite`, `cost postgres` | Identical, row for row |
| EF `cost` on SQLite and PostgreSQL (`proxy=1`), both fences | Identical in commands, commits, counted and sent round trips; the TCP column of the 100- and 1,000-instance rows varies by 1 to 3 reads, as README says |

The only other difference is the order tags of one concurrent fence case (`[commit first, take first]`), which vary
from run to run.

**At the final state.** The SQLite gates ran again at `e1cdc8a6ea` (R1, R4, R14 and the cancellation of a balancing
round and a release, with main merged in) against main at `6509a09a32` (the graph engine): the five simulation runs
(0 failed; 0, 12 and 14 parked; 386,774 native instances matched, 850 reruns, all told), the mutations seed for seed,
store conformance in memory and on SQLite in both fence modes, native smoke and cost, and EF cost in both fences give
identical outputs, but for those order tags.

### Not done

The runtime suite on PostgreSQL, SQL Server, MySQL and MongoDB (it takes any store provider; it ran on the in-memory
store); a leave in membership (R5); a lock in `TrialGate` (R6); the store contract change for R3; backing off a
declined activation (R7); a way for layer 2 to ask for a pass (R12); a delay after a failed read (R13).
