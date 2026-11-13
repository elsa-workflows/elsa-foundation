# The native execution model on the kernel

A spike: Elsa's native workflows as an execution model on the standalone kernel of this folder, measured and attacked.
It answers whether the kernel's model contract holds for real workflow semantics, and at what cost.

**After the merge (FINDINGS.md section 23).** This text describes the native branch as it was built. On the merged
kernel: the live channel of section 4 (`ILiveChannel`, `LiveNode`, `LiveNetwork`) and the kernel hooks N2
(`OnApplied`, `OnReset`) are gone; the model emits with the kernel's `Emit`, an HTTP start hands the request over as
`Ambient`, each node keeps its requests in the kernel's `Requests`, and the opt-out of the release rule is per route
(`HttpEndpoint.RespondBeforeCommit`), not per response. N1 (`ITurn.Publish`) stays. Frictions F-N3 to F-N6 and F-N8
are fixed in the kernel (`Events.Delivered`, `Recall`, `Start(..., detached)`, `Redelivered`, and a wait that does not
ask for kept events needs no meet ticket), and F-N9 by the owner's default that a wait gets no event published before
it. The simulation's check N6 is new. The seeds and costs below are the branch's; section 23 has the merged ones.

**Round 4 (FINDINGS.md section 24).** The names are round 4's: an instance's step is a *turn* (`ITurn`), off-step
work is a *background job* (`StartJob`, `IJob`, `job-finished`, the row's `jobs`), and the runtime's call into a
partition is a *pass*. `src/Kernel` is now three projects: `src/Kernel.Runtime`, `src/Kernel.Durable` and
`src/Kernel.Workflows`; a file named below without a path is in one of them. Seeds, counts and costs are unchanged.

Every claim says how it is known: **run** means a command below produced it; **reasoned** means nobody ran it.

| Path | What |
|---|---|
| `models/Native` | The authoring surface, the activities, the model (`NativeModel.cs`), the live channel and HTTP host (`Live.cs`) |
| `models/Native.Run` | Smoke tests of every activity and the HTTP cases, the cost measurement, the in-memory cost |
| `sim/NativeWorkflows.cs`, `sim/NativeSim.cs` | Native instances in the simulation's mix, five checks, the model's deliberate flaws |

```
dotnet build models/Native.Run -c Release
dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll smoke
dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll cost sqlite
dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll cost postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel;Database=kernel_native"
dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll memory 3000
dotnet build sim -c Release
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --native --seeds 20000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --native --seeds 5000 --from 500001 --actions 1000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --native-mutations --seeds 20000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --native --native-flaw InFlightInMemory --seed 2     # replay one, shrunk
```

Without `--native` the simulation runs exactly as before; its seeds replay unchanged (run: 20,000 seeds pass).

## 1. The authoring surface, as ported

Kept from Elsa 4 (see `docs/reports/elsa-4-activity-contract-parity-2026-07.md`):

- **Leaf activities** derive from `Activity<TResult>` (or `Activity`) and return a closed `Transition`: Complete with a
  typed result and an outcome, Fault, Cancel. One attempt per activation; a fresh object each time.
- **Stateful activities** derive from `StatefulActivity<TResult, TState, TTrigger>`: `ExecuteAsync(StartContext)`
  suspends with a state and triggers, `ResumeAsync(ResumeContext)` gets the state and the trigger on a fresh object.
  `StartContext.IsTriggerDelivery` and `TryGetTrigger` tell a start trigger from a mid-flow wait, as in Elsa 4.
- **Composites** derive from `CompositeActivity`: `Execute`, `OnChildCompleted`, `OnChildFaulted`. The context
  offers `Schedule(child, tag, scope)`, `Complete(outcome)`, `Fault`, `Join`, `Cancel(child)`, `Running`, and a
  private state (`GetState<T>`, `SetState`).
- **Inputs** are properties marked `[Input]`, hydrated from bindings: literal, variable, result of an earlier
  activity (with a path), or expression. **Outputs** are the one typed result; `Capture(variable, path)` promotes a
  part of it to a variable, as `OutputCaptures` do. **Variables** are engine intrinsics: `SetVariable` is applied by
  the interpreter, never executed. **Outcomes** are strings; a flowchart routes on them.
- `[Background]` is Elsa 4's background placement for a leaf; `[External]` is the side-effect profile External.

Ported activities (run: `smoke` exercises each in Ephemeral, Balanced and Durable): WriteLine, SetVariable, Sequence,
If, Flowchart (on the graph engine of `models/Graph`: gateways, implicit dead-path joins), Parallel (threshold join), ForEach (iteration scope with
`CurrentItem`, `CurrentIndex`), While, Delay, Event, PublishEvent, DispatchWorkflow (with and without waiting),
HttpEndpoint, WriteHttpResponse, CallService (slow external work, `[Background]`), SendNotification (`[External]`).

What changed, and why:

| Change | Why |
|---|---|
| Activities never see a store, scheduler, stager or another instance. What they want from the world is a **trigger** in Suspend (`EventTrigger`, `TimerTrigger`, `ChildTrigger`, `HttpTrigger`) or an **effect** on the transition (`PublishEffect`, `DispatchEffect`, `EmitEffect`) | The model turns each into one kernel call. That is what removes every leak (section 3) |
| `Transition` is not generic; the helpers serialize the typed result | One type fewer; the result goes into JSON state anyway |
| Composite inputs are evaluated at every activation (Execute and every callback) | While's condition is read again after each pass without Elsa 4's rematerialize marker. ForEach copies its items into its state, so a changing variable does not change a running loop |
| A stateful activity's inputs are evaluated again on resume, not taken from a pinned snapshot | No snapshot to store. The activities that wait keep what they need in their state |
| Sequence is stateless: the next child follows from the position of the one that completed | Smaller state |
| `If` children fill named slots (Then, Else); its outcome is True or False either way | The same activity works inside a flowchart |
| The Flowchart runs on the graph engine (`models/Graph`, main's Flowchart engine): paths, arrivals, dead paths. A node with several inbound connections is a join decided by the dead-path join (main's default); gateways (`Gateway`) are explicit; a first-wins loser is cancelled. A run starts at one node, main's: the pending start trigger when it is a child of the Flowchart, else the start node (the first child without inbound connections); other unconnected children do not start, and a trigger below one of them is not reached (main refuses that placement when a workflow is published; the prototype has no publish step). Unlike main, the trigger counts only while it is undelivered (a Flowchart run again by a loop starts at the start node), and no node carries main's trigger flag, so loop-closing connections are classified from the start node only | Before, without dead paths, an All join after a decision never fired and the flowchart completed without it (run: `smoke`, "an All join after a decision", then 'left no no', now 'left yes yes') |
| No DI, no services in activities. The host gates Background work and performs External effects | Enough for the spike; the activity map's DI would come back unchanged |
| A tiny expression language (`Expression.cs`), compiled to closures once per text | Instead of JavaScript |
| `Cancel` maps to completion with outcome Cancelled | There is no cancel callback on composites yet |

## 2. The model's state and messages

State (`NativeState`, JSON in the instance row):

- `Frames`: live activities only (a composite with children, or an activity that waits), each with its private
  state, its iteration scope and its committed attempt. A leaf that completes in the burst never becomes a frame.
- `Agenda`: scheduled work, in order: execute a node as a new frame (its id fixed when scheduled), or resume a wait.
- `Waits`: wait id to frame, kind (event, timer, child, job) and key (subscription key, child id, job key).
- `Variables` (the workflow's scope; iteration scopes live on frames), `Results` (last result per node), the start
  trigger not yet delivered, and the reply address of the request being answered.

Messages: `start` (with the start spec), `native.continue` (a Durable boundary), `native.timer` (a Delay is due),
`event` (a delivered event or HTTP request), `native.resume` (an outsider resumes a wait by its stimulus without the
event tables), `child-finished`, `job-finished`, `native.cancel`.

A turn takes its inbox, turns each message into agenda work, then runs the agenda until everything waits or ends. A
wait is removed when its resume is processed, so a second delivery (two events, a stale timer) does nothing.
Persistence modes are commit boundaries:

- **Balanced:** one turn until all waits or ends: one commit per burst (capped at 1,000 activities per turn).
- **Durable:** after every activity the turn ends and sends `native.continue` to itself. An `[External]` activity
  first commits its attempt (journal entry `a:frame:1`) in a turn of its own, and runs in the next.
- **Ephemeral:** `StartSpec.Ephemeral`; the kernel writes nothing when the instance ends in its first turn and sent
  nothing. Otherwise it commits like Balanced.

Journal entries are short strings: `c:frame:node` (completed), `j:...` (a join fired: `j:frame` for a Parallel,
`j:frame:node:round:by` for a Flowchart, where the round is the engine's iteration key and `by` the scheduling id the
engine gives the firing), `a:frame:n` (attempt),
`s:` and `d:` (a child started, waited for or detached), `r:` (a child wait resumed), `f:`, `x:` (faulted, cancelled).
The simulation's checks read them.

## 3. The leaks, and how each disappeared

| Leak in Elsa 4 | Here | Evidence |
|---|---|---|
| Delay writes a timer through `IDurableTimerScheduler` | `TimerTrigger`: a message to self with a due time | run (smoke; sim) |
| DispatchWorkflow reads three stores and stages a dispatch record and a post-commit intent | With wait: `ChildTrigger`, the kernel's `Start` and its `child-finished`. Without: `DispatchEffect`, a `Send` with a start spec | run |
| PublishEvent stages through `IPublishStimulusStager` | `PublishEffect`: the kernel's publish, through `ITurn.Publish` (**kernel change N1**) | run |
| Parallel counts branch states through `IActivityExecutionStateStore.ListAllByParentAsync` | Counts its own branches in its private state | run |
| Slow work: `AgentModelTurn`'s journal and lease renewer, background placement | `[Background]` leaf: `ITurn.StartJob`; its inputs are materialized in the turn, it runs as a background job, its result is a `job-finished` | run |
| The structural context's `SchedulerWorkItem`, `ActivityExecutionState`, staged requests | `CompositeContext`: schedule, complete, join, fault, cancel, own state | run |
| WriteHttpResponse's result read by the host | `EmitEffect` on the live channel, released after the commit (**hook N2**) | run |

The rest of the map's list (GraphActivity, CreateUserTask, HumanReview, RunEtl, RunRules, EvaluateDecision) was not
ported. Reasoned: user tasks and reviews are a subscription plus a resume, as Event is; ETL and rules are a child
start or a Background leaf.

## 4. HTTP

The owner's requirement: a user designs a workflow that handles a request and produces the response. The live channel
is one interface, `ILiveChannel.Emit(instance, replyTo, channel, item, beforeCommit)`, so the kernel's `Emit`,
`Ambient` and start-and-observe from the other branch can replace it. Behind it: `LiveNode` (held items, open
requests), `LiveNetwork` (an in-process stand-in for the transport between nodes, not durable), `HttpRoutes` (the
route table, built from the definitions, read nothing at request time) and `HttpHost`.

| Case | How | Evidence |
|---|---|---|
| 1. A request starts a workflow | The route table finds the workflow; the host places it in a partition the node owns, hands the start over in memory with the request as the trigger, and observes it | run, one node; Ephemeral: zero database calls |
| 2. Respond, then continue | WriteHttpResponse does not end the workflow; the request returns at the release and the workflow runs on (here into a background job) | run |
| 3. A wait before the response | A background job or a 20 ms Delay first; the instance stays in memory and the request stays open | run |
| 4. A second request resumes a workflow waiting at an HttpEndpoint | The receiving node looks up the subscription on the route and adds the delivery with a reply address `node/request`; the owner runs the turn and sends the response to that address | run, on one node and across two nodes (two workers, two partitions, one store; the transport is in process) |
| 5. Released only after the commit of the turn that wrote it | The model hands emitted items over at the end of a turn that did not throw; `LiveNode` holds them until the kernel's `OnApplied` says the turn's commit landed (or there was nothing to write), and drops them when the turn was parked | run: in Balanced the instance row exists when the response is released; in Ephemeral nothing is written and it is released at once; with `BeforeCommit` it is released before the row exists |
| The owner lost while a request waits, same node | `OnReset` drops what was held and answers open requests on that partition with 503; the workflow finishes under the new epoch and its late answer goes nowhere | run |
| The owner lost, receiving node elsewhere | The request times out (504); nothing tells the receiving node | reasoned. Missing: a signal from owner to the waiting node, or the waiting node watching the partition's lease |

Not done: client disconnect mapped to cancel, streaming, bodies by reference (the request sits in `Results` and in
captured variables, so in state: 571 bytes for http-reference).

## 5. What it costs

Run, with the counting of FINDINGS section 5: PostgreSQL round trips are command executions plus COMMITs (Npgsql
sends BEGIN with the first command); SQLite counts are calls into the engine (BEGIN, each command, COMMIT), with the
commands alone in brackets. Instances are started on their owner and handed over in memory; resumes from outside are
inserts that the owner finds by one poll, and both inserts and polls are counted. Today's figures are
the current core's command counts of 11 November, an untracked run of the tool in `Essentials/tests/Activities/Http/CommandCost/Tests/`: round trips on PostgreSQL, commands on SQLite.

PostgreSQL, round trips. Native: the fence as its own statement / the fence as concurrency tokens in the batch.

| Run | Mode | Today | Native | Native commits |
|---|---|---:|---:|---:|
| writeline-sequence, 5 / 10 / 20 / 40 | Ephemeral | 48 (32 without the record) | 0 | 0 |
| writeline-sequence, 5 / 10 / 20 / 40 | Balanced | 48 | 3 / 2 | 1 |
| writeline-sequence, 5 | Durable | 155 | 18 / 12 | 6 |
| writeline-sequence, 10 | Durable | 230 | 33 / 22 | 11 |
| writeline-sequence, 20 | Durable | 380 | 63 / 42 | 21 |
| writeline-sequence, 40 | Durable | 680 | 123 / 82 | 41 |
| fork-join-resume, start and direct resume | Ephemeral | 142 | 15 / 11 | 4 |
| fork-join-resume, start and direct resume | Balanced | 142 | 15 / 11 | 4 |
| fork-join-resume, start and direct resume | Durable | 223 | 21 / 15 | 6 |
| fork-join-resume, start and a published event | Balanced | | 17 / 13 | 4 |
| fork-join over three event branches, three resumes | Balanced | | 17 / 13 | 4 |
| fork-join over three event branches, three resumes | Durable | | 40 / 29 | 11 |
| http-reference | Ephemeral | 80 | **0** | 0 |
| http-reference | Balanced | **80** | **3 / 2** | 1 |
| http-reference | Durable | 153 | 12 / 8 | 4 |
| respond, then continue (case 2) | Balanced | | 6 / 4 | 2 |
| a background job or a 20 ms Delay before the response (case 3) | Ephemeral or Balanced | | 6 / 4 | 2 |
| resume at an HttpEndpoint, both requests, one node or two (case 4) | Balanced | | 16 / 12 | 4 |

SQLite, engine calls (commands):

| Run | Mode | Today, commands | Native |
|---|---|---:|---:|
| writeline-sequence, 5 / 10 / 20 / 40 | Balanced | 54 / 64 / 84 / 124 | 5 (3) for every length |
| writeline-sequence, 5 / 10 / 20 / 40 | Durable | 146 / 221 / 371 / 671 | 45 (33) / 85 (63) / 165 (123) / 325 (243) |
| fork-join-resume, direct resume | Balanced | 131 | 26 (18) |
| fork-join-resume, direct resume | Durable | 194 | 42 (30) |
| flowchart, three event branches, an All join, three direct resumes | Ephemeral / Balanced / Durable | | 29 (23) / 29 (23) / 85 (65): the same as the old native Flowchart (round 4) |
| http-reference | Ephemeral / Balanced / Durable | 83 / 83 / 149 | 0 / 5 (3) / 29 (21) |

Where the commits of fork-join-resume go (run, from the traced commands): the start (row, subscription, meet ticket),
a commit that only consumes the meet ticket after reading the event table, the commit that marks the polled resume
seen (finding F3), and the turn that finishes. A direct resume needs no subscription at all, but the Event activity
cannot know that it will come that way.

Size and the model's own work, in memory (run: `memory 3000`, the in-memory store, one worker; the toy sequence of
`sim/Models.cs` is the kernel without a model's work):

| Run | Ephemeral: allocated, time per run | State bytes per commit, Balanced (Durable) |
|---|---|---:|
| toy: ten no-ops | 9.0 KiB, 9 µs | 73 |
| writeline-sequence, 10 | 27.4 KiB, 31 µs | 178 (376, at most 399) |
| writeline-sequence, 40 | 84.6 KiB, 82 µs | 178 (392) |
| http-reference | 24.4 KiB, 17 µs | 578 (639) |
| fork-join-resume | | 366, at most 506 |

The model costs about 1.9 KiB and 2 µs per activity (reflection to hydrate inputs, JSON values). Not significant
beside one round trip. In Balanced and Durable the in-memory test store dominates the allocations (330 KiB per
commit for the toy), so those columns are not shown.

## 6. The simulation

Native instances join the mix (40% of the outside actions when `--native` is on): sequences with an External
activity, a three-way join over event waits, a two-of-three threshold join, a flowchart parallel join over two waits,
two waited children in a Parallel plus a detached one, a ForEach with a Delay in the body, a While and a background job,
publishers that publish to other instances' waits, and (round 4) `n-graph`, the graph engine's shapes in one Flowchart:
a loop from the start node with a fork and an implicit join in its body, the decision that leaves it (its untaken
side is a dead path every round), a first-wins race between an Event and a Delay (the loser is cancelled and the join
behind it released), and an All join after a decision. Starts come from outside (retried) or are handed to an owner
in memory; events come by publish (before or after the wait), by direct resume, or from the publishers; some
instances are cancelled. Modes are mixed. The kernel's faults are the simulation's, unchanged.

Checks:

| Check | The brief's words | How |
|---|---|---|
| N1 | Every activity that completed in committed state completed exactly once | No frame id appears twice in `c:` entries of the committed journal (every action); at the end the multiset of completed activity types equals a run without faults |
| N2 | A join fires once | No `j:` entry twice. A Parallel joins once; a Flowchart join fires at most once per scheduling id in a round, so a join behind a merge may fire once per token (narrowed in round 4: N2 no longer holds a Flowchart join to once per round; the design sketch's G2 does, once the graph checks are in) |
| N3 | A child is started once and its parent resumes once | A child id is started by one frame, a frame starts one child, a child wait is resumed only by its own child and once; at the end every waited child of a finished parent resumed it exactly once |
| N4 | A non-replay-safe activity's attempt is committed before it runs in Durable mode | When an External effect happens, the committed journal holds its attempt |
| N5 | Final variables and outputs match a run without faults | Every finished native instance (children too) has the variables of the same definition and input run alone, without faults; everything started from outside finished or was cancelled by request |

The graph invariants of the design sketch (round 4, `sim/NativeSim.cs`: `NoteGraph`, `CheckGraphStates`). They read
the native Flowchart's own journal entries, which ride in the turn's commit: `g|x` a path the step cancelled, `g|f` a
node a join or continuation scheduled, with the sources of the arrivals it consumed, live and dead (taken from the
engine's unpruned state, `FlowchartStep.Live`), `g|c` a completion with the paths still live. They also read the
committed state of instances with a Flowchart; which connections reach a node comes from the definition.

| Check | The sketch's words | How |
|---|---|---|
| G1 | Path conservation: every path created is completed, discarded as dead, or still live | In committed state every active path has its child and the child runs, and a graph with live paths has something running under it (otherwise a token is lost); in the journal no node runs on dead arrivals only |
| G2 | A join fires at most once per round | No node fires twice with the same iteration key |
| G3 | An All join fires once every inbound flow of its round has arrived, live or dead | At each firing of a node with several inbound connections the consumed arrivals cover every inbound source; the exception is a branch a first-wins race cut off in that round (a cancelled node, or one only cancelled nodes lead to), which the engine releases when the graph goes quiet |
| G4 | A graph completes exactly when it has no live path | A completion has no live path; a Flowchart frame with no live path and nothing running under it fails |
| G5 | No path outlives its cancelled scope | No live path in a race scope that is over; no child runs on a cancelled path |

G2 and G3 skip a merge and every node forward of it: a merge passes every token on, so a node behind it may fire
once per token in a round. The mix has no merge.

Results (run):

| Run | Seeds | Result |
|---|---|---|
| 300 actions | 20,000 | 0 failed; 162,630 finished native instances matched a run without faults |
| 1,000 actions | 5,000 (from 500001) | 0 failed; 116,392 matched. 352 instances were parked by the kernel's poison rule as innocent bystanders (FINDINGS section 7, rule 3); the checks skip parked instances |
| Without `--native` | 20,000 | 0 failed: the kernel changes did not disturb the existing invariants |

The checks bite (run: `--native-mutations`, each flaw alone, first failing seed):

| Flaw | What it breaks | Caught |
|---|---|---|
| `InFlightInMemory` | Durable: the executed item leaves the agenda in memory only | N1, seed 2. Shrunk to two actions and a takeover: `N1 1/j1: activity frame f6 (s2) completed twice in committed state` |
| `JoinWithoutGuard` | Parallel without its joined flag, and the join does not cancel the other branches | N2, seed 5: `the join f2 fired twice` |
| `ResumeAnyChild` | A child-finished resumes every child wait | N3, seed 2: `the wait for child 1/j5~1.1 was resumed by child 1/j5~1.0` |
| `NoAttemptCommit` | Durable runs an External activity without committing its attempt first | N4, seed 2 |
| `ScopeInMemory` | The iteration scope is kept in memory only | N5, seed 6: `sum=53; without faults it ends with sum=63` |
| `NoDeadPaths` (round 4) | An untaken connection emits no dead arrival | G3, seed 2: `the join end of graph fired in round head:2 before no arrived, live or dead` (the graph went quiet and the engine released the join without the decision's untaken side) |
| `NoRoundReset` (round 4) | A loop-closing connection opens no new round | G2, seed 2: `node a of graph fired twice in round -` |
| `DeadPathRunsNode` (round 4) | An all-dead node is run instead of passing the deadness on | G1, seed 2: `a dead path ran node race of graph (round -)` |
| `CancelLeavesPaths` (round 4) | A first-wins race completes without cancelling its losers | G5, seed 2: `path path:280 at r1 of graph lives on in the race scope scope:275, which is over` |

The four graph flaws are the engine's (`FlowchartOptions.Flaws`, marked MUTATION in `models/Graph`); the mutation
check fails one that another check catches first. The round-4 seeds are those of the mix with `n-graph` in it, which
moves every seed; the five older flaws' first seeds moved with it.

Found while building the checks (run): one seed of 1,000 actions failed my own event check because a parked
instance keeps its subscriptions (seed 500963); the check now skips instances that do not run. Round 4 met the same
in the kernel's I8 (seed 2282 at 1,000 actions, once `n-graph` shifted the seeds), which now skips an instance the
poison rule parked while its faulted row exists; and a native defect: in Durable mode a cancel recalled a fired
Delay's timer message that an earlier turn had consumed, because the resume still waited in the agenda (I9, seed
8486); a timer whose message has arrived is no longer recalled. And a threshold join
is not deterministic in which branch activities complete: three events in one inbox complete all three Event
activities before the join cancels the third branch; one by one, the third is cancelled while it waits. Variables
agree either way, so for that workflow only the variables are compared.

## 7. Where the contract was awkward or had to change

Changes, marked `NATIVE` in the kernel's source:

- **N1. Publishing from a turn** (`ITurn.Publish`, `Kinds.Publish`). Scenario: PublishEvent. `ITurn` had no way to
  publish; `Events.PublishAsync` needs the store, so a model could only publish by holding the store itself. The
  smallest change: the commit carries a ticket to self, and the owner publishes after it lands, exactly as it reads
  events for a meet ticket (about 20 lines with comments in `Turn`, `Worker`, `Contracts`; N1 and N2 together add 42 lines to the kernel). The inbox alone still says what is owed. Run.
- **N2. A post-commit signal** (`WorkerOptions.OnApplied`, `OnReset`). Scenario: WriteHttpResponse in Balanced. The
  release rule needs to know that the turn's commit landed, or that the turn was parked or its worker reset; a model
  cannot see that. The kernel's own `Emit` should own it. Run.

Frictions, without a change:

- **F-N3. A delivered event names neither its key nor its wait.** Scenario: every Event resume. The model reads the
  wait from the end of the message id (`ev:{event}:{instance}:{wait}`). Run.
- **F-N4. A turn cannot take back a timer.** Scenario: a threshold join cancels a branch waiting in a Delay; a
  workflow is cancelled with a Delay pending. The sketch says a timer is cancelled by deleting its message; `ITurn`
  has no call for it, so the message arrives later, finds no wait, and costs a poll and a commit to drop (the cost is
  reasoned). BPMN boundary timers will make many of these.
- **F-N5. No detached start.** Scenario: DispatchWorkflow without waiting. `Start` always makes a child (the child
  then reports to a parent that did not wait). `Send` with a start spec is detached, but the kernel then loads the
  target before creating it, because only ids minted by `Start` are known to be fresh. Reasoned from `Worker.RunTurn`.
- **F-N6. A model cannot tell a redelivery.** Scenario: N4. In Durable, an External effect whose attempt was
  committed ran again under that same attempt 629 times in 20,000 seeds (476 in 5,000 of 1,000), each time after a
  turn's result was lost. To raise "may have run" as an incident instead, a turn needs to know that its message was
  held by a lost owner; the kernel knows (`Delivery.Suspect`) and could put it on the message. Run.
- **F-N7. Ephemeral is lost at the first wait.** Scenario: HTTP case 3. A 20 ms Delay or a background job before the
  response is a message, so the instance commits twice (4 to 6 round trips) where it would otherwise cost zero. Short
  timers kept in memory for an instance that is not written would keep request-response workflows at zero. Run.
- **F-N8. The meet ticket costs a commit.** Scenario: fork-join-resume. Every turn that subscribes pays a read of the
  event table and a commit that only consumes the ticket, even when the resume will come directly. Run (traced).
- **F-N9. Kept events change Event's meaning.** Scenario: the cost measurement, first version. An event published
  for a name stays an hour, so a later workflow waiting for the same name is resumed by it at once. Elsa 4 drops a
  stimulus nobody waits for. Native now has buffered events whether or not it wants them; names were made unique per
  run to measure. Run.
- **F-N10. A publish to a waiter in the same partition goes through the table.** Scenario: smoke, the listener. The
  delivery is an insert that the owner finds only by its next poll, never handed over in memory. Run.
- **F-N11. Two places say "ephemeral".** `StartSpec.Ephemeral` is the kernel's, the persistence mode is the model's;
  the model's `Spec` helper keeps them in step. Reasoned.

What fitted without friction (run): `StartJob` and `job-finished` for slow work and its cancel; `Start` and
`child-finished`; `Send` with a delay for timers; a Durable boundary as a message to self; one burst per turn; the
journal entry as the history and as what the checks read.

## 8. What a BPMN or an agent model would need that native did not

Reasoned.

- **BPMN:** cancelling timers from a turn (F-N4), for boundary timers that mostly never fire; subscriptions of kind
  start, for message and signal start events (not built in the kernel); placement other than with the parent, for
  multi-instance over thousands of items; interrupting a running task is `CancelJob` and fits; a message that
  arrives early is a kept event (F-N9 is what BPMN wants).
- **Agents:** streamed output cannot wait for a commit, so the live channel needs the before-commit path per item,
  and many items per turn; a conversation grows, and the state is rewritten at every commit (native rewrites 150 to
  1,000 bytes; a thread is megabytes), so segments or values by reference; tool calls are jobs (`StartJob`) and fit; a human
  approval is a subscription and fits.
- **Both:** the draft-4 "workflow instance" layer should own what native did by hand: cancelling the children it
  started, status and sub-status, what lists show, and a history envelope (native's journal entries are bare strings).

## 9. Not done

- MongoDB and the other SQL engines; the concurrent PostgreSQL stress test with native instances.
- The cross-node HTTP transport is in process; the owner-lost case across nodes is reasoned.
- Client disconnect mapped to cancel, streaming responses, request bodies by reference.
- GraphActivity, CreateUserTask, HumanReview, RunEtl, RunRules and EvaluateDecision; Switch, For, Do, CompensationScope.
- Incident strategies; retries of faulted activities.
