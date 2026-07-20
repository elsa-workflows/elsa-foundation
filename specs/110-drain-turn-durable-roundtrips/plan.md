# Implementation Plan — spec 110

## Summary

Characterization (STEP 1) is complete and shipped as [research.md](./research.md) plus the permanent
`DurableRoundTripDiagnostics` benchmark. It refutes the "per-turn durable commit" premise: the commit
collapse is already delivered by coalescing. The remaining actionable work is a drain-scoped,
reconstructible, immutable-by-`ArtifactId` read cache over `IWorkflowExecutableStore.FindAsync` on the
execution dispatch path (FR-002…FR-004), guarded by a byte-identical cache-on/off test (FR-003/FR-005)
and measured by the diagnostic (FR-001).

## Phase 0 — Instrument (DONE)

- `benchmarks/Elsa/Workflows/Runtime/Benchmarks/DurableRoundTripDiagnostics.cs`: counting decorators
  over `IWorkflowExecutableStore` and the durable `IWorkflowSchedulerWorkQueue` (inserted before the
  coalescing decorator captures the inner queue), plus `checkpointCommit` document counting. Reports
  durable transactions per run by kind for 2-node and hot-loop×10 under Immediate and Coalesced.

## Phase 1 — Drain-scoped executable read cache (design outline; NOT yet implemented)

Decision points to resolve before coding (this is why it is not rushed into this session):

1. **Scope carrier.** Reuse the drain's DI scope. Preferred: a `Scoped` caching decorator over the
   `Singleton` `IWorkflowExecutableStore`, resolved by the dispatch handlers within the drain scope.
   Must resolve the inner singleton without lifetime captivity — i.e. the singleton
   `WorkflowExecutableRootWriteLeaseManager` and GC path must keep an **uncached** handle
   (`CoalescingInner`-style holder, or a separate `IWorkflowExecutableReadCache` seam the handlers
   consult while the lease manager keeps the raw store). Confirm the drainer opens a DI scope per
   drain (ADR 0031 notes `DrainAsync` opens the ambient frame; verify it is a real DI scope, not only
   an `AsyncLocal` push).
2. **Cache seam vs decorator.** A narrow `IWorkflowExecutableReadCache` (get-or-load by `ArtifactId`)
   injected into the five hot-path handlers is more surgical and keeps FR-004 (lease/GC reads stay
   uncached) structurally guaranteed, at the cost of touching each call site. A decorator is less
   invasive but must exclude the lease-manager dependency by construction.
3. **Eviction.** Drain-end disposal; no TTL needed (immutable artifacts, pinned for the run).

## Phase 2 — Guardrails & tests (full projects)

- Byte-identical cache-on/off guardrail: same committed checkpoint state and outputs (mirrors ADR 0031's
  fast-path-off guardrail).
- Counting-store test: one durable `FindAsync` per pinned artifact per drain.
- Concurrency test: two executions' caches are isolated.
- Full projects: `Elsa.Workflows.Runtime.Tests`, `Elsa.Persistence.Groundwork.Tests`,
  `Elsa.Activities.Runtime.Tests` + architecture guard if a runtime seam is added.

## Phase 3 — Measure

Re-run `DurableRoundTripDiagnostics`; report executable-read reduction (~5×N → ~1) and the wall-time
delta, with every other durable-op count unchanged.

## Risk / why Phase 1 is a separate careful unit

The commit path is already optimal, so there is **no** low-risk commit-path change to make — the value
is entirely in the read cache, and the read cache's risk is DI lifetime/captivity and GC-read
correctness (FR-004), not algorithmic. It should land as its own reviewed unit with the full runtime
suite available, sequenced against ADR 0031's drain-scope work to avoid a second scope mechanism.
