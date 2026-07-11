# Research: Runtime HTTP Hot-Path Performance

## Decision 1: Compose after all runtime persistence providers

**Decision**: Implement the policy shell feature through CShells `IPostConfigureShellServices` and apply store decorators from `PostConfigureServices`.

**Rationale**: CShells first invokes every enabled feature's `ConfigureServices`, then invokes post-configurers against the fully populated collection. Groundwork providers replace the default runtime store registrations during normal configuration. Applying coalescing earlier would capture in-memory stores and allow a later provider to discard the decorators.

**Alternatives considered**:

- JSON feature order: rejected because configuration order is not a stable dependency or provider-selection contract.
- `DependsOn` every persistence provider: rejected because the providers are mutually exclusive and a feature cannot depend on every possible choice.
- Put settings directly on `WorkflowsRuntimeApiFeature`: workable but less cohesive; persistence policy is an independently selectable runtime concern.

## Decision 2: Reuse the existing coalescing algorithm

**Decision**: Productionize `AddCoalescingRuntimeCheckpointPersistence` and `MaxSegmentCheckpoints`; do not introduce a second HTTP-specific fast path.

**Rationale**: The existing policy already folds non-suspending checkpoint changes, flushes mandatory boundaries, retains the durable segment-entry queue item until the atomic flush, preserves fencing, and has two-generation crash-convergence evidence.

**Alternatives considered**:

- Skip selected checkpoints only for HTTP: rejected because it would create transport-specific durability semantics.
- Return the HTTP response before the runtime drain: rejected for this unit because request-scope lifetime, failure reporting, and replay semantics would change.
- Disable persistence for short workflows: rejected because it violates the durable runtime contract.

## Decision 3: Keep platform default Immediate, opt the reference server into Coalesced

**Decision**: The feature defaults to Immediate for compatibility; the committed reference development server explicitly selects Coalesced with cap 50.

**Rationale**: Existing applications retain the narrowest crash-replay window unless they opt in. The reference host demonstrates the validated low-latency configuration and can roll back with one setting.

**Alternatives considered**:

- Change `AddWorkflowRuntime()` globally to Coalesced: rejected because that is a silent behavior change for every host.
- Leave the reference server Immediate: rejected because it would not solve the reported out-of-box server experience.

## Decision 4: Count durable commit markers in isolated SQLite stores

**Decision**: Use the existing HTTP TestServer fixture with one temporary Groundwork SQLite database per policy and query the `checkpointCommit` document kind before/after a request.

**Rationale**: Commit markers are the durable transaction authority. Counting them is deterministic, provider-realistic, and independent of timing noise or decorator internals.

**Alternatives considered**:

- Mock the checkpoint writer: rejected because it would not prove the real persistence path.
- Assert wall-clock latency in ordinary tests: rejected because shared CI machines make such tests flaky.
- Inspect logs: rejected because log capture is indirect and optional.

## Decision 5: Separate correctness gates from performance evidence

**Decision**: CI asserts response/state equivalence, physical commit reduction, cap behavior, and crash recovery. An opt-in measurement command reports cold/warm percentiles and optionally enforces the performance budget.

**Rationale**: Structural metrics are deterministic. Latency remains essential evidence but needs a controlled or explicitly requested environment.

**Alternatives considered**:

- No latency harness: rejected because commit reduction alone does not prove the user-visible outcome.
- Always fail on p95: rejected because it would turn normal CI into an environment benchmark.

## Decision 6: Defer provider and dispatch redesign until measured

**Decision**: Do not add SQLite WAL/synchronization profiles, per-execution checkpoint locks, caches, or durable-enqueue response modes in the first implementation. Measure the coalesced path and continue only if it misses the budget.

**Rationale**: Coalescing directly targets the observed thirteen-commit amplification and is already safety-proven. Mixing several optimizations would make attribution and rollback difficult.

**Alternatives considered**:

- Tune SQLite first: rejected because thirteen transactions dominate one transaction's journal settings.
- Replace the global checkpoint gate immediately: rejected because serial single-request latency is already bad and concurrency-safe lock changes need separate evidence.
