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
| A1: concurrency correctness (**diagnosed**, closed 10 October 2026) | [#2532](https://github.com/elsa-workflows/elsa-foundation/issues/2532) | `RuntimeDbPaging2392Reference` at 16 and 32 clients returns HTTP 200 `Alice Smith` for every request. Every execution settles with zero incidents. No timers. A 429 is admission backpressure, not a failure: the client retries it per `Retry-After`, and the run is judged by lost work, never by shed count (owner decision, 9 October 2026; #2548). Result at `1bef1fe`: 7 of 8 pass. SQLite Immediate/32 loses no acknowledged work but exceeds the 60s client timeout. The owner closed A1 as diagnosed, and the latency goes to Track B as [#2553](https://github.com/elsa-workflows/elsa-foundation/issues/2553). |
| A2: request-path telemetry writes | [#2533](https://github.com/elsa-workflows/elsa-foundation/issues/2533) | Explain the `elsa_otel_*` writes attributed to the request trace (48/134 Coalesced commands), then remove them from that path or document how to disable them. |
| Lease contention fix (**done**) | [#2538](https://github.com/elsa-workflows/elsa-foundation/issues/2538), [spec 200](../../specs/200-root-write-lease-rows/spec.md) | Done in #2539: one record per lease, keeping GC safety. A1 lease errors fell from 204 to 0. The follow-up [#2548](https://github.com/elsa-workflows/elsa-foundation/issues/2548) (#2549) stopped admission-shed starts being reported as started. |
| B: bounded throughput ramp (**unblocked**, 10 October 2026) | [#2534](https://github.com/elsa-workflows/elsa-foundation/issues/2534) | Release build, ramp of 1–64 clients, Immediate/Coalesced × persisted diagnostics on/off. Records the plateau and the saturating resource. One-off report. It covers SQLite Immediate at 32 clients ([#2553](https://github.com/elsa-workflows/elsa-foundation/issues/2553)). |

## Boundaries

- No durability weakening, default cadence flip, generic cache or inspection redesign (#1237 keeps inspection decoupling).
- Rejected candidates #1239 and #2407 stay rejected unless new evidence addresses their failed guards.
- Optimizations chosen from Track B evidence become separate, separately reviewed units.
- Manual package publication, releases and deployment remain out of scope.

## Completion

A1 passes or is diagnosed. A2 has a recorded decision with evidence. B publishes a pinned report naming the plateau and the saturating resource for each variant, with failed windows retained. Any follow-up optimization is listed here or in its own bucket.
