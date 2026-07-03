# Elsa 4 — Naming-System Audit

**Scope:** every public type in `src/**/*.cs` (excluding `bin`/`obj`/`elsa3`).
**Corpus:** 5,681 `.cs` files → **1,809 unique public type names**.
**Method:** grep-aggregated the public-type vocabulary, computed prefix/suffix/length distributions, then read the bodies of every type whose name this report challenges (the `Workflows/Runtime/Core` cluster in particular) so the proposals reflect actual behavior, not surface pattern-matching. Checked consistency against `EXTENSION_POINTS.md` (the sanctioned Source/Contributor/PreProcessor/PostProcessor/Validator/Handler vocabulary) and `docs/glossary/`.

---

## 1. Executive summary

The naming **system** is, on the whole, disciplined: there is a real, mostly-enforced suffix grammar (Store, Handler, Contributor, Source, Provider, Feature, Command/Request/Result, Options, Context…), the extension-point suffixes are honored, and the glossary is unusually rich. The problem the owner senses is real but **narrow and concentrated**: it is not that names are *wrong*, it's that one subsystem — `Elsa.Workflows.Runtime.Core` — has grown a **dense private jargon** (Drain / DrainCoordinator / Drainer, CommandProcessor / CommandDispatcher / StartDispatcher / PipelineDispatcher, ControlPlaneState / OperationalState / SchedulerState, AmbientServices, PauseGate, Hold, Passivation, Outbox, WorkItem) whose members are **individually defensible but collectively hard to hold in your head**, because near-synonyms are used for genuinely different concepts and the *distinguishing* word is buried in the middle of a 40–52-character name.

Three systemic issues drive most of the "technically correct but not self-descriptive" feeling:

1. **Length inflation from left-heavy qualifier stacks.** 202 public types (11%) exceed 35 characters; 74 exceed 40. The reader must parse `Runtime` + `Workflow` + `Execution` + `Scheduler`/`Checkpoint`/`Outbox` + a role word before reaching the *distinguishing* token. The prefixes `Workflow` (181), `Runtime` (163), and `Activity` (112) are so common they carry almost no information at point of use.
2. **Near-synonym role suffixes with no codified difference.** Processor vs Dispatcher vs Coordinator vs Drainer vs Invoker vs Handler are all "runs work," and the codebase uses several of them within one call-chain for adjacent responsibilities. Nothing tells a newcomer which is which.
3. **Overloaded head-nouns for distinct concepts.** `…State` names three unrelated runtime facets (holds, leases/heartbeat/drain, scheduler continuation). `Drain` is simultaneously a lifecycle mode (`RuntimeDrainMode`), a stored fact (`RuntimeDrainState`), and two different *actors* (`…Drainer`, `…DrainCoordinator`).

**What is genuinely good and must be protected:** the Elsa-3-inherited domain nouns (`Bookmark`, `Trigger`, `Incident`, `Activity`, `Workflow`, `Variable`, `Checkpoint`, `Outbox`), the `.Core`/Feature/ShellFeature layering, the Source/Contributor/PreProcessor/PostProcessor extension grammar, and record-heavy value objects. Do **not** touch these.

**Bottom line:** this is a *localized* language-tidying job on ~30–40 runtime types plus a mechanical style guide, not a repo-wide rename. Blast radius for the highest-value fixes is small (most offenders are referenced by 2–8 files).

---

## 2. Vocabulary statistics

### 2.1 Size & shape
- **1,809** unique public type names across **5,681** files.
- Interfaces follow the `I`-prefix convention consistently (spot checks found no violations).

### 2.2 Name-length distribution (leading `I` stripped)

| Bucket (chars) | Count | % |
|---|---:|---:|
| ≤ 15 | 249 | 14% |
| 16–20 | 318 | 18% |
| 21–25 | 404 | 22% |
| 26–30 | 399 | 22% |
| 31–35 | 237 | 13% |
| 36–40 | 128 | 7% |
| > 40 | 74 | 4% |

**Median name length ≈ 25 chars.** The long tail (>35 chars, 202 types) is the readability problem. Worst offenders:

| Len | Name |
|---:|---|
| 52 | `WorkflowParentActivityCompletionSchedulerWorkHandler` |
| 52 | `WorkflowGraphOperationBatchRiskClassificationRequest` |
| 52 | `GroundworkDeleteWorkflowDefinitionPermanentlyCommand` |
| 51 | `GroundworkActivityExecutionInspectionStoreException` |
| 50 | `AsyncLocalWorkflowExecutionAmbientServicesAccessor` |
| 49 | `WorkflowExecutionDrainCycleLimitExceededException` |
| 48 | `RuntimeCheckpointInconsistentDurabilityException` |

### 2.3 Most common prefixes (first CamelCase word)

| Prefix | Count | Note |
|---|---:|---|
| `Workflow*` | 181 | so common it is near-noise at point of use |
| `Runtime*` | 163 | often redundant inside `…Runtime.Core` namespaces |
| `Activity*` | 112 | fine — genuine domain noun |
| `Agent*` | 110 | two *different* "agents" collide (AI agent vs workflow-execution agent) |
| `Groundwork*` | 49 | codename for the bespoke persistence stack — jargon |
| `Secret*` | 46 | fine |
| `Default*` / `Noop*` / `InMemory*` | 32 / 8 / (many) | sanctioned default-impl prefixes — good |
| `Open*` (OpenIddict/OpenTelemetry) | 36 | fine |
| `Structured*`, `Persisted*`, `Elsa3*` | 20 / 14 / 30 | fine |

### 2.4 Most common suffixes (role grammar)

| Suffix | Count | | Suffix | Count |
|---|---:|---|---|---:|
| Store | 84 | | Provider | 41 |
| Request | 71 | | Factory | 38 |
| Handler | 69 | | Command | 37 |
| Feature | 63 | | Result | 35 |
| Context | 49 | | Policy | 31 |
| Exception | 46 | | Status | 28 |
| Options | 42 | | View | 26 |

Long tail of role suffixes actually in use: Response, Service, Registry, Middleware, **Manager (21)**, Descriptor, Processor (17), Resolver, Evaluator, Source (14), Filter, Validator (10), Dispatcher (9), Contributor (9), Accessor (8), Sink (6), Reader/Writer, Reconciler, Publisher, Invoker (4), Guard, Coordinator (3), Drainer, Gate, Envelope. This is a **large role vocabulary** — ~60 distinct suffixes — which is the second-order cause of the "too many words to learn" feeling.

### 2.5 Near-collision clusters (names differing by one word, meaning confusingly-similar things)

| Cluster | Members | Problem |
|---|---|---|
| **Drain actors** | `IWorkflowSchedulerDrainer`, `IWorkflowExecutionDrainCoordinator`, `WorkflowExecutionDrainCoordinator` | Two "who drains" abstractions differing only by Drainer/DrainCoordinator; the difference (per-execution worker vs command-triggered orchestrator) is not in the names. |
| **Command runners** | `IWorkflowExecutionCommandProcessor`, `WorkflowSchedulerCommandProcessor`, `IWorkflowExecutionStartDispatcher`, `IRuntimeExecutionPipelineDispatcher`, `IRuntimePostCommitIntentDispatcher` | Processor vs Dispatcher used for adjacent steps of the same pipeline with no codified distinction. |
| **`…State` facets** | `ControlPlaneState`, `OperationalState`, `SchedulerState` (+ their 3 Store/IStore/InMemory/Groundwork variants = 12 types) | One suffix, three unrelated concepts. A newcomer cannot guess which holds leases vs holds vs pending work. |
| **Drain as noun** | `RuntimeDrainMode`, `RuntimeDrainState`, `RuntimeDrainStatus` | Mode/State/Status trio on one concept — Status is `enum`, State is the stored fact, Mode is intent; borderline but tolerable. |
| **Invoker vs Dispatcher vs Handler middleware** | `CommandHandlerInvokerMiddleware`, `EventHandlerInvokerMiddleware`, `RequestHandlerInvokerMiddleware` vs the Dispatchers above | "Invoker" here means "the middleware that finally calls the handler" — fine internally, but adds a 4th "runs it" word. |

---

## 3. Suffix semantics table (derived from code)

Meaning column = what the suffix *actually* denotes in this repo (read from bodies), not the textbook meaning.

| Suffix | Actual meaning here | Consistent? | Notes / violations |
|---|---|---|---|
| **Source** | Pull contributor: `GetX()`/`Read()` returns values. | ✅ | Matches `EXTENSION_POINTS.md`. Clean. |
| **Contributor** | Push contributor: `Contribute(ctx)` mutates a context. | ✅ | Matches sanctioned kind. Clean. |
| **PreProcessor / PostProcessor** | Phase-scoped contributor (expressions, functions). | ✅ | e.g. `ConfigurationAccessFunctionPreProcessor`. Clean. |
| **Validator** | Action-named contributor returning findings. | ✅ | Sanctioned. |
| **Handler** | **Overloaded.** (a) mediator `IRequestHandler`/`ICommandHandler`; (b) persistence-lifecycle "entity Handler" (sanctioned); (c) **scheduler `…WorkHandler`** = the terminal delegate that executes one work-item kind. | ⚠️ | Meaning (c) is a *third* sense not covered by EXTENSION_POINTS.md; `…SchedulerWorkHandler` (16 types) is really a *strategy/executor*, not a lifecycle handler. This is the single most overloaded suffix. |
| **Store** | Persistence repository over one aggregate (`I…Store` + `Groundwork…`/`EFCore…`/`InMemory…` impls). | ✅ | 84 uses, remarkably consistent. Model suffix of the codebase. |
| **Processor** | "Runs a unit and returns nothing/void-ish" — `ProcessAsync`. | ⚠️ | Overlaps Dispatcher/Coordinator; see §5. |
| **Dispatcher** | "Routes a thing to the right runner then invokes it." | ⚠️ | `StartDispatcher` actually *dispatches to an agent*; `PipelineDispatcher` *selects+wraps a pipeline*; `IntentDispatcher` *fans out post-commit intents*. Three different verbs under one suffix. |
| **Coordinator** | Orchestrates a multi-step operation across services. | ⚠️ | Only used for `DrainCoordinator`; overlaps Drainer. |
| **Drainer** | Executes the drain loop for one execution. | ⚠️ | Actor vs Coordinator distinction invisible. |
| **Invoker** | Middleware that calls the resolved handler (terminal mediator step). | ✅ | Internally consistent (3 mediator uses). |
| **Accessor** | Reads an ambient/async-local value. | ✅ | `AsyncLocal…Accessor` clean, but see AmbientServices in §4. |
| **Provider** | Factory/lookup that yields impls or descriptors. | ✅ | 41 uses, consistent. |
| **Factory** | Constructs objects. | ✅ | Consistent. |
| **Manager** | **Banned-word smell.** 21 uses. Some are legitimate external contracts (`AspNetCoreIdentityUserManager`, `IApplicationManager` from OpenIddict). Others are vague Elsa-owned (`DefaultAuthenticationProviderManager`, `DefaultSecretManager`, `ITaskStateManager`, `ICacheManager`). | ❌ | Elsa-owned `Manager`s should say what they do (Registry/Store/Resolver). |
| **Service** | 22 uses; catch-all. Mostly Agent-domain (`IAgentSessionService`, `IAgentProposalService`). | ⚠️ | Tolerable but vague; `…Service` is the "I couldn't pick a suffix" default. |
| **Info** | 6 uses. Vague. | ❌ | `ActivityDefinitionVersionInfo`, `AgentStepInfo`, `LogExceptionInfo` — should be `…Descriptor`/`…Summary`/`…Details`. |
| **Context** | Ambient state bag threaded through a pipeline. | ✅ | 49 uses, consistent. |
| **Options** | DI-bound settings record. | ✅ | Consistent. |
| **Command / Request / Result / Response** | Mediator message shapes. | ✅ | Consistent and predictable. |
| **Feature / ShellFeature** | Modularity registration unit. | ✅ | 63 uses, consistent. Good. |
| **Envelope** | Wraps a command with delivery metadata (id, idempotency key, sequence). | ✅ | Single use (`WorkflowExecutionCommandEnvelope`) — precise and good. |
| **Gate** | Returns an allow/deny decision at a boundary. | ✅ | `PauseGate` reads well (see §4). |
| **WorkItem** | A queued unit of scheduler work. | ✅ | Good, evocative. |
| **Hold** | An administrative pause record. | ✅ | Good, evocative (see §4). |

**Helper/Util check:** only **one** offender (`EventHandlerHelper`). Excellent discipline — the classic dumping-ground suffixes are essentially absent.

---

## 4. Domain-term assessment (the language a newcomer must learn)

Judged as vocabulary, protecting good names and flagging jargon. Comparison baseline: Elsa 3's approachable nouns — *bookmark, burst, trigger, incident* (present at `/Users/sipke/Projects/Elsa/elsa-core`).

| Term | Verdict | Reasoning |
|---|---|---|
| **Bookmark, Trigger, Incident, Activity, Workflow, Variable, Outbox, Checkpoint** | ✅ **Protect** | Concrete, evocative, industry- or Elsa-3-familiar. Outbox is a recognized pattern; Checkpoint is precise. Do not rename. |
| **WorkItem** | ✅ **Protect** | Universally understood "a queued unit of work." |
| **Hold** (`ControlPlaneHold`) | ✅ **Good noun** | "Put a hold on it" is natural English for an admin pause. Keep `Hold`; reconsider only the `ControlPlane` qualifier (see below). |
| **PauseGate** | ✅ **Good** | "Gate" = boundary that grants/denies passage; "pause gate" is self-descriptive. Keep. |
| **Drain / Draining** | ✅ **Concept good, actors muddled** | Draining a queue is standard terminology. The *noun* is fine; the *actor* names (`Drainer` vs `DrainCoordinator`) are the problem — see §5. |
| **Envelope** (`CommandEnvelope`) | ✅ **Protect** | Precise messaging term; carries id/idempotency/sequence. Keep. |
| **Slot** (`ExecutableChildSlot`, `RuntimePipelineSlots`) | ✅ **OK** | "Slot" for a fillable structural position is standard (control-flow branches). Keep. |
| **Quiesce** (`RuntimeDrainMode.Quiesce`) | ⚠️ **Precise but esoteric** | Correct term of art, but few developers know it. Acceptable on an internal enum member; add a glossary line. Keep, document. |
| **ControlPlaneState** | ⚠️ **Borrowed metaphor** | "Control plane" (from networking/k8s) means "the admin/management channel." Here it holds admin *holds*. Evocative to infra people, opaque to workflow authors, and collides as `…State` (see §5). Prefer `WorkflowAdministrativeState` / `AdminHoldState` or just fold into `Hold`. |
| **OperationalState** | ❌ **Vague** | "Operational" is a filler adjective. Body actually holds **lease + heartbeat + drain + interruption** — i.e. *execution-liveness/ownership*. Rename to something concrete (see §5). |
| **SchedulerState** | ⚠️ **Acceptable** | It *is* the scheduler's continuation state — accurate. Collides only because the other two `…State` are vaguer. Keep, but the trio needs disambiguation. |
| **AmbientServices** (`IWorkflowExecutionAmbientServicesAccessor`) | ❌ **Two vague words stacked** | "Ambient" + "Services" says "some services that are… around." 40–50 char names result. It's an async-local service scope for the current execution. Prefer `IWorkflowExecutionScope`/`…ContextAccessor`. |
| **Passivation** (`WorkflowExecutionAgentPassivationBoundary/Request`) | ⚠️ **Actor-model jargon** | Borrowed from Akka/Orleans grain "passivation." Correct if the team thinks in actors, but obscure otherwise; produces 40+ char names. Consider `Deactivation`/`Idle`/`Unload`. |
| **ExecutionAgent** (`IWorkflowExecutionAgent`) | ⚠️ **Name collision risk** | Collides conceptually with the *AI* `Agent*` domain (110 types). "Agent" now means two unrelated things in one codebase. Consider `IWorkflowExecutionActor`/`…Worker`/`…Host` to free "Agent" for the AI side. |
| **Groundwork** (49 types) | ⚠️ **Codename** | It is the bespoke persistence provider (sibling to EFCore). Codenames are learnable *if* documented; it is *not* in the glossary. Either add a glossary entry or rename to a descriptive provider name. |
| **Nuplane** (`Elsa.Modularity.Nuplane`) | ⚠️ **Codename** | Same as Groundwork — undocumented codename for a modularity provider. Document or rename. |

**Overall domain-language grade: B.** The *nouns* are mostly strong (Bookmark/Trigger/Incident/Hold/Outbox/Checkpoint/WorkItem/Envelope). The weakness is the **abstract state/coordination layer** (`OperationalState`, `AmbientServices`, `ControlPlane`, Drainer/Coordinator) and **undocumented codenames** (Groundwork, Nuplane). Elsa 3's approachability came from concrete nouns; Elsa 4 keeps those but has layered an infra-jargon dialect on top of the runtime that newcomers must decode.

---

## 5. Prioritized rename proposal

Blast radius = # `.cs` files referencing the symbol (`grep -rl`). Grouped into coherent families so the system stays consistent. **P1 = high value / low risk.**

### Family A — Runtime "who runs the work" verbs (codify Processor vs Dispatcher vs Coordinator vs Drainer)

Establish one rule (see §6) and apply:

| # | Current | Proposed | Rationale | Blast radius |
|---|---|---|---|---|
| P1 | `IWorkflowSchedulerDrainer` / `WorkflowSchedulerDrainer` | `IWorkflowSchedulerDrainRunner` (or keep `Drainer`, rename the other) | Distinguish the per-execution *worker* from the *orchestrator*. | 4 / 2 |
| P1 | `IWorkflowExecutionDrainCoordinator` / `…Coordinator` / `…CoordinatorOptions` | `IWorkflowDrainOrchestrator` + `…Options` | "Coordinator/Drainer" are indistinguishable; make one the Orchestrator (command-triggered) and one the Runner (loop). | 4 / 2 |
| P2 | `IWorkflowExecutionCommandProcessor` + `NoopWorkflowExecutionCommandProcessor` | `IWorkflowExecutionCommandExecutor` | Body = "executes commands from the single-writer mailbox." Executor > Processor for "does the work." | 5 |
| P2 | `WorkflowSchedulerCommandProcessor` | `WorkflowSchedulerCommandRouter` | Body routes commands to scheduler handling — Router, not Processor. | 2 |
| P3 | `IWorkflowExecutionStartDispatcher` | keep, or `IWorkflowStartDispatcher` | Drop redundant `Execution`; it dispatches *starts*. | 5 |

### Family B — The three `…State` facets (disambiguate the overloaded head-noun)

| # | Current | Proposed | Rationale | Blast radius |
|---|---|---|---|---|
| P1 | `OperationalState` (+ `I/InMemory/Groundwork…Store`) | `ExecutionLivenessState` / `ExecutionLeaseState` | Holds lease+heartbeat+drain+interruption = liveness/ownership, not generic "operational." | 8 (+3 stores) |
| P2 | `ControlPlaneState` (+ 3 stores) | `WorkflowHoldState` / `AdministrativeState` | It stores admin *holds*; make the noun concrete and drop the k8s metaphor. | 4 (+3 stores) |
| P3 | `SchedulerState` | keep | Accurate once the siblings are fixed. | 8 |

### Family C — Reduce left-heavy qualifier stacks (length > 40)

Drop redundant leading qualifiers that the namespace already supplies (`Runtime.Core` → `Runtime` prefix is noise; `WorkflowExecution` → `Workflow`).

| # | Current | Proposed | Rationale | Blast radius |
|---|---|---|---|---|
| P2 | `AsyncLocalWorkflowExecutionAmbientServicesAccessor` (50) | `AsyncLocalWorkflowExecutionScope` | Kill "AmbientServicesAccessor"; it's an execution scope. | 7 (interface) |
| P2 | `IWorkflowExecutionAmbientServicesAccessor` (40) | `IWorkflowExecutionScope` | same | 7 |
| P3 | `WorkflowParentActivityCompletionSchedulerWorkHandler` (52) | `ParentCompletionWorkHandler` | Namespace already says Workflow/Scheduler. | 1–2 |
| P3 | `RuntimePostCommitOutboxProcessingException` (42) | `OutboxProcessingException` | `Runtime.Outbox` namespace carries the rest. | 2 |
| P3 | `WorkflowExecutionDrainCycleLimitExceededException` (49) | `DrainCycleLimitExceededException` | drop `WorkflowExecution`. | 1 |

### Family D — Vague-word cleanup (Manager / Info / Service)

| # | Current | Proposed | Rationale | Blast radius |
|---|---|---|---|---|
| P2 | `DefaultAuthenticationProviderManager` / `IAuthenticationProviderManager` | `…ProviderRegistry` or `…ProviderResolver` | "Manager" hides whether it stores or resolves. | small |
| P2 | `DefaultSecretManager` / `ISecretManager` | `ISecretResolver`/`ISecretStore` per role | See body: split store vs resolve. | medium |
| P3 | `ActivityDefinitionVersionInfo`, `WorkflowDefinitionVersionInfo` | `…Summary` / `…Descriptor` | "Info" → concrete role suffix. | small |
| P3 | `AgentStepInfo`, `LogExceptionInfo` | `AgentStepDescriptor`, `LogExceptionDetails` | same | small |

**Keep `AspNetCoreIdentityUserManager`, `IApplicationManager` (OpenIddict), `IRoleManager`, `ILiquidTemplateManager`** — these mirror external framework contracts; renaming would break the analogy. Manager is only banned for *Elsa-owned* abstractions.

### Family E — Codename / collision hygiene (P3, optional, discuss first)

| # | Current | Proposed | Rationale | Blast radius |
|---|---|---|---|---|
| P3 | `IWorkflowExecutionAgent` + `WorkflowExecutionAgent*` (10) | `IWorkflowExecutionActor` / `…Worker` | Frees "Agent" for the AI domain; removes the two-meanings collision. | ~10 |
| — | `Groundwork*` (49), `Nuplane` | *Document in glossary* (preferred over rename) | Codenames are fine if learnable; the fix is a glossary entry, not 49 renames. | doc-only |

> **Note on discipline:** the *reason* renames are cheap is that the runtime cluster is young and narrowly referenced (most symbols: 2–8 files; only `RuntimeSchedulerWorkItem` at 41 and `RuntimeCheckpointCommit` at 18 are broadly wired). Do the runtime family renames now, before adoption widens.

---

## 6. Proposed naming rules (mechanical style guide for coding agents)

**R1 — Max 4 CamelCase components** for a type name; hard cap at 5. If you need more, a qualifier is redundant with the namespace — drop it. (202 current violators.)

**R2 — Don't repeat the namespace in the type name.** Inside `Elsa.Workflows.Runtime.Core`, a type does **not** need both `Runtime` and `WorkflowExecution` prefixes. Leading `Workflow`/`Runtime`/`Activity` are only allowed when they *disambiguate* from a sibling without them.

**R3 — Banned vague words for Elsa-owned types:** `Manager`, `Helper`, `Util(s)`, `Info`, `Data`, `Object`, `Service` (when a more specific role fits), `Processor` (prefer a concrete verb). Exception: names that mirror an external framework contract (Identity `UserManager`, OpenIddict `IApplicationManager`).

**R4 — One suffix, one meaning. Codified role suffixes:**
- `…Source` = pull/returns (`Get`/`Read`). `…Contributor` = push/mutates ctx. `…PreProcessor`/`…PostProcessor` = phased contributor. `…Validator` = returns findings. (Sanctioned — keep.)
- `…Store` = persistence over one aggregate.
- `…Provider` = yields impls/descriptors. `…Factory` = constructs. `…Resolver` = maps key→value. `…Registry` = holds registrations.
- `…Executor` / `…Runner` = *does* the work (terminal). `…Router`/`…Dispatcher` = *selects a target and forwards*. `…Orchestrator`/`…Coordinator` = *sequences a multi-step operation*. Pick exactly one per layer and never use two synonyms for adjacent steps.
- Reserve `…Handler` for (a) mediator handlers and (b) sanctioned entity-lifecycle handlers. The scheduler `…WorkHandler` family is grandfathered but should ideally be `…WorkExecutor`.

**R5 — Prefer concrete domain nouns over borrowed infra metaphors** unless the metaphor is glossary-documented: favor `HoldState`/`LivenessState` over `ControlPlaneState`/`OperationalState`; document `Quiesce`, `Passivation`, `ControlPlane`, `Groundwork`, `Nuplane` in `docs/glossary/elsa.md` **or** rename them.

**R6 — One concept, one head-noun.** If three types end in `…State`, their heads must make the *distinction* obvious (`Hold`, `Liveness`, `Scheduler…`), not rely on a vague adjective.

**R7 — Default-impl prefixes are fixed and good:** `Default…`, `InMemory…`, `Noop…`, plus provider prefixes `EFCore…`/`Groundwork…`/`Sqlite…`. Keep using them; they're a strength.

**R8 — Reserve `Agent`** for the AI-assistant domain. Workflow execution "agents" should use `Actor`/`Worker`/`Host` to avoid the cross-domain homonym.

---

## 7. Findings index

- **NM-1** (systemic, P1): Length inflation — 202 public types > 35 chars, 74 > 40, driven by left-heavy qualifier stacks (`Runtime`+`WorkflowExecution`+…). Apply R1/R2. See §2.2, Family C.
- **NM-2** (systemic, P1): Near-synonym "runs work" suffixes (Processor/Dispatcher/Coordinator/Drainer/Executor/Invoker/Handler) with no codified distinction; several appear in one call-chain. Apply R4. See §2.5, Family A.
- **NM-3** (P1): `…State` head-noun overloaded across three unrelated runtime facets (`ControlPlaneState`, `OperationalState`, `SchedulerState`) + their 12 store variants. Apply R6. Family B.
- **NM-4** (P1): `OperationalState` is vague — actually execution liveness/ownership (lease+heartbeat+drain+interruption). Rename `ExecutionLivenessState`. §4, Family B.
- **NM-5** (P2): `Handler` suffix carries three senses; scheduler `…WorkHandler` (16 types) is really an executor/strategy, not a lifecycle handler — a third meaning absent from `EXTENSION_POINTS.md`. §3, R4.
- **NM-6** (P2): `AmbientServices` — two filler words producing 40–50 char names; it's an execution scope. Rename `…Scope`. §4, Family C.
- **NM-7** (P2): `Manager` used for 21 types; ~half are vague Elsa-owned abstractions (`DefaultSecretManager`, `DefaultAuthenticationProviderManager`, `ITaskStateManager`). Banned word — split into Store/Resolver/Registry. Keep the external-mirroring ones. §3, Family D, R3.
- **NM-8** (P2): Drain actors `Drainer` vs `DrainCoordinator` are indistinguishable by name for genuinely different roles (per-execution loop vs command-triggered orchestrator). Family A.
- **NM-9** (P3): `Info` suffix (6 types) is vague → `Summary`/`Descriptor`/`Details`. §3, Family D.
- **NM-10** (P3): "Agent" is a cross-domain homonym — AI `Agent*` (110 types) vs `IWorkflowExecutionAgent` (10). Reserve `Agent` for AI; rename runtime to `Actor`/`Worker`. §4, Family E, R8.
- **NM-11** (P3): Undocumented codenames `Groundwork` (49) and `Nuplane` are not in the glossary. Preferred fix: add glossary entries (rename is high-cost, low-value). §4, Family E, R5.
- **NM-12** (P3): Infra-metaphor jargon `ControlPlane`, `Passivation`, `Quiesce` correct-but-esoteric; document in glossary or replace with plain terms. §4, R5.
- **NM-13** (positive): Extension-point grammar (Source/Contributor/Pre-/PostProcessor/Validator), `.Core`/Feature layering, `Store` (84×), Command/Request/Result, and default-impl prefixes are applied with strong consistency. Helper/Util essentially absent (1 offender). **Protect these.** §3, §6/R7.
- **NM-14** (positive): Core domain nouns (Bookmark, Trigger, Incident, Outbox, Checkpoint, WorkItem, Hold, PauseGate, Envelope, Slot) are concrete and evocative — the Elsa-3 approachability is preserved here. Do not rename. §4.

**Recommended sequence:** land Family A + B (runtime cluster, small blast radius, highest confusion payoff) first; then D (vague words); glossary-document E/§4 jargon; treat C as opportunistic (rename on touch). Total high-value surface ≈ 30–40 types, almost all referenced by ≤ 8 files.
