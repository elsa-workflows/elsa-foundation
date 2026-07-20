# Tasks — spec 110

## Phase 0 — Characterize (DONE this unit)

- [x] T001 Build the durable-round-trip instrument: counting decorators over `IWorkflowExecutableStore`
      and the durable `IWorkflowSchedulerWorkQueue` (inserted before coalescing captures the inner
      queue), plus `checkpointCommit` document counting.
      → `benchmarks/Elsa/Workflows/Runtime/Benchmarks/DurableRoundTripDiagnostics.cs`
- [x] T002 Run 2-node and hot-loop×10 under Immediate and Coalesced; record durable transactions per
      run by kind. → [research.md](./research.md) table.
- [x] T003 Determine what each per-turn durable round-trip is (fence touch, consumed-item delete,
      overlay bookkeeping, markers, lease). Result: commit collapse already shipped; buffered turns
      overlay-only; residual is the executable read. → research.md "What each durable round-trip is".
- [x] T004 Re-aim verdict and record it in the program bucket. → this spec + report.

## Phase 1 — Executable read cache (NOT done; distinct reviewed unit)

- [ ] T010 Confirm the drainer opens a real per-drain DI scope (vs only an `AsyncLocal` push) and
      choose the cache carrier (Scoped decorator vs narrow `IWorkflowExecutableReadCache` seam).
- [ ] T011 Implement the drain-scoped, immutable-by-`ArtifactId`, read-through cache on the five
      dispatch hot-path read sites (start, invoke, complete, create-bookmark, dispatcher). Lease/GC
      reads (`LoadClosureAsync`, deletion guard) stay uncached (FR-004).
- [ ] T012 Ensure no singleton→scoped captivity: lease manager / GC keep the uncached store handle.
- [ ] T013 Guardrail test: byte-identical committed state and outputs with cache on vs off.
- [ ] T014 Counting-store test: exactly one durable `FindAsync` per pinned artifact per drain;
      isolation across concurrent executions.
- [ ] T015 Full projects green (`Elsa.Workflows.Runtime.Tests`, `Elsa.Persistence.Groundwork.Tests`,
      `Elsa.Activities.Runtime.Tests`); architecture guard if a runtime seam is added.

## Phase 2 — Measure

- [ ] T020 Re-run `DurableRoundTripDiagnostics` with the cache on; report executable-read reduction
      (~5×N → ~1) and wall-time delta; confirm all other durable-op counts unchanged.
