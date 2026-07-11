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
