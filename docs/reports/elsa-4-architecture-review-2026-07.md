# Elsa 4 Architecture & Code-Quality Review — Consolidated Findings and Improvement Roadmap

**Date:** 2026-07-02
**Scope:** full `src/` + `tests/` of `elsa-foundation` (~84,300 LoC, 1,546 C# files, 109 projects, 1,809 public types), compared against Elsa 3 (`elsa-core`) as the production baseline.
**Method:** nine parallel deep-read reviews (runtime, Elsa 3 parity, infrastructure substrate, design/activities/expressions/HTTP, persistence, modularity/layering, naming system, test quality, remaining domains), followed by independent verification of all Critical/High claims against the code. Finding IDs (`RT-`, `E3-`, `IN-`, `DS-`, `PS-`, `MD-`, `NM-`, `TS-`, `MS-`) are stable references for hand-off to coding agents; each finding below carries enough evidence to act on without re-deriving the analysis.

---

## 1. Executive summary

Elsa 4 is a **disciplined, promising rewrite whose inner core is architecturally ahead of Elsa 3, but whose outer layers are incomplete and whose safety rails have specific, fixable holes.**

Direct answers to the review questions:

1. **Is it well-structured? DRY? Open/closed?** Structure and layering discipline are genuinely strong — the Design↔Runtime seam is enforced by tests, provider isolation is clean, and 45 per-domain `EXTENSION_POINTS.md` catalogs have zero index drift. DRY is uneven: the EF Core persistence base is a model of reuse, while the runtime has ~250-line near-duplicate scheduler handlers, eight copy-pasted "Navigator" classes, ten near-identical Groundwork store bridges, and three copy-pasted pipeline builders. Open/closed is honored in the extension-point system but violated by two ambient service locators and a static mutable security flag.
2. **Is the drainer on par with Elsa 3?** The drainer is **architecturally superior** on durability, crash-consistency, testability, idempotency, and determinism — it is the right core. It is **not yet at functional parity**: triggers, timers, global stimulus routing, and a distributed runtime do not exist, and per-activity overhead (~9 queue hops, 4–5 checkpoint commits per activity under the default immediate policy) is a real constant-factor regression vs. Elsa 3's single in-memory burst.
3. **Are the contributor interfaces over-complicated?** **No — the contributor pattern itself is sound and cheap for contributors** (1 file + 1 DI line to extend a flow; the ceremony is paid once by the domain owner). The complexity feeling is real but mislocated: it comes from (a) the mediator substrate *underneath* (two mechanically identical Command/Request stacks, ~30 types, with copy-pasted diverging pipeline builders and a marker-resolution anti-pattern the events side explicitly avoids) and (b) the runtime's dense private naming dialect. Fix those; keep the contributor pattern.
4. **Are type names self-descriptive?** The naming *system* is disciplined (real suffix grammar, near-zero `Helper`/`Util` pollution), but the concern is valid and localized: ~30–40 types in `Elsa.Workflows.Runtime.Core` stack 3–5 qualifiers deep (`Runtime`+`WorkflowExecution`+`Scheduler`+…), use near-synonym "runs work" suffixes (Drainer/Coordinator/Processor/Dispatcher) without a codified distinction, and overload the head-noun `State` for three different facets. A targeted rename pass (§6) fixes this now while blast radii are still 2–8 files per symbol.

**The five most important issues found (all verified in code):**

| # | Issue | Findings |
|---|---|---|
| 1 | **Fault semantics are incomplete end-to-end**: activity faults become incidents, but no code path ever writes `WorkflowExecutionStatus.Faulted`; drainer-level handler crashes become a string in a result object that the command processor discards; callers always see "Accepted" | RT-1, RT-12, RT-14 |
| 2 | **The durability chain has a hole**: even with durable Groundwork stores enabled, the scheduler work queue is in-memory-only and no background pump re-drives the outbox or recovery scanner after a crash — durable *storage* without durable *resumption* | PS-2, RT-3 |
| 3 | **No persisted-state schema versioning**: a constant `"1.0.0"` is written and never read; loading old state after a record change is undefined behavior; the runtime persistence bridge also bypasses the repo's own single-serializer policy | PS-1, PS-3 |
| 4 | **API security is globally disabled by a static flag** in the only runnable app, unconditionally, with no authn middleware and no Identity composition proven anywhere | MS-12, MS-13, MS-16 |
| 5 | **Single-writer correctness is assumed, not enforced**: the drainer's peek→pause→dequeue window and terminal-status caching are only safe under the in-process mailbox; nothing claims per-execution ownership for any future durable/multi-node provider | RT-2, E3-3 |

**What is genuinely good (protect these):** the Design→Publish→Runtime seam with content-addressed immutable `WorkflowExecutable` artifacts; atomic checkpoint-with-continuation commits via transactional outbox with idempotency markers; the decomposed persisted state model (vs. Elsa 3's god-object contexts); the extension-point catalog discipline; the test culture (zero mocking frameworks, real in-memory implementations, dual-provider sqlite/memory contract tests, 542/542 runtime tests in 258ms); the EF Core persistence feature-base; and the contributor-interface convention itself.

---

## 2. The engine: drainer vs. Elsa 3 burst (verdict + parity)

**How each works.** Elsa 3: `WorkflowRunner` runs an in-memory burst — a `while (scheduler.HasAny)` loop over a `Stack<ActivityWorkItem>`, one commit at the end (default), bookmarks/incidents on live 700–800-line context objects, cross-node safety via `IDistributedLock`. Elsa 4: commands become durable `RuntimeSchedulerWorkItem`s in a per-execution FIFO queue; `WorkflowSchedulerDrainer` peeks→pause-gates→dequeues→dispatches to `IWorkflowSchedulerWorkHandler`s; each handler commits a `RuntimeCheckpoint` atomically **with its continuation** via a post-commit outbox; `WorkflowExecutionDrainCoordinator` loops drain+outbox until quiesced; a per-execution `SemaphoreSlim` mailbox (`InProcessWorkflowExecutionAgentProvider`) serializes writers.

**Parity matrix (condensed; full evidence in E3 report):**

| Capability | Status in Elsa 4 |
|---|---|
| Activity scheduling/invocation, bookmarks (suspend/resume), fork/join, composite children, variables/scopes, runtime expressions, cancellation, execution inspection/journal | ✅ present (cancellation is *upgraded*: atomic checkpoint vs. token-driven) |
| Durability across crash, exactly-once continuation, idempotency | ✅ **stronger than Elsa 3** (atomic checkpoint + commit markers + outbox ack) |
| Commit strategy | 🔵 intentionally different: per-transition checkpoints vs. one commit per burst — finer-grained, higher cost (E3-6) |
| Running-instance migration | 🔵 intentionally removed: instances pin to immutable hashed `PinnedExecutable` artifacts (E3-7) — document as a design change |
| **Triggers / trigger indexing** | ❌ missing entirely — no way to start a workflow from an external stimulus (E3-1, Critical) |
| **Timers / scheduled resumption** | ❌ missing entirely (E3-2, Critical) |
| **Global stimulus routing** ("which instances anywhere wait for X?") | ❌ `BookmarkStimulusLookup` is intra-instance only (E3-5, High) |
| **Distributed/clustered runtime** | ❌ only `InProcess` agent provider; abstraction is ready, no provider exists (E3-3, Critical) |
| Standalone core execution | 🟡 `Runtime.Core` alone throws via `Missing*SchedulerWorkHandler` fallbacks; `Elsa.Activities` is required (E3-4) |
| Lifecycle event emit-point breadth | 🟡 far fewer than Elsa 3's notifications (E3-9) |

**Judgment:** keep the drainer. It fixes Elsa 3's real pain (lost bursts on crash, god-object contexts, coarse locks, ad-hoc commit semantics). The parity work is the "meet the outside world" layer, not the core.

---

## 3. Critical & High findings (verified, roadmap-ready)

### 3.1 Correctness & safety

- **RT-1 (Critical, recalibrated) — Fault semantics incomplete.** Verified: `ActivityFaultIncidentRecorder` *does* commit `IncidentState` + faulted `ActivityExecutionState` for all three activity fault arms (input materialization, construction, execution — `WorkflowInvokeActivitySchedulerWorkHandler.cs:178,221,362`), including child-fault parent evaluation for joins (#308). What's missing: (a) **no code path anywhere assigns `WorkflowExecutionStatus.Faulted`** — a workflow with a blocking incident stays `Running` forever and `IsTerminal()` never fires for faults; (b) a *handler-level* crash in the drainer is captured as `exception.ToString()` in a `RuntimeSchedulerWorkItemResult` (`WorkflowSchedulerDrainer.cs:201-212`), the item is already dequeued (dropped, no retry/poison), and (c) `WorkflowSchedulerCommandProcessor.ProcessAsync` ignores the `DrainAsync` return entirely — enqueue callers always observe success (RT-14). **Action:** define the workflow-level fault policy (blocking-incident → `Faulted` transition or documented "incident-paused" status), propagate drain outcomes to dispatch callers, add a poison/retry policy for handler crashes, and unify fault capture (structured incident, not `ToString()` — RT-12).
- **RT-2 (Critical) — Single-writer assumed, never enforced.** The peek→pause-gate→dequeue TOCTOU window and the "read terminal status once per drain" optimization (`WorkflowSchedulerDrainer.cs:105-141`) are only correct because `InProcessWorkflowExecutionAgentProvider`'s per-execution `SemaphoreSlim` mailbox serializes all entry. No store, queue, or drainer claims per-execution ownership (no lease/fencing check on dispatch). Any second dispatch path, durable queue, or multi-node provider silently breaks it. `RuntimeExecutionLease.fencingToken` exists but nothing rejects stale-fenced commits (also untested — TS gap 2). **Action:** make the ownership contract explicit — either document + guard-test "all dispatch MUST route through the agent mailbox," or enforce lease/fencing checks at checkpoint-commit time.
- **PS-2 (Critical) — Durable checkpoint, non-durable resumption.** `AddGroundworkRuntimeStores()` swaps 10 contracts but **not** `IWorkflowSchedulerWorkQueue` (`WorkflowsRuntimeApiFeature.cs:64` registers `InMemoryWorkflowSchedulerWorkQueue` unconditionally; no Groundwork implementation exists). Post-commit outbox delivery is only driven inline per-execution; `IRuntimeRecoveryScanner` and the system-wide outbox sweep are registered but **never invoked** — zero `IHostedService`/`BackgroundService` in the runtime. Crash after commit, before dispatch → continuation durably recorded, never acted upon. Related: **RT-3** — `FailedRetryable` outbox items with future `AvailableAt` only retry when a new command happens to arrive. **Action:** ship a durable work queue implementation + a hosted background pump for (a) system-wide outbox sweep and (b) recovery scanning; until then, document that Groundwork = durable storage, not durable resumption.
- **PS-1 (Critical) — No persisted-state schema versioning.** `ElsaRuntimeStorageManifest.SchemaVersion = "1.0.0"` is written on every document and read by nothing; no state record carries a version field; no upcaster mechanism exists; `docs/serialization.md` never mentions evolution. Any field change on `WorkflowExecutionState`/`ActivityExecutionState`/`BookmarkState`/`SchedulerState` is a silent breaking change for every suspended workflow. **Action:** per-document-kind version stamp read on deserialize; an upcaster registry; CI fixture suite that round-trips historical documents; make bumping the version a test-enforced step of any state-record change.
- **PS-3 (High) — Runtime state bypasses the single-serializer policy.** 13 files under `src/Elsa/Persistence/Groundwork/Stores/` hand-roll `System.Text.Json` with independent options (`GroundworkRuntimeJson.cs`) instead of `IPayloadSerializer`, and are not on `docs/serialization.md`'s sanctioned-exception list. This is exactly the layer that must carry the PS-1 version envelope. **Action:** route through `IPayloadSerializer` (or formally sanction + document the exception) and host the version envelope there.
- **MS-12 + MS-13 + MS-16 (Critical, security) — Security disabled globally in the reference app.** `EndpointSecurityOptions.SecurityIsEnabled` is a static, process-wide mutable flag consulted by every `ElsaEndpoint*` base (`src/Elsa/Api/FastEndpoints/Constants/EndpointSecurityOptions.cs:9-11`); `src/Apps/Elsa.Server/Program.cs:73` calls `DisableSecurity()` **unconditionally** (no environment guard); the app wires no `UseAuthentication`/`UseAuthorization` and references no Identity feature assembly — the authn/authz composition story is unproven anywhere. **Action:** replace the static with per-shell `IOptions`-bound configuration, gate insecure mode behind explicit config + startup warning, and make the reference app demonstrate a secured default.
- **PS-5 (High) — Undocumented global write locks.** Two process-wide semaphores serialize all writes of a kind (Groundwork checkpoint writer; generic EF Core save command) — a correctness crutch that caps throughput and gives no multi-instance protection. **Action:** replace with per-execution/per-entity concurrency (optimistic tokens — also missing on design-time EF entities, PS-6).
- **IN-4 (High) — `[SingleNodeTask]` can crash secondary nodes.** Lock acquisition throws `TimeoutException` after the default 10-minute wait instead of skipping; nothing catches it in startup task execution → secondary node shell init blocks 10 min then aborts. **Action:** try-acquire + skip semantics for single-node startup tasks.
- **IN-2 (High) — `BackgroundEventPublisher` links the caller's dead token** into fire-and-forget dispatch, misreporting expected cancellations as dispatch failures (`Events/Channels/BackgroundEventPublisher.cs:45-74`). **Action:** don't link the enqueue-time caller token; use host lifetime only.

### 3.2 Performance (hot paths)

- **E3-6 / RT-10 (High-impact) — Per-activity overhead.** ≥4 queue hops and 4–5 mandatory checkpoint commits per single activity under `ImmediateRuntimeCheckpointPersistencePolicy`; mandatory checkpoints cannot be skipped. **Action:** a burst-coalescing persistence policy that folds intra-drain checkpoints into one commit at quiescence for non-suspending segments — this is the single biggest throughput lever and makes the durability/performance trade a *policy*, as Elsa 3's commit strategies were.
- **IN-3 (High) — `JsonPayloadSerializer.GetOptions()` builds new `JsonSerializerOptions` per call** (`Serialization/SystemText/Services/JsonPayloadSerializer.cs:68-84` — verified), defeating STJ's `JsonTypeInfo` cache on every serialize/deserialize. **Action:** cache the options instance (rebuild on registry change).
- **IN-1 (High) — Mediator marker-resolution anti-pattern.** `CommandHandlerInvokerMiddleware`/`RequestHandlerInvokerMiddleware` resolve non-generic `ICommandHandler` markers and filter client-side — instantiating **every** handler in the container per dispatch, plus uncached reflection invoke (verified `Mediator/Commands/CommandHandlerInvokerMiddleware.cs:25-46`; the Events side resolves closed generics correctly). **Action:** resolve the closed generic; cache compiled invokers. Same for **IN-10** (events strategy reflection per publish).
- **DS-10 (High) — No JS engine/pipeline reuse:** a fresh Jint `Engine` + the full pre-processor pipeline runs per expression evaluation; **DS-9 (High):** no sandboxing (no timeout/recursion/statement limits; `CancellationToken` discarded). **Action:** engine pooling or cached setup delegate + Jint constraints wiring.
- **RT-11 (Medium):** `CompleteActivity` payload deserialized up to 4× per dispatch; **RT-16/RT-17 (Low):** LINQ/allocation churn in `WorkflowExecutionContext.GetOutput` and per-iteration `ListAsync(limit:1)` peek.

### 3.3 Architecture & DRY

- **RT-4 (High):** the entire runtime composition root lives in the *API* feature (`WorkflowsRuntimeApiFeature`), everything singleton. Split hosting-agnostic runtime registration from the HTTP surface.
- **RT-6 (High):** ADR 0029 Move 2 (specs/083) is half-applied — only the Cancel handler stages to the checkpoint workspace; the activity checkpoint slot is a placeholder while handlers commit inline. Finish the decomposition before more handlers accrete the old pattern.
- **RT-7 (High):** two ambient service locators (`IWorkflowExecutionAmbientServicesAccessor` state-store lookup inside the drainer; AsyncLocal `IRuntimePipelineContextAccessor` smuggling the mutable workspace to handlers). Replace with explicit parameters/context members.
- **RT-5 (High):** incident *querying/consumption* surface unfinished — incidents are recorded (see RT-1) but nothing reads them for workflow-status transitions or operator surfaces; the affordance dead-ends.
- **IN-15 + IN-6/IN-7 (Medium, high leverage):** Command vs. Request are mechanically identical parallel stacks (~30 types); the three pipeline builders are copy-pasted and have **diverged** (accumulate-vs-replace `Setup()` semantics; capability drift). **Action:** one generic `PipelineBuilder<TContext,TDelegate>` in `Pipelines.Core`; collapse Command/Request into one request/response hierarchy (keep `ICommand` as an alias if the intent-signal is valued).
- **DRY duplication inventory:** eight near-identical control-flow "Navigator" classes ~900 LoC (DS-7); Schedule/Start/Complete scheduler handlers ~80% duplicated ~250 LoC each (RT-9); ten Groundwork store bridges ~850 LoC no shared base (PS-7); per-provider Sqlite feature boilerplate (PS-8); duplicated `AuthorizeProposalAsync` in three Agent endpoints (MS-7); two copy-pasted `InMemoryDocumentStore` test fakes (TS-3); telescoping constructors — 7 on `WorkflowSchedulerDrainer`, 7 on the commit store, test-only (RT-8).
- **DS-1/DS-2/DS-6 (High):** Publishing has no `.Core` (contracts live in the endpoint `.Api` project); the production publish path persists into an **in-memory** artifact store; the documented 18-event Draft mutation lifecycle has no HTTP surface (empty stub endpoint files checked in, DS-5).
- **MD-1 (High, failing now):** 2 of 35 architecture-guard tests fail on the checked-out tree (`src/elsa3` lowercase vs. expected `src/Elsa3`) — invisible on case-insensitive filesystems, will fail case-sensitive CI.
- **MD-2/MD-3/MD-4 (Medium):** constitution drift — the Runtime→Design "hard rule" has one tracked exception the text doesn't acknowledge; `InternalsVisibleTo` used in 5 projects despite an explicit ban (and the guard suite doesn't check for it — a blind spot); the constitution's pinned 13-domain tree diverges from the actual 22 domains (3 listed domains don't exist; the largest real domain, `Elsa.Activities`, is absent).
- **MD-5/MD-6 (Medium, DX):** intentional micro-fragmentation (11 projects <100 LoC; smallest 32 LoC) per the constitution's "prefer finer split" gate — worth a threshold amendment; inversely `Elsa.Workflows.Runtime.Core` is a 15,042-LoC outlier that behaves like an engine, not a contracts `.Core`.

### 3.4 Missing breadth (maturity, not defects)

- **DS-16 (High):** no HTTP activities at all (no `HttpEndpoint`, `SendHttpRequest`, webhooks), no timer/email/scripting/messaging activities; the built-in library is 7 control-flow + 2 structural + 12 primitives vs. Elsa 3's dozens. Workflow-as-activity is construct-only (DS-8).
- **MS-1 (High):** the only identity store is in-memory — no durable implementation exists. **MS-4/MS-5 (High):** secrets master key is non-rotatable (key change = permanent data loss) and the default audit sink is a silent no-op.
- **MS-9 (Medium/High):** zero `ActivitySource` in the repo — Elsa's own engine emits no distributed traces; the "OpenTelemetry" domain is an ingestion backend for *external* telemetry (easily misread). **MS-14 (Medium):** no global ProblemDetails/error contract.
- **TS gaps (Medium):** no crash-injection test between commit and outbox delivery; no stale-fencing-rejection test; no first-class cancellation contract suite; registration tests over-pin `ImplementationType`/lifetime beyond what the constitution mandates (TS-1 — refactor tax).

---

## 4. Contributor pattern verdict (user question 3)

Keep it. Evidence: the cheapest instance (`IJsonConverterSource` → aggregating handler → registry → startup task) costs a contributor exactly one interface implementation and one DI registration; the vocabulary (Source/Contributor/PreProcessor/PostProcessor/Validator/Handler) plus the "exactly one aggregating handler per contribution flow" rule solves Elsa 3's genuine problem of N invisible `INotificationHandler`s competing over one conceptual contribution with ad-hoc ordering. The Events substrate is ~26 types but comparable to Elsa 3's notification stack, just better documented.

The ceremony that does *not* earn its keep is underneath: the duplicated Command/Request mediator stacks (IN-15), the three divergent hand-copied pipeline builders (IN-6/7), and the mediator's marker-resolution dispatch (IN-1) — the redesign's own stated principle, applied to only half the infrastructure. Simplify there (see roadmap W10) and the "over-complicated" feeling should largely dissolve. One doc bug: `Events/EXTENSION_POINTS.md` omits the shipped Parallel strategy (IN-11).

---

## 5. Elsa 3 → Elsa 4 concept map (migration crib)

| Elsa 3 | Elsa 4 | Note |
|---|---|---|
| burst of execution | drain cycle(s) until `Quiesced` | |
| `WorkflowRunner` + pipeline | `WorkflowSchedulerDrainer` + `WorkflowExecutionDrainCoordinator` | |
| `IActivityScheduler` (in-memory stack) | `IWorkflowSchedulerWorkQueue` (per-execution, durable-intent) | |
| `ActivityWorkItem` | `RuntimeSchedulerWorkItem` (+ `WorkflowExecutionCommandEnvelope`) | now idempotent commands |
| `ActivityExecutionContext` / `WorkflowExecutionContext` (god objects) | `ActivityExecutionState` / `WorkflowExecutionState` + `DurableValueState` + `SchedulerState` + `OperationalState` | decomposed persisted state |
| `Bookmark` + `AutoBurn` | `BookmarkState` + Create/Resume commands | |
| commit strategies / `ICommitStateHandler` | `RuntimeCheckpoint` + `RuntimeCheckpointCommitter` + persistence policy | commit → **checkpoint** |
| `INotificationHandler` broadcast | `IEventHandler<T>` (independent subscription) **or** typed contributor interface (fan-in) | the split is the point |
| `IDistributedLock` per instance | agent mailbox single-writer + idempotency keys | in-process only today |
| `ActivityIncident` | `IncidentState` + `ActivityFaultIncidentRecorder` | recorded; workflow-status wiring missing (RT-1) |
| `ExecutionLog`/Journal | `ActivityExecutionInspectionProjection` | |
| running-instance version migration | pinned immutable `PinnedExecutable` artifact | intentional removal (E3-7) |
| `TriggerIndexer` / `IBookmarkQueue` | **no equivalent yet** | E3-1/E3-5 |

---

## 6. Naming system (user question 4)

The audit inventoried all 1,809 public types. Statistics: 202 names >35 chars (worst 52); noise prefixes `Workflow` (×181), `Runtime` (×163), `Activity` (×112); "Agent" is a cross-domain homonym (AI agents ×110 vs. `IWorkflowExecutionAgent`). Protected good names: Bookmark, Trigger, Incident, Outbox, Checkpoint, WorkItem, Hold, PauseGate, Envelope, Slot, the `.Core` grammar, and the extension-kind suffixes.

**Priority rename families** (full table with blast radii in the naming report; most symbols touch 2–8 files — do this *now*, before adoption widens):

- **A — "who runs work" verbs:** codify Drainer (per-execution loop worker) vs. Orchestrator (cycle loop: rename `WorkflowExecutionDrainCoordinator`) vs. Router (rename `WorkflowSchedulerCommandProcessor`) vs. Executor (rename `IWorkflowExecutionCommandProcessor`).
- **B — the three `…State` facets:** `OperationalState` → `ExecutionLivenessState` (it holds lease/heartbeat/interruption); `ControlPlaneState` → `WorkflowHoldState`; keep `SchedulerState`.
- **C — de-stack qualifiers the namespace already supplies:** e.g. `AsyncLocalWorkflowExecutionAmbientServicesAccessor` → `AsyncLocalWorkflowExecutionScope`; `WorkflowParentActivityCompletionSchedulerWorkHandler` → `ParentCompletionWorkHandler`.
- **D — vague-word cleanup:** Elsa-owned `Manager`/`Info` types get role suffixes (Registry/Resolver/Summary); external-framework mirrors (OpenIddict/Liquid managers) stay.
- **E — homonym hygiene:** consider `IWorkflowExecutionAgent` → `…Actor`/`…Worker` to free "Agent" for the AI domain; document `Groundwork`/`Nuplane`/`CShells` codenames in the glossary rather than renaming.

**Proposed mechanical rules** for the constitution/style guide: ≤4 name components once the namespace is counted; one codified verb-suffix table (Handler = reacts, Processor = batch, Dispatcher = routes elsewhere, Coordinator/Orchestrator = multi-step policy, Runner/Executor = does the work); ban `Manager`/`Helper`/`Info` for Elsa-owned types; head-noun must name the *content*, not the metaphor.

---

## 7. Improvement roadmap (hand-off work units)

Ordered by risk-adjusted value. Each unit is scoped for a dedicated coding agent; IDs reference §3 findings for full context. **Per-unit implementation briefs (goal, verified current state, scope checklist, acceptance criteria, dependencies) live in [elsa-4-architecture-review-2026-07/roadmap.md](elsa-4-architecture-review-2026-07/roadmap.md) — hand that document to implementing agents.**

**Phase 0 — safety & correctness (do first, independent of each other):**
- **W1. Fault semantics end-to-end** (RT-1, RT-12, RT-14, RT-5): workflow `Faulted`/incident-paused status policy, drain-result propagation to dispatch callers, poison/retry policy, structured fault capture. *Blocks honest production claims.*
- **W2. Durable resumption chain** (PS-2, RT-3): durable `IWorkflowSchedulerWorkQueue`, hosted outbox sweep + recovery-scanner pump, crash-injection integration test (TS gap 1).
- **W3. State versioning contract** (PS-1, PS-3): version envelope read on load, upcaster registry, historical-fixture CI suite, serializer-policy reconciliation.
- **W4. Endpoint security model** (MS-12/13/16): kill the static flag, per-shell options, secured-by-default reference app, Identity composition proven in `Elsa.Server`.
- **W5. Ownership enforcement** (RT-2 + TS gap 2): explicit single-writer contract, fencing-token rejection at commit, tests.
- **W6. Repo hygiene quick wins:** fix the 2 failing architecture-guard tests (MD-1), `InternalsVisibleTo` guard + allow-list (MD-3), constitution domain-tree refresh (MD-2/MD-4), duplicate ProjectReference (MD-7), `PropertyAccessorExtentions` typo (IN-12).

**Phase 1 — engine parity (sequential-ish):**
- **W7. Triggers + global stimulus routing** (E3-1, E3-5): trigger index over published artifacts, stimulus→instance routing feeding the dispatchers. *Largest single parity gap.*
- **W8. Durable timers** (E3-2): timer store enqueueing `ResumeBookmark` at due time.
- **W9. Checkpoint-coalescing persistence policy** (E3-6, RT-10): burst-equivalent commits at quiescence; benchmark harness to quantify (also satisfies the Groundwork governance gate flagged in PS-4).

**Phase 2 — substrate & code quality (parallelizable):**
- **W10. Mediator consolidation** (IN-1, IN-15, IN-6/7, IN-13): closed-generic dispatch, unify Command/Request, one shared generic pipeline builder.
- **W11. Hot-path fixes** (IN-3, IN-2, IN-10, RT-11, DS-9, DS-10): serializer-options caching, background-publisher token fix, cached invokers, single payload deserialization, Jint sandboxing + reuse.
- **W12. Runtime structure** (RT-4, RT-6, RT-7, RT-8): finish ADR 0029 Move 2 slots, split composition root from API feature, remove ambient locators, collapse telescoping ctors (primary ctor + options).
- **W13. DRY sweep** (RT-9, DS-7, PS-7/8/9, MS-7, TS-3, DS-3/DS-4): dedupe handlers/navigators/store bridges/fakes; remove dead `ExpressionDescriptor`; resolve `ArgumentValue`/`ArgumentState` collision.
- **W14. Naming pass** (families A–E + rules into the constitution; NM findings): mechanical, do after W12 lands to avoid churn.
- **W15. Test hardening** (TS-1, TS-9, IN-4, IN-5): registration tests → resolvability, cancellation contract suite, single-node-task skip semantics + test, remove dead `StopAsync` or wire it.

**Phase 3 — breadth (product-driven ordering):**
- **W16. Activity library** (DS-16, DS-8): HTTP endpoint/request activities first (unlocks real use), then timers-as-activities, scripting, workflow-as-activity execution.
- **W17. Publishing completion** (DS-1/2/5/6): Publishing `.Core`, durable artifact store, Draft mutation HTTP surface.
- **W18. Identity/Secrets hardening** (MS-1, MS-4, MS-5): durable identity store, key-ring rotation for the secret protector, real default audit sink.
- **W19. Self-observability** (MS-9, MS-14): `ActivitySource` spans on drain/dispatch/activity execution; global ProblemDetails.
- **W20. Distributed agent provider** (E3-3): lease/partition ownership + durable transport — after W2/W5 land, since it depends on their contracts.
- **W21. Modularity ergonomics** (MD-5/MD-6): micro-project threshold amendment; audit `Runtime.Core`'s 15K LoC against the `.Core` charter.

---

## 8. Source reports

This document consolidates nine detailed sub-reports (runtime; Elsa 3 comparison; infrastructure; design/activities/expressions/HTTP; persistence; modularity; naming; tests; remaining domains) produced during the 2026-07-02 review session. The sub-reports — together with the per-work-unit implementation briefs for W1–W21 — are committed in [docs/reports/elsa-4-architecture-review-2026-07/](elsa-4-architecture-review-2026-07/README.md). They contain per-finding file:line evidence, full parity/inventory/naming tables, and open questions. All Critical/High findings in this document were independently re-verified against the working tree at commit `ffafa32f`; where verification changed a sub-report's conclusion (RT-1, RT-5), the sub-report carries an inline correction note and this document is authoritative.
