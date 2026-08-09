# Execution model comparison: Elsa 4, Zeebe, Camunda 7, and the BPMN specification

Date: 2026-08-09
Status: research report. No implementation. Recommendations are proposals, not decisions.

## What this is

A structural comparison of four execution models, aimed at one question: where is Elsa 4's design
genuinely good, where is it accidental, and what should change.

Everything asserted about Elsa is grounded in a file path or an ADR in this repository. Everything
asserted about Zeebe, Camunda 7, and BPMN is grounded in their published documentation, linked
inline. Where I am speculating, the sentence says so.

Sources read for Elsa: ADRs [0020](../adr/0020-runtime-checkpoint-commit-post-commit-work.md),
[0029](../adr/0029-runtime-execution-flows-through-the-pipelines.md),
[0030](../adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md),
[0031](../adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md),
[0032](../adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md),
[0047](../adr/0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md),
[0063](../adr/0063-bpmn-moves-to-a-host-agnostic-library.md); the runtime scheduler, drainer, queue,
pipeline dispatcher, resumption pump, and distributed placement leaf; the Flowchart and BPMN activity
modules; [spec 114](../../specs/114-concurrency-throughput-instrument/research.md) for measured
concurrency behavior. For Elsa 3, `~/Projects/Elsa/elsa-core` at `release/3.8.0`, ADRs 0005 and 0007.
For the extracted BPMN core, `github.com/valence-works/bpmn` (`Bpmn.Semantics`), its ADR 0002.

---

## 1. The four models in one page

**Elsa 4.** Durable, message-driven work items are the source of truth. One command envelope enters
`WorkflowSchedulerCommandRouter.ProcessAsync`
([src](../../src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerCommandRouter.cs)), is enqueued on a
per-execution queue, and drives a *drain to quiescence* for that one workflow execution.
`WorkflowSchedulerDrainer.DrainAsync`
([src](../../src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs)) claims the FIFO head,
dispatches it through the runtime pipeline, acks it, and repeats until the queue empties or the
execution reaches a terminal status. Durable state advances at checkpoint commits whose cadence is
decided by policy (ADR 0032), and post-commit work is recorded as outbox intents rather than
dispatched inline (ADR 0020). A background `RuntimeResumptionPumpTask`
([src](../../src/Elsa/Workflows/Runtime/Resumption/RuntimeResumptionPumpTask.cs)) re-drives anything
the in-process path dropped.

**Zeebe / Camunda 8.** Each partition is "a persistent stream of process-related events"
([partitions](https://docs.camunda.io/docs/components/zeebe/technical-concepts/partitions/)). A single
stream processor per partition consumes commands, validates them against RocksDB-held state, applies
them, and writes events plus *follow-up commands* back onto the same stream. Camunda states the design
goal directly: "Zeebe achieves this by also writing follow-up commands to the stream as part of the
processing of other commands"
([internal processing](https://docs.camunda.io/docs/components/zeebe/technical-concepts/internal-processing/)),
which is how it avoids distributed transactions. Actual work leaves the engine entirely: job workers
poll for jobs ([job workers](https://docs.camunda.io/docs/components/concepts/job-workers/)).
Read models are built by exporters reading the log
([exporters](https://docs.camunda.io/docs/self-managed/concepts/exporters/)).

**Camunda 7.** Database-backed, with the transaction as the primary abstraction. "The transition from
one such stable state to another stable state is always part of a single transaction, meaning that it
succeeds as a whole or is rolled back on any kind of exception"
([transactions](https://docs.camunda.org/manual/latest/user-guide/process-engine/transactions-in-processes/)).
Authors insert extra transaction boundaries by hand with `asyncBefore` / `asyncAfter`. The
`JobExecutor` acquires jobs with a locking query, runs them on a thread pool, decrements a retry
counter on failure, and raises an incident at zero
([job executor](https://docs.camunda.org/manual/latest/user-guide/process-engine/the-job-executor/)).
Concurrency safety is optimistic locking plus exclusive jobs: "Jobs from a single process instance are
never executed concurrently."

**BPMN 2.0.** Not an execution model, a semantics. Tokens flow along sequence flows; gateways split and
merge them; the hard part is that the inclusive gateway's join is *non-local*, requiring reasoning
about which upstream flows can still deliver a token. The formal treatment of this is
[Christiansen, Carbone and Hildebrandt, WSFM 2010](https://davidchristiansen.dk/pubs/wsfm2010.pdf),
which notes that the non-local nature of inclusive gateways forces a backward token search over
upstream flows. Every engine here either implements that search or approximates it.

---

## 2. Comparison table

The axes are the ones that turned out to discriminate. I dropped several that looked promising and
did not (language runtime, expression engine, designer model), and added two that were not on my
list going in (admission control locus, read-model coupling).

| Axis | Elsa 4 | Zeebe / Camunda 8 | Camunda 7 | BPMN 2.0 spec |
|---|---|---|---|---|
| **Durable unit of truth** | Checkpoint of *state* + a queue of pending work items (ADR 0020) | Append-only log of *commands and events*; state is a RocksDB projection | Rows in a relational DB, mutated in place | Not addressed |
| **Single-writer granularity** | One workflow execution (`InProcessWorkflowExecutionActorProvider` per-actor `SemaphoreSlim(1,1)`; ADR 0031 ratification decision 2) | One partition (one stream processor thread) | One process instance, enforced by *exclusive jobs* | Not addressed |
| **How the writer is protected** | Best-effort placement lease for routing, plus a monotonic **fencing token** re-checked at commit ([Distributed README](../../src/Elsa/Workflows/Runtime/Distributed/README.md)) | Raft leadership per partition | Optimistic locking (`REV_` column) + exclusive-job flag |  Not addressed |
| **Who decides the commit boundary** | A **policy**, per workflow, over a fixed mandatory set (ADR 0032) | The engine: batch follow-up commands until quiescence, then commit | The **author**, per activity, via `asyncBefore`/`asyncAfter` | Not addressed |
| **Safety default if the author says nothing** | Fail-safe: `SideEffectProfile` defaults to `External` ⇒ mandatory boundary (ADR 0032 R1) | N/A (no author knob) | **Unsafe**: no async boundary means rollback to the last wait state, re-running everything since | Not addressed |
| **How work reaches the executor** | Push. The engine dispatches into in-process activity code | Pull. Workers poll; "Zeebe queues jobs until workers request them" | Both. Job executor pushes; external tasks are pulled | Not addressed |
| **Admission control / backpressure** | **None that sheds.** A `SemaphoreSlim(1,1)` store connection gate queues without bound or timeout (§4.1; measured in [spec 114](../../specs/114-concurrency-throughput-instrument/research.md)) | Adaptive per-partition limiter that rejects commands | Bounded job queue + acquisition backoff (up to 60 s) | Not addressed |
| **Intra-instance parallelism** | Interleaved on one writer, never concurrent (ADR 0031) | Interleaved on one processor thread | Serialized by exclusive jobs | Concurrent tokens; parallelism is a host choice |
| **Poison containment** | Per work item: ack, poison record, blocking incident; no retry by default; escalation to the workflow is an authored `IIncidentStrategy` | Instance is banned; partition keeps processing everything else | Retry counter (default 3) then incident; other jobs unaffected | Not addressed |
| **Hung-work detection** | **None**: the work claim *and* the ownership heartbeat both renew for as long as the dispatch runs, and those are the recovery scanner's only two signals | Absolute job timeout; the job is reassigned | Job lock expiry; another node re-acquires | Not addressed |
| **Read model derivation** | Folded into the same commit (`activityExecutionInspections`; ADR 0001, ADR 0032 R3) | Separate exporters reading the log asynchronously | Same DB, queried directly; history tables written in the same transaction | Not addressed |
| **Semantics/host separation** | **Pure**, in the BPMN core: `(graph, state, event) -> (state, continuation, commands)` ([Bpmn.Semantics ADR 0002](https://github.com/valence-works/bpmn)). Impure in Flowchart and in the runtime | Semantics live inside the stream processor | Semantics live in behavior classes coupled to the persistence session | Defines the semantics, not the seam |
| **Operational substrate** | Whatever store the host already runs (SQLite, Postgres, Mongo) inside the host process | A dedicated broker cluster, RocksDB, gateways, plus an exporter target | An app server plus a relational DB | N/A |

---

## 3. Where Elsa is genuinely better

### 3.1 The commit boundary has a fail-safe default, and Camunda 7's does not

This is the strongest result in the comparison and it is not close.

Camunda 7 makes the author draw transaction boundaries. If they do not, "the state is rolled back to
the last persistent wait state of the process instance"
([transactions](https://docs.camunda.org/manual/latest/user-guide/process-engine/transactions-in-processes/)).
Forgetting `asyncBefore` on a service task that posts to a payment API means a downstream failure
re-runs the payment. The failure mode of the missing declaration is *incorrectness*.

Elsa inverts it. ADR 0032 R1 puts `SideEffectProfile { External, ReplaySafe }` on the pinned
`ActivityContract`, defaults it to `External`, and argues the asymmetry explicitly: "mis-declaring a
side-effecting activity as `ReplaySafe` is a data-integrity bug ... whereas leaving a genuinely pure
activity unmarked merely forgoes throughput. The failure modes are asymmetric, so the default must fail
toward durability." An unmarked activity gets a mandatory boundary. The failure mode of the missing
declaration is *slowness*.

The same flag then gates hop fusion (ADR 0047: `External` is never fused) and the pre-activation
flush (ADR 0032 R2). One declaration, three optimizations, one direction of failure. That is a better
piece of design than either incumbent has, and it is the thing I would keep if I could keep only one.

Zeebe sidesteps the question rather than solving it: because activity code never runs inside the
engine, there is no "did my side effect survive the rollback" problem to have. That is a legitimate
answer, but it is only available to an engine that refuses to run user code, which Elsa has decided
not to be.

### 3.2 Fencing is separated from placement

Elsa's distributed leaf states the decomposition plainly: "Placement decides where work runs; fencing
decides whether a commit is allowed to persist. Double durable execution is prevented by fencing, not
by placement" ([README](../../src/Elsa/Workflows/Runtime/Distributed/README.md)). The placement lease is
best-effort and expiring; correctness rests on a monotonic token re-checked inside the commit.

The mechanism survives contact with batching, which is the test that matters. When many executions'
commits are folded into one shared transaction, each member's fence is validated *per member* inside
that shared unit of work: `GroundworkRuntimeCheckpointWriter.ApplyStagedAsync` calls
`ValidateAndTouchExpectedFenceAsync(transactionalStore, commit, …)` against the transactional store, so
a stale token is caught before any of that member's documents land
([src](../../src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs)). The
unit of work is all-or-nothing, so one member's stale fence poisons the batch, and the coordinator rolls
the shared transaction back and re-drives every member through its own single-commit path. No member is
half-applied, and one execution losing its lease never fails another's commit. The fence is validated
*and touched* under optimistic concurrency, so a concurrent lease change is detected as a write conflict
rather than a lost update.

Zeebe gets the same property from Raft, which is a much bigger hammer and requires the broker to be a
consensus cluster. Camunda 7 gets it from optimistic locking, which is correct but gives no way to
reason about *which node* owns an instance, so it leans on exclusive jobs to avoid the interleaving in
the first place. Elsa's version is the right decomposition for a system that wants to run inside the
customer's application process with no consensus protocol, and it is stated as a design rule rather
than discovered as a bug fix.

### 3.3 Every optimization ships with a byte-identical proof

ADR 0031's load-bearing invariant is "durable state is truth; memory is cache; memory is never a
correctness dependency," and each subsequent optimization carries a guardrail that a run with the
optimization disabled commits byte-identical checkpoint state (ADR 0047 guardrail 1; spec 109's
ENABLED-vs-DISABLED end-to-end comparison). Hop fusion, coalescing, and the in-process fast path each
ship behind a toggle with an A/B that stays runnable.

Neither Camunda project documents an equivalent discipline. Zeebe's batch processing
([blog](https://camunda.com/blog/2023/03/zeebe-batch-processing/)) is presented as a latency
improvement with a benchmark, not as a change proven state-equivalent to its predecessor. This is a
process advantage rather than an architecture one, but it is the reason Elsa was able to take
`58 → 36 → 5` dispatches (ADR 0047 follow-up) without a correctness incident, and it should be
defended.

One limit is worth naming here rather than only in §8, because it is the discipline's blind spot. A
byte-identical guardrail proves an optimization does not change what gets committed. It cannot tell you
whether the optimization ever *applies*. §4.5 is precisely that case: hop fusion passes its guardrail
perfectly and currently reaches no leaf activity a user can place in a workflow.

### 3.4 The BPMN semantics core is purer than either vendor's

`Bpmn.Semantics` is a synchronous, command-returning function:
`(graph, prior state, host snapshot, event) -> (next state, continuation, commands)`, with exactly
three host commands, [ADR 0002](https://github.com/valence-works/bpmn). Its argument for the shape is
better than the usual purity argument: "Teardown may only be staged on a non-fault continuation ...
When behaviors hold a live host reference, nothing prevents them firing immediately, and the invariant
survives only by discipline. When they return commands, nothing *can* fire before the interpreter
returns, and the invariant holds by construction."

The practical payoff is that a bug report is four JSON values and a regression test needs no host. In
Zeebe the semantics are inside the stream processor; in Camunda 7 they are inside behavior classes
holding a persistence session. Neither can be reproduced from a serialized tuple.

### 3.5 One store, no export pipeline

Elsa's runtime state is queryable in the store the host already runs. Zeebe cannot answer "what is this
instance doing" from the broker; it answers from Elasticsearch, populated by an exporter reading the
log ([exporters](https://docs.camunda.io/docs/self-managed/concepts/exporters/)), and log compaction is
bounded by "the lowest acknowledged position across all exporters." That is a second storage system,
a second failure mode, and a visibility lag, purchased to get an append-only log.

For Elsa's positioning (a library inside an ASP.NET Core app), avoiding that is not a compromise, it is
the product. It is worth saying out loud because §5.3 argues Elsa should steal *part* of the exporter
idea, and the boundary between "steal the decoupling" and "adopt the cluster" matters.

### 3.6 Fault escalation is authored, with a strict default

The same shape as §3.1, in a second place. Whether an activity fault terminates its workflow is decided
by the workflow's `IIncidentStrategy`, resolved at drain quiescence by
`IncidentStrategyResolutionDrainObserver`. Two ship: `FaultIncidentStrategy`, the default, which
terminates, and `ContinueWithIncidentsIncidentStrategy`, which keeps the workflow running with the
incident open.

Both incumbents hard-code this. Zeebe bans the instance; Camunda 7 leaves it alive with an incident.
Each is a reasonable default and neither is expressible as the other. Elsa's default is the strict one,
which is the right direction for the same asymmetry argument as §3.1: a workflow that stops when it
should have continued is visible and recoverable, and one that continues when it should have stopped is
neither.

### 3.7 Single-writer granularity is finer than Zeebe's

Zeebe's writer is one thread per partition, so every instance on that partition shares one serial
processor. Elsa's writer is one actor per workflow execution, so a slow workflow blocks only itself.
Spec 114 confirms there is no engine-level cap on concurrent drains.

I want to be honest about how much this is worth. Zeebe can afford a partition-wide serial processor
precisely because each command is tiny and never runs user code; the cost that would justify finer
granularity has been exported to the job workers. So this is an advantage in Elsa's model rather than
an advantage over Zeebe's. It becomes a real advantage only when compared against Camunda 7, where
exclusive jobs serialize an instance *and* the acquisition query is a shared bottleneck.

---

## 4. Where Elsa is worse

### 4.1 There is no admission control anywhere. None.

Spec 114 set out to find the drain-concurrency cap and found there is not one:
"`InProcessWorkflowExecutionActorProvider` serializes commands only *per workflow-execution id* ...
There is no engine-level semaphore that caps how many workflows drain at once."

The consequence is measured, not theorized. On one shared SQLite writer:

| N concurrent runs | total wall | throughput |
|---:|---:|---:|
| 8 | 2 308 ms | 3.5 runs/s |
| 32 | 5 746 ms | 5.6 runs/s |
| 128 | **84 952 ms** | **1.5 runs/s** |

Throughput *falls* between N=32 and N=128 while commits stay at exactly 1 per run. That is congestion
collapse: more offered load producing less completed work.

**Verification sharpened this rather than softening it.** My original "no mechanism exists" was slightly
wrong in a way that makes the finding worse, not better. A mechanism does exist, and it is the *cause*
of the collapse. Groundwork's `RelationalDocumentStore` holds a `SemaphoreSlim(1, 1)` connection gate and
every unit of work begins with `await connectionGate.WaitAsync(cancellationToken)` — no timeout, no
bound on queue depth. Under offered load N, all N wait, none is rejected, and latency grows without
limit. A serializing gate with an unbounded wait queue converts overload into latency instead of into
rejection, which is exactly the shape that produces collapse rather than a plateau.

**The bounds that do exist bound the wrong things.** I checked each:

| Bound | Default | What it actually bounds |
|---|---|---|
| `WorkflowDrainOrchestratorOptions.MaxDrainCycles` | 64 | Cycles *within one drain*; a runaway-loop backstop that throws `DrainCycleLimitExceededException` |
| `RuntimeSchedulerDrainRequest.MaxWorkItems` | unset | Items per drain cycle |
| `RuntimeActorEvictionOptions.PassivateOnTerminal` | true | *Memory* — the live actor registry. Its own doc says it is "deliberately not an LRU/idle policy" |
| `RuntimeResumptionOptions.MaxExecutionsPerSweep` | 100 | The **recovery** path, per tick |

Nothing bounds live dispatch. There is no rate limiter, bulkhead, load-shedding path, or queue-depth
check anywhere in `src/Elsa`; the only `Rejected` dispatch statuses are identity, authority, and
validation mismatches, never overload.

**The most useful finding is the last row.** `RuntimeResumptionOptions`'s doc comment states the exact
intent that live dispatch lacks: it "Bounds every sweep so a large backlog or a poisoned execution
cannot overwhelm the host." The team has already written this kind of bound, correctly, with the right
reasoning, for the recovery path. It was simply never applied to the path that carries production
traffic. That asymmetry is what makes R1 a port of an existing pattern rather than a new design.

Both incumbents shed rather than queue. Zeebe runs an adaptive limiter per partition and rejects
commands when in-flight exceeds the current limit
([internal processing](https://docs.camunda.io/docs/components/zeebe/technical-concepts/internal-processing/)).
Camunda 7 bounds the job queue and backs off acquisition "to avoid acquisition conflicts in clusters
and to reduce database load"
([job executor](https://docs.camunda.org/manual/latest/user-guide/process-engine/the-job-executor/)).

This is the single largest structural gap. It is also the one most likely to be discovered by a
customer rather than by us, because it only appears under concurrency and every benchmark before spec
114 measured single-run latency.

### 4.2 A hung activity holds its claim forever

Verified in full, and it is worse than the original wording: the flaw is in *two* renewal loops, not
one, and they cover for each other.

**Loop one, the work claim.** `WorkflowSchedulerDrainer.RenewClaimUntilStoppedAsync` is a `while (true)`
that renews on a cadence of `VisibilityTimeout / 3` (default 1 minute, so every 20 s) and stops only when
the dispatch completes ([src](../../src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs)).

**Loop two, the execution ownership lease.**
`WorkflowDrainOrchestrator.RenewOwnershipUntilStoppedAsync` has the identical shape: `while (true)`,
cadence `LeaseDuration / 3` (default 1 minute, so every 20 s), heartbeating until the drain returns
([src](../../src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs)).

**Neither bound is a total.** Both are renewal *intervals*. No `RuntimeSchedulerWorkClaimOptions` or
`RuntimeExecutionOwnershipOptions` field expresses a maximum, and no timeout or deadline concept exists
on `ActivityContract` or anywhere else in the runtime. The only execution timeout in the whole codebase
is Jint's script-evaluation timeout, which is a different layer solving a different problem.

**So recovery cannot see it, by construction.** `RuntimeRecoveryCandidateSelector` decides candidacy on
exactly two signals: `IsLeaseExpired(lease, request)` against the lease's `ExpiresAt`, and
`HeartbeatDueAt(heartbeat) = heartbeat.RecordedAt + HeartbeatTimeout`
([src](../../src/Elsa/Workflows/Runtime/Core/Services/RuntimeRecoveryCandidateSelector.cs)). Both are
refreshed by the two loops above for as long as the dispatch is stuck. An activity blocked on a socket
with no timeout therefore keeps its work claim, keeps its ownership lease, keeps its heartbeat fresh,
occupies its execution's single writer, and presents to every store and scanner as perfectly healthy.

**The underlying error, stated plainly.** Both loops answer "is the renewer alive?" when the question
that matters is "is the work progressing?" A renewal loop running on a healthy thread pool thread proves
nothing about the dispatch it is renewing for. This is why it is a pattern rather than a slip in one
loop: the same substitution was made twice, in two files, by the same reasoning.

Zeebe's equivalent is absolute: "If the job is not completed or failed within the configured job
activation timeout, Zeebe reassigns the job to another job worker." Camunda 7's lock expiry is likewise
absolute. Elsa's visibility timeout protects against *process death* but not against *process hang*, and
those are different failures.

*One adjacent fact that cuts in Elsa's favor and shortens the fix:* the runtime already has the
vocabulary. `IRuntimeVolatileWaitPolicy` bounds a *declared* in-memory wait with a `maximumDuration`,
plus host-shutdown and cancellation behaviors and a durable-fallback policy
([src](../../src/Elsa/Workflows/Runtime/Services/DefaultRuntimeVolatileWaitPolicy.cs)). That is the
same decision shape a dispatch watchdog needs, applied to the case where the activity *asked* to wait.
What is missing is the involuntary case.

### 4.3 An infrastructure fault costs siblings a sweep interval

**Correction to an earlier draft of this report.** I originally wrote this as "a single fault stops the
whole drain" and claimed that in a parallel fork, branch A faulting stops branch B. That is wrong, and
the mistake was assuming the drain loop's `break` sees ordinary activity faults. It does not.

```
if (result.Status == RuntimeSchedulerWorkItemResultStatus.Faulted)
    break;
```

That `break` in `WorkflowSchedulerDrainer.DrainAsync` fires only when a *handler* throws out to the
drainer. An activity that throws never gets there: `WorkflowInvokeActivitySchedulerWorkHandler` catches
it ([src:506](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs)),
routes it to `RecordFaultAsync`, and returns normally. The drainer records `Completed` and keeps
draining, so sibling branches with queued work continue in the same burst. The reachable class for the
`break` is the same narrow infrastructure one as §4.7.

**And the fault-to-workflow decision is authored, not hard-coded.** `RecordFaultAsync` commits a
blocking incident; at quiescence `IncidentStrategyResolutionDrainObserver` applies the workflow's
`IIncidentStrategy`. Two are built in: `FaultIncidentStrategy` (the default, terminates the workflow)
and `ContinueWithIncidentsIncidentStrategy` (keeps it running with the incident open). So "how much
should a fault stop" is a per-workflow authoring choice with a strict default, which is the design I
claimed was absent. Camunda 7's default is closer to `ContinueWithIncidents`; Zeebe's instance ban is
closer to `Fault`. Elsa can express either.

Where a sibling branch genuinely is stopped, it is deliberate and argued. `RecordFaultAsync` rides a
`ChildFaultParentEvaluation` work item on the incident checkpoint, and `Flowchart.OnChildFaultedAsync`
faults the whole Flowchart, because a Flowchart join requires *every* inbound branch and a faulted
branch can never arrive, so the join would wait forever (#308). It mirrors the `Parallel` composite's
default threshold and it is documented at the point of decision. That is a semantic choice about join
liveness, not a scheduling accident.

**What survives.** For a handler-level infrastructure fault, the `break` does end the burst, and the
queued siblings then wait for `RuntimeResumptionPumpTask` backlog discovery
(`ListPendingWorkflowExecutionIdsAsync` → re-drive), which is `SweepInterval` away — default 10 seconds
— and geometrically further under the per-execution backoff if the re-drive keeps failing. That is a
real latency cliff, on a rare path, and it is the only part of my original claim that holds. It is also
the mechanism behind R6, which is correspondingly narrower than I first sized it.

### 4.4 The read model is coupled to the write cadence

Inspection projections fold into the same checkpoint commit, so relaxing commit cadence coarsens
observability. ADR 0032 R3 handles this honestly by projecting `inspectionGranularity` as
`activity-level | boundary-level` so consumers can render the truth, which is much better than losing
the data silently. But the underlying coupling remains: throughput and observability are on the same
dial, and turning one turns the other.

Zeebe does not have this trade at all. The log is written for correctness; exporters read it for
observability; neither cadence affects the other. This is the one place where the append-only log
buys something Elsa's model structurally cannot replicate without decoupling projection from commit.

### 4.5 `External` activities still pay the full hop multiplier

ADR 0047 cut `ReplaySafe` activities from roughly seven dispatches per graph edge to one or two. It
explicitly does not fuse `External` activities, for good reasons (attempt visibility, at-most-once
effect placement). But `External` is the *default*, and it is the profile of exactly the activities
users care about: HTTP calls, message sends, database writes. So the shipped hot-loop numbers
(`58 → 36 → 5` dispatches) describe the best case, and a realistic workflow of mostly-`External`
activities sits much closer to the 58.

**Verification found this understated.** I wrote that a realistic workflow "sits much closer to the 58."
The truth is sharper: **no workflow built only from shipped activities can be fused at all.**

Exactly nine shipped types carry `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`, and every
one of them is a control-flow composite: `Do`, `Flowchart`, `For`, `ForEach`, `If`, `Parallel`,
`Sequence`, `Switch`, `While`. **Zero shipped leaf activities are marked.** `ActivityContract`'s
constructor defaults `sideEffectProfile` to `External`, so every leaf a user can place in a loop today
is `External` and therefore never fused.

The leaf in the `58 → 36 → 5` A/B is `NoOpStep`, and it is declared in the *benchmark assembly*, not in
`src/` ([EngineExecutionBenchmarks.cs:681](../../benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineExecutionBenchmarks.cs)).
The benchmark is scrupulous about this — its own doc comment says it measures "the exact relaxation the
hot-loop A/B measures against the unmarked (External) `WriteLine` leaf" — so this is not a misleading
number. It is an optimization whose applicability set, for leaves, is currently empty.

That reframes the finding. It is not a consequence of the fail-safe default I praised in §3.1; the
default is right. It is that the *marking pass never happened for leaves*. ADR 0032 R1's own worked
classification lists "pure compute/transform" as `ReplaySafe` and intended exactly this, and only the
containers ever got the attribute. (Its table also names `SetVariable`, which in shipped code turns out
to be an engine intrinsic, `elsa.intrinsic.set@1`, rather than a CLR activity, so that particular row may
be moot. The broader class of pure leaves is not.)

**The marking pass was then run, and it yields exactly one activity.** That result is worth more than
the attribute it produced, because it says the gap is not the missing marks.

Classification of every unmarked shipped activity:

| Activity | Verdict | Reason |
| --- | --- | --- |
| `Break` | **Marked `ReplaySafe`** | No inputs, no I/O, returns a constant transition. The only leaf that clears the bar without an author judgement |
| `Fault` | External | Provably pure, but valueless: it always faults, so its `IncidentRecorded` boundary flushes immediately anyway. A contract change for zero gain |
| `Inline` | External | A user-code carrier by design; the runtime cannot know an author's expression is pure |
| `WriteLine`, `WriteLines` | External | ADR 0032 R1 rules on these explicitly: author-declarable, "the runtime must not assume it" |
| `ReadLine` | External | Consumes console input; non-deterministic |
| `GraphActivity` | External | **Attempt-dependent.** `PrepareEntryCheckpointAsync` branches on `AttemptNumber > 1` and reads back persisted values instead of re-capturing, so it relies on exactly the durable attempt identity the pre-activation flush guarantees |
| `PublishEvent`, `DispatchWorkflow`, `SendHttpRequest`, `WriteHttpResponse` | External | Externally observable effects |
| `Delay`, `Timer`, `Cron` | External | Suspend or trigger; fusion exits on suspension regardless |
| `RunJavaScript` | External | Arbitrary script plus variable write-back |
| `BpmnProcess`, `BpmnDecision` | **Blocked, not judged** | `BpmnProcess` qualifies on the merits (a routing composite like `Flowchart`), but ADR 0063 freezes both contract fingerprints for the extraction baseline, and the profile participates in the fingerprint |

**And the real blocker is not marking at all.** `ReplaySafeFusionDriver.ShouldFuse` requires
`node.IntrinsicKind is null` — intrinsics are categorically excluded, deliberately, because "those keep
their durable pre-activation boundary"
([src](../../src/Elsa/Workflows/Runtime/Services/ReplaySafeFusionDriver.cs)). They could not be marked
even in principle: `ExecutableNodeCompiler.CompileIntrinsicNode` emits `activityContract: null`, so an
intrinsic node carries no profile to set.

That is decisive, because **Elsa 4's pure-compute leaves are the intrinsics**: `Set`, `Merge`, `Reduce`,
`Return`, `Control`, `SetOutput`, alongside the genuinely stateful `Finish`, `SetCorrelationId` and
`SetInstanceName`. A hot loop of variable arithmetic is a loop of `Set` intrinsics, and no attribute
anywhere can make one fusable.

So §4.5's cause moves one level down. It is not that nobody applied the attribute; it is that the
population the attribute was designed for barely exists in CLR activities, and the population that does
the pure work is excluded by a blanket guard. The exclusion is right for three of the nine kinds and
unexamined for the rest — and the runtime already draws that exact distinction elsewhere, since
[ADR 0031](../adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)'s
correction notes that "only a mutating `Finish`/`Correlate`/`SetName` leaf pays" the control-leaf state
load.

**Both halves have since been implemented — see R9, which is now closed.** `Break` is marked, and the
intrinsic exclusion is per kind, so `Set`/`Merge`/`Reduce`/`Return`/`Control` now fuse. One claim in the
paragraphs above needs correcting in light of that work: I implied intrinsics also paid a mandatory
flush per node, because their commits are stamped `CheckpointRequirement = Mandatory`. They did not.
`IntrinsicCompleted` is not in the coalescing policy's mandatory set, and the committer's mandatory guard
forbids only `Skip`, never `Deferred` — so intrinsic completions were already coalescing correctly. The
gap was hop count alone.

**And R9b has now measured it.** The `External` production shape pays **56 dispatches and 11 commits**
per hot-loop×10 run against the published best case's 5 and 1. Fifty-six is ADR 0047's *pre-fusion*
number. The prediction in this section — that a real workflow "sits much closer to the 58" — turned out
to be nearly exact. Full table and the two further findings in R9b.

### 4.6 There is no way to run activity code outside the engine

Elsa activities are C# classes that run inside the drain. There is no job-worker protocol, no external
task pattern, no lease/complete/fail wire contract. The consequences:

- A slow activity occupies its execution's single writer and its DI scope for the whole duration.
- Activity implementations cannot be scaled, deployed, or written independently of the host.
- There is no polyglot story at all.

Camunda 7 has external tasks; Zeebe has nothing *but* workers. Bookmarks plus stimuli give a manual
approximation — suspend, let something external happen, resume via `DispatchStimulusEndpoint` — but that
is a workflow-authoring pattern, not an engine contract with leases, timeouts, and retry accounting.

**Verification added one fact that changes the framing: this is a regression, not an absence.** Elsa 3
had `RunTask`, catalogued in the parity audit as the "external task/callback pattern"
([parity report](elsa-4-activity-contract-parity-2026-07.md)). It appears in this repository only as a
gap row in that report; there is no implementation in `src/`. So the capability existed, was inventoried
as missing, and has not been ported. That is a materially different claim from "Elsa never offered
this," and it means the demand question (R8's precondition) has partial evidence already: Elsa 3 users
had this surface.

*One adjacent fact, the third of its kind in this report:* the lease-based at-least-once worker pattern
already exists in-repo, applied elsewhere. `WorkflowAlterationModels` carries a "Lease carried by an
at-least-once alteration job worker." As with `IRuntimeVolatileWaitPolicy` (§4.2) and
`RuntimeResumptionOptions` (§4.1), the shape Elsa needs is already written somewhere in the codebase and
simply not applied to the path in question.

Whether this matters still depends on positioning, which is why it stays a recommendation with a
precondition (R8) rather than a defect.

### 4.7 The default retry policy is "do not retry", but the failure is visible

**Correction to an earlier draft of this report.** I flagged "an infrastructure fault parks silently" as
unverified. Having now traced it end to end, the *silent* half is wrong and the *no retry* half is
right.

What actually happens. A handler throws, `DispatchAsync` ack-deletes the item (deliberately, to bound
poison delivery) and calls `HandleHandlerCrashAsync`. Both collaborators I assumed might be absent are
registered by default in `RuntimeCoreServiceCollectionExtensions`: `IWorkflowSchedulerPoisonStore`
(`InMemoryWorkflowSchedulerPoisonStore`, swapped for `GroundworkWorkflowSchedulerPoisonStore` on the
Groundwork lane) and `IRuntimeDomainRetryPolicy`. So the poison record is always written, and the
policy is not absent: `NoopRuntimeDomainRetryPolicy` returns an explicit `DoNotRetry` with the reason
"Default runtime domain retry policy does not retry." My description of the mechanism as "no policy
registered" was wrong; the outcome, `Poisoned`, is right, and it is a deliberate decision rather than a
fallthrough.

The drain then reports `StoppedOnFault`, `WorkflowDrainOrchestrator.NotifyObserversAsync` runs the drain
observers, and `PoisonedSchedulerWorkIncidentObserver`
([src](../../src/Elsa/Workflows/Runtime/Services/PoisonedSchedulerWorkIncidentObserver.cs)) projects
every `Poisoned` record into a **blocking, `Critical` incident** with failure type
`SchedulerWorkPoisoned`, a deterministic id, the handler name, failure count, and fault detail in the
metadata. It is registered *before* `BlockingIncidentWorkflowFaultObserver` on purpose, and records a
system-authored `WaitForIntervention` outcome so the workflow's lifecycle state is preserved while the
incident is inspectable. Existing incidents are never overwritten, so an operator-resolved incident
stays resolved and repeated drains are idempotent.

The observer exists precisely because the silent version was the previous behavior, and its own doc
comment describes it: without it, "the workflow stays Running, the activity stays Scheduled, and
neither the incidents API nor the Studio timeline surfaces anything."

Three things remain true and are the honest version of this finding:

1. **The default really is no retry.** Camunda 7 retries three times by default; Zeebe's retry count is
   client-supplied but the engine always raises an incident on exhaustion. Elsa's default goes straight
   to an incident on the first infrastructure fault. That is a defensible choice (an infrastructure
   fault is not obviously transient) but it is a weaker default than either incumbent, and a host that
   wants retries must implement `IRuntimeDomainRetryPolicy` itself.
2. **Surfacing has a crash window.** The observer only runs on a drain that stopped on a fault. The
   source notes it: "A process crash between that drain and this notification leaves the record
   unsurfaced until the workflow's next faulted drain; the record itself stays inspectable." So the
   poison record is durable but the incident may not exist until something else faults.
3. **Incident recording is best-effort.** A persistence failure while writing the incident is caught,
   logged with the original fault, and the drain continues. That is the right priority ordering, but it
   means the durable poison record can outlive any operator-visible projection of it.

None of these is the defect I originally described. The path is instrumented, deliberate, and
documented.

---

## 5. The six threads

### 5.1 Is the sticky drain the same idea as Zeebe's stream processor?

Yes, arrived at independently, and ADR 0031 says so in its "Prior art" paragraph, though it cites
Temporal, Orleans, and DTFx rather than Zeebe. The shared insight is identical: a durable log or queue
is the truth, and a single writer processes a run of work in memory without round-tripping durability
between steps. Zeebe's follow-up-commands-on-the-same-stream and Elsa's post-commit-intents-plus-drain
are the same trick, and both exist for the same reason, to avoid a distributed transaction.

The granularity differs, and that is where the interesting part is. **What partitioning buys Zeebe
that Elsa lacks:**

1. **A bounded number of writers, hence a bounded number of fsyncs.** With P partitions there are at
   most P concurrent commit streams, so batching is natural and structural. Elsa has one writer *per
   execution*, so at N concurrent executions there are N commit attempts against one store. Elsa
   retrofits the batching instead (spec 115's group-commit coordinator, R2), which works as a
   mechanism but has to be bolted on rather than falling out of the topology, and which measurably does
   not recover the throughput because the fsync was not the only serialized round-trip.
2. **A natural place to put admission control.** Zeebe's limiter is per partition, and a partition is
   a thing with a queue depth. Elsa's actors have no shared object to meter against.
3. **A total order over a set of instances,** which makes exporters and read models trivially
   consistent. Elsa's projections have no global order and so must be consistent per execution.

**Does Elsa need partitioning? No.** Partitioning is Zeebe's answer to "how do I get ordering and a
bounded writer count when I have refused to use a shared database." Elsa has a shared database, which
is a different and equally valid answer to the same question. Adopting partitions would import a
rebalancing protocol, a cross-partition correlation problem (Zeebe has one: message correlation routes
by correlation key), and a constraint that a workflow execution can never move, in exchange for
properties Elsa can obtain more cheaply. What Elsa needs is items 1 and 2, and it can get both without
partitioning: item 1 from batching at the store boundary (group commit already does this for the fsync;
the round-trips around it are still open, R2), item 2 from an admission limiter (R1). Neither requires a
partition, and the partition would not have delivered them for free anyway: Zeebe gets item 1 because a
partition is a *log*, and Elsa's store is not one.

### 5.2 Push versus pull

Pull buys three things, and they are separable:

- **Backpressure for free.** A worker takes what it can handle. There is nothing to tune because the
  worker's capacity *is* the limit. This is the strongest argument for pull and it is the one Elsa
  most needs. But it is obtainable without pull: an admission limiter gives the same property with
  push.
- **Worker autonomy.** Independent deployment, scaling, and language. Not obtainable without a pull
  protocol, and not obviously wanted by Elsa's users, who chose Elsa to write C# activities in their
  own app.
- **Timeout as the primary failure detector.** This is the underrated one. Because the worker holds a
  lease with an absolute deadline, "worker died" and "worker hung" are the same event and need no
  liveness protocol. Elsa distinguishes them and handles only the first (§4.2). This property *is*
  obtainable in Elsa's model, by bounding the claim lease, and it should be.

So: two of the three are obtainable without changing the model, and they are the two that matter for
robustness. The third is a product decision. My recommendation is to take the two, and to treat
worker autonomy as an optional activity family rather than a model change (R8, and non-recommendation
N3).

### 5.3 Checkpoint cadence versus event sourcing

| | Elsa checkpoint | Zeebe log + snapshot |
|---|---|---|
| **Recovery** | Re-dispatch queued work items from the last flushed checkpoint; safety rests on queue-level enqueue-by-identity plus handler idempotency (ADR 0031 ratification decision 1) | Replay the log from the last snapshot; state is a pure function of the log prefix |
| **Replay determinism** | Requires handlers to be idempotent, and requires activities to be honestly marked `ReplaySafe` | Guaranteed by construction; no marking required |
| **Write amplification (count)** | Low, and coalescing drives it to 1 commit per drain segment | High; every command and event is a record |
| **Write amplification (cost)** | High per write: a multi-document transaction with random access in a relational store | Low per write: sequential append, batched, amortized fsync |
| **Debuggability** | Checkpoint history plus inspection projections; coarsens under coalescing | The log *is* the audit trail and can be replayed |

Two honest observations.

First, Elsa's recovery story is *weaker in kind*, not just in degree. Zeebe's state is a function of
the log; Elsa's recovery is correct only if the idempotency ladder holds at every rung. ADR 0031 knows
this and mandates enqueue-by-identity at the queue provider, with handler idempotency demoted to
defense in depth, which is the right structure. But it is a contract that providers must honor rather
than a property that cannot be violated.

Second, Elsa already has the better debuggability *story* in one place and has not generalized it.
`Bpmn.Semantics` ADR 0002's claim that "an evaluation is fully determined by four values ... all of
which serialize to JSON" is a stronger reproducibility property than a log, because the log requires a
running broker to replay and the four-tuple does not. The runtime does not have this: a hop's inputs
are a work item, an execution state, a durable-value projection, and a pinned executable, none of which
are captured together. Making that tuple capturable is R7.

### 5.4 Flowchart versus BPMN: are these two token engines that should be one?

**They are already more alike than the question assumes.** Both have exactly the architecture the BPMN
library's ADR 0002 describes: a pure decision port returning commands, applied by an engine.

| | Flowchart | Elsa BPMN |
|---|---|---|
| Decision port | `IFlowchartPolicy.Execute(ctx) -> FlowchartPolicyDecision { Commands }` ([src](../../src/Elsa/Activities/Flowchart/Contracts/IFlowchartPolicy.cs)) | `IBpmnElementBehavior.OnTokenArrived(ctx) -> BpmnBehaviorDecision { Commands }` ([src](../../src/Elsa/Activities/Bpmn/Models/BpmnBehaviorDecision.cs)) |
| Command kinds | 12 (`FlowchartPolicyCommandKind`) | 8 (`BpmnBehaviorCommandKind`) |
| Split/join vocabulary | `parallelFork`, `parallelJoin`, `inclusiveFork`, `inclusiveJoin`, `decision`, `merge`, `firstWins` ([src](../../src/Elsa/Activities/Flowchart/Internal/Policies/FlowchartPolicyKinds.cs)) | `ParallelGateway`, `InclusiveGateway`, `ExclusiveGateway`, `EventBasedGateway` |
| Join accounting | `FlowchartJoinCoordinator`, keyed by `(target, scope, iterationKey)` | `BpmnTokenCoordinator`, keyed by `(element, iterationKey)` |
| OR-join algorithm | Reachability over live children: `state.ActiveChildren.Any(child => graph.CanReach(child.NodeId, inbound.Source.NodeId))` | Activation-aware join accounting over live tokens and active children |
| Determinism discipline | "All record ids remain a pure function of `FlowchartExecutionState.Sequence`, whose only mutation home is `FlowchartStateMutator`" | The same sentence, verbatim, about `BpmnExecutionState.Sequence` and `BpmnStateMutator` |

The last row is not a coincidence; `BpmnExecutionEngine`'s own doc comment says it is "mirroring
`FlowchartExecutionEngine`'s decomposition." These are two implementations of the same design, and the
non-local OR-join, the hardest correctness problem in this field, exists twice.

**The case for unification.** The duplicated parts are exactly the dangerous parts. Both do a backward
reachability search to decide whether an un-arrived inbound branch can still be reached, which is the
thing the WSFM paper needed a formal semantics to get right. Both key join state by iteration to
survive loops. Both mint deterministic ids from a sequence counter. A bug fixed in one is a bug still
present in the other, and there is no test that would catch the divergence.

**The case against re-expressing Flowchart over the BPMN port.** I think this one wins, for three
reasons.

1. **Flowchart nodes are not BPMN elements.** In BPMN, a task and a gateway are different elements; a
   task with three outbound flows is illegal without a gateway. In Flowchart, a node is both: a node
   with three outbound connections is a work item *and* a split, and its policy is metadata on the node
   (`FlowchartNodeMetadata.PolicyKind`). Mapping Flowchart onto BPMN means synthesizing a hidden
   gateway element per branching node. That breaks the 1:1 correspondence between authored node ids and
   runtime element ids, which inspection, alterations (ADR 0049), and provenance all depend on.
2. **Flowchart's default join is implicit; BPMN's is explicit.** Any Flowchart node with more than one
   inbound connection joins (`ShouldWaitForImplicitJoin` returns early at `<= 1`). That is a deliberate
   authoring affordance: users draw a diamond-free graph and get correct synchronization. Expressing it
   in BPMN requires materializing an inclusive gateway at every convergence, which changes what the
   user sees and what they can export.
3. **Flowchart carries `ExecutionScope`, and BPMN should not.** Flowchart's scopes are tied to Elsa's
   per-iteration variable scoping (ADRs 0027 and 0028) and its race/cancel model. `Bpmn.Semantics` has
   no analog and gains nothing by acquiring one; its ADR 0001 commits it to being host-agnostic and
   spec-faithful, and a non-BPMN element family would compromise both.

**The sequencing point that actually matters.** ADR 0063 (BPMN moves to `github.com/valence-works/bpmn`)
is *Proposed*, not accepted. After it executes, any shared abstraction between the two engines becomes
a cross-repository, host-agnostic third package, and the cost of extraction roughly triples. So the
decision about what, if anything, to share must be made **before or with** 0063, not after. If nothing
is shared, that should be a recorded decision rather than an outcome.

**My answer.** No, do not make Flowchart a BPMN dialect. Yes, extract the two genuinely shared pure
functions before 0063 executes:

- the activation-aware join predicate: given a graph reachability oracle, a set of live work, a target,
  and a partition key, decide whether to wait;
- the deterministic sequence-and-id minting discipline.

Both are small, both are pure, both are the highest-risk code in each engine, and both are neutral
enough to live below a BPMN library without polluting it. Everything else should stay duplicated on
purpose, because the two models genuinely differ.

**On Elsa 3 ADR 0007.** Treating it as a tested hypothesis is the right reading, and it failed for a
specific reason worth naming. `MergeMode` put the join decision on an *enum chosen by the author*, with
five values whose distinctions (`Stream` vs `Merge` vs `Converge`) are differences in which inbound set
is consulted. That is a knob describing an implementation detail, and its own ADR admits the modes were
reverse-engineered from bugs ("premature scheduling in forks", "stalling in loops", "inconsistent
merges"). It rejected real gateways as "overkill for Elsa's simplicity". What Elsa 4 shows is that the
alternative to five hand-tuned modes is not five gateway activity types either; it is **one
activation-aware join computed from the graph**, with policies as an extension point for the cases that
genuinely differ. Elsa 4's default Flowchart join needs no author declaration at all, and that is the
part 0007 got wrong: it was a configuration answer to a computation problem.

### 5.5 Where Elsa loses time that the others do not, and vice versa

**Elsa loses time on:**

- **The durable-value `ListAsync` per invocation.** ADR 0031 documents it: the invoke handler projects
  variables, inputs, identity, and upstream outputs from the durable value store before the activity can
  bind inputs. "Against a real store this is a query per activity." Zeebe reads variables from RocksDB
  in the same process in microseconds. This is structural: Elsa has no authoritative in-process copy of
  variable state, by design, because the burst cache is reconstructible-only and must never become the
  source of truth. The cost is the price of that invariant.
- **The terminal-status re-read per dispatched item.** `IsWorkflowTerminatedAsync` loads the execution
  state document after every dispatch. The drainer already fights this (the `#293` comment explains why
  it is post-dispatch only and not per-peek), but on a durable provider it is still a document load per
  item.
- **Several serialized store round-trips per run, of which the commit is only one.** Coalescing takes
  a run to one commit, which is the floor per run. Cross-execution batching exists (spec 115) and folds
  those commits, but the wall clock does not move, because the marker pre-read and the root-write-lease
  acquire and release serialize on the same connection gate. Zeebe pays one sequential append per
  command and amortizes the fsync across many; Elsa pays several random-access round-trips per run and
  can currently only amortize one of them.
- **Full hop cost for `External` activities** (§4.5).

**Elsa gains time on:**

- **No broker hop.** A Zeebe workflow's simplest path is client → gateway → broker → log → processor →
  job stream → worker → back. Elsa's is a method call. For a synchronous HTTP-triggered workflow the
  measured warm p95 is 38.5 ms end to end
  ([runtime HTTP performance](runtime-http-performance-2026-07.md)); Zeebe's published target is "under
  one second for the 99th percentile" for one instance
  ([blog](https://camunda.com/blog/2023/03/zeebe-batch-processing/)), though these are not comparable
  workloads and I am not claiming they are.
- **No export lag.** State is queryable the moment it commits.
- **A whole workflow in one transaction.** With coalescing, a 10-activity `ReplaySafe` run commits once.
  Zeebe cannot do this across a wait state; Camunda 7 cannot do it across an async boundary. For short
  synchronous workflows this is a category difference, and it is the measured 13 commits → 1 result.

### 5.6 Poison, retries, backpressure

| | Elsa 4 | Zeebe | Camunda 7 |
|---|---|---|---|
| Bad work item | Ack-deleted, poison record written, retry per `IRuntimeDomainRetryPolicy` (default `Noop` ⇒ `DoNotRetry` ⇒ `Poisoned`), then projected to a blocking `Critical` incident by `PoisonedSchedulerWorkIncidentObserver` | Command rejected, error event published, instance banned but still queryable | Retry counter decremented (default 3), incident at zero |
| Effect on siblings | Activity fault: drain continues, siblings run in the same burst. Handler fault: drain breaks, siblings wait ~10 s for the sweep | Partition continues immediately | Executor continues immediately |
| Fault-to-instance escalation | Authored per workflow: `FaultIncidentStrategy` (default) or `ContinueWithIncidentsIncidentStrategy`, applied at quiescence | Fixed: the instance is banned | Fixed: instance survives with an incident |
| Effect on other instances | None (per-execution actors) | None (instance banned, partition healthy) | None, except shared acquisition-query pressure |
| Repeat-offender fairness | **Good**: per-execution geometric backoff in `RuntimeResumptionPumpTask` excludes a poisoned execution from subsequent sweeps so it "cannot occupy a re-drive slot on every tick and starve healthy executions" | Banned, so it never competes again | Retries exhaust, then the incident stops re-acquisition |
| Load shedding | **None.** The store's connection gate queues without bound; the only per-tick caps are on the recovery sweep, not live dispatch | Adaptive per-partition limiter rejects commands | Bounded queue, acquisition backoff to 60 s |

Elsa's per-execution backoff in the resumption pump is genuinely well designed and is the one fairness
mechanism it has. Its surfacing is also better than I expected before tracing it (§4.7): a poisoned work
item becomes a blocking `Critical` incident with the handler, failure count, and fault detail attached,
deduplicated on a deterministic id so operator resolution sticks.

What it does not have is anything that stops *healthy* load from destroying throughput, which is the
more common failure. Note where the three columns actually diverge: on a *bad* item all three contain
the damage, and Elsa arguably contains it best because the blast radius is one execution rather than one
partition or one shared acquisition query. On *good* items arriving too fast, Elsa is the only one of
the three with no answer at all.

---

## 6. Ranked recommendations

Cost is rough, in the repo's usual work-unit sizing: S is a bounded change with tests, M is a spec-sized
unit, L is a multi-unit program.

### R1. Admission control at the durable-writer boundary. Cost: M, and lower than first sized.

Meter concurrent drains (or checkpoint commits) against a configurable limit and **shed or defer beyond
it, rather than queueing**. That last clause is the whole recommendation: §4.1's verification showed the
queueing already happens, on Groundwork's unbounded `SemaphoreSlim(1, 1)` connection gate, and that
unbounded queue is the collapse mechanism. Adding another queue changes nothing. The limit has to
produce a refusal or a deferral that the caller can see.

Verification also lowered the cost. `RuntimeResumptionOptions` already implements this pattern for the
recovery path — per-tick caps, a whole-sweep backoff, and a per-execution skip window — with a doc
comment stating the intent verbatim: bound the work "so a large backlog or a poisoned execution cannot
overwhelm the host." R1 is porting that to live dispatch, not inventing it.

*What would have to be true:* that real deployments run more than a handful of concurrent executions
against one store. If Elsa's typical deployment is a dozen long-running human-in-the-loop workflows,
this is theoretical. I believe it is not, but that belief is not evidence, and the cheapest way to
settle it is to instrument concurrency in a real workload before building the limiter.

*The open design question,* which I have not resolved: what a shed request should look like to a caller
who dispatched synchronously over HTTP. `WorkflowExecutionCommandDispatchStatus` has `Deferred`, which
the distributed leaf already uses for "accepted for routing, not run locally", and that is probably the
right shape — but it changes an endpoint's contract from "your workflow ran" to "your workflow is
queued", and that is a product decision, not an engine one.

### R2. Attack round-trip count per run, not commit count. Cost: M.

**Correction to an earlier draft of this report: group commit already exists.** Spec 115 shipped
`RuntimeGroupCommitCoordinator`
([src](../../src/Elsa/Persistence/Groundwork/Stores/RuntimeGroupCommitCoordinator.cs)), a flush-pipeline
group commit that folds concurrent checkpoint commits into one shared unit-of-work and one fsync. It is
opt-in via `AddGroundworkRuntimeGroupCommit` and **default off**, and the reason is measured: on a quiet
machine "no level's ratio distribution is statistically distinguishable from 1.0"
([spec 115 research](../../specs/115-group-commit-fsync-sharing/research.md)). The batching itself works
(at N=128, 119 to 123 of 128 commits fold into 9 to 18 transactions), but folding the fsyncs does not
move the wall clock on WAL-mode SQLite.

The reason is the finding that supersedes my recommendation, and spec 115 states it: the checkpoint
commit "is only one of several per-run store round-trips (marker pre-read + root-write-lease
acquire/release also serialize on the same SQLite connection gate)." Group commit folds the commits and
leaves the other serialized round-trips alone. So the remaining lever is **reducing serialized store
round-trips per run**, for example folding the lease touch and the marker read into the checkpoint
transaction itself.

*What would have to be true:* that the marker pre-read can be eliminated or moved inside the
transaction without losing the idempotent-replay reconciliation it exists to support, which is the same
mechanism that makes group commit's all-or-nothing rollback safe. That coupling is the hard part.
Independently, the Postgres counterfactual still does not exist, and if MVCC removes most of the gap
this whole line is SQLite-specific.

*Also worth saying:* group commit remains valuable where it was designed to pay, and spec 115 says so:
stores with expensive per-commit fsyncs (`synchronous=FULL`, network filesystems, a provider without
WAL). It is a correct feature with an honest default, not a dead end.

### R3. Bound both renewal loops so a hung dispatch can be revoked. Cost: S.

Give `RenewClaimUntilStoppedAsync` and `RenewOwnershipUntilStoppedAsync` a maximum *total* duration
(distinct from their renewal intervals), and cancel `dispatchCancellation` / `drainCancellation` when it
is exceeded. Both loops already hold the cancellation source they would need to trigger; this is adding
a ceiling to an unbounded loop in two places and deciding what the default is.

Verification (§4.2) both confirmed and enlarged this: it is two loops, not one, and fixing only the work
claim would leave the ownership heartbeat still masking the hang from the recovery scanner. They must be
bounded together or the fix does nothing observable.

Resolve the ceiling the way `IRuntimeVolatileWaitPolicy` already resolves a declared wait's
`maximumDuration`: a policy with a host default, overridable per activity contract. That existing policy
is the precedent for the shape, so the design question is narrower than it looks.

*What would have to be true:* that activities cooperate with cancellation. They receive a
`CancellationToken`, so the mechanism is there, but an activity blocking in unmanaged code will not
respond, and the design has to decide what to do then. Two defensible answers: stop renewing and let the
claim lapse so a survivor can take the work (correct for the store, but the stuck thread is still stuck),
or poison the item and surface an incident. I would do both, in that order.

This remains the highest value-per-cost item on the list, and verification raised its value: an
unbounded hang is currently indistinguishable from healthy work at every layer that could detect it.

### R4. Decide the Flowchart/BPMN sharing question before ADR 0063 executes. Cost: S to decide, S to
extract.

Extract the activation-aware join predicate and the sequence-id discipline into one neutral place, or
record explicitly that they stay duplicated and why. Either outcome is fine; drifting into the split
without deciding is not.

*What would have to be true:* that ADR 0063 has not been executed. It is currently Proposed, so this is
a sequencing window that closes.

### R5. Decouple inspection projection from the checkpoint commit. Cost: L.

Move inspection evidence onto the post-commit outbox (ADR 0020's existing seam) and let a projector
consume it, so commit cadence and observability granularity stop sharing a dial. This is the one Zeebe
idea genuinely worth importing, in the narrow form: take the exporter's *decoupling*, not its log.

*What would have to be true:* that consumers accept eventual consistency for inspection reads. ADR 0032
R3 already forces them to accept coarsened reads, so the question is whether "delayed but complete" is
better than "immediate but coarse". I think it is, but this is a product judgment, not a technical one.
Also: this only pays off if it lets coalescing be turned on where it currently is not, so it should be
sized against how often `Immediate` is chosen for observability reasons rather than durability ones.

### R6. Re-arm the sweep immediately after a handler fault. Cost: S.

Narrower than I first sized it, because §4.3's verification showed the `break` is only reachable on a
handler-level infrastructure fault, not on an activity fault. On that path, nudge the resumption pump
for that execution instead of leaving its queued siblings to wait out `SweepInterval` (default 10 s).

*What would have to be true:* that the pump can be signalled per execution without giving up the
per-execution backoff that keeps a repeat offender from monopolizing sweep slots. The nudge must be
subject to the same backoff, or it becomes the hot-loop the ack-before-poison ordering exists to
prevent.

I have dropped the more ambitious version (keep draining siblings whose work is independent of the
faulted item). It was premised on ordinary activity faults reaching the `break`, which they do not, so
its value is a fraction of what I assumed and its precondition — deciding "independent branch" from
queued items — is unchanged in difficulty.

### R7. Make a runtime hop reproducible from a serialized tuple. Cost: M.

Capture (work item, execution state, durable-value snapshot, pinned executable id) on fault, so a
production failure can be replayed in a test with no host. This is `Bpmn.Semantics` ADR 0002's property
applied to the layer that does not have it.

*What would have to be true:* that the durable-value snapshot is bounded enough to capture. For large
external values it is not, and the capture would have to record references rather than contents, which
weakens the "replay offline" claim.

### R8. An opt-in external-work activity family. Cost: L.

A job-worker protocol over the existing bookmark and stimulus machinery: activate with a lease, complete
or fail with a retry count, time out and re-offer. Not a change to how ordinary activities run.

*What would have to be true:* that there is real demand for out-of-process or polyglot activity
implementations. Every argument I can make for this is an argument from what Camunda users do, not from
what Elsa users have asked for, so it should not be built until someone asks. Listed here because §5.2
concluded that two of pull's three benefits are obtainable without it, and this is what the third
would cost.

### R9. Make the intrinsic fusion exclusion per-kind. **Done.**

Both halves of the original R9 have now been executed, and the second one is what mattered.

**The marking pass produced one attribute.** `Break` is marked `ReplaySafe`; every other shipped leaf is
correctly `External`, blocked by ADR 0063, or valueless to mark. The classification table is in §4.5. It
was worth doing to learn that it is not the lever.

**The lever was the blanket intrinsic exclusion,** one line in `ReplaySafeFusionDriver.ShouldFuse`
(`&& node.IntrinsicKind is null`). It is now per kind:

| Kinds | Fusable | Why |
| --- | --- | --- |
| `Set`, `Merge`, `Reduce` | Yes | Write a variable frame — in-workflow state a segment replay reproduces |
| `Return`, `Control` | Yes | Set the invocation's result and outcome from a materialized binding |
| `SetOutput` | Yes | Writes a durable value whose state id is a pure function of the output name, so a replayed write folds last-writer-per-state-id onto the same value |
| `SetCorrelationId`, `SetInstanceName` | No | Mutate externally queryable identity; correlation is a message-correlation key |
| `Finish` | No | Terminates the run and commits the unconditionally mandatory `WorkflowCompleted` |

**`SetOutput` was held back in a first revision, and that was a mistake worth naming.** The stated reason
was that a workflow output is "published for the caller" and so belongs on ADR 0032's mandatory side.
That conflated two independent dials. *When* an output becomes durable is decided by the checkpoint
persistence policy, and `IntrinsicCompleted` is not in
`CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames` — so under coalescing a
`SetOutput` was **already deferring and folding into the segment flush**, with or without fusion. Fusion
decides only how many dispatches the node costs. Excluding it protected nothing about output visibility
and bought only hops. A host that needs an output durable the instant it is written selects
`Mode = Immediate`; that lever is untouched.

**What shipped:**

- `WorkflowIntrinsicFusion.IsFusable` next to the kind enum. It is a pure function of the *pinned*
  `ExecutableNode.IntrinsicKind`, so it needs no synthetic `ActivityContract` and moves no artifact hash
  — this is where I revise my own earlier wording, which asked for a compiled profile. The kind is
  already on the artifact; adding a second carrier would have churned every pinned executable for
  nothing. Its fall-through is non-fusable, so a new kind is durable-by-default until classified.
- `ExecuteFusedStartAsync` gained the intrinsic shape. This was the real work: **an intrinsic has no
  invoke stage** — its start stage is the entire execution — so the fused span had to grow a third
  outcome. The return type is now a `FusedStartResult` (`Fallback` / `CompletedAtStart` / `Invoke`), and
  the driver skips the invoke dispatch and goes straight to the D2 completion pump. The completion work
  item rides the same post-commit intent the discrete path emits.

**Evidence.** Two guardrail shapes added to `ReplaySafeFusionGuardrailTests`: a `Set`-intrinsic hot loop
proving fusion-ON commits byte-identical durable state to fusion-OFF while engaging on every node and
paying strictly fewer dispatches, and a `SetOutput` line proving a non-fusable kind never fuses and still
commits identically. Plus a per-kind classification theory with a reflection-based completeness check, so
adding a kind without classifying it fails a test rather than silently inheriting the fall-through. Five
new kill points in `ReplaySafeFusionCrashConvergenceTests` walk the intrinsic span — inside the fused
intrinsic, in the inline completion pass between intrinsics, and across the D2→D1 recursion boundary —
and all converge to the crash-free terminal.

Suites run green: 270 activities-runtime, 1564 workflows-runtime, 752 Groundwork, 358 architecture, and
the engine benchmarks (which also cover the R9b instrument change).

**One correction to my own §4.5 reasoning, found while implementing.** I had assumed intrinsics paid a
mandatory flush per node because every intrinsic commit is stamped
`CheckpointRequirement = Mandatory`. They do not. `IntrinsicCompleted` is absent from
`CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames`, and the committer's
mandatory guard only forbids `Skip`, not `Deferred` — so intrinsic completions were already being
coalesced. The win from this change is hop count, not commit count, which makes it smaller than the
framing in §4.5 implied. Worth stating plainly rather than leaving the earlier implication standing.

### R9b. Measure an `External`-dominated workflow. **Done.**

`Durable_Sqlite_HotLoop_ProductionShapes` reports the three shapes a workflow is actually built from,
under one policy (Coalesced, cap 256) so the leaf class is the only variable. The benchmark also now
captures **scheduler dispatches per run** — the deterministic hop-count evidence behind ADR 0047's
`58 → 36 → 5`, which `MeasureAsync` had never recorded.

hot-loop×10, durable SQLite, Coalesced. Counts are deterministic and identical across repeat runs; wall
times are from a machine at load ~2.4–3.3 and carry the usual caveat:

| Leaf shape | commits/run | dispatches/run | p50 |
|---|---:|---:|---:|
| **External CLR leaf** (`WriteLine`, unmarked) — the production shape | 11 | **56** | ~259 ms |
| **`Set` intrinsic** — newly fusable via R9 | 1 | **5** | ~37 ms |
| **`SetOutput` intrinsic** — newly fusable via R9 | 1 | **5** | ~39 ms |
| ReplaySafe CLR leaf (`NoOpStep`, benchmark-only) — the published best case | 1 | 5 | ~79 ms |

**Three things this settles.**

1. **§4.5's claim was right, and the margin is the whole story.** A real workflow of `External` leaves
   pays **56 dispatches**, which is ADR 0047's *pre-fusion* figure of 58. The headline `5` was never
   reachable by anything a user could compose. The production shape costs 11× the commits and 11.2× the
   dispatches of the number the campaign reports.
2. **R9 moved a real workflow onto the floor.** `Set` and `SetOutput` intrinsic loops both hit 1 commit
   and 5 dispatches — the same floor as the benchmark-only leaf, and reachable, because intrinsics are
   what `Set`/`Merge`/`Reduce`/`SetOutput` authoring compiles to.
3. **An unexpected result worth acting on: the intrinsic is ~2.2× faster than the fused CLR leaf at
   identical commits and dispatches** (~35.7 ms vs ~79 ms). Hop count and commit count are equal, so the
   difference is everything a CLR activity costs *inside* a hop: DI activation, input-snapshot
   materialization, the attempt claim. That makes "express pure loop bodies as intrinsics, not
   activities" a measured performance recommendation rather than a stylistic one, and it suggests the
   next engine lever is the per-hop CLR activation cost rather than the hop count, which R9 has now
   floored.

*Sequencing note, unchanged:* this belonged before R1 and R2, and it earns that placing — the
concurrency curve in §4.1 was measured with the `NoOpStep` leaf, so it describes the fusable floor and
not the `External` shape most load will actually have.

### R10. Ship a bounded default retry policy for infrastructure faults. Cost: S.

`NoopRuntimeDomainRetryPolicy` is a deliberate `DoNotRetry`, so the first infrastructure fault in a
handler goes straight to a blocking incident (§4.7). Both incumbents retry first: Camunda 7 three times
by default, Zeebe on a client-supplied count, with an incident only on exhaustion. A small bounded
`RetryAfter` default (the disposition and the `NextRetryAt` re-drive path already exist and are honored
by `RuntimeResumptionPumpTask`) would match that without new machinery.

*What would have to be true:* that scheduler-handler faults are usually transient. I do not know that
they are. A handler fault is a fault in Elsa's *own* dispatch code, not in user activity code, and a
deterministic bug there would retry pointlessly before landing in the same incident. That is the
argument for the current default, and it is not a bad one. The evidence to settle it is what the poison
records in a real deployment actually contain, which nobody has looked at.

---

## 7. Non-recommendations

These look attractive from the comparison and would be wrong.

### N1. Do not adopt partitioning.

Zeebe partitions because it refused a shared database and therefore had to invent ordering and bounded
writers from scratch. Elsa has a shared database. Adopting partitions would import: a rebalancing
protocol, a "process instance can never move" constraint, and a cross-partition correlation problem
(Zeebe's message correlation must route by correlation key precisely because instances are pinned).
In exchange it would deliver a bounded writer count and a place to hang admission control, both of which
R1 and R2 deliver without any of that machinery. The per-execution actor is a *better* granularity for
Elsa's shape, not a worse one.

### N2. Do not make an append-only command log the source of truth.

It is the most seductive idea in Zeebe's design and the most expensive to import. Making the log
authoritative means state becomes a projection, which means every query needs a projection pipeline,
which means an exporter, which means a second store. That is exactly the step that turns Camunda 8 into
a cluster product you operate rather than a library you reference, and it is the opposite of Elsa's
positioning (§3.5). Take the decoupling (R5); leave the log.

### N3. Do not flip to pull-based workers as the default model.

Elsa's value proposition is that you write a C# activity in your own application and it runs. A pull
model means you write a worker, register it against a topic, and manage its lifecycle. That is a
different product. The parts of pull that are actually about robustness (backpressure, absolute
timeouts) are obtainable in a push model and are R1 and R3.

### N4. Do not re-express Flowchart over the BPMN library's port.

Argued in §5.4. The short version: Flowchart nodes are simultaneously tasks and gateways, its joins are
implicit, and it carries an execution-scope model tied to Elsa's variable scoping. Any of the three can
be worked around; all three together mean the mapping would be a translation layer with its own bugs,
sitting between the author's diagram and the runtime, and it would compromise the BPMN library's claim
to be a BPMN library. Extract the two shared pure functions (R4) instead.

### N5. Do not add concurrent drain within a single workflow execution.

ADR 0031 ratified single-writer-per-execution as the intra-workflow parallelism ceiling, and it is
right. Both incumbents independently reached the same conclusion: Zeebe by making the stream processor
a single thread, Camunda 7 by defaulting to exclusive jobs ("Jobs from a single process instance are
never executed concurrently"). Three systems converging on the same restriction is strong evidence it
is load-bearing. Parallelism scales across executions.

### N6. Do not spend another unit on per-hop micro-costs.

Spec 109's Step-0 profile measured the per-hop JSON round trip at approximately 2% of an in-memory hop
and 0.36% of a durable hop, and scope construction at approximately zero. The remaining costs are hop
*count* (addressed by ADR 0047), fsync (addressed by ADR 0032, floor now reached per run), and store
contention (not addressed). Anything smaller is noise, and the evidence to say so already exists.

### N7. Do not add author-facing transaction boundaries in the Camunda 7 style.

`asyncBefore` / `asyncAfter` gives fine control and an unsafe default. Elsa's `SideEffectProfile` gives
coarser control and a safe default, and coarser is the right trade because the runtime, not the author,
knows what a boundary costs. If per-activity control is ever needed, it should be an override on top of
the profile, not a replacement for it.

---

## 8. What I did not verify

Stated so nobody treats these as established.

**A pattern worth recording first.** Every §4 finding was verified against source at the reader's
request. Three did not survive: the group-commit fencing question (§3.2, R2), the poison
surfacing path (§4.7), and the drain-stops-on-fault claim (§4.3). All three failed the same way, and the
reason is structural: in each case the safety mechanism lives in a *collaborator* the hot path does not
name — a drain observer, a DI-registered default policy, a handler-internal catch. Reading
`WorkflowSchedulerDrainer` end to end tells you almost nothing about what a fault actually does to a
workflow. That is a legitimate separation (the drainer should not know about incident strategies), but
it means the scheduler's code is not a reliable guide to the scheduler's behavior, and any future review
should start from the observer and default-registration lists rather than the loop.

Four were **confirmed, and every one grew**:

- §4.2, the unbounded lease, is two renewal loops rather than one, and the recovery scanner's only two
  candidacy signals are precisely what those loops keep fresh.
- §4.1, the missing admission control, has a mechanism after all: a `SemaphoreSlim(1, 1)` store
  connection gate that queues without bound. Not a defense, the collapse mechanism itself.
- §4.5 is not "the benchmark is optimistic." No shipped leaf activity is marked `ReplaySafe`, so ADR
  0047's fusion cannot apply to any workflow built from built-ins, and the benchmark's fusable leaf
  lives in the benchmark assembly.
- §4.6 is a regression, not an absence. Elsa 3's `RunTask` was inventoried as missing and never ported.

The distinction that predicted which way each finding went, for all seven, is whether the thing I called
missing is *elsewhere* or genuinely *absent*. The three refuted findings were behavior living in a
collaborator the hot path does not name. The four confirmed ones are concepts the codebase does not
express on the path in question: a maximum dispatch duration, a shed-rather-queue admission decision, a
marked pure leaf, an out-of-process work contract.

**And the sharpest recurring detail: in three of the four confirmed gaps, Elsa already has the shape it
needs, applied somewhere else.** `IRuntimeVolatileWaitPolicy` bounds a declared wait but not an
involuntary one (§4.2). `RuntimeResumptionOptions` bounds the recovery path but not live dispatch
(§4.1). `WorkflowAlterationModels` carries an at-least-once worker lease for alterations but not for
activities (§4.6). These are not missing designs. They are designs that stopped one path short, which is
both why they are cheap to finish and why they were easy to miss.

A corollary worth stating, because it cuts against my own §3.3 praise: none of the four surviving gaps is
of a kind the byte-identical guardrail discipline can catch. That discipline proves an optimization does
not change committed state. It says nothing about whether the system degrades gracefully, nor about
whether an optimization is *reachable* — §4.5 is exactly a case where the guardrail passes perfectly and
the optimization still applies to nothing a user can build.

- I did not read Zeebe's source, only its documentation and Camunda's engineering blog. The claim that
  the stream processor is a single thread per partition comes from Camunda's own descriptions, not from
  code.
- The latency comparison in §5.5 puts an Elsa HTTP measurement next to a Zeebe published target. They
  are different workloads on different hardware and the comparison is illustrative only.
- The Postgres concurrency counterfactual does not exist. Spec 114's Postgres run was contaminated by
  host load and capped at N=32 by connection limits, so "the single writer is the ceiling" is
  established for SQLite and assumed for everything else.
