# Findings

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
