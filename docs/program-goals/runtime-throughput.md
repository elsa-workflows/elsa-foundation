# Runtime Throughput

- **Status:** Active. Owner-approved 9 October 2026.
- **Area:** Workflow runtime / concurrent execution correctness / bounded throughput evidence.
- **Stewards:** Sipke plus the runtime throughput control room.
- **Program:** [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531).

## Purpose

[Runtime Database Access](runtime-db-access.md) reduced per-execution database commands and measured latency, but it never measured throughput. Its only concurrent control used four clients. This program proves the reference workflow stays correct under concurrent load, removes request-path work that does not belong there, and measures one bounded throughput baseline with bottleneck attribution. The [findings comment](https://github.com/elsa-workflows/elsa-foundation/issues/2382#issuecomment-6077532251) records the starting evidence and the ranked options.

## Owner decision and ADR 0073 D7 boundary

On 9 October 2026 Sipke approved both tracks and a **scoped ADR 0073 D7 exception for Track B only**. The exception covers one one-off throughput ramp with the bounds below. It does not reinstate benchmark infrastructure, CI timing gates, budgets, SLAs or a general benchmark service. Outside Track B, D7 stands: Track A work proves correctness and command counts and records no elapsed time.

## Objectives

| Track | Issue | Outcome |
|---|---|---|
| A1: concurrency correctness | [#2532](https://github.com/elsa-workflows/elsa-foundation/issues/2532) | `RuntimeDbPaging2392Reference` at 16 and 32 clients returns HTTP 200 `Alice Smith` for every request. Every execution settles with zero incidents. No timers. A 429 is admission backpressure, not a failure: the client retries it per `Retry-After`, and the run is judged by lost work, never by shed count (owner decision, 9 October 2026; #2548). |
| A2: request-path telemetry writes | [#2533](https://github.com/elsa-workflows/elsa-foundation/issues/2533) | Explain the `elsa_otel_*` writes attributed to the request trace (48/134 Coalesced commands), then remove them from that path or document how to disable them. |
| Lease contention fix | [#2538](https://github.com/elsa-workflows/elsa-foundation/issues/2538), [spec 200](../../specs/200-root-write-lease-rows/spec.md) | A1 failed 7 of 8 cases. All root-write leases of an artifact share one coordination row, and concurrent executions of one workflow exhaust its retries. Store one record per lease while keeping GC safety, so that the A1 matrix passes. |
| B: bounded throughput ramp (on hold until the lease fix lands) | [#2534](https://github.com/elsa-workflows/elsa-foundation/issues/2534) | Release build, ramp of 1–64 clients, Immediate/Coalesced × persisted diagnostics on/off. Records the plateau and the saturating resource. One-off report. |

## Boundaries

- No durability weakening, default cadence flip, generic cache or inspection redesign (#1237 keeps inspection decoupling).
- Rejected candidates #1239 and #2407 stay rejected unless new evidence addresses their failed guards.
- Optimizations chosen from Track B evidence become separate, separately reviewed units.
- Manual package publication, releases and deployment remain out of scope.

## Completion

A1 passes or is diagnosed. A2 has a recorded decision with evidence. B publishes a pinned report naming the plateau and the saturating resource for each variant, with failed windows retained. Any follow-up optimization is listed here or in its own bucket.
