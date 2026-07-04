# Elsa 4 Architecture Review Remediation

Status: active.

Area: cross-domain remediation of the 2026-07 architecture review findings (W1–W21).

Steward(s): Sipke plus active architects/agents.

## Purpose

Coordinate execution of the improvement roadmap produced by the
[Elsa 4 architecture review 2026-07](../reports/elsa-4-architecture-review-2026-07.md).
The per-work-unit implementation briefs (goal, verified current state, scope, acceptance
criteria, dependencies, skill routing) live in
[docs/reports/elsa-4-architecture-review-2026-07/roadmap.md](../reports/elsa-4-architecture-review-2026-07/roadmap.md)
and are the canonical scope definitions; this bucket tracks which units are planned, in
flight, or done, and which sessions/branches own them.

## In Scope

- Work units W1–W21 as briefed in the roadmap.
- Sequencing and conflict coordination between units and with in-flight work
  (notably specs/083 Move 2 on the runtime spine).
- Promoting remediation outcomes into their proper source-of-truth layers
  (constitution gates, glossary terms, maps, specs).

## Out Of Scope

- New review findings (file those in reports first).
- Redefining unit scope outside the roadmap briefs (amend the roadmap document instead).
- Runtime pipeline decomposition itself (owned by [Runtime Execution Seam](runtime-execution-seam.md); W12.2 defers to it).

## Active Objectives

Phase 0 (safety & correctness) — **COMPLETE 2026-07-03**; all six units merged to main:

1. W6 Repo hygiene quick wins (MD-1/2/3/4/7, IN-12) — **done** ([#373](https://github.com/elsa-workflows/elsa-foundation/pull/373); constitution v3.1.0 + expanded guard suite).
2. W3 State versioning contract (PS-1, PS-3) — **done** ([#426](https://github.com/elsa-workflows/elsa-foundation/pull/426) → re-delivered to main via [#428](https://github.com/elsa-workflows/elsa-foundation/pull/428); `IGroundworkRuntimeDocumentSerializer`, per-kind versions, upcaster registry, v1 golden fixtures, schema-evolution contract in `docs/serialization.md`).
3. W4 Endpoint security model (MS-12/13/16) — **done** ([#429](https://github.com/elsa-workflows/elsa-foundation/pull/429); endpoints secured by construction, per-shell `ApiSecurityFeature` opt-out, OIDC 401 fix, Elsa.Server secured in all environments).
4. W2 Durable resumption chain (PS-2, RT-3) — **done** ([#427](https://github.com/elsa-workflows/elsa-foundation/pull/427); durable Groundwork scheduler work queue on the W3 serializer, `IRuntimeResumptionService` + resumption pump feature, `docs/runtime-durable-resumption.md`).
5. W5 Ownership enforcement (RT-2) — **done** ([#430](https://github.com/elsa-workflows/elsa-foundation/pull/430); fencing at the checkpoint-commit funnel, monotonic lease tokens surviving release, drain-scoped lease/heartbeat closing window C's visibility half, drainer TOCTOU tripwire).
6. W1 Fault semantics end-to-end (RT-1/5/12/14) — **done** ([#431](https://github.com/elsa-workflows/elsa-foundation/pull/431); Running→Faulted via `BlockingIncidentWorkflowFaultObserver`, poison store + retry policy in the drainer crash path, `AcceptedButFaulted` drain-result propagation, structured fault capture, `ListIncidents` operator endpoint).

Phase 1 (feature parity) — **COMPLETE 2026-07-03**; all three units merged to main:

1. W8 Durable timers (E3-2) — **done** ([#433](https://github.com/elsa-workflows/elsa-foundation/pull/433); `durableTimer` document kind + `IDurableTimerStore` (in-memory + Groundwork), `DurableTimerPumpTask` in the new Scheduling package, `Delay` activity in the new Activities.Scheduling package, `[ResumeTarget]` compilation in `WorkflowExecutableCompiler` — the first suspending activity through the real publish pipeline; `docs/runtime-durable-timers.md`). Timer/Cron START triggers deferred to a W7-dependent follow-up.
2. W9 Checkpoint coalescing persistence policy (E3-6, RT-10) — **done** ([#435](https://github.com/elsa-workflows/elsa-foundation/pull/435) + hardening test [#437](https://github.com/elsa-workflows/elsa-foundation/pull/437); opt-in `AddCoalescingRuntimeCheckpointPersistence` with ambient-session decorators, default Immediate path byte-identical, single atomic flush at quiescence/boundary gated by W5 fencing, two-generation crash-convergence proof, benchmark: 3→1 durable commits per burst = Elsa 3 parity; coalescing doctrine + governing invariant in `docs/runtime-durable-resumption.md`).
3. W7 Trigger subsystem + global stimulus routing (E3-1 Critical, E3-5) — **done** ([#434](https://github.com/elsa-workflows/elsa-foundation/pull/434); publish-time trigger index over published artifacts (`workflowTriggerBinding` kind, indexing failure fails the publish), `IStimulusRouter` start + cross-execution fan-in resume through the existing single-writer dispatchers, narrow `IBookmarkStimulusIndex` with additive by-stimulus index, real `Event` start-trigger activity via the `IActivityTriggerStimulusProvider` seam, `WorkflowsRuntimeTriggersFeature`, secured `POST runtime/workflows/stimuli` endpoint). Closes the largest Elsa 3 parity gap: a stimulus with no execution id can start and fan-in resume workflows.

Phases 2–3 (remaining W-units): queued; see the roadmap's dependency graph.

### Follow-up findings recorded during Phase 0 execution

- **Ack-based dequeue for full window-C closure** (from W5): guaranteed item-level replay
  requires the durable scheduler work queue to hold a dequeued item until the consuming
  handler's checkpoint commits (release-on-ack), instead of load-then-delete. W5's lease
  primitive unblocks this; see the "still open" increment in
  [docs/runtime-durable-resumption.md](../runtime-durable-resumption.md). Candidate new unit.
- **Design endpoints bypass endpoint security** (from W4, pre-existing): 15 endpoints under
  `src/Elsa/Activities/Design/Api/` and `src/Elsa/Workflows/Design/Api/` call
  `AllowAnonymous()` explicitly and serve anonymously even on secured shells. Candidate new
  unit (or fold into W18 identity work).
- **Durable poison store** (from W1): `IWorkflowSchedulerPoisonStore` ships with an
  in-memory default; a Groundwork-backed implementation (through the W3 serializer) is the
  natural follow-up so poison records survive restarts.

### Follow-up findings recorded during Phase 1 execution

- **Node-scoped resume targets** (from W8): executable resume targets are keyed by the
  `[ResumeTarget]` attribute id, so a workflow supports only one instance of a given
  resume-target activity (duplicates fail compilation loudly). Node-scoped ids
  (`ExecutableNodeId` + attribute id) lift the limit and require a matching resume-resolver
  change; see `docs/runtime-durable-timers.md`.
- **Native due-time range index in Groundwork** (from W8): Groundwork queries are
  equality-only, so the timer pump's `ListDueAsync` loads the whole timer partition and
  filters in memory; a native range index is the scale follow-up.
- **Timer/Cron start triggers** (from W8 scope cut): W7's trigger index has now landed;
  Timer/Cron start-trigger activities on top of `IActivityTriggerStimulusProvider` + the
  `durableTimer` store are ready to build. Candidate next-wave unit.
- **Event wait-form (mid-flow suspension)** (from W7): the `Event` activity ships
  start-only; a suspending wait-form ([ResumeTarget] resume path, dual start/wait modes)
  is a straightforward follow-up now that the publisher compiles resume targets.
- **Groundwork added-index backfill** (from W7; **fixed in Groundwork preview.16**, adopted via
  GW-BUMP): adding an index to an existing document unit now backfills projections for
  pre-existing documents on a manifest version bump (Groundwork PR #21 — delete-then-insert
  inside the materialization transaction), so they are visible to the new index without a
  re-save. The empirical probe that guarded the earlier gap was flipped into
  `GroundworkAddedIndexBackfillRegressionTests`, and all four `Groundwork.*` packages were
  bumped 0.0.1-preview.10 → preview.16. See `docs/serialization.md`.
- **Start-path idempotency is process-local** (from W7): `IStimulusStartDeduplicator` is an
  in-memory default; without an idempotency key the start path is at-least-once (a duplicate
  stimulus delivery may double-start). A durable dedup ledger is the hardening follow-up.

### Follow-up findings recorded during Phase 2 execution (W14 naming pass)

- **`ISecretManager` rename deferred to W18** (identity/secrets): the review's Family D
  target name `ISecretStore` is already taken by a semantically-distinct existing type — the
  per-provider backing store (`EncryptedSecretStore`, `ConfigurationSecretStore`, aggregated
  by `ISecretStoreRegistry`). `ISecretManager` is a higher-level CRUD/lifecycle facade
  (Create/Find/List/Update/Rotate/Revoke/Delete/Test/ResolvePayload) over those stores, so it
  cannot take the `…Store` name without collision. **W18 follow-up: ISecretManager naming +
  store-vs-resolver split decided together in W18; interim rename deliberately skipped
  (`ISecretStore` name occupied by per-provider backing stores).** A fresh interim name
  (`ISecretService`/`ISecretCatalog`/`ISecretDirectory`) was rejected to avoid the
  double-rename churn W18's split would immediately re-litigate. Renamed in W14: only the
  clean Family D targets.
- **`ControlPlaneCommand` activation-reason enum member** (cosmetic): after Family B renamed
  `ControlPlaneState`→`WorkflowHoldState`, the `WorkflowExecutionActorActivationReason`
  member `ControlPlaneCommand` still reads "control plane". Left unchanged — enum member names
  are serialized wire values (member/int), not renamed under the type-only naming pass. A
  future wire-versioned pass could align it; the term is glossary-documented in the meantime.

## Linked Surfaces

- [Consolidated review report](../reports/elsa-4-architecture-review-2026-07.md)
- [Roadmap briefs W1–W21 + skill routing](../reports/elsa-4-architecture-review-2026-07/roadmap.md)
- [Detail sub-reports](../reports/elsa-4-architecture-review-2026-07/README.md)
- Related buckets: [Runtime Execution Seam](runtime-execution-seam.md) (W1/W5/W12),
  [Code Reality And Test Maturity](code-reality-and-test-maturity.md) (W15),
  [Diagnostics Observability Readiness](diagnostics-observability-readiness.md) (W19),
  [Constitution Readiness](constitution-readiness.md) (W6.3, W14, W21).

## Current Roadmap Notes

- One unit per branch/PR; reference finding IDs; follow the roadmap's execution protocol
  (Speckit flow, constitution gates, skill routing, map refresh).
- **PR-base pitfall (learned in Phase 0):** PRs created by worker sessions can default their
  base to the session's base branch instead of `main`. Always create with
  `gh pr create --base main` and verify `baseRefName == main` before review/merge.
- **Second-lander rule (learned in Phase 0):** when parallel units touch the same runtime
  files, whoever lands second merges main and re-runs all affected suites before their PR
  is reviewed.
- Update this bucket as units complete or move; link PRs next to each objective.
