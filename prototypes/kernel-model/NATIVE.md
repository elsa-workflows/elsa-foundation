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
`../../essentials/tests/ActorFoundation/Legacy/Workflows`; a file named below without a path is in one of them. Seeds, counts and costs are unchanged.

**Round 4, the graph engine (FINDINGS.md section 26).** The Flowchart runs on [the Graph engine](../../essentials/src/Workflows/Runtime/Graph), main's Flowchart engine
extracted as a library: paths, arrivals, dead paths and gateways (section 1); a Flowchart cost row (section 5); the
graph invariants G1 to G5, the workflow `n-graph` and four graph flaws in the simulation (section 6). An All join
after a decision fires now; it did not before (run, both ways).

Every claim says how it is known: **run** means a command below produced it; **reasoned** means nobody ran it.

| Path | What |
|---|---|
| [Native model](../../essentials/src/Workflows/Runtime/Native/Model) | The execution model and structural activities, including Flowchart |
| [Native activities](../../essentials/src/Workflows/Runtime/Native/Activities) | Activity authoring contracts, bindings and built-in leaf activities |
| [the Graph engine](../../essentials/src/Workflows/Runtime/Graph), `../../essentials/tests/Workflows/Runtime/Graph/Tests` | The graph engine (main's Flowchart engine) and main's conformance corpus against it (`dotnet test ../../essentials/tests/Workflows/Runtime/Graph/Tests -c Release`) |
| `../../essentials/tests/ActorFoundation/Native/Run` | Smoke tests, the live channel and HTTP host (`Live.cs`), and cost measurements |
| `../../essentials/tests/ActorFoundation/Simulation/NativeWorkflows.cs`, `../../essentials/tests/ActorFoundation/Simulation/NativeSim.cs` | Native instances in the simulation's mix, the native checks N1 to N7 and the graph checks G1 to G5, the model's and the engine's deliberate flaws |

```
dotnet build ../../essentials/tests/ActorFoundation/Native/Run -c Release
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll smoke
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost sqlite
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel;Database=kernel_native"
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll memory 3000
dotnet build ../../essentials/tests/ActorFoundation/Simulation -c Release
dotnet ../../essentials/tests/ActorFoundation/Simulation/bin/Release/net10.0/Kernel.Sim.dll --native --seeds 20000
dotnet ../../essentials/tests/ActorFoundation/Simulation/bin/Release/net10.0/Kernel.Sim.dll --native --seeds 5000 --from 500001 --actions 1000
dotnet ../../essentials/tests/ActorFoundation/Simulation/bin/Release/net10.0/Kernel.Sim.dll --native-mutations --seeds 20000
dotnet ../../essentials/tests/ActorFoundation/Simulation/bin/Release/net10.0/Kernel.Sim.dll --native --native-flaw InFlightInMemory --seed 1     # replay one, shrunk
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
If, Flowchart (on the graph engine of [the Graph engine](../../essentials/src/Workflows/Runtime/Graph): gateways, implicit dead-path joins), Parallel (threshold join), ForEach (iteration scope with
`CurrentItem`, `CurrentIndex`), While, Delay, Event, PublishEvent, DispatchWorkflow (with and without waiting),
HttpEndpoint, WriteHttpResponse, CallService (slow external work, `[Background]`), SendNotification (`[External]`),
and since round 5 C the two outbound activities that carry an effect key: SendHttpRequest (`[External]`; the key is the
`Idempotency-Key` header) and SendEmail (`[External]`; the key is the local part of the `Message-ID`).

**Effect keys (round 5 C).** `ActivityContext.EffectKey` is `EffectKey.For(instance, node, iterationPath, attempt)` of
`../../essentials/tests/ActorFoundation/Legacy/Workflows`: **`{instance origin}/{node}/{iteration path}/{attempt}`**, the owner's shape of 16 November,
from committed facts only. The instance origin is the instance id (fixed at creation; the key never reads partitions
or positions) with its creation. The node is the activity's stable id in the definition, the one the model already
uses. The **iteration path** (`ActivityContext.IterationPath`, `Burst.IterationPath`) says which activation of the
node this is: for every frame between the root and the activation, what its parent composite says the child's
iteration is (`CompositeActivity.IterationOf`, from the parent's committed state), root first, as `parent=iteration`
segments joined by dots, plus `node=retryN` for a retried activity's successor (its generation, from the committed
retry lineage); `-` when nothing repeats the node. ForEach gives the body's `CurrentIndex`; While counts its passes in
its own state (`WhileState.Iteration`, new in round 5 C); a Flowchart gives the engine's round (`head-2`) and the token
(`^m-ea`: the merge and the source node of the arrival, chained through every merge passed in the round; `^j#2`: a
join's firing ordinal in the round, from the Flowchart's committed `firings`), both carried on the child's tag; a
StateMachine gives the state and its visit serial (`B-4`, from the committed
`MachineState`, reached through the child's lexical scope); Sequence, If and Parallel run each child once and give
nothing. The attempt is 0, or 1 for an
External activity in Durable mode (committed before it runs, N4); it takes no other value, a Background job's is 0, and
a retry is a new generation in the path, never attempt 2. A Background job's path and key are computed in the turn that
writes its ticket and committed with it.

Why the frame number is not in the key (the owner's reason): in Balanced and Ephemeral the frame numbers are minted
from the committed `Next` in agenda order, and a replay over a longer inbox (a timer that became due meanwhile) orders
the agenda differently, so a frame number can land on another activity, or on another iteration of the same one
(the alias case of `../../essentials/tests/ActorFoundation/Native/Run/AliasChecks.cs`: a While body sending twice beside a sibling branch with Sets;
with the frame in the key, iteration 2 of the lost turn and iteration 1 of the replay shared one key). The iteration
path is independent of agenda order: one logical activation keeps one key across the lost turn and its replay, on
whichever owner, and two activations never share one. A renumbered frame therefore changes nothing: the key never named
it. No extra commit is paid. An activity puts the key where its receiver
looks for a repeat and returns the request as an `OutboundEffect` in its transition (`HttpSendEffect`,
`EmailSendEffect`); the model hands the effect to the host's transport (`NativeOptions.OnOutbound`, an
`OutboundSending`) inside the turn, before its commit, so it runs at least once. The kernel records no key and drops no
repeat: deduplication is the receiver's, conditional on its keeping the keys it has seen.

Three constructs repeat a node outside loops; each gets its number from committed state the same way (the owner's word
of 16 November: the node plus its loop and branch iteration numbers, which replay cannot reorder, and different
activations always get different keys). A Flowchart node forward of a **Merge** fires once per token within a round;
each arrival is identified by its source: the merge's activation appends `^merge-source` to the token of the completing
child that reached it, a plain successor inherits its predecessor's token, a new round starts afresh; the engine's path
ids are not used, since they are minted in mutation order. A Flowchart **join** (several inbound connections, not a
merge) fires once per round, or once per token when it lies forward of a merge, under whichever arrival releases it;
its firing gets its ordinal in the round from the Flowchart's own committed count (`^j#1`, `^j#2`; `firings` beside the
engine's state in the frame, as the While counts its passes), so the nth firing is the nth in the lost turn and in its
replay, whatever arrivals made it up, and the nodes after the join inherit that token; what the nth firing carries as
content may then differ between the lost turn and its replay, which the owner's rule accepts (the receiver keeps the
first sending). `AliasChecks` proves both replay-stable in all three modes: two event branches meet at a merge and the
Http behind it fires twice with `flow=^m-ea`, `flow=^m-eb`; a join forward of the merge, with both inbounds from the
merge or one from outside it, fires twice with `flow=^j#1`, `flow=^j#2`; the lost turn and its replay over the longer
inbox carry the same keys, and with the round alone each pair shared one. `n-out` carries the merge-join shape into the
mix, its two tokens released by two events, so the join fires in one turn or in two. A **StateMachine** re-enters states
on events; a state's activity is one activation per visit, named by the visit's committed serial (`machine=B-2`,
`machine=B-4` for a state entered twice), which `Statecharts.Run --effect-keys` witnesses in all three modes (the sim's
N7 runs underneath it). A **retry** is a new generation: `Burst.RetryGeneration` walks the committed lineage receipt by
receipt (the receipt that made this frame a successor, then the one that made its source a successor), so a second retry
of one activation is `leaf=retry2`, which `Native.Recovery.Run --effect-keys` (also in its default run) witnesses with a
leaf that faults twice and is retried twice, in Balanced and Durable, and shows the `FlatRetryGeneration` mutation
collapsing the second retry onto the first's key. `Native.Run variants <mode>`, `replay-variants`, `join-variants <mode>`
and `join-replay-variants` run the second and third review rounds' shapes (nested loops, loops in both branches, a
ForEach over a collection a sibling grows, a While whose limit a sibling raises, an exclusive split rejoining at a merge
inside a loop, the two joins, a timer loop beside an event loop, a join inside a loop, two joins in series, a join whose
outside inbound arrives last, a join whose arrivals a timer regroups) in all three modes.

Rules for whoever writes activities, composites or mix workflows against the key:

- **One key per activation.** `ActivityContext.EffectKey` is one key for one activation. An activity that sends in both
  `StartAsync` and `ResumeAsync`, or sends twice in one execution, must derive its own sub-keys from it (`key + "/1"`,
  `key + "/2"`); the built-in `SendHttpRequest` and `SendEmail` are leaves with one effect each.
- **An authored composite that schedules a child more than once must override `IterationOf`** with a number from its
  committed state; the default is null, and two activations of the child would then share a key. Sequence, If and
  Parallel schedule each child once and need nothing; ForEach, While, Flowchart and StateMachine override it.
- **A StateMachine's control children** (`StateMachineControl`, scheduled with a tag and no lexical scope) have no visit
  segment; they are internal and authored activities do not run from them.
- **The mix** must use workflows whose key set does not depend on event or timer order (a join whose outside inbound may
  arrive last, a While whose limit a sibling raises, a ForEach over a collection a sibling grows are out), since N7's
  identity half compares against one fault-free run that takes every available event in one turn; `n-join2` and
  `n-graph` are excepted for the same reason, and none of them sends.

What changed, and why:

| Change | Why |
|---|---|
| Activities never see a store, scheduler, stager or another instance. What they want from the world is a **trigger** in Suspend (`EventTrigger`, `TimerTrigger`, `ChildTrigger`, `HttpTrigger`) or an **effect** on the transition (`PublishEffect`, `DispatchEffect`, `EmitEffect`) | The model turns each into one kernel call. That is what removes every leak (section 3) |
| `Transition` is not generic; the helpers serialize the typed result | One type fewer; the result goes into JSON state anyway |
| Composite inputs are evaluated at every activation (Execute and every callback) | While's condition is read again after each pass without Elsa 4's rematerialize marker. ForEach copies its items into its state, so a changing variable does not change a running loop |
| A stateful activity's inputs are evaluated again on resume, not taken from a pinned snapshot | No snapshot to store. The activities that wait keep what they need in their state |
| Sequence is stateless: the next child follows from the position of the one that completed | Smaller state |
| `If` children fill named slots (Then, Else); its outcome is True or False either way | The same activity works inside a flowchart |
| The Flowchart runs on the graph engine ([the Graph engine](../../essentials/src/Workflows/Runtime/Graph), main's Flowchart engine): paths, arrivals, dead paths. A node with several inbound connections is a join decided by the dead-path join (main's default); gateways (`Gateway`) are explicit; a first-wins loser is cancelled. A run starts at one node, main's: the pending start trigger when it is a child of the Flowchart, else the start node (the first child without inbound connections); other unconnected children do not start, and a trigger below one of them is not reached (main refuses that placement when a workflow is published; the prototype has no publish step). Unlike main, the trigger counts only while it is undelivered (a Flowchart run again by a loop starts at the start node), and no node carries main's trigger flag, so loop-closing connections are classified from the start node only | Before, without dead paths, an All join after a decision never fired and the flowchart completed without it (run: `smoke`, "an All join after a decision", then 'left no no', now 'left yes yes') |
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

### HTTP over the public Actor Foundation

From `prototypes/kernel-model`, run:

```bash
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Simulation -- --public-http-edge-check
```

This check starts Kestrel on a dynamically assigned loopback port and runs actual HTTP requests against the
public Actor Foundation with an in-memory Embedded owner. It starts a Native approval, pauses its first
checkpoint to check that the response stays held, and resumes it with a JSON request. Public reads confirm
the same actor at its first and second journal revisions. An unmatched HTTP event with `KeepFor`
returns 404 and leaves no publication, delivery or caller registration. Retrying the same event ID
after the wait commits delivers the new payload. The check also covers 503 on owner reset, 504 at the
reply deadline, 500 when a held response's turn parks, and completion after the client disconnects. Run with `--flaw EmitBeforeCommit` to make the held-response
assertion fail before the checkpoint lands.

The companion check keeps the HTTP receiver live while its remote logical owner is lost:

```bash
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Simulation -- --public-http-remote-check
```

Two Worker identities share the in-memory store and deliver replies through an in-process transport. The
receiver opens a real loopback HTTP connection. It verifies the persisted reply address, then takes the
partition at a higher epoch with a second Worker. A loss before commit returns the successor's response
through the same connection when the deadline is open and returns one 504 when the deadline has passed.
A loss after the commit lands and before any reply crosses the transport returns one 504 at the deadline.
Every schedule finishes the same approval at journal revision 2. The fixture disconnects the lost
owner's reply link and keeps its Worker available to check the late commit. Independent process loss,
PostgreSQL-backed HTTP and cross-tenant routing have separate K8 acceptance cases.

### Held direct replies on PostgreSQL

Use a fresh dedicated PostgreSQL database for each fence mode and set `KERNEL_PREVIEW_POSTGRES`. From
`prototypes/kernel-model`, run:

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Native/Run -- pg-root-held-direct checked
```

Use `in-batch` for the other owned fence. Append `after-deadline` to hold the emitted reply in the
fixture link until PostgreSQL time reaches the registered deadline. The receiving edge then records a
timeout while the workflow retains its committed revision.

The fixture-private protocol 10 admits a bound live request to
an already-checkpointed root and stores its reply address with the exact direct input. It rejects another
tenant and an expired registration. A second Worker takes at a higher epoch and consumes that same
input through a Native approval, with the receiver still waiting immediately before commit. The
transport checks the committed checkpoint and settled input on another connection when the response
is sent. The first reply carries the original registration and tenant. Its input settles without retaining the route, and replay
with a new registration returns Duplicate. The ordinary owner, checkpoint reader and pending-input
reader refuse the new format.

Both owners run in one process, with an orderly handover and an in-process reply link.

### PostgreSQL HTTP approval preview

With `KERNEL_PREVIEW_POSTGRES` pointing to a fresh dedicated database, run from `prototypes/kernel-model`:

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Native/Run -- pg-http-preview in-batch timely
```

The Native runner starts an actual loopback Kestrel host on a dynamic port and holds a real HTTP
connection through the public `IIngress.RequestAsync` surface. A tenant-bound direct approval
is stored against the exact actor. A second Worker takes at a higher epoch and commits one
revision. The fixture checks the committed state on a separate connection as the reply is sent. The
original HTTP connection receives `202 approved`. The other tenant's live reference returns 404 without
adding input. An identical operation with a changed body never gets attached to the first
registration and reaches its own 504 deadline. The approval's state and journal are unchanged.

Use `checked` for the other owned fence mode. Use a fresh database for each run. Replace `timely` with
`late` to hold the live reply through the deadline and observe HTTP 504 with the approval committed.
Use `disconnect` to close the actual HTTP connection before the handover and see the admitted
approval complete. Use `slow-body` to send the valid JSON across the route deadline, or `blocked` to hold
the target row until the edge has returned 504. Both cases verify no input was added, then run a normal
successful approval. Before listening, the loopback preview samples PostgreSQL time and retries any
sample whose round trip exceeds 25 ms. The HTTP budget begins at handler entry and incoming replies are
checked against current PostgreSQL time. The edge can return 504 while a canceled database call unwinds.
Use `clock-delay` to discard a delayed startup sample, then delay admission. The fixture brackets the
stored deadline against a separate PostgreSQL observation and still requires HTTP 202. Each run also verifies that public timeout replies include the
committed Running or Finished status when the caller waits for that provider result.

This preview uses fixture-private protocol 10 and two Workers with orderly handover in one process.
It enables the public request contract for exact actor references inside the opt-in PostgreSQL
preview.

### PostgreSQL HTTP across abrupt owner loss

Use the same setup with a fresh PostgreSQL database per run:

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Native/Run -- pg-http-owner-loss in-batch before
```

A Kestrel receiver keeps the original connection open while a separate owner process waits with the
Native approval staged. A standby process is refused while the first lease is live. The driver ends
the original process, and PostgreSQL records ownership moving to the next epoch after the lease
expires. The survivor consumes the exact stored approval, commits one revision and sends the response
to the receiver through a private process pipe. The transport reads the finished checkpoint and the
settled approval row before emitting the carrier. The caller receives `202 approved`.

Use `before-late` to let the caller reach 504 first. The survivor still commits the approval. Use
`after` to end the original owner after PostgreSQL commits and before it sends a response. The
caller receives 504, and the standby takes ownership without executing the approval again. Both 504
cases record when the client received its response against the stored deadline and lease expiry. The
fixture watches the standby through shutdown for any trailing carrier. Use `checked` for the other
fence mode.

These cases run against the fixture-private exact-reference profile. The pipe carries live output
between local processes. Cluster transport, HTTP-triggered starts and event requests remain separate
K8 cases.

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
| a background job or a 20 ms Delay before the response (case 3) | Ephemeral | | **0** (6 / 4 before round 4) | 0 |
| a background job or a 20 ms Delay before the response (case 3) | Balanced | | 6 / 4 | 2 |
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
`../../essentials/tests/ActorFoundation/Simulation/Models.cs` is the kernel without a model's work):

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
| N2 | A join fires once | No `j:` entry twice. A Parallel joins once; a Flowchart join fires at most once per scheduling id in a round, so a join behind a merge may fire once per token (narrowed in round 4: N2 no longer holds a Flowchart join to once per round; the design sketch's G2 does, below) |
| N3 | A child is started once and its parent resumes once | A child id is started by one frame, a frame starts one child, a child wait is resumed only by its own child and once; at the end every waited child of a finished parent resumed it exactly once |
| N4 | A non-replay-safe activity's attempt is committed before it runs in Durable mode | When an External effect happens, the committed journal holds its attempt |
| N5 | Final variables and outputs match a run without faults | Every finished native instance (children too) has the variables of the same definition and input run alone, without faults; everything started from outside finished or was cancelled by request |
| N7 (round 5 C) | An outbound effect carries a stable effect key; one key, one logical activation; a key never leaves in two turns that both landed | Four halves. At the host's transport (`OnOutbound`): the HTTP request carries the key in `Idempotency-Key`, the email as `<key@domain>` in its `Message-ID`. At the same place, the lost-turn half: an activation is named by its node and the content it alone determines (the mix's rule: bodies name the iteration; `NativeWorkflows.ContentNamesActivation` lists the nodes, `hj` fires twice with one body and is out), and that name has one key in every sending, in a turn that lands or in one that is lost, so a lost turn that sent another activation's valid key is seen here and nowhere else. At the commit (`Wire.Commit`, `World.Landed`): the sendings of the turns a batch writes ride on it, and the store records them the moment the commit lands, whatever becomes of the answer on the wire (an answer that arrives late, after the owner's lease ran out and it took the partition again, gives nothing back to the pending list: the landing is recorded and the take discarded the memory the call came from; a stale refusal, from a call that outlived a take, leaves the new incarnation's pending sendings alone, since the take is the one place that discards them; `Kernel.Sim --landed-check` replays the three cases); a second sending of a key is a redelivery only while the first one's turn did not land (its commit lost, refused, or never made because its owner died), so a key that left in two turns that both landed, or twice in one, is two activations under one key, whatever the reference run says. At the end, against the fault-free reference run of the same definition and input (an oracle that takes no identity from the sending's own node, path or frame fields; it compares the key string stripped of its instance origin and attempt with the keys the reference minted on its own): the reference sends every key once, and every key an instance sent is one the reference sends. What N7 cannot see: a collision whose two sendings never both land and whose node is not in the content list (an instance that ends in memory without a write, a turn lost before its commit); a resend's content, which is not compared (the receiver keeps the first); a moved key that equals another activation's reference key when that activation never sends in the same run and its content does not name it; and nothing at all for a mix workflow whose key set depends on event order, which is a false red and is kept out of the mix. The sweep counts the same-key resends, the frame numbers a replay handed to another activation and the nodes sent from under a second iteration path; none of the counters is a gate. `n-out` (a While and a ForEach sending twice from one node beside a timer branch, and two event branches meeting at a merge that forks again to a join) puts those cases in the mix |

The graph invariants of the design sketch (round 4, `../../essentials/tests/ActorFoundation/Simulation/NativeSim.cs`: `NoteGraph`, `CheckGraphStates`). They read
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
| 300 actions | 20,000 | 0 failed; 133,483 finished native instances matched a run without faults (round 4, with `n-graph` in the mix and the graph checks on) |
| 1,000 actions | 5,000 (from 500001) | 0 failed; 97,388 matched; 2 instances parked by the kernel's poison rule as innocent bystanders (FINDINGS section 7, rule 3); the checks skip parked instances |
| 1,000 actions | 20,000 | 0 failed; 386,774 matched; 14 parked |
| Without `--native` | 20,000 | 0 failed: the native and graph changes did not disturb the kernel's invariants |

The checks bite (run: `--native-mutations`, each flaw alone, first failing seed):

| Flaw | What it breaks | Caught |
|---|---|---|
| `InFlightInMemory` | Durable: the executed item leaves the agenda in memory only | N5, seed 1 (round 4; N1, seed 2 before `n-graph` moved the seeds): `0/j69 (n-loop) ended with i=4 ... sum=189; without faults it ends with i=2 ... sum=63`. Shrunk to 9 actions |
| `JoinWithoutGuard` | Parallel without its joined flag, and the join does not cancel the other branches | N2, seed 1: `1/j53: the join f2 fired twice` |
| `ResumeAnyChild` | A child-finished resumes every child wait | N3, seed 3: `the wait for child 0/j26~1.1 was resumed by child 0/j26~1.0` |
| `NoAttemptCommit` | Durable runs an External activity without committing its attempt first | N4, seed 18 |
| `ScopeInMemory` | The iteration scope is kept in memory only | N5, seed 1: `sum=53; without faults it ends with sum=63` |
| `NoDeadPaths` (round 4) | An untaken connection emits no dead arrival | G3, seed 2: `the join end of graph fired in round head:2 before no arrived, live or dead` (the graph went quiet and the engine released the join without the decision's untaken side) |
| `NoRoundReset` (round 4) | A loop-closing connection opens no new round | G2, seed 2: `node a of graph fired twice in round -` |
| `DeadPathRunsNode` (round 4) | An all-dead node is run instead of passing the deadness on | G1, seed 2: `a dead path ran node race of graph (round -)` |
| `CancelLeavesPaths` (round 4) | A first-wins race completes without cancelling its losers | G5, seed 2: `path path:280 at r1 of graph lives on in the race scope scope:275, which is over` |
| `UnstableEffectKey` (round 5 C) | The effect key is minted once per turn instead of derived from committed facts: consistent within the turn, so the carrying check passes, and another key on a resend | N7 (the key moved), seed 2: `1/j46 (n-out) sent m1/-.<n>, which a run without faults never sends: an activation's key moved` (the minted suffix differs from run to run) |
| `NodelessEffectKey` (round 5 C) | The key names no node, so two activities in one scope share one | N7 (the landed half), seed 1: `1/j8: the key 1/j8/node/-/0 left twice in one landed turn` (the reference run's duplicate, `node/-` twice, catches it at seed 3 when the landed half is removed) |
| `PathlessEffectKey` (round 5 C) | The key has no iteration path, so two iterations of one activity share one | N7 (the landed half), seed 2: `1/j46: the key 1/j46/h1/-/0 left twice in one landed turn` (the reference's duplicate `h1/-` catches it at seed 2 as well) |
| `TurnLocalJoinOrdinal` (round 5 C) | A Flowchart join's firing ordinal is counted per turn in memory instead of from the committed `firings`, so the join's second token, arriving in a later turn, fires as `#1` again | N7 (the landed half), seed 2: `1/j46: the key 1/j46/hj/mj=^hj#1/0 left twice in one landed turn` (both tokens arrived in one turn, each fired `#1`); the reference run, which takes both events in one turn, sends `^hj#1` and `^hj#2` and cannot see it |
| `FlatRetryGeneration` (round 5 C) | Every retry successor is generation 1, so the second retry of one activation shares the first's key | Not caught by the sweep: the mix has no retry, and the gate allows it (`--allow-uncaught FlatRetryGeneration`). `Native.Recovery.Run --effect-keys` shows it bite: `leaf=retry1` twice where the sound model gives `leaf=retry1`, `leaf=retry2` |
| `FreshTurnSwapsLoopKeys` (round 5 C) | A turn that is not a redelivery swaps a While's first two iterations in the key; the replay after a lost first attempt derives the right keys, so the wrong, valid keys live only in turns that never landed | N7 (the lost-turn half), seed 269: `0/j17: h1 with '"i=1"' was sent under two keys, 0/j17/h1/loop=2/0 and 0/j17/h1/loop=1/0: a sending used another activation's key`; the first attempt was lost, its replay sent the right key, and no other half can see a key that lands once. Since K1 slice 7 it is first caught at seed 3589 (`2/j13`, the same check): the set worlds, with four partitions and one worker per node, make a lost first attempt of a While's early iterations rarer, so the PR gate runs the native mutations at 12,000 seeds, over three times that |

The four graph flaws are the engine's (`FlowchartOptions.Flaws`, marked MUTATION in [the Graph engine](../../essentials/src/Workflows/Runtime/Graph)), the four the
design sketch's plan names; the mutation check fails one that another check catches first. G4 has no mutation of its
own: no flaw of the plan aims at it, so that it bites is not shown. The round-4 seeds are those of the mix with `n-graph` in it, which
moves every seed; the five older flaws' first seeds and messages above are round 4's (`InFlightInMemory` is now caught first by N5, not N1).
Round 5 C put two more activities in `n-seq` and `n-out` in the mix, which moved them again (run, 10,000 seeds):
`InFlightInMemory` N5 at seed 1, `JoinWithoutGuard` N2 at seed 1, `ResumeAnyChild` N3 at seed 1, `NoAttemptCommit` N4 at
seed 4, `ScopeInMemory` N5 at seed 1; the four graph flaws at seed 1, each caught by its own check. The gate pins 15
native mutations, one of them allowed to escape the sweep (`FlatRetryGeneration`, above); the two effect-key witnesses the
sweep cannot reach (`Native.Recovery.Run --effect-keys`, `Statecharts.Run --effect-keys`) run in the workflow's sim step. The three key-shape flaws are
judged against the fault-free reference run, which carries the same flaw: a key the reference sends twice is a collision,
a key the run sends and the reference never does has moved; the join-ordinal flaw hides from that reference and is caught
by the landed half.

Found while building the checks (run): one seed of 1,000 actions failed my own event check because a parked
instance kept its subscriptions (seed 500963); at that stage the check skipped instances that did not run. Round 4 met the same
in the kernel's I8 (seed 2282 at 1,000 actions, once `n-graph` shifted the seeds), which initially exempted an instance the
poison rule parked while its faulted row existed. The owner's 13 November decision now removes active subscriptions
when Park faults the instance; I8 requires that cleanup and retains its saved waits only as inactive checkpoint data.
Also found: in Durable mode a cancel recalled a fired
Delay's timer message that an earlier turn had consumed, because the resume still waited in the agenda (I9, seed
8486); a timer whose message has arrived is no longer recalled. And a threshold join
is not deterministic in which branch activities complete: three events in one inbox complete all three Event
activities before the join cancels the third branch; one by one, the third is cancelled while it waits. Variables
agree either way, so for that workflow only the variables are compared; and for `n-graph` (round 4), where which racer wins depends on the run.

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
  held by a lost owner; the kernel knows (`Delivery.Suspect`) and could put it on the message. Run. The later decision
  makes the exposed value an observed-history hint, not proof that an unflagged turn has never run; see [LEASE.md](LEASE.md).
- **F-N7. Ephemeral is lost at the first wait.** Scenario: HTTP case 3. A 20 ms Delay or a background job before the
  response is a message, so the instance commits twice (4 to 6 round trips) where it would otherwise cost zero. Short
  timers kept in memory for an instance that is not written would keep request-response workflows at zero. Run.
  Since round 4 no longer a friction (decision 4, FINDINGS 25.6): case 3 in Ephemeral mode costs 0 database calls.
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

## Detached creator handles

A Native detached dispatch through the public contract returns the child's exact reference. Its creator is still
inserted atomically with the parent's turn. Public recall accepts an issued self-timer handle, so the parent has
no operation that can use the detached creator's internal ID. New public detached starts leave that ID out of the
parent checkpoint. The rule applies while the creator is pending, so it needs no check of the child's inbox.

The authored check leaves the first child creator pending, opens a successor, and finishes the child. It
dispatches a second detached child and an attached child, then sends a qualified terminate to the parent.
Both detached children complete at revision 1. The attached child remains registered and receives the cascade.
Run the check in Balanced and Durable from `prototypes/kernel-model`:

```bash
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Simulation -- --native-detached-handles-check
```

The terminal projection still counts every handle carried in a checkpoint. Older handles stay stored because the
checkpoint lacks the metadata to distinguish a detached creator from an attached creator. If repeated growth
parks an older parent, inspect the Park and its retained inputs before retrying. Retrying the same operation
against the same checkpoint can reach the bound again. An authorized operator can discard the selected Park and
all its retained inputs, or terminate the selected instance through the existing qualified command. Discard leaves
the instance faulted. The provider-encoding and near-limit termination cases retain their own checks.

### Termination at the checkpoint limit

Run the authored Native envelope check from `prototypes/kernel-model`:

```bash
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Simulation -- --native-terminal-envelope-check
```

In Balanced and Durable, the real model opens 3,500 invocation inboxes, attaches one
waiting descendant and emits three sets of 500 detached creators. The stopped-owner
fixture restores the old `CustomHandles` representation from those issued IDs while
preserving the authored checkpoint. The successor runs all growth and termination through
the built-in Worker and fenced in-memory store. Growth above the terminal budget parks.
TG-1(c1) uses the current public header reserve to derive the no-handle bound. The
historical 16,416,460-byte state predates that reserve. Adjacent controls reject one
byte above the admitted limit, including the protected invocation shape. The exact-fit
and protected ThenDelete cases keep all inbox identities, close them and commit one
bound descendant terminate with the final parent notice. The protected case returns
`terminated and kept` and preserves the invocation receipt. TG-1(c1) sends a provisional
notice when it parks. The later parent-bound terminate cascades while preserving the
faulted checkpoint.
The cases exercise the in-memory encoding. Physical-provider qualification stays at K4.

### Termination before the first upgraded turn

```bash
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Simulation -- --native-version-terminate-check
```

The old-writer fixture saves a canonical Native checkpoint stamped as v13 with an open
invocation inbox, one queued receipt and a bound child. The v14 model loads it at the
qualified terminate, before any ordinary turn, and closes the inbox with the retained
terminal receipt. The canonical fixture has no v14 job recipes and its bytes are
stable on upgrade. The state-growth projection also reserves any positive UTF-8
serialization difference when the model has terminal metadata to write. A state-only
model's header-only Suspend retains its old checkpoint and normal model growth budget.

A separate test model deliberately expands the v13 state by more than the terminal
reserve. An owner explicitly composed with the workflow actor type and no model
registered can terminate the existing row without rewriting its version or receipt.
The bound child is cancelled and protected ThenDelete keeps the checkpoint. This is the
existing unavailable-model path. Shipped deployment controls and physical-provider
envelopes have separate qualification.

The PostgreSQL format-11 fixture checks both a readable upgrade and the explicit
unavailable-model fallback. It stages the deliberately expanded terminal state against
the real writer and verifies atomic refusal. A later owner scoped to that one creation
runs the original Terminate and preserves the previous physical state and header. The
adjacent actor resumes under the normal owner after the fallback owner releases the partition.
Set `KERNEL_PREVIEW_POSTGRES` to a fresh dedicated database and run from the repository root.

```bash
dotnet run -c Release --project essentials/tests/ActorFoundation/Native/Run -- pg-root-terminate owned checked fallback
```

New live format-11 checkpoints reserve space for the fully encoded terminal header.
An older checkpoint above the projected cap may settle without increasing its projection.
It can use as much of the header as the physical cap allows. The provider fixture proves the boundary with an escaped state, 16 KiB of `TypeData`
and four labels whose keys and values each use their full limit. Run the EF store
conformance command `postgres <connection> root-checkpoint-limit --terminal-header`
on a disposable PostgreSQL server.

The opt-in PostgreSQL format 12 stores opaque root state in the existing lossless
binary encoding. It preserves null, empty, NUL, supplementary Unicode and unpaired
UTF-16 units. The encoded body has the same byte count as the Foundation's UTF-8
budget. Live writes reserve the declared terminal body growth and maximum encoded
terminal header. Terminal writes check their actual encoded size. The body, header
and state transition share the fenced commit. Format-11 owners and readers refuse
format 12. A format-12 reader verifies the store marker before selecting the body
column. The Core Preview continues to use its current format.

Use `--stable-body` with the store conformance command above. For the live Native
termination replay, use `pg-root-terminate owned checked stable-body`. Use
`stable-body-fallback` to verify that an unavailable-model termination keeps the
saved binary body and header byte for byte.

Format 13 retains the binary body. Its point and list reads issue a terminal selection bound to the tenant,
exact generated reference, terminal revision and terminal time. The terminal mark
and the public header are projected together in the same transaction as the
checkpoint. Disabling the projector refuses reads and rolls back checkpoint
writes. An exclusive setup pass can rebuild the complete projection
from committed checkpoints. Its first-checkpoint and takeover fixture runs with
`pg-root-terminate owned checked terminal-selection`. Retention and delete command admission
still need their own qualification. The ordinary Core Preview keeps its existing
read layout.

Format 13 also issues a running selection for the committed generated creation.
A host can open the selected ingress with permission for the exact actor. The
public workflow runtime submits that selection with Terminate. The trusted host
authorizes the target. PostgreSQL checks the selection, format and current
checkpoint before writing the operator occurrence and receipt together. A duplicate to the same actor
returns its original receipt. Using that operation ID with another actor under
the same tenant is refused. The fixture submits through the public contract, then
delivers under a later epoch. The command path supports a one-way Terminate.
Reason text, ThenDelete, held replies and cascades keep their own qualification.

The opt-in format-14 retirement fixture sends a qualified Delete to an ended
generated root through the host and real Worker. The model's deletion protection
receives the committed terminal time. A successful fenced retirement keeps the
identity marker and deduplication window, removes the public projection and
physical state, clears pending payloads, and replaces that actor's history with
one deletion marker. Ownership, checkpoint reads and retirement use the schema
of the same format marker. The commit verifies that the projection and payloads
are gone and that the deletion marker was inserted. The fixture also exercises
same-owner deletion, original receipt replay and complete rollback when a store
write is suppressed. The runnable fixture is
`pg-root-retirement owned checked` against an empty PostgreSQL database.

The opt-in format-15 fixture adds a selected Terminate, then delete command.
The host authorizes termination and deletion for the same generated reference.
The worker applies the command in one fenced commit. An eligible running
root retires with one new terminal revision and one deletion marker. Native
receipt protection can keep the cancelled checkpoint and its terminal journal.
A command admitted while the root was running can also retire it after an
earlier Terminate, preserving that terminal mark. Build Native.Run in
Release with a locked restore and run `pg-root-then-delete owned checked`
against an empty PostgreSQL database. The test seeds a receipt in Native
state and uses the real Native cancellation and protection logic.

The opt-in format-16 retention fixture runs the existing WorkflowActor retention
pass over the public PostgreSQL actor projection, one page at a time. The public
workflow list and retention selection read the persisted definition label and terminal
time after the owner state wire, header, terminal and journal lookups are disabled.
The fixture verifies those owner lookups fail and that both public read paths seal the
actual terminal revision and end time. The owner schema is restored before the fenced
retirement pass. A running workflow also exposes the source definition, model, installed
state version and mode used by migration selection. The migration runner and production
list routing retain their own K9 acceptance. The trusted host admission derives a
reserved operation id from the exact tenant, generated
creation and terminal mark. An interrupted scan can replay its admission
receipts with newly sealed selections. The worker uses the policy in force at
delivery and checks model protection against the committed checkpoint before
it retires an eligible root. Build Native.Run in Release with a locked restore
and run `pg-root-retention owned checked` against an empty PostgreSQL database.

The `pg-root-plan-selection owned checked` fixture uses the separate PostgreSQL
generated-root timer profile. The real migration operator selects from projected
workflow headers while the owner body and header are unavailable, and admits a
signed control. The plan actor validates the control and the PostgreSQL worker
commits the selected references and cursor to its Full journal. A second plan
records an empty page and an opaque continuation. A fresh operator and public
PostgreSQL reader recover the cursor and report the exact eligible reference as
pending. The timer profile refuses ephemeral starts and excludes roots awaiting
their first turn. Separate header guard controls cover missing versions, ephemeral
workflows and other actor types. The fixture covers selection for FC-C4. Production
host composition, PostgreSQL `Migrate` dispatch and target application retain
their own K9 and K11 acceptance.

The opt-in format-17 fixture holds a selected operator request through
`IIngress.RequestAsync`. Admission stores the tenant-bound reply address with
the exact command. A fenced commit binds the response or refusal to that
envelope. If retirement settles another queued held operator, the transaction
locks that envelope and returns its registration to the Worker for a live
`missing` result. The preview admits at most 64 pending held operators per
root. Duplicates can still replay their receipts, and one-way operator
admission uses its existing rules. The
fixture observes plain Terminate, standalone Delete, Terminate then delete,
protected Delete, competing operators and rollback followed by takeover.

The host starts the request deadline on a monotonic clock anchored to a
bounded PostgreSQL startup sample. Admission I/O uses the same deadline.
A stalled admission acknowledgement cannot delay a timely reply or a timeout.
After a known admission, a bounded read can attach the exact creation's
committed status observed when resolving the timeout. The fixture does not
establish historical status at the deadline for a transaction crossing it.
An expired reply is dropped even when the operator later commits. Replies
use the live fixture transport and can be lost on process death or an
unknown commit outcome. Build Native.Run in Release with a locked restore
and run `pg-held-operator owned checked` against an empty PostgreSQL database.

### PostgreSQL pending inbox inspection

`IActorReads.InboxAsync` returns pending direct inputs and admitted operator
commands on direct-input root profiles. The page binds to the tenant and
generated actor reference. The issuer and inbox sources must share the marker's
schema. Each page reads its own committed metadata and database time together
with that format marker. The index orders each root's occurrences by
delivery sequence and physical occurrence. Continuations and handles are
opaque. Handles remain stable across reader and owner changes.

The page reports the original public id, kind, due time, attempts and the
stored byte count of the identifier, kind, framing and payload. The message
body and reply address stay in the database. Consumed deduplication rows and
retired roots are absent. The fixture includes a future input, tied positions,
cursor scope, a held operator and retirement. Run `pg-root-inbox owned checked ordinary`
or `pg-root-inbox owned checked operators` from the locked Release build of
Native.Run against an empty PostgreSQL database, and `pg-held-operator owned checked`
for the held operator. Use another database for each writer and fence combination.
Park, recovery handles, job inboxes and self-send inboxes retain their own cases.

These profiles qualify generated roots with no attached work. Descendant and
job cascades, parked work, retention scheduling and production reply transport
have separate acceptance cases.

### PostgreSQL first Park opening

The opt-in protocol 19 qualification runs a public actor that throws while
handling a direct input. One fenced transaction keeps its prior checkpoint
body and public header, updates the status and fault, records the Park time
and revision, keeps the failed occurrence with its full envelope, removes
the actor's subscriptions and writes the Park journal entry. An ordinary
receipt retry still returns its original admission. The sweep preserves the
kept occurrence after its receipt expires, and a later owner leaves queued
input pending.

Run `pg-root-park-opening owned checked` from a locked Release build of
Native.Run against an empty PostgreSQL database. Repeat with each writer
and fence mode in its own database. The fixture uses a generated root and
direct input. The next provider increments cover fresh arrivals while
parked, public inspection and recovery controls, and composition with the
Workbench preview.

### PostgreSQL Park inspection

Protocol 20 adds Park time and fault to the projected actor header in the
same transaction as an opening. The public point and list reads show Park
while the authoritative owner body and header are unavailable. The list
can filter parked and unparked actors. Inbox pages distinguish a kept
occurrence from pending input and retain the ordinary tenant-bound cursor.
They expose the delivery metadata and size while the full payload stays in
the provider.

Run `pg-root-park-inspection owned checked` from Native.Run against a fresh
PostgreSQL database. The fixture drives a real opening first, refuses an
opening with its projection trigger disabled, and reads the result after
a new owner takes over. Recovery controls and authentication of Park
selections have their own increments.

## 10. Private durable lexical-resource probe

The [lexical runner](../../essentials/tests/Workflows/Runtime/Native/Scopes/Run/README.md) explores generic scopes whose values survive separate ordinary activity phases. Resources, ownership and optional work/frame references live in the existing native checkpoint; the existing variable/expression/capture paths resolve them between nearer ordinary frame scopes and outer scopes. It adds no actor, store, semantic queue, writer or value interpreter.

Native model state version4 records this private capability. Older1–3 no-resource checkpoints, including genuine JSON-null checkpoints from an initial Park, keep their prior meanings. Unsupported/mixed/dangling/cyclic shapes are refused; reader fixtures do not qualify a historical writer, mixed-binary deployment or provider. Owner completion, cancellation and ordinary fault disposal release resources; an absorbing parent can continue its siblings. Forced kernel termination retires the new handles/resources while retaining other existing forensic state. A technical Park retains its previous checkpoint under the existing policy; this mechanism adds no incident retry or terminal resume.

This is non-packable M0 evidence for #3815/#4229. Attaching scopes to actual StateMachine visits, normal trigger work, Action/Entry initialization, shared source waves, recovery, compensation and product/Studio integration remains required. It does not approve a product scope schema or stronger variable isolation/secret retention. Existing cost figures above are historical; lexical workflow/provider costs have not been measured.
