# Shell activation performance — July 2026

## Status

Final implementation and acceptance evidence for [spec 091](../../specs/091-lazy-shell-activation/spec.md),
[spec 092](../../specs/092-workflow-executable-cache/spec.md),
[issue #624](https://github.com/elsa-workflows/elsa-foundation/issues/624), and
[issue #625](https://github.com/elsa-workflows/elsa-foundation/issues/625).

## Preliminary baseline

| Milestone | Result |
|---|---:|
| Default-shell activation barrier | 43,500.964 ms |
| First `hello-world` response after activation | 610.067 ms |
| Workflow response | HTTP 200, `Hello World!` |

Environment:

- Elsa Foundation revision: `92e784219e1b00de4d67dd449c808a1785d9ff42`
- Build: Release, .NET 10, Apple Silicon macOS
- Runtime data: SQLite backup of the 58 MB `elsa-groundwork-runtime.db`
- Design and identity stores: created clean for the measured revision
- Server transport: loopback HTTP with a prebuilt `Elsa.Server.dll`

This is a diagnostic single-boot baseline, not the final performance claim. Spec 091 requires a frozen-data 20-boot before lane and a matching 20-boot after lane with raw samples and p50/p95.

## Repeated pre-change lane

The committed pre-fast-path build (`23292e11`) was measured for 20 isolated boots with the spec 091 harness. Every boot cloned the same frozen data, reached excluded readiness, and returned exactly HTTP 200 with `Hello World!`.

| Milestone | p50 | p95 | Range |
|---|---:|---:|---:|
| Listening | 932.706 ms | 2,102.955 ms | 695.507–4,121.638 ms |
| SQLite-backed shell activation | 8,193.687 ms | 15,134.934 ms | 6,584.378–18,108.551 ms |
| Shell ready from launch | 9,409.582 ms | 16,716.020 ms | 7,661.016–19,325.721 ms |
| First workflow request after ready | 826.421 ms | 3,071.259 ms | 578.143–3,113.574 ms |
| First success from launch | 10,236.003 ms | 19,787.279 ms | 8,434.194–22,439.294 ms |

Provenance:

- Release server SHA-256: `ec8b231897b4cc737cabc74ff619bef4395effbcc121399f5bb4079b696987a8`
- Frozen content/data SHA-256: `aa453949e67d95aabe9316654cb88d8dae904ff53f77bd5c4288efc89a19dbe6`
- SDK: .NET `10.0.300`; environment: Production; Apple Silicon macOS
- Raw retained report: `/tmp/elsa-624-before-20-23292e11/report.json` on the reference machine

The shell-ready baseline already satisfies the absolute 30-second ceiling; the after lane must additionally improve its p95 by at least 30%. The first-request p95 does not yet satisfy 750 ms, confirming that post-readiness executable materialization remains a separate cost for the immutable-artifact cache follow-up (#625).

## What the baseline proves

CShells starts the process without activating the default shell. A shell-routed request then performs feature discovery, shell composition, persistence initialization, ordered startup tasks, workflow route-table refresh, endpoint registration, and only then request dispatch. The first request therefore observes the entire activation delay.

The measured 43.5-second barrier completed successfully. Once activation finished, the existing published workflow returned successfully in 610 ms. Subsequent warm requests remain covered by the separate runtime HTTP performance lane.

## Failure evidence

The first frozen snapshot also included an older design database. Current-main activation rejected it after 109 seconds with `ActivityVersionHashMismatchException`, because an existing logical activity version had a different reconciliation hash. That database was not used for the successful baseline.

This is useful readiness evidence: a listening process with a failed shell must remain live but unavailable for workflow traffic. Readiness cannot be inferred from socket availability, and a readiness probe must not suppress or bypass reconciliation failure.

## Initial phase attribution

Existing logs show feature discovery and feature configuration finish before the long persistence/startup interval. Source-path analysis identifies these owned boundaries:

1. runtime feature-catalog discovery;
2. opaque CShells composition and endpoint mapping;
3. Groundwork SQLite document-store materialization;
4. ordered Elsa startup tasks, including EF migrations and reconciliation;
5. workflow HTTP route-table refresh.

The current Groundwork SQLite admission path composes a plan and normally performs a complete inspection/validation walk on every process open. Spec 091 adds phase telemetry first and preserves the repeated pre-change lane. Its current optimization records an applied-plan fingerprint after successful admission and, only when `SkipSchemaInspectionWhenPlanUnchanged=true`, skips the repeated walk for an exact match. The setting defaults to `false` because a matching plan cannot detect out-of-band schema drift while the host was down.

## Budgets

- Shell-ready p95: no more than 30 seconds and at least 30% below the repeated baseline.
- First workflow response after ready p95: no more than 750 ms.
- Subsequent warm workflow p95: no more than 50 ms.

Wall-clock budgets are enforced only by the controlled measurement commands. Deterministic CI gates readiness semantics, shell isolation, materialization selection, route initialization, durability, and response correctness.

## Optimized 20-boot result

The exact-history fast path was measured at committed revision `072a5662` against the same frozen content/data hash and machine as the repeated pre-change lane.

| Milestone | Before p95 | After p95 | Change |
|---|---:|---:|---:|
| Listening | 2,102.955 ms | 1,022.705 ms | −51.4% |
| SQLite-backed shell activation | 15,134.934 ms | 7,319.983 ms | −51.6% |
| Shell ready from launch | 16,716.020 ms | 8,206.186 ms | −50.9% |
| First workflow request after ready | 3,071.259 ms | 942.694 ms | −69.3% |
| First success from launch | 19,787.279 ms | 9,381.591 ms | −52.6% |

The shell-ready result clears both acceptance thresholds: it is below 30 seconds and improves by more than 30%. All 20 boots returned the exact expected workflow response. After-build provenance:

- Release server SHA-256: `4bf6d9c1c798fdee5191d9bd78dd54cc1ecfee2dcb79b212e9fe8b242e6167e3`
- Frozen content/data SHA-256: `aa453949e67d95aabe9316654cb88d8dae904ff53f77bd5c4288efc89a19dbe6`
- Raw retained report: `/tmp/elsa-624-after-20-072a5662/report.json` on the reference machine

## Warm-lane residual and follow-up

The optimized Production lane's 200 measured requests produced warm p95 `359.953 ms`, above the existing 50 ms budget. A historical diagnostic run on the pre-current-main storage composition with `RematerializeOnStartup=true` produced warm p95 `35.645 ms`; a vacuumed copy produced `31.999 ms`. This demonstrates that full index-projection rebuild/physical compaction can mask the repeated post-readiness lookup/materialization cost. Current main subsequently replaced that repair switch with admission-plan fingerprinting and per-boot schema inspection.

This residual is not hidden as a successful acceptance result. Issue #625 was pulled into the same delivery and now adds a bounded immutable workflow-executable cache at the runtime store seam. The final post-cache lanes below remain the merge gate.

## Cache scope and route lookup findings

The executable cache is broader than HTTP workflows. It wraps the durable `IWorkflowExecutableStore`, so workflow starts, bookmark resumes, child-workflow invocation, and scheduler turns all reuse the same immutable artifact within one shell/provider lifetime. It deliberately does not cache mutable workflow source references: publication, retirement, scope, expiry, and artifact selection remain authoritative before an artifact ID is loaded.

HTTP route lookup already has a separate in-memory projection. `RouteTable` stores an immutable per-shell snapshot in `IMemoryCache`, atomically replaces it on refresh, orders routes by specificity, and precompiles each `TemplateMatcher`; request-time matching does not parse templates or query persistence. There is therefore no missing route-cache layer in the measured path. The remaining lookup is a linear scan of the ordered in-memory snapshot; replacing it with a trie/DFA or method/path index should be considered only after a high-route-count benchmark shows it material.

Cache behavior and safety:

- positive artifacts are retained by content-addressed artifact ID; null, failure, and cancellation are retried;
- concurrent misses for one ID share a provider load, while cancelling one waiter does not cancel the shared load;
- deterministic least-recently-used eviction keeps resident entries within the configured capacity;
- save/delete invalidate rather than admitting the caller's object, preserving the provider's idempotent-save authority;
- cache state is shared across request scopes inside one shell service provider, isolated by authorized persistence scope, and starts empty after shell replacement or process restart; all built-in Groundwork runtime and unified provider features enable it by default, while operators that cannot accept node-local immutable retention can disable it;
- metrics expose only bounded hit/miss, eviction-reason, and provider-load outcome/duration dimensions.

## Operator knobs and rollback

- `Elsa:Readiness:WarmDefaultShell=false` restores request-triggered lazy activation; readiness stays observational and returns unavailable until another request activates the shell.
- `Elsa:Readiness:DefaultShellName` selects the single shell observed and prepared by the root host. Other shells remain lazy and isolated.
- SQLite runtime and unified features expose `SkipSchemaInspectionWhenPlanUnchanged`. It defaults to `false`, preserving per-boot out-of-band drift detection. Enabling it trades that inspection for a single applied-plan fingerprint read when the composed plan is unchanged.
- Durable Groundwork runtime and unified features expose `CacheWorkflowExecutables` (default `true`) and `WorkflowExecutableCacheCapacity` (default `256` artifacts per shell). Set caching to `false` for immediate rollback; capacity must be positive only while enabled. Artifact IDs are content-addressed and mutable source-reference selection remains authoritative, but invalidation is node-local until issue #636 adds a distributed invalidation protocol.
- SQLite runtime and unified features additionally expose `ReuseAccessBoundStores` (default `true`) and `AccessBoundStoreCacheCapacity` (default `256` access bindings per shell). The cache retains immutable access binding and compiled-route adapters only; each operation still owns an independent pooled connection and transaction. Set reuse to `false` to restore per-operation store materialization.
- Missing or changed admission fingerprints always execute the full schema inspection/application path regardless of the skip setting.

## Final exact-revision evidence

The final Release server was measured at implementation revision
`382301460ce34ada7f43a0946706680b6eeea563` with Groundwork `0.0.1-preview.95`.
The server output closure SHA-256 was
`6bd8340bff2dca25fc4f79e3b928c681e1325ed9a96b4d4fcdeffaae4c105c8b`.
Every measured request returned HTTP 200 with the exact body `Hello World!`.

Twenty isolated boots copied the same frozen SQLite baseline before starting the process:

| Milestone | p50 | p95 | Budget |
|---|---:|---:|---:|
| Listening | 432.488 ms | 441.332 ms | diagnostic |
| Shell ready | 2,451.364 ms | 2,566.966 ms | ≤30 s |
| First workflow request after ready | 582.265 ms | 627.631 ms | ≤750 ms |
| First success from launch | 3,032.672 ms | 3,167.738 ms | diagnostic |

The controlled warm lane used four independent copies of one frozen database and measured
20 warmups plus 200 requests per configuration:

| Executable cache | Reusable SQLite stores | Concurrency | p50 | p95 | Result |
|---|---|---:|---:|---:|---|
| on | on | 1 | 30.081 ms | 40.723 ms | passes 50 ms |
| off | on | 1 | 28.755 ms | 40.126 ms | passes 50 ms |
| on | off | 1 | 60.866 ms | 120.487 ms | fails 50 ms |
| off | off | 1 | 61.386 ms | 75.959 ms | fails 50 ms |
| on | on | 2 | 24.643 ms | 32.515 ms | passes 50 ms |
| off | on | 2 | 25.053 ms | 32.898 ms | passes 50 ms |
| on | off | 2 | 67.449 ms | 74.405 ms | fails 50 ms |
| off | off | 2 | 69.655 ms | 76.189 ms | fails 50 ms |

The CPU trace and factorial result identify repeated SQLite/Groundwork store construction,
schema-target validation, and route-plan binding as the hot-path cost. Groundwork now accepts the
exact startup-admitted physical target instead of rebuilding and deserializing applied schema state
for every operation. Elsa compiles route plan sets once and retains a bounded set of immutable
access-bound store adapters. This is a broader persistence-runtime optimization: HTTP starts,
direct starts, bookmark resumes, dispatched children, scheduler turns, and other Groundwork-backed
runtime operations use the same store-session seam.

Executable caching remains enabled by default even though its toggle is neutral for this tiny,
already-warmed artifact. Its value grows with executable size, number of first-per-artifact loads,
and materialization complexity; it is not the dominant cost in this particular micro-workflow.

Raw reports and samples are committed under
[`docs/reports/evidence/092-workflow-executable-cache/final-2026-07-29/`](evidence/092-workflow-executable-cache/final-2026-07-29/).
The hosted Ubuntu workflow in
[`http-workflow-performance.yml`](../../.github/workflows/http-workflow-performance.yml)
publishes the same deterministic synchronous workflow and enforces a 250 ms default-on warm p95
regression ceiling on relevant pull requests. The broader threshold accounts for shared-runner storage
and scheduling variance; the controlled local acceptance budget remains 50 ms.

## Subsequent recommendations

1. Keep the committed local factorial and hosted default-on lane as regression evidence; do not infer performance from cache unit tests alone.
2. [Issue #636](https://github.com/elsa-workflows/elsa-foundation/issues/636) owns distinct-key load backpressure, cache-lifetime cancellation, distributed invalidation, gauges, and heap evidence before considering weighted admission or larger capacities.
3. [Issue #637](https://github.com/elsa-workflows/elsa-foundation/issues/637) owns route matching at representative high route counts before replacing the current precompiled immutable snapshot. The existing route layer is already cached and persistence-free on requests.
4. Keep the cache process-local. A distributed executable-object cache would reintroduce serialization and coordination without evidence of a cross-node bottleneck.
5. Revisit negative caching only with an explicit short expiry and publication invalidation contract; retaining not-found results indefinitely would hide newly durable artifacts.
