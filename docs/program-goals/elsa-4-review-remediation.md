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

Phase 2 (structure & DX) — **COMPLETE 2026-07-04**; all six units merged to main:

1. W15 Test hardening (TS-1, TS-9, IN-4) — **done** ([#441](https://github.com/elsa-workflows/elsa-foundation/pull/441); `TaskExecutor` non-blocking lock acquisition with skip+log, decorator/ordering/identity assertions preserved through downgrades, 12 cancellation contract tests).
2. W10 Mediator consolidation (AR-2, AR-5) — **done** ([#442](https://github.com/elsa-workflows/elsa-foundation/pull/442); unified `PipelineBuilder<TContext>`/`PipelineDelegate<TContext>`, closed-generic handler dispatch via `HandlerInvokerMiddleware` + `CompiledHandlerInvoker`, `TryAddEnumerable` registrations; Events builder migrated).
3. W11 Hot-path fixes (IN-2, IN-5, IN-10) — **done** ([#443](https://github.com/elsa-workflows/elsa-foundation/pull/443); background publisher on host lifetime with wired `StopAsync`, Jint engine-options caching, Events dispatch caching).
4. W13 DRY sweep (PS-7/8/9, DS-3/4/7, MS-7, TS-3) — **done** ([#449](https://github.com/elsa-workflows/elsa-foundation/pull/449); 8 duplication families collapsed one-per-commit, net −1369 LoC: generic `ValidateStateIdMatches<TState>`, shared `ExecutableStructureReader`, `GroundworkDocumentStore` base proven by golden fixtures, `SqliteShellFeatureDefaults`, `AgentProposalAuthorization`, canonical in-memory document-store fake in `Elsa.Persistence.Groundwork.Testing`, dead `ExpressionDescriptor` deleted, `ActivityArgumentValue`/`ActivityArgumentState` renames).
5. W12 Runtime structure (RT-4/6/7/8/11) — **done** ([#450](https://github.com/elsa-workflows/elsa-foundation/pull/450); `AddWorkflowRuntimeCore` host-agnostic composition root, ADR-0029 Move 2 finished across all handlers with ordered commit-list Checkpoint slot, both ambient service locators deleted, required-dependency single constructors, `ConditionalWeakTable` payload memo; continuation spec `specs/084`).
6. W14 Naming pass (NM findings, R1–R8) — **done** ([#457](https://github.com/elsa-workflows/elsa-foundation/pull/457); rename families A/B/D/E/C type-only with all persisted wire identifiers preserved verbatim, constitution §E6 type-naming rules v3.2.0, glossary entries for renamed terms + codenames, `ISecretManager` deferred to W18 — see follow-ups).

Side units landed with Phase 2:

- **Groundwork added-index backfill** — gap fixed upstream (Groundwork PR #21, published as
  `0.0.1-preview.16`) and adopted via [#455](https://github.com/elsa-workflows/elsa-foundation/pull/455)
  (all four `Groundwork.*` pins bumped; probe flipped to `GroundworkAddedIndexBackfillRegressionTests`).
- **Audit-issue reconciliation** — the 50 automated-audit issues #374–#423 dispositioned against
  merged remediation ([#456](https://github.com/elsa-workflows/elsa-foundation/pull/456);
  report + disposition table on [#424](https://github.com/elsa-workflows/elsa-foundation/issues/424):
  5 fixed/closed, 5 partially fixed, 40 still open incl. all 5 Tier-0 security issues → Phase 3 W18).
- **Hotfix [#454](https://github.com/elsa-workflows/elsa-foundation/pull/454)** — repaired a
  crossed-merge build break between [#440](https://github.com/elsa-workflows/elsa-foundation/pull/440)
  and [#453](https://github.com/elsa-workflows/elsa-foundation/pull/453) (`ConstructedActivity`
  projections move).

Phase 3 (W16–W21): **complete** — W18 solo first ([#461](https://github.com/elsa-workflows/elsa-foundation/pull/461)),
then the W16/W17/W19/W21 parallel wave ([#465](https://github.com/elsa-workflows/elsa-foundation/pull/465),
[#463](https://github.com/elsa-workflows/elsa-foundation/pull/463),
[#464](https://github.com/elsa-workflows/elsa-foundation/pull/464),
[#462](https://github.com/elsa-workflows/elsa-foundation/pull/462)), then W20 solo
([#467](https://github.com/elsa-workflows/elsa-foundation/pull/467)). All roadmap units W1–W21 are merged.

- **W18 Identity & secrets hardening** — **done** ([#461](https://github.com/elsa-workflows/elsa-foundation/pull/461)): the 5 Tier-0
  security issues [#374](https://github.com/elsa-workflows/elsa-foundation/issues/374)
  (AgentEndpointActor fail-closed), [#375](https://github.com/elsa-workflows/elsa-foundation/issues/375)
  (getConfiguration case/hierarchy bypass), [#376](https://github.com/elsa-workflows/elsa-foundation/issues/376)
  (OpenTelemetryRedactor never redacted Traces), [#377](https://github.com/elsa-workflows/elsa-foundation/issues/377)
  (shared constant-time management API-key auth helper), and
  [#406](https://github.com/elsa-workflows/elsa-foundation/issues/406)
  (role→permission claim expansion), plus the W18 brief hardening: durable Groundwork
  identity store (MS-1, own `elsa-identity` manifest + golden fixtures), secrets master-key
  rotation via a validated key-ring (MS-4, `ISecretKeyRing`, [`docs/secrets-key-rotation.md`](../secrets-key-rotation.md)),
  and audit visible-by-default with failed-operation records (MS-5/5b, `LoggingSecretAuditSink`;
  fixes the pre-existing `SecretAuditTests` setup gap). Deliberately excluded: the
  `ISecretManager` store-vs-resolver split (proposal-only in the PR body per the W14 deferral)
  and the design-endpoints `AllowAnonymous` bypass (kept as the tracked finding below).

- **W19 Self-observability (MS-9, MS-14)** — **done** ([#464](https://github.com/elsa-workflows/elsa-foundation/pull/464), closes #393): engine
  tracing + API error contract. MS-9 introduces the first `ActivitySource` in the repo — an
  `IWorkflowEngineTracer` replacement contract (`Elsa.Workflows.Runtime.Core.Diagnostics`)
  whose allocation-free no-op default (`NullWorkflowEngineTracer`) is swapped by the opt-in
  `WorkflowsRuntimeTracing` shell feature (`Elsa.Workflows.Runtime.Tracing`) for the real
  `ActivitySourceWorkflowEngineTracer`. Four behaviour-preserving span sites on source
  `Elsa.Workflows.Runtime` (drain → dispatch → activity.execute / checkpoint.commit): no new
  awaits in the fenced drain/commit sequences, no W12 slot reordering, tags set only via
  `activity?.SetTag` after values exist, stable names in `WorkflowEngineTelemetry`; an
  `ActivityListener` span-tree acceptance test asserts the parent-child structure. MS-14 adds a
  global ProblemDetails error contract (`ProblemDetailsFastEndpointConfigurator` →
  `config.Errors.UseProblemDetails()`) for every Elsa endpoint (W16's new endpoints inherit it),
  and folds issue [#393](https://github.com/elsa-workflows/elsa-foundation/issues/393): the
  FastEndpoints handler base classes map a new `EntityNotFoundException` (`Elsa.Primitives`,
  the lowest project already referenced by both the endpoint bases and the Design stores — no
  new dependency edge) to `404`, with the bounded Design/Activities-Design not-found lookup
  throw sites converted. Docs: [`docs/reference/engine-telemetry.md`](../reference/engine-telemetry.md)
  draws the ENGINE-telemetry vs. OpenTelemetry-ingestion distinction the review flagged. Cross-links
  the [Diagnostics Observability Readiness](diagnostics-observability-readiness.md) bucket.

- **W17 Publishing completion** — **done** ([#463](https://github.com/elsa-workflows/elsa-foundation/pull/463), closes #397/#398): DS-1 extracted a
  contracts-only `Elsa.Workflows.Publishing.Core` from the `.Api` endpoint project (compiler
  impl deliberately left in `.Api` — no third sub-100-LoC project), covered by the Architecture
  layering guard. DS-2 verified the production publish path is already durable — the executable
  persists through the runtime's Groundwork-backed `IWorkflowExecutableStore`, not the in-memory
  transient staging store — so no Publishing-owned duplicate store/manifest was added (a conflicting
  document kind would be a wire-level bug); proven by a restart-survival test (file-backed SQLite
  reopen) and documented in [`docs/serialization.md`](../serialization.md). DS-5/DS-6 resolved
  every checked-in empty endpoint stub: implemented `Get`/`Update`/`Delete` under
  `Workflows/Design/Api/Endpoints/Definitions` (`Update` = the Draft-mutation gate forwarding to the
  single coarse `IUpdateDraftCommand`), each secured by construction via `ConfigurePermissions()`
  (W4, never anonymous) with handler + permission-guard tests in the new
  `Elsa.Workflows.Design.Api.Tests` project; deleted the `Versions/Delete` stub plus all three
  `Activities/Design/Api` stubs (published versions are immutable — no per-version delete). The W7
  publish→trigger-index hook was verified already wired (`PublishWorkflowRequestHandler` indexes
  within the publish flow; indexing failure fails the publish) — verified, not reworked. Folded
  [#397](https://github.com/elsa-workflows/elsa-foundation/issues/397) (version-source resolution
  moved inside the compile error path so the typed `WorkflowExecutableCompilationException`
  propagates unwrapped) and [#398](https://github.com/elsa-workflows/elsa-foundation/issues/398)
  (expiry/TTL bound on `InMemoryWorkflowTestRunStore`), both with tests. New Publishing `.Core`
  seam catalogued in [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).

- **W21 Modularity ergonomics (MD-5, MD-6, MD-10)** — **done** ([#462](https://github.com/elsa-workflows/elsa-foundation/pull/462); branch
  point `1d5bb6bb`). Governance/analysis unit, no `src/` change beyond 5 registration tests.
  MD-5 amendment and ADR 0033 were both **ratified 2026-07-04** at Phase 4 kickoff (see below).
  **MD-5:** fresh LoC audit (13 projects <100 physical LoC; smallest 32) — all 13 map to a named
  exception class, so **zero forced merges**; proposed a *soft* minimum-viable-project amendment
  (framework §2.16.1: guidance threshold + six exception classes, no hard gate) as a draft routed
  through Constitution Readiness, not applied to `constitution.md`
  ([report](../reports/elsa-4-w21-md5-minimum-project-size-amendment.md)).
  **MD-6:** `Elsa.Workflows.Runtime.Core` charter audit — grown to 19,029 LoC, of which
  `Services/` (10,092 LoC / 94 files) is engine logic breaching the §2.1 `.Core` charter (a
  *semantic* breach the mechanical dependency-envelope guards miss); disposition = contracts-vs-engine
  split aligned with ADR-0029 / `specs/084`, recorded as **proposed**
  [ADR 0033](../adr/0033-runtime-core-splits-contracts-from-engine.md) +
  [audit report](../reports/elsa-4-w21-md6-runtime-core-charter-audit.md) (ratification owned by the
  runtime-execution-seam architect; no split executed).
  **MD-10:** §2.23.1 feature-registration audit re-enumerated at branch point — 70 concrete features,
  47→**52 covered** after stamping 5 pattern-matched registration tests (Mediator, MemoryCache,
  Secrets, Liquid, JavaScriptLibraries; tests/ only), 18 remaining gaps filed with per-feature
  file:line evidence grouped by scaffolding need
  ([gap report](../reports/elsa-4-w21-md10-feature-registration-test-gap.md)).
  Snapshot caveat stated: counts are at `1d5bb6bb`; parallel W16/W17 will shift them.

- **W16 Activity library (DS-16, partial DS-8/DS-9)** — **done** (PR
  [#465](https://github.com/elsa-workflows/elsa-foundation/pull/465); second-lander after
  W17/W19/W21, merged clean). Closes DS-16 with four incremental packages, each activity shipping
  activity + descriptor + registration + unit tests + sample workflow. **1a `Elsa.Activities.Http`
  SendHttpRequest:** outbound call via `IHttpClientFactory` with sensible timeout/redirect defaults
  and status→outcome error mapping. **1b HttpEndpoint + WriteHttpResponse + `HttpEndpointMiddleware`:**
  start trigger through W7's `IActivityTriggerStimulusProvider` seam (webhook-style stimulus);
  async/202 baseline — `WriteHttpResponse` records the intended response (status/headers/body) into
  workflow state as an observable typed artifact (documented contract, no silent no-op). Sync
  response correlation deferred (follow-up "HTTP synchronous response correlation" — keyed channel +
  timeout + the multi-node problem W20 creates, designed together not bolted on). **2 Timer/Cron
  start triggers (`Elsa.Activities.Scheduling` + `Elsa.Workflows.Runtime.Scheduling`):** dedicated
  recurring-trigger schedule store (new §E6 doc kind `recurringTriggerSchedule` + golden fixture,
  in-memory + Groundwork) + hosted pump through `IStimulusRouter`; missed-occurrence policy = fire at
  most once and advance to next future occurrence, never replay backlog; W20 cluster-safety hook =
  `TryAdvanceAsync` compare-and-swap on `NextOccurrence`. Cronos pinned in `Directory.Packages.props`.
  **3 `Elsa.Activities.Scripting` RunJavaScript:** hardened on the existing Jint infra (W11's
  `JintEngineFactory` already applies cancellation + timeout/statement/recursion constraints to the
  activity path — partial DS-9). Two new Runtime.Core seams catalogued
  ([`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md)): `IRecurringTriggerScheduleStore` +
  `IRecurringTriggerScheduleProvider`. **Deferred (design-only in PR body):** DS-8 workflow-as-activity
  execution semantics (own gate); remaining DS-9 non-activity evaluator paths; legacy internal
  `Elsa.Workflows.Runtime.JavaScript` RunJavaScript stub cleanup (provably wire-invisible — superseded,
  not renamed); email/messaging provider modules.
- **W20 Distributed actor provider (E3-3)** — **done** ([#467](https://github.com/elsa-workflows/elsa-foundation/pull/467); branch point
  `a5970003`). New opt-in leaf `Elsa.Workflows.Runtime.Distributed` adding a clustered
  `DistributedWorkflowExecutionActorProvider` (sibling to `InProcessWorkflowExecutionActorProvider`;
  W14 `WorkflowExecutionActor*` family symmetry) that layers per-execution **placement** over the
  in-process provider. Two leaf-owned contracts (§2.7 — Runtime.Core gains zero references):
  `IExecutionPlacementStore`/`IExecutionPlacementService` (CAS lease ownership, `TimeProvider`-driven
  durations) and `IExecutionCommandTransport` (durable, ack-based/at-least-once cross-node command
  inbox). A `ExecutionPlacementPumpTask` renews held placements, claims backlog, and drains it locally,
  re-driving stranded commands on failover when a dead node's placement + transport leases expire. The
  **placement-is-routing / fencing-is-safety** layering is the heart of the unit: placement only picks
  which node drains, while the unchanged W5 single-writer fencing token checked at checkpoint commit is
  the authoritative double-execution guard (a superseded node's late write is rejected with
  `RuntimeStaleFencingTokenException`). The two-node kill-mid-drain acceptance test drives real W5
  ownership over a shared liveness store and asserts BOTH inbox re-drive on failover AND fencing
  rejection of the dead node's commit — command commits exactly once. This unit ships an **in-memory
  harness only**; a durable Groundwork placement + transport store is a **named follow-up**, a
  mechanical drop-in against the now-frozen contracts and the committed v1 golden fixture (document kind
  `executionCommandTransport`, protected by a drift test). W16's `TryAdvanceAsync` recurring-pump
  cluster-safety seam was **not** touched (out of required scope). New leaf seams catalogued in
  [`EXTENSION_POINTS.md`](../../src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md) and glossary.

Phase 4 (W22–W32 + product track): **approved 2026-07-04** by Sipke per the
[Phase 4 handoff](../reports/elsa-4-architecture-review-2026-07/phase-4-handoff.md) §3, with
Wave-A-first ordering (outgoing control room's recommendation). Kickoff decisions:

- **Ratified:** the MD-5 minimum-project-size amendment (applied as framework §2.16.1,
  v3.0.0 → v3.1.0, Elsa cascade v3.2.0 → v3.3.0 — see the
  [amendment index](../reports/constitution-amendment-index.md)) and
  [ADR 0033](../adr/0033-runtime-core-splits-contracts-from-engine.md) (accepted; execution is W28).
- **Wave A — COMPLETE 2026-07-05.** All five correctness units + the #378 hotfix merged,
  22 issues closed, every unit failing-test-first with control-room QA (detached-worktree
  suites + revert red-proof) before merge:
  - **#378 hotfix** — **done** ([#474](https://github.com/elsa-workflows/elsa-foundation/pull/474)):
    Elsa-3 import De Morgan inversion; inputs/outputs populate again.
  - **W23 serialization/expressions** — **done** ([#476](https://github.com/elsa-workflows/elsa-foundation/pull/476)):
    #407 Jint generic-collection enumeration, #408 identifier-start validator, #409 dead
    `$ref`/`$id` machinery removed (byte-compare golden fixture proves wire shapes unchanged),
    #422 item 3 Liquid double-parse-failure NRE.
  - **W24 HTTP/files** — **done** ([#478](https://github.com/elsa-workflows/elsa-foundation/pull/478)):
    #388 exhausted-stream XML, #389 dispose-before-cleanup, #390 cache rewind + truncation,
    #416 slice 1 zip entry-stream leak; new `Elsa.Http.Tests` suite (public-sealed visibility,
    no InternalsVisibleTo per constitution gate).
  - **W25 persistence/EFCore** — **done** ([#480](https://github.com/elsa-workflows/elsa-foundation/pull/480)):
    #394 type-wide save semaphore removed, #395 migrations DbContext disposal, #396
    ChangeToken TOCTOU (generation marker + Lazy CTS), #403 OTel-parity prune retry +
    idempotent dispose, #404 definition-scoped lock + exists-check + typed conflict
    (`Design.Api → Locking.Core` edge, maps regenerated), #417 item 6
    `WorkflowVersionNumbering.NextMajor` dedup.
  - **W26 API/design** — **done** ([#481](https://github.com/elsa-workflows/elsa-foundation/pull/481)):
    #384 PageArgs contract (new `Elsa.Primitives.Tests` suite), #387 dead exception filter,
    #391 agent-proposal logging + cancellation propagation, #392 TenantAgnostic threading,
    #411 async store trio + subscribe-before-replay SSE with sequence de-dup, #414 items 1+5
    (`AgentSessionAuthorization`, `ElsaEndpointPermissions.Compose`).
  - **W22 runtime/flowchart** — **done** ([#479](https://github.com/elsa-workflows/elsa-foundation/pull/479)):
    #381 Do/While body validation, #383 output-argument contravariant rebinding, #386 outbox
    validation un-wrapped, #399 ActivityExecutions StateId validation, #382 flowchart state
    prune-on-save + diagnostics cap behind a Stage-1.5 design gate — §E6 golden fixture pins
    the **ordinal** enum encodings + `elsa.flowchart.executionState` key; Canceled/Faulted
    paths deliberately retained for race-loser late-completion absorption.
- **Wave B — COMPLETE 2026-07-05** (W27 → W28 → W29, sequenced, all merged with control-room
  QA — detached-worktree suite reproduction + revert/mutation bite-proofs — before each merge):
  - **W27 Groundwork durable placement + transport stores — done 2026-07-05**
    ([#484](https://github.com/elsa-workflows/elsa-foundation/pull/484), merge `c8f9b393`).
    Durable stores for the frozen leaf contracts in a NEW bridge project
    `Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork` (Identity-bridge shape,
    self-contained against the Groundwork packages — no `Elsa.Persistence.Groundwork`
    reference). The W20 "mechanical drop-in" claim was **refuted at Stage-1**: no reference
    edge existed between the leaf and Groundwork persistence, and no shipped Groundwork store
    did true cluster-safe CAS. Per user ruling ("no workarounds for Groundwork"), the missing
    primitive was added UPSTREAM first:
    [valence-works/Groundwork#23](https://github.com/valence-works/Groundwork/pull/23)
    (`ExpectedVersion = 0` = create-only across all providers; spec 014 amendment makes the
    full EV semantics matrix normative; published as `0.0.1-preview.18`, pins bumped 16 → 18).
    On that primitive: strict store-enforced per-execution sequence uniqueness via create-only
    `{executionId}:{sequence}` transport ids; exact single-lease placement first-claim (no
    seed window); renewal/release via envelope-version CAS matched on owner+token. New §E6
    kind `executionPlacement` frozen at v1 (golden fixture + drift test); the committed
    `executionCommandTransport` fixture stayed byte-identical. Two-node kill-mid-drain
    acceptance now runs over BOTH cluster flavors (in-memory + Groundwork over one shared
    store). The repo test double `InMemoryDocumentStore` was aligned to provider-true EV
    semantics with a double-vs-real-SQLite matrix contract test pinning them together.
  - **W28 ADR 0033 execution — done 2026-07-05**
    ([#487](https://github.com/elsa-workflows/elsa-foundation/pull/487), merge `2a05165b`).
    Behavior-preserving split of `Elsa.Workflows.Runtime.Core`: `Services/` (96 files) +
    `Resolvers/` + composition root (renamed `AddWorkflowRuntimeCore` → `AddWorkflowRuntime`) +
    tracer implementations + concrete checkpoint/invoke middleware moved to the new engine
    project `Elsa.Workflows.Runtime`; contracts/models stay in `.Core` (NuGet identity
    preserved; namespaces retained on all moved types — zero persisted identifiers changed,
    all golden fixtures byte-identical). **Models/ boundary ratified by user 2026-07-05: move
    nothing** — full 77-file per-type audit committed as
    [elsa-4-w28-models-boundary-audit.md](../reports/elsa-4-architecture-review-2026-07/elsa-4-w28-models-boundary-audit.md).
    Two documented ADR deviations: the 2 coalescing interfaces (expose concrete
    `RuntimeCoalescingSession`) and the 2 runtime pipeline builders (construct concrete
    middleware) moved with the engine. Engine-side consumer set amended from the ADR's 5 to 8
    (adds Distributed, Tracing, Flowchart — injects `RuntimeCheckpointCommitter` — and
    Publishing.Api; Runtime.JavaScript verified contract-only). Five `internal`→`public`
    widenings in `.Core` forced by the assembly boundary (no-IVT §2.23.3). New
    `RuntimeCoreEngineShapeGuardTests` semantic guard (suffix-keyed, red-proven, with a
    predicate-liveness fact); Architecture suite 47 → 49. All nine affected suites at
    identical before/after counts.
  - **W29 security/design follow-ups — done 2026-07-05**
    ([#491](https://github.com/elsa-workflows/elsa-foundation/pull/491), merge `4938c062`).
    Item 1 (design-endpoints `AllowAnonymous` removal) found **already delivered** by the
    earlier D-sweep + the `ApiSecurity.AllowAnonymous` kill-switch (default off,
    Development-only, warning-logged) — residual shipped: endpoint-security regression pins
    for all 18 design endpoint files + an inventory guard against unpinned new endpoints.
    `ISecretManager` store-vs-resolver split executed per the PR #461 proposal
    (`ISecretResolver` → `ISecretValueResolver`, `ResolvePayloadAsync` off the facade, duplicate
    lifecycle-policy evaluation eliminated; swap-registration test proves resolver replaceable
    without touching the manager). Secrets golden-fixture gate: kind `secret` pinned at
    `tests/Elsa/Secrets/Tests/Fixtures/v1/secret.json` (two-version fixture covering both
    payload wire variants, fake ciphertext only; drift + legacy-load tests per the Identity
    pattern; the computed `LatestActiveVersion` wire quirk deliberately pinned). #414 item 7
    fixed at ALL FOUR raw exception-log sites (Anthropic + 3 GitHubCopilot) — redacted
    rendering via `Normalize(ex.ToString())`, failing-first with capturing-logger proofs;
    cross-provider redaction-helper dedup deliberately left to W31. Granular design
    permissions (`Design:Read`/`Write`) explicitly NOT introduced — would change the deployed
    permission contract and needs Studio coordination; own gated unit if ever wanted.
- **Wave C — W30 god-class refactors COMPLETE 2026-07-06** (all three behavior-preserving, each with
  bite-proven guards and control-room QA on a detached worktree before merge; handoff at
  [wave-c-handoff.md](../reports/elsa-4-architecture-review-2026-07/wave-c-handoff.md)):
  - **W30a FlowchartExecutionEngine (#275)** — done
    ([#495](https://github.com/elsa-workflows/elsa-foundation/pull/495), merge `93864a53`): engine
    920→267 lines; 7 collaborators; single-home `Sequence`/`ScheduleNode` rule; W22 `#382`
    `PruneForPersistence` moved verbatim; `elsa.flowchart.executionState` wire byte-identical
    (CT-1/CT-2/CT-3 determinism goldens, QA-bite-verified via a `NewId` sequence perturbation);
    `Scopes` residual untouched (own later gate). Single project, no maps/guard churn.
  - **W30b WorkflowExecutableCompiler (#418)** — done
    ([#494](https://github.com/elsa-workflows/elsa-foundation/pull/494), merge `f756473a`): compiler
    466→91-line orchestrator + 4 DI collaborators; duplicate `ProjectChildren` tree-traversal collapsed
    to one walk (counting-decorator fact); 7-definition golden corpus pins `WorkflowExecutable` +
    `ArtifactHash` byte-identical (QA-bite-verified via artifact-id hash length). Runtime.Core `Models/`
    frozen (untouched).
  - **W30c ExtensionBuilderStorage (#421)** — done
    ([#497](https://github.com/elsa-workflows/elsa-foundation/pull/497), merge `418ad4db`): façade
    2,210→1,288 (net −269 LoC); extracted `GitClient` (single git stack, `GIT_TERMINAL_PROMPT=0` folded
    in, retiring Stack B + the three dead methods `GetActiveBranch`/`IsRepositoryDirty`/`GetRemoteState`
    per item 2), `RepositoryInspector`, `RepositoryFileSystem`, `RepositoryTemplateRenderer`,
    `BuildOrchestrator`; `WithStateAsync`/`ReadStateAsync` collapse the gate boilerplate (item 1). Seven
    frozen `state.json` keys + enum wire values preserved; git prompt-safety guard strengthened to a
    direct assertion after QA found the first version vacuous (no-TTY env). Single-writer `_gate` intact.
    Items 3/4/5 OUT of scope. No project/DI/contract/maps change.
- **Wave C — event-delivery split + W31 DRY batch COMPLETE 2026-07-06** (all 8 units control-room-QA'd
  on detached worktrees — `/code-review` + build + affected suites + architecture guard 49/49 +
  independent mutation/compile-pin bite — before each merge; every behavior-changing, cross-provider,
  or design-fork item deferred to its own gate):
  - **Event-delivery split (#505**, merge `e49195f6`): ratified `IInlineEventPublisher`/`IDeferredEventPublisher`
    as thin PUBLIC wrappers over the retained (public, per Sipke's Stage-1 revision) `IEventPublisher`;
    `DraftValidationGate` binds to `IInlineEventPublisher` (strategy param deleted) — the structural lock
    that makes the merged-#500 footgun unrepresentable *at the gate* (compile-pin bite). `ParallelProcessingStrategy`
    deleted (zero callers + `List` data race). 19 call sites migrated 1:1 (15 inline, 4 deferred).
  - **W31 DRY batch (#506–#512):** #422 Expressions (#506 — JS library-resource static cache + Liquid
    cache-key prefix); #414 Agent (#507 — parameterized `AgentLogRedaction.Redact(msg,secrets,fallback)`
    preserving the Anthropic-1 vs Copilot-3-secrets difference, `AgentConfigurationBinding`, duplicate-tool-name
    fail-fast); #413 Flowchart (#508 — `MatchingOutboundFlowchartPolicyBase` keeping all 8 public policy
    types [W13 precedent], ForEach O(n²)→O(n), dead-code); #412 Scheduler (#509 — `SchedulerWorkHandlerHelpers`
    public-ized, `NewEnqueueSchedulerWorkIntent`/`DeserializePayload`/`ResolveExecutableNode` dedup, §E6 intent
    strings byte-identical); #415 Persistence (#510 — `UpsertCommandGenerator` `ResolveEntityShape`/`ExtractRowValues`
    helpers [SQL byte-identical], `InMemoryKeyedStateStore<TKey,TState>` base for 5 clean stores); #416 Http
    (#511 — `ResponseOwningStream` leak fix, `OrderBy(Priority)` contract-alignment [slice-3 gate], `Lazy<T>`
    race fix via public factory ctor, parser/handler bases); #417 Design-persistence (#512 — `StateSource*HandlerBase`
    [caught + red-first-fixed an abstract-base assembly-scan registration bug], `SubmittedActivityTreeValidator`,
    scanner resolver-path cache). §2.23.3 honored throughout — types promoted to `public sealed` for testability,
    **zero `[InternalsVisibleTo]`**. Batched `docs/maps` regen in this closure PR (test-file drift the per-PR
    csproj-ref rule didn't trigger).
  - **Deferred backlog → W32 / correctness follow-ups** (tracked, some may want Sipke input): #413 items 3/6,
    #412 items 3/5/8 + Start masking bug, #415 items 1 (Groundwork async-init design fork) + 5, #417 items
    1/3/4/7/8 + AddVersion hardening, and the UpsertCommandGenerator non-Sqlite dialect golden gap.
- **Wave C — W32 follow-up wave COMPLETE 2026-07-06** (7 units from the #514 deferred backlog, ruled
  one-at-a-time with Sipke, all control-room-QA'd — build + affected suites + arch guard 49/49 +
  independent mutation/compile-pin bite — before each merge; maps regenerated in this closure PR):
  - **#412 correctness (#517):** #412 item 5 Cancel terminal-state no-op guard (`IsTerminal()` → no
    commit, preserves Completed/Faulted + cancel idempotency) + Start deserialize catch-filter
    narrowed to a `ParamName` whitelist (stops masking unrelated `ArgumentException`s).
  - **#417 correctness (#515):** item 4 silent-drop surfacing (duplicate-assembly `LogWarning`;
    unknown `DuplicateHandling` → throw), item 7 `DescriptorPayload` deserialize guarded (soft-fail),
    item 8 `GetWithDefinitionAsync` → `EntityNotFoundException` in both stores.
  - **AddVersion hardening (#516):** author-supplied version collision check on the Activities
    `AddVersionCommandHandler` via `FindByDefinitionAndSortKeyAsync` → new
    `ActivityDefinitionVersionConflictException` (mirrors the Workflows sibling; Workflows path was
    already guarded, not touched).
  - **#415 item 1 async-init (#518):** Groundwork Sqlite/PostgreSql sync-over-async factory replaced
    with an `IHostedService`/`IShellInitializer` (Prepare phase) that materializes the store at
    startup into a `public sealed GroundworkDocumentStoreHolder` singleton (owns handle disposal —
    conforms to the CShells shell-singleton-ownership rule). `Add*UnifiedPersistence` signatures
    intact; `Microsoft.Extensions.Hosting.Abstractions` added to the two provider projects.
  - **#417 item 3 attr-inheritance (#519):** `[Version]`/`[Required]` now inherit via a base-chain
    walk (`ReflectionOnlyAttributes` helper); zero in-repo blast radius (no in-repo `[Version]` usage),
    repairs behavior for external base-class-attribute libraries.
  - **Flowchart `Scopes` §E6 (#520):** new persisted `loopIterationCounters` field (appended last,
    golden re-frozen byte-identical through `sequence`, id-determinism preserved) decouples iteration
    numbering from the scope count → `LoopIteration` scopes now pruned (bounded ≤4 after 100 iters).
    No backward-compat (unshipped runtime, per Sipke).
  - **#412 item 3 Window C (#521):** scheduler drainer reordered **peek → dispatch → ack-delete**
    (Mechanism A, no §E6) — the source work item is only ack-deleted after its effect is durable, so a
    fallback-write crash redelivers via the resumption sweep instead of stranding the activity;
    ack-on-fault-before-poison prevents hot-looping. **Closes the documented "Window C"** durability
    gap (`docs/runtime-durable-resumption.md` updated: dequeue side now at-least-once).
  - **Still deferred in [#514](https://github.com/elsa-workflows/elsa-foundation/issues/514):** #413
    items 3/6 (cross-provider N+1 + async-Start contract), #417 item 1 (reconciler N+1 cross-backend),
    #412 item 8 (`ActivityFactory` ctor-contract), #415 item 5 (checkpoint store), UpsertCommandGenerator
    non-Sqlite dialect golden hardening.
- **Wave C — W33 #514 residual backlog CLOSED 2026-07-07** (the last deferred items; each design-gated
  one-at-a-time with Sipke, control-room-QA'd on the COMBINED tree — full affected test PROJECTS +
  arch guard 49/49 + independent bite — before merge; [#514](https://github.com/elsa-workflows/elsa-foundation/issues/514) closed):
  - **UpsertCommandGenerator dialect goldens (#524):** added `Microsoft.EntityFrameworkCore.SqlServer` +
    `Npgsql.EntityFrameworkCore.PostgreSQL` (test-only) and byte-exact goldens for the SqlServer (varbinary-NULL
    CAST) and Postgres (json/jsonb CAST) dialects. MySql log-skipped (Pomelo pins EF9, conflicts with the EF10
    tree); Oracle log-skipped (heavyweight driver for a cosmetic alias) — documented in-test.
  - **#417 item 1 reconciler N+1 (#526):** batched the 3 per-version reads — collection-phase `ListAsync(Ids)`
    prefetch, reconcile-phase union of `Ids`/`ActivityTypeKeys` `IN` reads (new plural filter field, no OR-mode),
    and one new cross-provider `IActivityDefinitionVersionStore.ListByDefinitionIdsAsync`. An in-loop mutable
    Definition/Version index (updated on every create/append) makes it byte-identical to the per-read behavior,
    incl. the same-new-definition-twice-in-one-pass ordering trap (new single-pass result-equivalence tests).
  - **#413 item 3 CountBranches N+1 (#528):** added additive `IActivityExecutionStateStore.ListByParentAsync`
    (InMemory + Groundwork + Coalescing + 2 doubles) backed by a new Groundwork keyword index
    `by-parent-activity-execution` over the ALREADY-persisted dot-path `state.parentActivityExecutionId` — **no
    document-shape change, no version bump** (Sipke-ratified §E6; Condition-7 backfill triggers on the index-set
    change). Groundwork queries the parent index then defensively post-filters by `workflowExecutionId`.
    `Parallel.CountBranchesAsync` reads only the composite's children instead of the whole workflow. **QA gate
    caught a regression:** the worker's initial `ElsaRuntimeStorageManifest.SchemaVersion` bump `1.0.0→1.1.0`
    broke `ElsaRuntimeDocumentVersions.Parse` for every kind (that constant is the frozen legacy stamp, not a
    migration knob) — surfaced only by the FULL Groundwork test project, reverted before merge.
  - **#413 item 6 Flowchart async Start (#531):** removed the last sync-over-async holdout — deleted the blocking
    `FlowchartStatePersister.SaveState` wrapper; engine `Start`→`StartAsync`; `Flowchart.Execute`→`ExecuteAsync`
    (picks the existing `ActivityBase` virtual, no base-contract change). The prescribed bite exposed that the
    legacy `Start_*` tests didn't isolate Start's initial persist (a later child-completion persist masked it);
    a new tight guard (`Start_FirstFlowchartStateCommitCarriesInitialScheduledState`) now bites, alongside the
    CT1–CT3 determinism suite. §E6-clean (byte-identical golden confirms).
  - **Ruled WON'T-FIX (Sipke, 2026-07-07):** #412 item 8 (`ActivityFactory` ctor `AddAll` — negligible-value
    per-scope churn, removing it is a public-ctor change breaking 2 characterization tests + it's a deliberate
    self-sufficiency safety net) and #415 item 5 (checkpoint-store `_writeGate` test seam has no production race;
    the 8 `Validate*`/`Apply*` per-lane pairs are documented intentional defense-in-depth — collapsing is an
    over-abstraction trap over subtly-different per-lane whitelists).
- **Peer-session Validations work landed through the control-room gate** (Sipke ruled 2026-07-06 that
  peer PRs route through the gate — see the handoff §3): #485 (draft-validation persistence, user-merged),
  #496 (FR-033 unknown-activity-version validator, self-merged before the policy; its post-merge review
  found a null-deref), then the QA'd stack — **#500** (Core-purity fix: `DraftValidationGate` depends on
  the `IEventPublishingStrategy` abstraction, dropping the illegal `Validations.Core → Events.Strategies`
  reference that had left the architecture guard red at 48/49), **#501** (maps regen #500 missed), **#498**
  (null `ActivityVersionId` guard in `CatalogVersionResolver`, red-proof verified), **#499** (nullable
  `IActivityDefinitionLookup.FindVersion` + superseded-category test sweep).
- **Wave C tail (hand to the incoming control room):** the **ratified event-delivery split**
  (`IInlineEventPublisher`/`IDeferredEventPublisher`, strategy param removed — closes the merged-#500
  footgun where a caller could pass `Background` and silently bypass draft validation; design in the
  handoff §2.1), then **W31 DRY batch** (remaining #412/#413/#414 items 3/4/6 — **incl. the cross-provider
  agent log-redaction helper deferred from W29** — #415 live slices, #416 slices 2–6 with slice 3's own
  EXTENSION_POINTS Priority-ordering gate, #417 remainder incl. the Activities/Design AddVersion sibling
  hardening, #422 items 1–2), then **W32 cleanup** (#423, #279, MD-10 gaps, plus the flowchart `Scopes`
  residual O(n) growth — wire-shape change, own §E6 gate).
- **Product track:** server-side execution output **#254 CLOSED 2026-07-05**
  ([#477](https://github.com/elsa-workflows/elsa-foundation/pull/477), Seam R1): workflow
  outputs readable on the instance-details API with policy-driven redaction (QA hardened the
  non-inline-payload marker path). R2 (synchronous execute-and-return) routed into the W16
  "HTTP synchronous response correlation" co-design; R3 filed as
  [elsa-foundation-studio#218](https://github.com/elsa-workflows/elsa-foundation-studio/issues/218).
- **New issue filed during the wave:** [#473](https://github.com/elsa-workflows/elsa-foundation/issues/473)
  (design-context `IsValidVariableName` filter rejects all dotted activity type keys — likely
  connected to the dead execution-time JS accessors gap).

### Follow-up findings recorded during Phase 0 execution

- **Ack-based dequeue for full window-C closure** (from W5): guaranteed item-level replay
  requires the durable scheduler work queue to hold a dequeued item until the consuming
  handler's checkpoint commits (release-on-ack), instead of load-then-delete. W5's lease
  primitive unblocks this; see the "still open" increment in
  [docs/runtime-durable-resumption.md](../runtime-durable-resumption.md). Candidate new unit.
- **Design endpoints bypass endpoint security** (from W4, pre-existing): 15 endpoints under
  `src/Elsa/Activities/Design/Api/` and `src/Elsa/Workflows/Design/Api/` call
  `AllowAnonymous()` explicitly and serve anonymously even on secured shells. Candidate new
  unit (or fold into W18 identity work). **CLOSED via W29 disposition 2026-07-05:** the removal
  itself had already landed pre-W29 — the W4 D5 sweep replaced every design-endpoint
  `AllowAnonymous()` with `ConfigurePermissions()`, and the config-gated anonymous opt-in exists
  as the per-shell `ApiSecurity` feature (`ApiSecurityOptions.AllowAnonymous`, default `false`,
  honored only in the Development environment by `ApiSecurityFastEndpointsConfigurator`; identity
  D1/D2/D4 kill-switch hardening, commit `8a0f4fb4`). W29 shipped the residual: the
  Activities-side endpoint-security regression suite (`ActivityDesignEndpointSecurityTests`,
  with an inventory guard), so all 18 design endpoint files are pinned permission-guarded.
  Granular `DesignPermissions` constants were explicitly ruled OUT (would change the permission
  contract for deployed principals and needs Studio coordination; its own gated unit if ever).
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

### Follow-up findings recorded during Phase 2 execution

- **#379 parent-completion fault propagation (hang risk)** — surfaced by the reconciliation
  pass: `WorkflowParentActivityCompletionSchedulerWorkHandler` commits an incident on a
  handler-thrown child fault but never invokes `ChildFaultParentEvaluation` (both sibling
  handlers do), so a 3+ level fork/join can hang permanently. **In flight as a standalone
  hotfix unit** (launched post-W14; see issue #379).
- **Pre-existing `Elsa.Secrets.Tests` failure** — `SecretAuditTests` (EncryptionKey
  configuration) fails on clean main, independently confirmed during W13 QA; predates the
  wave. Candidate quick fix; fold into W18 identity/secrets if not fixed sooner.
- **Map-manifest staleness signal** — `docs/maps/manifest.json` reported
  `relevant_inputs_dirty: true` on clean checkouts during W11 (pre-existing). The generator's
  dirtiness detection needs a sweep so freshness signals stay trustworthy.
- **`TaskExecutorSingleNodeTests` future home** (from W15): lives in the Runtime test project
  but exercises `Elsa.Tasks`; a dedicated `Elsa.Tasks.Tests` project is the natural home when
  the tasks domain grows its own suite.
- **W13 primitives left two one-liner fixes unwired** (tracked as issues): #381 (`Do`/`While`
  never consult the shared navigator's `IsBody`) and #399 (`RuntimeCheckpointCommit` misses
  `ValidateStateIdMatches` for `activityExecutions`). Both labeled `ready-for-agent`.

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

### Follow-up findings recorded during Phase 3 execution

- **`ISecretManager` store-vs-resolver split** (from W18): proposal-only in the
  [#461](https://github.com/elsa-workflows/elsa-foundation/pull/461) PR body (keep the facade,
  formalize the resolver boundary around `ISecretResolver`→`ISecretValueResolver`). Needs its own unit.
  **Done in W29** exactly per the proposal: `ISecretValueResolver`/`DefaultSecretValueResolver`
  own the whole resolution path (store registry injected, duplicate lifecycle-version evaluation
  removed), `ISecretManager.ResolvePayloadAsync` deleted so the manager is a pure lifecycle
  facade, per-provider `ISecretStore` untouched; swap-registration test proves resolution and
  lifecycle are separately overridable.
- **Secrets golden-fixture gate** (from W18): the Secrets Groundwork persistence has no fixture
  drift gate of its own (pre-existing gap flagged during MS-1; Identity got one, Secrets did not).
  **Done in W29:** Identity-style drift + legacy-load tests pin kind `secret`
  (manifest `elsa-secrets` @ 1.0.0) against `tests/Elsa/Secrets/Tests/Fixtures/v1/secret.json`,
  covering both payload wire variants (encrypted metadata-only with a literal fake protected
  value, and plain value).
- **MD-5 amendment + ADR 0033 ratification** (from W21): **both ratified 2026-07-04** at Phase 4
  kickoff — MD-5 applied as framework §2.16.1, ADR 0033 accepted with execution scheduled as W28.
  18 feature-registration gaps remain filed in the MD-10 report (Wave C / W32).
- **W16 activity-library follow-ups** (named in the
  [#465](https://github.com/elsa-workflows/elsa-foundation/pull/465) PR body): DS-8
  workflow-as-activity execution semantics; HTTP synchronous response correlation (async/202 shipped);
  full DS-9 hardening on non-activity evaluator paths; email/messaging provider modules; legacy
  `Runtime.JavaScript` stub + `JavaScriptActivities` feature retirement (wire-invisible, evidence in
  PR body); demo-shell composition of the new activity features.
- **Repo-wide `EntityNotFoundException` sweep** (from W19): the #393 fix converted the bounded
  Design/Activities-Design not-found sites; other domains still throw untyped exceptions for
  not-found and fall to 500 instead of 404. Convert opportunistically or as a small sweep unit.
- **Groundwork durable placement + transport stores** (from W20): mechanical drop-in against the
  frozen leaf contracts and the committed `executionCommandTransport` v1 fixture; required before the
  distributed provider is production-usable across processes.

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
