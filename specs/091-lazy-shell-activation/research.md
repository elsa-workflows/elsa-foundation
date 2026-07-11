# Research: Observable Shell Readiness and Cold Activation

## Decision 1: Treat shell activation as the cold-start boundary

**Decision**: Measure process listening separately from `IShellRegistry.GetOrActivateAsync("default")`, then measure the first workflow request after activation.

**Rationale**: CShells startup is intentionally lazy. The first shell-routed request currently pays feature discovery, shell composition, persistence initialization, startup tasks, route refresh, and endpoint mapping. A root socket can be live while none of that work has run.

**Alternatives considered**:

- Time `dotnet run`: rejected because build, restore, and launch-profile TLS pollute the server measurement.
- Treat the first workflow curl as one number: rejected because it conflates host startup, activation, executable loading, and runtime execution.
- Use the root `/` request as the listening probe: rejected because default-shell fallback can route the request through shell middleware and trigger activation.

## Decision 2: Use excluded, non-blocking health probes

**Decision**: Map root `/health/live` and `/health/ready` paths and exclude both from CShells resolution. Readiness observes active-shell and warmup state and returns immediately.

**Rationale**: A health probe must not create the condition it is checking. CShells falls back to the default shell after a path-name miss, so an unexcluded health path can trigger cold activation. Blocking readiness connections for tens of seconds also harms probe behavior and hides the distinction between starting and ready.

**Alternatives considered**:

- Await activation inside `/health/ready`: rejected because every probe becomes a long request and readiness is no longer observational.
- Execute a published workflow from readiness: rejected because workflows can have side effects, require authorization, or be absent.
- Report ready on process start: rejected because the route table and endpoint surface are not available yet.

## Decision 3: Warm the default shell once after listening

**Decision**: A root background warmup waits for `ApplicationStarted` and then invokes the existing stampede-safe registry activation for only the configured default shell.

**Rationale**: This removes activation from the first client request without delaying Kestrel listening. The registry already serializes concurrent activation per shell name, and other shells keep independent lazy lifecycles.

**Alternatives considered**:

- Block hosted-service startup on activation: rejected because the process cannot expose liveness or an honest starting readiness state.
- Warm every configured shell: rejected because it breaks tenant isolation expectations and multiplies startup cost.
- Add activation logic to workflow middleware: rejected because it preserves the client-visible stall.

## Decision 4: Attribute owned phases without pretending to see CShells internals

**Decision**: Instrument feature-catalog warmup, overall registry activation, Groundwork initialization, and every Elsa startup task. Treat the remaining registry activation interval as the coarse composition/endpoint-registration phase.

**Rationale**: Elsa owns these boundaries and can emit reliable duration/outcome observations. Exact per-feature `ConfigureServices` and service-provider-build timing lives inside the external CShells package and cannot be split honestly from Foundation code.

**Alternatives considered**:

- Infer phases from sparse log timestamps: rejected as incomplete and hard to correlate.
- Fork or patch CShells in this work unit: rejected because the acceptance criteria can be met through owned boundaries; upstream fine-grained CShells telemetry can follow separately if the coarse phase dominates.
- Add high-cardinality identifiers to metrics: rejected because workflow, tenant, and exception-message dimensions are unsafe and unbounded.

## Decision 5: Skip unchanged Groundwork SQLite materialization by exact history

**Decision**: When the exact manifest identity/version and provider name/version row already exists, open the existing document store directly. Otherwise execute the existing full factory materialization. Add a force-rematerialize operator setting.

**Rationale**: The current factory constructs a fresh materialization plan on every process and backfills every declared portable index even though schema history records the exact applied tuple. On the frozen 58 MB runtime database, default-shell activation took 43.500964 seconds; logs and call-path analysis place the dominant repeated work in persistence initialization. The history row is committed atomically with materialization and is the correct unchanged-schema authority.

**Alternatives considered**:

- Disable all persistence initialization: rejected because a new or upgraded database would be unusable.
- Turn off EF migrations first: rejected because the runtime database initialization dominates this measured lane and migrations remain necessary for unmanaged local databases.
- Cache the document store across process restarts: rejected because process memory cannot outlive a restart and would not address schema work.
- Change Groundwork's public factory in this work unit: rejected because Foundation can safely specialize its existing SQLite provider leaf using public provider types; an upstream factory fast path remains a later consolidation opportunity.

## Decision 6: Freeze data, not the filesystem cache

**Decision**: Compare prebuilt binaries against identical SQLite backups, one fresh mutable copy per boot, with repeated/interleaved lanes and complete provenance.

**Rationale**: This prevents test runs from accumulating persistence changes while avoiding privileged or platform-specific OS cache flushing. Repetition and lane ordering make remaining temperature effects visible.

**Alternatives considered**:

- Reuse one mutable database: rejected because each boot would see a different state.
- Delete all databases: rejected because the workflow route and realistic runtime volume would disappear.
- Put wall-clock gates in ordinary CI: rejected because shared runner variance makes them flaky; deterministic semantic tests remain mandatory CI gates.

## Baseline Evidence

- Build: current `origin/main` at `92e784219e1b00de4d67dd449c808a1785d9ff42`, Release, .NET 10.
- Data: SQLite backup of the 58 MB Groundwork runtime database; clean design and identity stores to avoid stale reconciliation hashes.
- Default-shell activation barrier: 43.500964 seconds, HTTP 200.
- First `/workflows/http/hello-world` after activation: 0.610067 seconds, HTTP 200, body `Hello World!`.
- A stale design-store snapshot was rejected by reconciliation after 109 seconds with an activity hash mismatch, confirming readiness must remain unavailable on activation failure.
