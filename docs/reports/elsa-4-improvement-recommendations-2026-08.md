# Elsa 4 improvement recommendations, August 2026

Date: 2026-08-10
Status: recommendations. Nothing here is a decision; each item is written to be turned into an issue.

## How to use this

Every item below carries the same fields so a later session can lift one into an agent-ready GitHub issue
without re-deriving it: **Problem** (with file paths or measurements), **Do**, **Start at**, **Done when**,
**Cost**, **Confidence**, and where it matters **Not in scope**. Cost is the repo's usual sizing: S is a bounded
change with tests, M is a spec-sized unit, L is a multi-unit program.

Confidence is stated per item because it varies a lot:

- **Measured** — a number from a benchmark run in this repo.
- **Read** — verified by reading the code named in the item.
- **Inferred** — a conclusion I believe but did not run or read end to end.

Most of the evidence comes from [the execution model comparison](execution-model-comparison-2026-08.md), which
compared Elsa 4's drain and checkpoint machinery against Zeebe, Camunda 7 and the BPMN specification, and whose
findings were then each verified against source. Read that first for the reasoning; this document is the backlog
it produced.

**Already filed, do not re-file:** intrinsic hop fusion and the production-shape benchmark
([#1214](https://github.com/elsa-workflows/elsa-foundation/pull/1214), merged); the bounded dispatch deadline
([#1217](https://github.com/elsa-workflows/elsa-foundation/pull/1217)); Flowchart dead-path joins
([ADR 0064](../adr/0064-flowchart-infers-joins-from-propagated-dead-paths.md),
[#1220](https://github.com/elsa-workflows/elsa-foundation/pull/1220),
[#1221](https://github.com/elsa-workflows/elsa-foundation/issues/1221)).

---

## R0. The pattern behind several of these: designs applied one path short

Before the list, the finding that connects much of it. Elsa 4 repeatedly builds a correct mechanism and leaves it
one wire short of the path that needed it. This is not an outside observation; two of the repo's own ADRs exist
because of it.

- [ADR 0029](../adr/0029-runtime-execution-flows-through-the-pipelines.md) exists because the runtime pipeline was
  fully defined and **nothing invoked it**. A module registering middleware was silently never run.
- [ADR 0032](../adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) exists because
  `RuntimeCheckpointPersistenceMode.Deferred` was defined, documented as policy-driven, and **unreachable**: the
  only registered policy returned `Immediate` unconditionally.

Four more surfaced while writing the comparison:

| Mechanism | Applied to | Not applied to | Consequence |
| --- | --- | --- | --- |
| `IRuntimeVolatileWaitPolicy` (bounded wait, `maximumDuration`) | a *declared* in-memory wait | an involuntary one | A hung dispatch renewed its claim and heartbeat forever and was invisible to the recovery scanner |
| `RuntimeResumptionOptions` (per-tick caps, backoff) | the *recovery* sweep | live dispatch | No admission control anywhere on the path that carries production traffic |
| At-least-once worker lease (`WorkflowAlterationModels`) | *alteration* jobs | activity execution | No out-of-process work contract |
| `SideEffectProfile` marking | control-flow composites | leaf activities | ADR 0047's hop fusion reached nothing a user could compose |

**Recommendation.** Make "is this reached?" a shipping requirement for new seams. ADR 0029 already invented the
fix and used it once: a test asserting that a registered marker middleware is *actually invoked during a real
work-item dispatch*, not merely that the builder accepted it. Generalise that into a standing rule: a new
extension point, policy seam, or persistence mode ships with a test proving it is reached from the production
path under **default** configuration.

This is also the gap in the byte-identical guardrail discipline, which is otherwise the strongest engineering
practice in the runtime. A byte-identical guardrail proves an optimisation does not change committed state. It
cannot tell you whether the optimisation ever *applies*, and it says nothing about behaviour under load. Every
finding that survived verification was one of those two kinds.

**Do:** add the reachability rule to the contribution guidance and to the ADR template's follow-up checklist.
**Cost:** S. **Confidence:** Read.

---

## Performance

### P1. Cut per-hop CLR activation cost

**Problem.** At *identical* hop and commit counts, a fused CLR leaf runs roughly twice as slow as a fused
intrinsic. From `Durable_Sqlite_HotLoop_ProductionShapes` (hot-loop x10, durable SQLite, Coalesced cap 256;
counts deterministic and reproduced across runs):

| Leaf shape | commits/run | dispatches/run | p50 |
| --- | ---: | ---: | ---: |
| External CLR leaf (`WriteLine`) | 11 | 56 | ~259 ms |
| `Set` intrinsic | 1 | 5 | ~37 ms |
| `SetOutput` intrinsic | 1 | 5 | ~39 ms |
| ReplaySafe CLR leaf (`NoOpStep`) | 1 | 5 | ~79 ms |

Rows 2 and 4 pay the same 5 dispatches and 1 commit. The whole difference is what a CLR activity costs *inside*
a hop: DI activation, input-snapshot materialisation, and the attempt claim. Hop count is now floored by
[#1214](https://github.com/elsa-workflows/elsa-foundation/pull/1214), so this is the only remaining structural
lever for workflows built from CLR activities, which is all of them.

**Do:** profile the fused CLR leaf against the fused intrinsic to attribute the ~42 ms, then attack whichever
component dominates. Likely candidates in order of suspicion: per-activity DI scope and activation
(`ActivityActivationLease`, `ActivityActivationFailureHandler`), input-snapshot materialisation
(`ProduceRunningStateAsync` and the durable-value `ListAllDurableValueStatesAsync` projection), and the attempt
claim (`ActivityAttemptActivationClaimer`).

**Start at:** `benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineExecutionBenchmarks.cs`
(`Durable_Sqlite_HotLoop_ProductionShapes`), `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`,
`src/Elsa/Workflows/Runtime/Services/WorkflowStartActivitySchedulerWorkHandler.cs`.

**Done when:** the ~42 ms is attributed to named components with a profile, and either a fix lands with the
benchmark showing the gap narrowing, or the gap is documented as irreducible with the reason.

**Cost:** M, profile first. **Confidence:** Measured (the gap), Inferred (the attribution).

### P2. Re-measure the concurrency curve with an `External`-dominated workflow

**Problem.** [Spec 114](../../specs/114-concurrency-throughput-instrument/research.md)'s N in {1, 8, 32, 128}
curve, which is the entire evidence base for the shared-writer bottleneck, was measured with `NoOpStep`: a
`ReplaySafe` leaf that exists only in the benchmark assembly. It therefore describes the fusable floor, not the
shape production traffic has. The `External` shape pays 11x the commits and 11.2x the dispatches (P1's table), so
the curve's shape under realistic load is unknown.

**Do:** re-run the spec 114 curve with an `External` leaf, and with a `Set`-intrinsic leaf as the third column now
that intrinsics fuse. Report commits and dispatches per run alongside wall time.

**Start at:** `benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineConcurrencyBenchmarks.cs`,
`BenchmarkWorkflows.SetIntrinsicLeaf` / `NewWriteLineNode`.

**Done when:** the curve exists for all three leaf shapes and RB1's sizing rests on the `External` numbers.

**Cost:** S. **Confidence:** Read. **Sequencing:** do this before RB1.

### P3. Reduce serialized store round-trips per run

**Problem.** [Spec 115](../../specs/115-group-commit-fsync-sharing/research.md) shipped group commit, measured it,
and set the default to off because the wall-clock win did not survive a quiet machine. Its own closing note names
why: the checkpoint commit is only one of several per-run round-trips that serialize on the same connection gate.
The marker pre-read and the root-write-lease acquire/release serialize there too. Folding the commits leaves the
rest.

**Do:** fold the lease touch and the marker read into the checkpoint transaction, or otherwise remove them from
the serialized path.

**Start at:** `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs`
(`ApplyAtomicallyAsync`, `LoadMarkerAsync`, `ValidateAndTouchExpectedFenceAsync`, the root-write-lease wrapper).

**Done when:** per-run serialized round-trips are counted before and after, and the count drops.

**Not in scope:** removing the idempotent-replay reconciliation the marker pre-read exists to support. That
mechanism is also what makes group commit's all-or-nothing rollback safe.

**Cost:** M. **Confidence:** Read (the mechanism), and spec 115 states it directly.

### P4. Re-evaluate group commit where it was designed to pay

**Problem.** Group commit is implemented and off by default on measured evidence, but the measurement was taken on
WAL-mode SQLite, where the fsync it amortises is cheap. Spec 115 explicitly says to re-run the instrument where
per-commit fsyncs are expensive before deciding.

**Do:** run the group-commit A/B under `synchronous=FULL`, on a network filesystem, and on PostgreSQL, then either
change the default for those configurations or record that opt-in remains correct everywhere.

**Start at:** `src/Elsa/Persistence/Groundwork/Stores/RuntimeGroupCommitCoordinator.cs`,
`GroundworkGroupCommitRegistration.AddGroundworkRuntimeGroupCommit`, spec 115's instrument.

**Done when:** the decision is evidence-backed per storage configuration rather than per repo.

**Cost:** S. **Confidence:** Read.

### Performance non-goals

Do not spend a unit on per-hop micro-costs. Spec 109's Step-0 profile measured the JSON round-trip at ~2% of an
in-memory hop and ~0.36% of a durable one, and scope construction at approximately zero. Hop count is floored.
The remaining costs are P1, P3 and the store itself.

---

## Complexity and architecture

### C1. Collapse telescoping constructors

**Problem.** `WorkflowDrainOrchestrator` has **six** public constructors; `RuntimeCheckpointCommitter` has
**four**. Each overload adds optional collaborators, so DI overload resolution decides which collaborators a
component gets, and a narrower overload silently disables behaviour. This is not hypothetical: the repo already
recognised and fixed it once. `WorkflowSchedulerDrainer` has exactly one constructor, and its XML doc records
that RT-8 collapsed seven into one, with the workflow execution state store made **required by construction** so
"the W5 terminal-status guard can never be silently disabled by picking a narrower constructor."

**Do:** apply the RT-8 shape to `WorkflowDrainOrchestrator` and `RuntimeCheckpointCommitter`: one primary
constructor, required collaborators first, optional ones defaulting to their no-op implementations. Sweep for
others.

**Start at:** `src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs`,
`src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs`, and
`src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs` for the target shape and rationale.

**Done when:** each type has one public constructor, the DI registrations resolve it explicitly, and any
collaborator whose absence would disable a guard is non-optional.

**Cost:** S. **Confidence:** Read (counts verified directly).

### C2. Make the default DI registrations inventoriable

**Problem.** `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` is 481 lines
containing **173 `TryAdd*` calls**. This is where runtime *behaviour* actually lives. Concretely: three of the
seven "worse" findings in the comparison report were wrong, and each was wrong because the mechanism I assumed
was missing was a registered default the hot path never names (a drain observer, a retry policy, an incident
strategy). If a careful reader working from the code gets it wrong three times out of seven, the default set is
not discoverable.

**Do:** split the file by concern, and add a test-asserted inventory: for each defaulted contract, the default
implementation and a one-line statement of what it does. Generate it or assert it, so it cannot drift.

**Start at:** `RuntimeCoreServiceCollectionExtensions.cs`; the observer registrations near the end
(`PoisonedSchedulerWorkIncidentObserver`, `IncidentStrategyResolutionDrainObserver`,
`BlockingIncidentWorkflowFaultObserver`) are the highest-value entries because they decide fault outcomes.

**Done when:** a reader can answer "what happens by default when a handler faults" from one artifact, and a new
default cannot be added without appearing in it.

**Cost:** M. **Confidence:** Read.

### C3. Write the rule for when a new project is warranted

**Problem.** 158 source projects for ~245k lines of source (~1,553 lines per project), plus 99 test projects for
~298k lines of tests. In fairness the subtractive-obligation work measured project count growing 2.25x against
LoC growing 6.32x, so fragmentation is not accelerating. But the absolute number is a standing tax on restore,
build, IDE responsiveness and orientation, and there is no stated bar for adding one, so no individual addition
can be argued against.

**Do:** state the rule. A candidate: a new project is warranted only when it needs a *different dependency
envelope*, a *separate release cadence*, or a *provider isolation boundary the architecture guard enforces*.
Anything else is a folder. Then apply it to new work, not retroactively.

**Start at:** the framework constitution's module rules and `docs/agents/domain.md`.

**Done when:** the rule is written and the architecture guard or a review checklist references it.

**Not in scope:** a consolidation sweep. Merging projects to hit a number, without the rule, just moves the
problem.

**Cost:** S to write, ongoing to apply. **Confidence:** Measured (counts), Inferred (that the tax is material).

### C4. Finish ADR 0029 Move 2 for the invoke handler

**Problem.** `WorkflowInvokeActivitySchedulerWorkHandler` is 1,443 lines and is the densest thing in the runtime.
ADR 0029 committed Move 1 (pipeline as the execution spine) and explicitly deferred Move 2 (relocating inlined
phases into slot-bound middleware), sequencing this handler **last** because of hazards it names: atomic
checkpoint-commit folding of durable-value write-back, transactional fault arms that must not split across slots,
control-leaf intents, container scope-completion capture, and the inspection-accumulator toggle.

**Do:** decompose it per ADR 0029's addendum model (handler runs inside a core `Invoke` slot, stages results on
the workspace, later slots apply them), one phase at a time, each its own approved change.

**Start at:** `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`,
[ADR 0029](../adr/0029-runtime-execution-flows-through-the-pipelines.md) addendum,
[runtime pipeline wiring sizing](runtime-execution-pipeline-wiring-sizing.md).

**Done when:** each relocated phase is byte-identical by the existing guardrail harness, and the handler shrinks
measurably.

**Cost:** L. **Confidence:** Read.

---

## Developer experience

### D1. Make execution behaviour discoverable from the hot path

**Problem.** Reading `WorkflowSchedulerDrainer` end to end tells you almost nothing about what a fault does to a
workflow. An activity fault never reaches the drainer's fault arm at all (the invoke handler catches it and
records an incident); whether that incident terminates the workflow is decided by an authored `IIncidentStrategy`
applied by a drain observer at quiescence; a handler-level fault is poisoned and projected into a blocking
incident by a *different* observer. None of those collaborators is named by the loop. That separation is correct
design and a serious onboarding tax at the same time.

**Do:** either (a) add XML doc cross-links from the drain loop and dispatch arm to the collaborators that decide
outcomes, or (b) write an execution-behaviour map that starts from the observer list and the default
registrations rather than from the loop. (b) is more useful; (a) is cheaper and degrades less.

**Start at:** `src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs` (`DispatchAsync`,
`HandleHandlerCrashAsync`), `WorkflowDrainOrchestrator.NotifyObserversAsync`,
`src/Elsa/Workflows/Runtime/Services/PoisonedSchedulerWorkIncidentObserver.cs`,
`BlockingIncidentWorkflowFaultObserver`, `IncidentStrategyResolutionDrainObserver`.

**Done when:** "what happens when an activity throws" and "what happens when a handler throws" are each
answerable from one artifact, with the two paths distinguished.

**Cost:** S to M. **Confidence:** Read. This is the highest-leverage documentation work in the repo.

### D2. Sweep instruments for the counters that make their claims falsifiable

**Problem.** `MeasureAsync` in the engine benchmark recorded wall time, commits and executable reads, but not
**dispatches**, which is the deterministic, load-proof counter behind ADR 0047's headline `58 -> 36 -> 5`. The
instrument could not check its own campaign's central claim until it was added in
[#1214](https://github.com/elsa-workflows/elsa-foundation/pull/1214).

**Do:** audit the other instruments for the same shape. For each, ask what the deterministic counter is that
would falsify its claim, and record it.

**Start at:** `benchmarks/Elsa/Workflows/Runtime/Benchmarks/`,
`src/Elsa/Workflows/Runtime/Core/Diagnostics/RuntimeSchedulerDispatchDiagnostics.cs`,
`DurableRoundTripDiagnostics`, `RoutingStructureMaterializationDiagnostics`.

**Done when:** each instrument reports at least one load-proof counter alongside its wall times.

**Cost:** S. **Confidence:** Read.

### D3. Add a test builder for executable nodes

**Problem.** Constructing an `ExecutableNode` for a test requires roughly 15 to 25 lines of positional arguments,
and intrinsic nodes additionally need hand-built input bindings and a variable reference. This was written three
separate times in one work unit (fusion guardrail, crash convergence, benchmark), which is the usual sign.

**Do:** add a fluent builder to `Elsa.Activities.Testing` covering CLR leaves (with and without a side-effect
profile), intrinsic nodes per kind, and Flowchart composites over a set of leaves.

**Start at:** `tests/Elsa/Activities/Testing/WorkflowExecutionHarness.cs`,
`benchmarks/Elsa/Workflows/Runtime/Benchmarks/BenchmarkWorkflows.cs`,
`tests/Elsa/Activities/Runtime/Tests/ReplaySafeFusionGuardrailTests.cs` (the duplication).

**Done when:** the three call sites above use it and shrink.

**Cost:** S. **Confidence:** Read.

---

## User experience

Lowest-confidence section. I inspected the backend contracts but not Studio, which lives in a separate
repository.

### U1. Make `SideEffectProfile` discoverable to activity authors

**Problem.** An author writing a pure activity gets no throughput benefit unless they know to apply
`[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`. The marking pass in
[#1214](https://github.com/elsa-workflows/elsa-foundation/pull/1214) found that **no shipped leaf activity** had
it, so this is not only an external-author problem. There is no activity template and no analyzer project in the
repo, so the usual vehicles do not exist yet.

**Do:** cheapest first. Document it in the activity-authoring guidance with the fail-safe reasoning (unmarked
costs throughput, mis-marked costs correctness). If that proves insufficient, the larger options are a Roslyn
analyzer that flags an activity with no I/O and no external calls as a `ReplaySafe` candidate, or surfacing the
profile in the contract-surface snapshot so a reviewer sees it.

**Start at:** `src/Elsa/Activities/Runtime/Core/Attributes/ActivitySideEffectProfileAttribute.cs`,
[ADR 0032](../adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) R1's worked classification,
`tests/Elsa/Activities/Design/Tests/Contracts/ActivityContractSurfaceSnapshotTests.cs`.

**Done when:** an author can discover the profile without reading an ADR.

**Cost:** S for docs, M for an analyzer. **Confidence:** Read (the gap), Inferred (that docs suffice).

### U2. Publish the intrinsic-versus-activity performance guidance

**Problem.** A `Set` intrinsic loop runs at ~37 ms where the equivalent fused CLR activity loop runs at ~79 ms,
at identical hops and commits (P1). Users have no way to know this, and the authoring surfaces do not hint at it.

**Do:** document "express pure loop bodies as intrinsics" with the measurement, and consider a designer hint when
a loop body is a pure CLR activity that an intrinsic could express.

**Start at:** the measurement in P1; `src/Elsa/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs`
for what the designer already exposes.

**Done when:** the guidance exists somewhere an author will find it.

**Cost:** S for docs. **Confidence:** Measured (the gap), Inferred (that authors hit it).

### U3. Confirm Studio consumes the cadence and inspection-granularity badge

**Problem.** ADR 0032 R3 required the instance read model to project `checkpointCadence` and
`inspectionGranularity` (`activity-level` vs `boundary-level`) so consumers can explain why intra-segment
per-activity evidence is coalesced. The **backend half shipped**: `RuntimeCheckpointCadenceProjection` exists and
`GetWorkflowInstanceRequestHandler` serves both fields. Whether Studio renders them is unverified and lives in
`elsa-foundation-studio`. If it does not, enabling coalescing silently coarsens the timeline, which is precisely
the outcome R3 was written to prevent.

**Do:** check the Studio instance view for the two fields. If absent, render the badge.

**Start at:** `src/Elsa/Workflows/Runtime/Api/Coalescing/RuntimeCheckpointCadenceProjection.cs`,
`src/Elsa/Workflows/Runtime/Api/Handlers/GetWorkflowInstanceRequestHandler.cs`, then the Studio instance-details
view.

**Done when:** a coalesced run visibly reports boundary-level inspection, or the gap is filed against Studio.

**Cost:** S to verify. **Confidence:** Read (backend), Inferred (Studio).

---

## Robustness

### RB1. Admission control that sheds rather than queues

**Problem.** The largest structural gap in the comparison. Under concurrency, throughput *falls*: on one shared
SQLite writer, 5.6 runs/s at N=32 drops to 1.5 runs/s at N=128 while commits stay at exactly 1 per run. That is
congestion collapse. A mechanism does exist and it is the *cause*, not a defence: Groundwork's
`RelationalDocumentStore` holds a `SemaphoreSlim(1, 1)` connection gate and every unit of work begins with
`await connectionGate.WaitAsync(cancellationToken)`, with no timeout and no queue-depth bound. Every bound that
does exist bounds something else: `MaxDrainCycles` (64) bounds cycles within one drain, `MaxWorkItems` bounds items
per cycle, `RuntimeActorEvictionOptions` bounds *memory* and says so, and `MaxExecutionsPerSweep` (100) bounds the
**recovery** path. Nothing bounds live dispatch. Both incumbents shed: Zeebe runs an adaptive per-partition
limiter that rejects commands, Camunda 7 bounds the job queue and backs off acquisition.

**Do:** meter concurrent drains or checkpoint commits against a configurable limit and **shed or defer** beyond
it. Adding another queue in front of an unbounded queue changes nothing; the limit must produce a refusal the
caller can see. Port the pattern from `RuntimeResumptionOptions`, whose doc comment already states the intent
verbatim: bound the work "so a large backlog or a poisoned execution cannot overwhelm the host."

**Start at:** `src/Elsa/Workflows/Runtime/Resumption/Options/RuntimeResumptionOptions.cs` (the pattern),
`src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs` (the meter point),
`WorkflowExecutionCommandDispatchStatus` (`Deferred` already exists and the distributed leaf uses it for
"accepted for routing, not run locally").

**Done when:** the P2 curve shows throughput plateauing rather than falling, and a shed request is observable.

**Open design question:** what a shed request looks like to a caller who dispatched synchronously over HTTP.
`Deferred` is probably the right shape but changes an endpoint's contract from "your workflow ran" to "your
workflow is queued". That is a product decision, not an engine one, and should be settled before implementation.

**Cost:** M. **Confidence:** Measured (the collapse), Read (the mechanism). **Sequencing:** after P2.

### RB2. An opt-in out-of-process work contract

**Problem.** Elsa activities are C# classes that run inside the drain. There is no job-worker protocol, no
external-task pattern, no lease/complete/fail wire contract. A slow activity occupies its execution's single
writer and its DI scope for the whole duration, activity implementations cannot be scaled or deployed
independently, and there is no polyglot story. This is a **regression, not an absence**: Elsa 3 had `RunTask`,
catalogued in [the parity audit](elsa-4-activity-contract-parity-2026-07.md) as the "external task/callback
pattern", and it appears in this repository only as a gap row. Adjacent evidence that the shape is available: the
lease-based at-least-once worker pattern already exists in `WorkflowAlterationModels`, applied to alteration jobs.

**Do:** a job-worker family over the existing bookmark and stimulus machinery: activate with a lease, complete or
fail with a retry count, time out and re-offer. Not a change to how ordinary activities run.

**Start at:** `src/Elsa/Workflows/Runtime/Core/Models/Alterations/WorkflowAlterationModels.cs` (the lease shape),
the bookmark and stimulus dispatch path, `DispatchStimulusEndpoint`.

**Done when:** an out-of-process worker can lease, complete, and fail a unit of activity work, with timeouts
re-offering it.

**Precondition:** demand. Every argument for this is an argument from what Camunda users do, plus the Elsa 3
regression. Confirm someone wants it before building it.

**Cost:** L. **Confidence:** Read.

### RB3. Reconsider the default retry policy

**Problem.** `NoopRuntimeDomainRetryPolicy` returns an explicit `DoNotRetry`, so the first infrastructure fault in
a scheduler handler goes straight to a blocking incident. Camunda 7 retries three times by default; Zeebe's count
is client-supplied but the engine always raises an incident on exhaustion. Elsa's default is weaker than both.

**Do:** consider a small bounded `RetryAfter` default. The disposition and the `NextRetryAt` re-drive path already
exist and are honoured by `RuntimeResumptionPumpTask`.

**Start at:** `src/Elsa/Workflows/Runtime/Services/NoopRuntimeDomainRetryPolicy.cs`,
`WorkflowSchedulerDrainer.HandleHandlerCrashAsync`.

**Done when:** the default is evidence-backed either way.

**Caveat that may make the current default right:** a handler fault is a fault in Elsa's *own* dispatch code, not
in user activity code, and a deterministic bug there would retry pointlessly before landing in the same incident.
The evidence to settle it is what poison records in a real deployment actually contain, which nobody has looked
at. That look is the first task, not the change.

**Cost:** S. **Confidence:** Read.

### RB4. Decouple inspection projection from the checkpoint commit

**Problem.** Inspection projections fold into the same commit as state, so relaxing commit cadence coarsens
observability: throughput and inspection fidelity are on one dial. ADR 0032 R3 handles this honestly by
projecting the granularity so consumers can render the truth, but the coupling remains. Zeebe does not have this
trade at all, because the log is written for correctness and exporters read it for observability.

**Do:** move inspection evidence onto the post-commit outbox that
[ADR 0020](../adr/0020-runtime-checkpoint-commit-post-commit-work.md) already established, and let a projector
consume it.

**Start at:** `src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs` (the fold),
`RuntimePostCommitOutboxItems`, `IRuntimeActivityExecutionInspectionAccumulator`.

**Done when:** commit cadence and inspection granularity are independently configurable.

**Precondition:** the product decision that "delayed but complete" beats "immediate but coarse" for inspection
reads. Size it against how often `Immediate` is chosen for observability reasons rather than durability ones.

**Cost:** L. **Confidence:** Read. This is the one Zeebe idea worth importing, in its narrow form: take the
exporter's *decoupling*, not its log.

---

## Non-recommendations

These look attractive from the comparison and would be wrong. Recorded so nobody re-derives them.

- **Do not partition the runtime.** Zeebe partitions because it refused a shared database and therefore had to
  invent ordering and bounded writer counts from scratch. Elsa has a shared database. Partitioning would import a
  rebalancing protocol, a cross-partition correlation problem, and a "an execution can never move" constraint, to
  deliver properties RB1 and P3 give more cheaply.
- **Do not make an append-only command log the source of truth.** It is the most seductive idea in Zeebe's design
  and the most expensive to import: state becomes a projection, every query needs a projection pipeline, which
  needs an exporter, which needs a second store. That is the step that turns a library you reference into a
  cluster you operate.
- **Do not flip to pull-based workers as the default model.** Two of pull's three benefits (backpressure,
  absolute timeouts) are obtainable in a push model and are RB1 and the shipped dispatch deadline. The third,
  worker autonomy, is RB2 as an opt-in family.
- **Do not add concurrent drain within a single workflow execution.** ADR 0031 ratified single-writer-per-execution
  as the ceiling, and both incumbents independently reached the same restriction: Zeebe via a single-threaded
  stream processor, Camunda 7 via exclusive jobs ("Jobs from a single process instance are never executed
  concurrently"). Three systems converging is strong evidence it is load-bearing.
- **Do not let Flowchart grow BPMN's feature set.** Settled in
  [ADR 0064](../adr/0064-flowchart-infers-joins-from-propagated-dead-paths.md).
- **Do not consolidate projects to hit a number.** Write C3's rule first.

---

## Suggested order

1. **P2** (re-measure with the real leaf shape) and **D1** (execution behaviour map). Both cheap, and P2 gates RB1.
2. **C1** (constructors) and **D3** (test builder). Mechanical, low risk, immediate daily payoff.
3. **P1** (CLR activation cost). The largest measured performance lever remaining.
4. **RB1** (admission control). The largest structural gap, sized off P2's numbers.
5. **C2**, **D2**, **U1**, **U2**, **U3**, **P3**, **P4**, **RB3**. Independent, pick by appetite.
6. **C4** (ADR 0029 Move 2), **RB4** (decouple inspection), **RB2** (out-of-process work). Programs, each needing
   its own decision first.
