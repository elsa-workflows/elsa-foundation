# Shell activation performance — July 2026

## Status

Implementation baseline for [spec 091](../../specs/091-lazy-shell-activation/spec.md) and [issue #624](https://github.com/elsa-workflows/elsa-foundation/issues/624). The optimized repeated-run evidence will replace the preliminary table before the work unit closes.

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

The current Groundwork SQLite factory creates a materialization plan on every process open. That plan backfills declared document indexes even when the exact manifest/provider tuple is already recorded in `groundwork_schema_history`. Spec 091 adds phase telemetry first, preserves a repeated pre-change lane, then removes this unchanged-schema work behind an explicit full-rematerialization repair knob.

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
- cache state is shared across request scopes inside one shell service provider, isolated by authorized persistence scope, and starts empty after shell replacement or process restart; SQLite enables it by default, while PostgreSQL/distributed features require explicit opt-in because invalidation is node-local;
- metrics expose only bounded hit/miss, eviction-reason, and provider-load outcome/duration dimensions.

## Operator knobs and rollback

- `Elsa:Readiness:WarmDefaultShell=false` restores request-triggered lazy activation; readiness stays observational and returns unavailable until another request activates the shell.
- `Elsa:Readiness:DefaultShellName` selects the single shell observed and prepared by the root host. Other shells remain lazy and isolated.
- SQLite runtime and unified features expose `SkipSchemaInspectionWhenPlanUnchanged`. It defaults to `false`, preserving per-boot out-of-band drift detection. Enabling it trades that inspection for a single applied-plan fingerprint read when the composed plan is unchanged.
- Durable Groundwork runtime and unified features expose `CacheWorkflowExecutables` and `WorkflowExecutableCacheCapacity` (default `256` artifacts per shell). SQLite features default caching to `true`; PostgreSQL/distributed and legacy direct registrations default to direct reads until a host explicitly accepts immutable-artifact retention or supplies cross-node invalidation. Set caching to `false` for immediate rollback; capacity must be positive only while enabled.
- Missing or changed admission fingerprints always execute the full schema inspection/application path regardless of the skip setting.

## Current-main integration evidence

After merging current main into the work branch, the Release server built with zero errors and the deterministic cache, SQLite, PostgreSQL, unified-host, readiness, architecture, and HTTP integration lanes passed. The build reports 31 obsolescence warnings from unchanged current-main Groundwork and publishing sources; this PR introduces no warnings in its diff. The reference server configurations explicitly enable `GroundworkUnifiedPersistenceSqlite.CacheWorkflowExecutables` with capacity `256`.

A fresh local SQLite fixture published `GET /workflows/http/hello-world` (HTTP 200, exact body `Hello World!`) as definition `12FOt8yTb5f`, artifact `artifact-f6655508d471`. Cache-on/off 200-request diagnostics were then run from equivalent low-row-count snapshots. Those samples were collected while unrelated host processes consumed more than three CPU cores, so they are retained only as regression attribution—not acceptance evidence:

| Setting | Warm p95 | Result |
|---|---:|---|
| cache off | 1,793.517 ms | fails 50 ms budget |
| cache on (default) | 2,440.520 ms | fails 50 ms budget |

The run confirms that executable caching alone is insufficient on the integrated current-main runtime and that the machine was not quiet enough for a causal wall-clock conclusion. A macOS sample attributed substantial time to GC/allocation and SQLite-backed durable execution while the run count grew. The required quiet-host 20-boot and 200-request lanes therefore remain open; no PR should claim the 50 ms gate from these contaminated samples.

Raw diagnostic reports and samples are retained under
[`docs/reports/evidence/092-workflow-executable-cache/current-main-contaminated-2026-07-28/`](evidence/092-workflow-executable-cache/current-main-contaminated-2026-07-28/).

## Subsequent recommendations

1. Keep the final 20-boot first-after-ready and 200-request warm lanes as the merge gate; do not infer success from cache unit tests alone.
2. [Issue #636](https://github.com/elsa-workflows/elsa-foundation/issues/636) owns distinct-key load backpressure, cache-lifetime cancellation, distributed invalidation, gauges, and heap evidence before considering PostgreSQL default-on or weighted admission.
3. [Issue #637](https://github.com/elsa-workflows/elsa-foundation/issues/637) owns route matching at representative high route counts before replacing the current precompiled immutable snapshot. The existing route layer is already cached and persistence-free on requests.
4. Keep the cache process-local. A distributed executable-object cache would reintroduce serialization and coordination without evidence of a cross-node bottleneck.
5. Revisit negative caching only with an explicit short expiry and publication invalidation contract; retaining not-found results indefinitely would hide newly durable artifacts.
