# BPMN on the kernel

A spike: BPMN as its own execution model on the standalone kernel of this folder, hosting the valence-works
`Bpmn.Semantics` interpreter unchanged (owner decision of 12 November; design record, draft 5, "Containers and their
semantics", plan step 2). It answers whether the kernel's execution-model contract holds for a token interpreter that
is not Elsa's, and at what cost.

Every claim says how it is known: **run** means a command below produced it; **reasoned** means nobody ran it;
**traced** means it was read in code.

| Path | What |
|---|---|
| `models/Bpmn` | The model: `BpmnModel.cs` (state, options, the job), `BpmnTurn.cs` (one turn), `Processes.cs` (the host's table of work, the registry, a builder for expression flows), `Conditions.cs` (variables and flow conditions), `Cases.cs` (the spike's processes) |
| `models/Bpmn.Run` | `smoke` (the model's own cases and every spike case), `cost sqlite` and `cost postgres` |
| `sim/BpmnSim.cs` | BPMN instances in the simulation's mix, the token checks G1 to G6, the host's deliberate flaws |

```
dotnet build models/Bpmn.Run -c Release
dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll smoke
dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll cost sqlite
dotnet models/Bpmn.Run/bin/Release/net10.0/Kernel.Bpmn.Run.dll cost postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel;Database=kernel_bpmn"
dotnet build sim -c Release
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --bpmn --seeds 200000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --bpmn --seeds 20000 --actions 1000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --bpmn --native --seeds 20000 --actions 1000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --bpmn-mutations --seeds 20000
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --bpmn --bpmn-flaw NoLateReport --seed 1     # replay one flaw, shrunk
```

Without `--bpmn` the simulation runs exactly as before: the generator draws nothing extra, and every existing seed
replays unchanged (run: section 5).

## 1. The interpreter and the package

`Bpmn.Semantics` and `Bpmn.Model` 0.5.0, the published packages from nuget.org (MIT, no dependencies, `net8.0` and
`net10.0`), pinned in `models/Bpmn/Kernel.Bpmn.csproj`, with a source mapping in the prototype's `NuGet.config` that
names them and keeps `*` on the one source. An empty-cache restore pulls exactly those two (run). The interpreter is a
pure function: `(graph, prior token state, host snapshot, one event) -> (next state, continuation, commands)`; it has no
clock, no I/O and no threads, so it runs inside the deterministic simulation as it is (run).

What the interpreter asks of a host, and what the model gives it (traced against its `docs/guides/hosting-the-interpreter.md` and `docs/concepts/host-port.md`):

| The interpreter's | The model |
|---|---|
| A graph per definition, built once with the host's capabilities | `BpmnProcess`: `BpmnGraph.Build` with `Full | ConditionEvaluation` |
| The prior state, the one it last returned | `BpmnState.Tokens`, pruned, in the instance row |
| A snapshot: live work, variables, conditions, an enclosing scope | Built per call from the ledger and the variables; a called child has an enclosing scope |
| A host ledger keyed by handle, with each `StartWork`'s token id | `BpmnState.Live`: handle, binding, iteration, token id, element, and the kernel's side (kind and key) |
| Commands applied in the order returned | `BpmnTurn.Apply` |
| Finished work leaves `LiveWork` before its callback | `Done` removes it first |
| Flow conditions answered by the host | `Conditions`: `name op literal`, no FEEL |

## 2. How it maps onto the kernel

A turn feeds each inbox message to the interpreter as one event and applies the evaluation before reading the next
message: the commands in order, then the reports of the work they stopped. Once an evaluation ends the scope (an
uncaught fault, a terminate end event), the rest of the inbox is consumed without reaching the interpreter.

| BPMN | Interpreter | Kernel (`ITurn`) | Back to the interpreter |
|---|---|---|---|
| Start | `Start` | the `start` message | |
| Service task | `StartWork` | `StartJob(handle, args)`; the model's `ExecuteJobAsync` runs the named service over the variables the turn saw | `job-finished` → `OnWorkCompleted` (outcome names from the service) or `OnWorkFaulted` |
| Timer (catch or boundary) | `StartWork` | `Send(self, "bpmn.timer", delay)` | the message → `OnWorkCompleted` |
| Message catch | `StartWork` | `Subscribe("msg:{name}:{correlation}", handle, keep: true)` | the event delivery → `OnWorkCompleted`, and `Unsubscribe` |
| Call activity | `StartWork` | `Start(spec)`: a child in the same partition | `child-finished` → `OnWorkCompleted` (`Faulted` or `Cancelled` outcome, with the child's BPMN error code) |
| Escalation from a called child | `SignalEnclosingScope` | `Send(parent, "bpmn.signal")` | the parent's `OnWorkSignalled` on its call |
| Interruption | `CancelWorkSubtree` | `CancelJob`, `Recall`, `Unsubscribe`, or `kernel.terminate` to the child | the host reports the stopped unit at once |
| End | `Complete` / `Fault` | `SetStatus(Finished, output: the variables)` / `SetStatus(Faulted)` after stopping what still runs | |

Details that matter, each found by a review or the simulation:

- **Stopped work never reports on the kernel.** A cancelled job's result is nobody's, a recalled timer never comes,
  and an unsubscribed wait gets nothing. The interpreter keeps a cancelled token with its stopped handle until a late
  report arrives, so the host reports each stopped unit itself, right after the commands; the interpreter absorbs it
  and spends the handle (traced; G5 checks it, and the flaw `NoLateReport` shows it is needed).
- **A kept event is not consumed.** A catch subscribes with kept events, so a message published before it still
  counts. But the kernel gives a kept event to every wait that subscribes within its window, and BPMN consumes a
  message once: a catch that comes round again in a loop would take the same event again. The state keeps the ids of
  the events catches took and skips them (friction F-B1).
- **Diagnostics are not stored.** The interpreter appends a diagnostic for every decision (up to 200 kept); it never
  reads them back (traced in its 0.5.0 source), so the row drops them and the journal keeps the joins that fired, with
  the arrivals each consumed: what the checks read.
- **The journal** has short entries: `w handle element kind key` (work started), `d` (reported), `x` (stopped),
  `j element merged-token arrival/flow,...` (a join fired), `r` (a child reported), `s` (a child signalled), `e` (a job
  failed, with its error), `end`, `fault`.

## 3. The cases

`models/Bpmn/Cases.cs`; run by `smoke` on the in-memory store, both ways where a timer races (run, every case
passes):

| Case | Process | What it shows |
|---|---|---|
| Parallel | `b-parallel`: fork, two service tasks, join | two jobs; the join fires once with one arrival per inbound flow |
| Exclusive | `b-exclusive`: `amount > 100`, else the default | the host's condition picks the flow |
| Inclusive | `b-inclusive`: `express`, `insured`, else the default; inclusive join | one, two or the default flow; the join fires once on what was taken |
| A service task as a job | every task above | `StartJob`, `job-finished` |
| Interrupting boundary timer | `b-boundary`: a timer on a service task (300 ms; 1 s in the smoke and cost runs, so a job that should win does not lose on a loaded machine) | the timer first: the job is cancelled, the timeout path runs, the job's late result changes nothing; the job first: the timer is recalled |
| A message before its catch | `b-early`: a service task, then a catch on `order` | published before the instance starts, the catch still gets it (kept event) |
| Call activity | `b-call`: a child `b-child` with a service task, and a boundary timer on the call | the child's variables come back; the timer first: the child is terminated, its job cancelled |
| Loop | `b-loop`: exclusive merge, fork, two service tasks, join, `count < 3` back | three rounds; the join fires once per round, each arrival once |

The model's own smoke cases (`models/Bpmn.Run/Smoke.cs`, each found by review): a message catch in a loop takes one
kept message once; a fault in one branch of a fork ends the instance and stops the other branch, also when both
results reach one turn; a child's coded BPMN error is caught by a coded error boundary on the call; a child's
escalation reaches a non-interrupting escalation boundary on the call; conditions with operators inside literals.

## 4. What it costs

Run (`cost sqlite`, `cost postgres` against `postgres:17`), counted as NATIVE.md section 5 counts: PostgreSQL round
trips are command executions plus COMMITs (Npgsql sends BEGIN with the first command); SQLite counts are calls into
the engine (BEGIN, each command, COMMIT), with the commands alone in brackets. Each case runs once, started on its
owner and handed over in memory; jobs run in the owner's process; what comes from outside (a published message) is
counted, with the poll that finds it. The boundary timers are 1 s here. One commit is one transaction; SQLite gives the same counts with the fence in
the batch or on its own.

| Case | Commits | PostgreSQL round trips, fence on its own / in the batch | SQLite calls (commands) | State bytes per commit (max) |
|---|---:|---:|---:|---:|
| Parallel: fork, two jobs, join | 2 | 6 / 4 | 15 (11) | 667 (1,053) |
| Exclusive: two conditions, one job | 2 | 6 / 4 | 13 (9) | 483 (675) |
| Inclusive: two of three flows, two jobs, join | 2 | 6 / 4 | 15 (11) | 752 (1,147) |
| Boundary timer, the job finishes first | 2 | 6 / 4 | 15 (11) | 697 (1,108) |
| Boundary timer fires first and cancels the job | 3 | 9 / 6 | 23 (17) | 694 (1,108) |
| Message published before its catch (kept event), the publish included | 5 | 20 / 15 | 36 (26) | 582 (725) |
| Message published after the catch subscribed, the publish included | 5 | 20 / 15 | 36 (26) | 582 (725) |
| Call activity: a child with one job | 4 | 12 / 8 | 31 (23) | 615 (1,149) |
| Call activity: the boundary timer terminates the child | 5 | 16 / 11 | 38 (28) | 701 (1,149) |
| Loop: three rounds of fork, two jobs, join | 4 | 12 / 8 | 35 (27) | 908 (1,117) |

Where the commits go (run, from the traced commands):

- **A job is one commit to start and one for its result.** Two jobs started in one turn finish in one turn when their
  results arrive together (they did here: the jobs run in the owner's process), so a fork over two tasks costs what one
  task costs. A boundary timer adds no commit when the job wins: the timer is a message to self held in memory, and
  recalling it deletes a row that was written with the start (the commit that consumes the job's result deletes it).
  When the timer wins, its turn is a commit of its own.
- **The loop** is one commit per round plus the start: each round's two results and the next round's two job starts
  are one turn.
- **The call activity** is two instances: the parent's start (the child's start goes to the same partition in memory,
  so it needs no read), the child's start and its job, the child's end with its `child-finished`, the parent's end.
  Terminating the child costs one more commit (the child's terminate turn).
- **The message catch** costs five commits, the same whichever comes first: the start (row, job ticket); the job's
  result with the subscription and a meet ticket (the catch asks for kept events); the meet, which reads the event
  table, inserts the delivery and commits only to consume its ticket (friction F-N8); the commit that marks the polled
  delivery seen (finding F3); the turn that ends. The owner needs a poll for a delivery its own meet inserted
  (friction F-B2). The publish is two commands (insert the event, read the subscriptions).
- **State.** 480 to 910 bytes per commit, 1,150 at most here: the interpreter's token state (its record shape with
  every field named), the variables, and about 100 bytes per unit in flight in the ledger. The diagnostics are not
  kept; the interpreter would keep up to 200 of them.

Today's Elsa has no BPMN runtime to compare with. The nearest native row (NATIVE.md section 5: fork-join-resume,
Balanced, 15 / 11 round trips, 4 commits) waits for events, not jobs.

## 5. The simulation

BPMN instances join the mix with `--bpmn` (40 % of outside actions): the seven processes of section 3 and `b-fault`
(a fork whose one branch fails with no boundary), with random inputs, started from outside (retried) or handed to an
owner in memory; messages for `b-early`'s catch, now and then twice, before or while it waits; terminates from outside,
to root instances. The kernel's faults are the simulation's, unchanged (crashes, takeovers, lost answers, zombies,
pauses, clock jumps, suspend and resume). Jobs complete when the simulation says, so boundary timers race them on the
simulation's clock and both orders occur.

The design record's G1 to G5 are written for graph paths; for tokens they read as below, checked against the committed
rows after every action and at the end (`sim/BpmnSim.cs`):

| Check | The design record's words | How |
|---|---|---|
| G1 | Path conservation: every path created is completed, discarded as dead, or still live | Token conservation: the interpreter's work in flight (`ActiveWork`), the host's ledger and the kernel's work (row jobs, pending timer messages, subscriptions, child rows or their starts) are one set, both ways; a running child of a BPMN instance is its parent's live call, or a terminate is on its way |
| G2 | A join fires at most once per round | No arrival token is consumed by two firings, no firing is journalled twice; at the end the joins fired equal a run without faults (the loop's join three times) |
| G3 | An All join fires once every inbound flow of its round has arrived | A parallel join fires on exactly one arrival per inbound flow; an inclusive join on distinct inbound flows |
| G4 | A graph completes exactly when it has no live path | A Finished row has no live token, no ledger entry and no interpreter work; at idle every instance started has ended as it should (Finished; Faulted for `b-fault`; Cancelled if terminated) |
| G5 | No path outlives its cancelled scope | No cancelled token keeps a stopped handle; a row the model ended keeps no job, subscription or timer; a terminated row keeps no job or subscription; a cancelled child was stopped by its parent's call or its parent was terminated |
| G6 | (added) | A finished root's variables equal a run alone without faults (the timer-first run when a boundary timer won) |

An instance whose job the kernel parked (on trial when too many owners were lost, FINDINGS section 7, rule 3) faults
with that job's error: the kernel's doing, so I14 counts it as an innocent bystander, and G1, G4 and G5 still check how
the host ended it (found by `--bpmn --native`, seed 6706 of 1,000 actions).

Results (run, at the final head):

| Run | Seeds | Result |
|---|---|---|
| `--bpmn`, 300 actions | 200,000 | 0 failed; 1,362,891 finished instances matched a run without faults |
| `--bpmn`, 1,000 actions | 20,000 | 0 failed; 444,548 matched; 9 innocent instances parked at the threshold (the kernel's rule 3; the checks skip them) |
| `--bpmn --native`, 1,000 actions | 20,000 | 0 failed; 269,711 BPMN and 410,057 native instances matched; 12 parked; one BPMN job parked by the kernel |

Messages for `b-early` were published before their catch 121,302 times in the first run and to a waiting catch 380
times: a catch waits only between its job's result and its message, so the idle round's publish covers most of the
second path. The bystander rule ran once (seed 6706 of the run with native): its job-error branch is run, its caller
branch and the recognition of a kernel-parked row by its fault text are traced.

The host broken on purpose (`--bpmn-mutations --seeds 20000`, each flaw alone; a catch by another check than the one
named counts as a miss):

| Flaw | What it breaks | Caught |
|---|---|---|
| `KeepStoppedWork` | a stopped unit's job, timer, subscription or child is left running | G1, seed 1: `G1 child 1/b73~1.0 runs and its parent 1/b73 (Finished) has no call for it, and no terminate is on its way` |
| `NoLateReport` | a stopped unit is never reported to the interpreter | G5, seed 1: `G5 1/b73: cancelled token token:4 at call still holds stopped handle h1` |
| `KeepSubscription` | a message catch that fired keeps its subscription | G5, seed 1: `G5 1/b30 ended Finished and keeps jobs, subscriptions or timers` |
| `StaleTokens` | each message of a turn is evaluated against the turn's first token state | G1, seed 1: `G1 1/b40: the interpreter waits on tokens [token:12]; the ledger holds []` |
| `ReuseHandles` | a new unit can take a live unit's handle | I9, seed 1: `I9 sent message 1/b40!h1 exists already` |
| `ResurrectArrivals` | the stored state keeps the arrivals a join consumed | G4, seed 1: `G4 0/b19 finished with live tokens [join] or work []` |
| `NeverEnd` | the host misses the interpreter's Complete | G4, seed 1: `G4 BPMN instance 0/b3 (b-exclusive) ended as Running` |
| `LoseChildOutput` | a call's child output does not reach the state | G6, seed 10: `G6 0/b40 (b-call) ended with order="b40"; without faults it ends with child-out="child of b40", order="b40"` |
| `KeepChild` | a stopped call's child is not terminated | G1, seed 1: `G1 child 1/b73~1.0 runs and its parent 1/b73 (Finished) has no call for it, and no terminate is on its way` |

No host flaw makes a join fire twice (G2) or on the wrong flows (G3) without breaking the interpreter, which the spike
runs unchanged; those two checks guard the interpreter and the journal. Without `--bpmn` the simulation's output is
identical to main's, times aside, at both sizes, with `--native`, and in both mutation runs (run: `diff` of the logs).

## 6. The execution-model contract

**No change was needed** (traced: `models/Bpmn` uses `IExecutionModel` and `ITurn` as they are on main, and touches
no file under `src/`). Everything the interpreter asks for maps onto calls that exist: `StartJob` and `CancelJob`,
`Send` with a delay and `Recall`, `Subscribe` with `keep` and `Unsubscribe`, `Start` and a `kernel.terminate`
message, `Send` for a signal to the parent, `SetStatus`, `Record`, and `Events.Delivered` to read which wait an event
is for. NATIVE.md section 8 expected two needs of BPMN, and both were there: a turn can take back a timer (`Recall`,
from the merge), and a wait can ask for kept events (`keep`).

Frictions, none of them a contract change; whether any becomes one is the owner's question:

- **F-B1. A kept event is never consumed.** Scenario: a message catch in a loop. The kernel gives a kept event to
  every wait that subscribes within its window; BPMN consumes a message once. The model keeps the ids of the events
  its catches took, in its state, and skips them; the set grows by one id per message taken and is never trimmed here
  (a real host would drop ids past their window, which a turn cannot see). A kernel option to consume a kept event on
  delivery would remove it. Run (`smoke`: the catch in a loop; checked to fail without the fix).
- **F-B2. The owner's own meet goes through the table.** Scenario: a catch whose message was published before it. The
  meet that reads the kept events inserts the delivery as a message row, and the owner finds it only by a poll, as
  F-N10 found for a publish within one partition. Run (`cost`: one poll and one commit to mark it seen).
- **F-B3. Stopped work never reports.** Not a defect of the kernel: `CancelJob`, `Recall` and `Unsubscribe` are final.
  But the interpreter expects a late report for each unit it stopped, so the host makes one up at once. A model that
  forgot would keep cancelled tokens for ever (G5, flaw `NoLateReport`). Run.
- **F-B4. `child-finished` carries no code.** `ITurn.SetStatus(status, subStatus, output)` keeps `subStatus` nowhere
  (`Turn.SetStatus` drops it), and `ChildFinished` has the status and the output only, so a faulted child puts its
  BPMN error code in its output for the parent's call activity. Traced; run (`smoke`: the coded error boundary).
- **F-B5. A terminate from outside leaves the model's state as it was.** The workflow instance handles
  `kernel.terminate` without the model: the jobs are cancelled, the waits deleted, the children terminated, and the row
  is Cancelled, but its state still shows tokens waiting at their tasks and units in the ledger, and a pending timer's
  message still arrives and is dropped (a poll and a commit, as F-N4 reasoned for native). A viewer reading the
  tokens would show a cancelled instance as waiting. Traced; the cost is reasoned; the simulation's G5 checks what the
  kernel cancels.
- **F-B6. Signals to the parent are the model's own messages.** An escalation from a called child goes to the parent
  as `bpmn.signal`; the kernel has no notion of a child signalling its parent without ending. It needs none: a model
  message from child to parent keeps the order with the child's `child-finished` (one sender, one target). Run.
- **F-B7. The model relies on the interpreter not reading its diagnostics.** It drops them from the row to keep the
  state small; that holds for 0.5.0 (traced) but is not part of the library's documented contract. Worth asking
  upstream to document it, or to offer state without diagnostics.

## 7. The join code beside main's Flowchart engine

The design record (draft 5, "Containers and their semantics") asks whether a neutral core of tokens, flows, join
rounds and reachability exists between `Bpmn.Semantics` and main's Flowchart engine
(`Essentials/src/Activities/Flowchart/Internal`: `FlowchartJoinCoordinator`, `FlowchartStateMutator`,
`FlowchartGraph`, `FlowchartReachabilityAnalyzer`, `FlowchartScopeResolver`, the join policies). A finding only:
nothing was built. Traced in both engines' code (BPMN paths under the library's `src`, Elsa's under
`Essentials/src/Activities/Flowchart`); the verdicts on what could be shared are reasoned.

| Concept | Bpmn.Semantics 0.5.0 | Elsa Flowchart (main) | Verdict |
|---|---|---|---|
| The token | `BpmnToken`: id, element, arrival flow, parent, status, work handle, iteration key, stopped handle | `ExecutionPath` and a separate `FlowchartArrival`, with a scope id and the last outcomes | Same idea, different shape |
| Status | Active, AwaitingChild, WaitingAtJoin, Consumed, Canceled | Path: Active, Waiting, Completed, Canceled, Faulted; arrival: Arrived, Consumed, Dead | Different |
| Which nodes join | Parallel and inclusive gateways with more than one inbound flow; any other node merges exclusively | Any node with more than one inbound connection but a Merge | Different |
| Parallel readiness | Every inbound flow id has an arrival (`BpmnTokenCoordinator.IsJoinReady`) | Dead-path join (the default): every inbound source has answered, live or dead, and one is live | Different rules |
| Inclusive readiness | Wait while a live token of the same iteration can reach an un-arrived flow's source, over the whole graph, loop-back flows included (`AnyLivePositionCanReach`, `BpmnGraph.CanReach`) | Reachability join: wait while an active child in the same scope can reach an un-arrived source, forward only (loop-closing edges skipped); stuck joins are released once nothing is active | Same idea, different rules |
| Join key, rounds | (element, iteration key); the key is minted on a loop-back flow | (node, scope, iteration key); the key comes from a loop-iteration scope | Same idea, different source |
| Surplus arrivals | One arrival per flow per firing; the rest wait for the next firing | All pending arrivals are consumed | Different |
| Loop-closing edges | Depth-first back-edge set by flow id (`BpmnGraph`) | The same algorithm by node pair; `FlowchartGraph` says it mirrors `BpmnGraph.ComputeBackwardFlowIds` | The same thing |
| Reachability | Breadth-first over all flows | Breadth-first, with a forward-only option | The same algorithm, a different graph |
| Dead paths | None: inclusive joins work out deadness on demand | Dead arrivals on untaken edges, carried through all-dead nodes, stopped at loop edges | Different |
| State mutator, diagnostics | Ids from a sequence counter bumped by every change; an append-only diagnostic list | The same pattern, other records | Same pattern |
| Cancelling a scope | Cancels tokens and work, cascades to multi-instance and compensation, issues teardown commands | Cancels the losers of a race scope and a loop's pending paths; stopping children is the host's | Different |

**The difference shows in a run.** An inclusive join inside a loop, with a parallel sibling branch that waits at a
parallel join downstream, faults in `Bpmn.Semantics` 0.5.0 with `bpmn.join.deadlock`: the sibling's token can reach the
untaken branch only round the loop, and the whole-graph search counts that path. The same process without the loop
finishes. BPMN 2.0's rule for the inclusive join leaves out a path that passes a node with a path to an arrived flow
(here the inclusive split), so the join should fire; Elsa's forward-only search exists for this case
(`FlowchartGraph`'s comment on it). Run (`smoke`: the case expects the fault, so an upgrade that fixes it shows). An
upstream issue in valence-works/bpmn would be the way to report it; none was filed.

**Verdict: a neutral token package is not real enough to extract.** What is truly the same is generic graph and id
code: the back-edge classification (about 35 lines in each), the breadth-first reachability (25 to 30) and
sequence-based ids (5), about 70 to 80 lines per engine, with no token or join type in it. Everything a join
coordinator decides (which nodes join, readiness, surplus arrivals, the iteration-key scheme, dead paths,
cancellation) differs by rule, not by name, and both persisted formats are fixed (Elsa's golden fixtures, the
library's payload format). A shared package would give `Bpmn.Semantics` a dependency on Elsa-owned code and let a BPMN
change move Elsa's behaviour, the two risks the design record names for keeping the Flowchart out of that repository.
Recommendation: record the engines as siblings in idea, not in rules; keep the two small graph algorithms as mirrored
copies that point at each other, as `FlowchartGraph` already does; if anything is shared, make it a set of
conformance scenarios both engines run (an inclusive join in a loop, surplus arrivals), which shares no code.
Confidence: high that no worthwhile shared runtime core exists.

## 8. Not done

- Reading BPMN XML: the processes are built in code with the library's builder; `Bpmn.Interchange` is not used.
- FEEL: conditions are the host's tiny language. Elsa needs an expression story for BPMN either way.
- Multi-instance, compensation, transactions, event subprocesses, the event-based gateway, signal and message start
  events, timer start events: the interpreter covers them, the spike does not (a reviewer ran an event-based gateway
  both ways on this host, and it worked; not in the mix). Escalation from a root is the interpreter's no-op.
- A job's BPMN error code: a service that fails names no code, so only catch-all error boundaries catch it.
- MongoDB, SQL Server and MySQL; the concurrent stress test with BPMN instances; the memory measurement.
- What the viewer shows of a BPMN instance (its tokens, from the journal's `j` entries and the state), and the state
  of an instance terminated from outside (F-B5).
- Migrating running instances to another definition version (the design record's open question 1).
- Trimming the set of taken events (F-B1), and the upstream issue for the inclusive join of section 7.
